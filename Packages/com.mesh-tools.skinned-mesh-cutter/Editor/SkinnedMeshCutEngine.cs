using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>현재 작성 포즈의 유한 평면을 사용해 원본 스킨 좌표와 전체 본 가중치를 보존하며 분리합니다.</summary>
    public sealed partial class SkinnedMeshCutEngine
    {
        private readonly SkinnedMeshRenderer m_source;
        private readonly Settings m_settings;
        private readonly Vector3 m_planeNormal;
        private readonly Vector3 m_planeRight;
        private readonly Vector3 m_planeUp;
        private readonly List<Vertex> m_vertices = new List<Vertex>();
        private readonly List<Triangle> m_triangles = new List<Triangle>();
        private readonly Dictionary<Edge, Segment> m_cutSegments = new Dictionary<Edge, Segment>();
        // 위치를 용접한 위상 정점은 교차 경계뿐 아니라 출력 표면 연결 판정에도 사용한다.
        private readonly List<Vertex> m_topologyVertices = new List<Vertex>();
        private readonly Dictionary<Vector3Int, List<int>> m_vertexWeldGrid = new Dictionary<Vector3Int, List<int>>();
        private readonly Dictionary<Edge, Vertex> m_edgeIntersections = new Dictionary<Edge, Vertex>();
        private readonly List<Loop> m_closedLoops = new List<Loop>();
        private Matrix4x4[] m_skinMatrices;
        private bool[] m_uvChannels;
        private int[] m_uvDimensions;
        private bool m_hasNormals;
        private bool m_hasTangents;
        private bool m_hasColors;
        private bool m_hasExplicitCutMask;

        /// <summary>읽기 가능한 원본 스킨과 절단 평면 설정을 검증하고 준비합니다.</summary>
        public SkinnedMeshCutEngine(SkinnedMeshRenderer source, Settings settings)
        {
            m_source = source;
            m_settings = settings;

            if (source == null || source.sharedMesh == null || !source.sharedMesh.isReadable)
            {
                throw new InvalidOperationException("읽기 가능한 SkinnedMeshRenderer가 필요합니다.");
            }

            if (settings.Size.x <= 0f || settings.Size.y <= 0f || settings.Tolerance <= 0f)
            {
                throw new InvalidOperationException("절단 평면의 폭·높이와 허용 오차는 0보다 커야 합니다.");
            }

            m_planeNormal = settings.Rotation * Vector3.forward;
            m_planeRight = settings.Rotation * Vector3.right;
            m_planeUp = settings.Rotation * Vector3.up;
            ReadSource();
            ReadTriangleMask();
        }

        /// <summary>선택한 표면을 분리하고 닫힌 경계에만 캡과 UV를 생성합니다.</summary>
        public Result Cut()
        {
            FindIntersections();
            RepairOpenBoundaries();
            SelectCutBoundaries();
            List<Polygon> polygons = BuildClippedPolygons();
            bool[] detached = ClassifyOutputPolygons(polygons);
            CompletePartContours();
            MeshBuilder part = new MeshBuilder(this);
            MeshBuilder remainder = new MeshBuilder(this);

            for (int i = 0; i < polygons.Count; i++)
            {
                Polygon polygon = polygons[i];
                MeshBuilder builder = detached[i] ? part : remainder;
                builder.AddPolygon(polygon.Vertices, polygon.Submesh);
            }

            if (m_settings.GenerateCap)
            {
                AddCaps(part, remainder);
            }

            Mesh partMesh = null;
            Mesh remainderMesh = null;

            try
            {
                partMesh = part.Build(m_source.sharedMesh.name + "_PART");
                remainderMesh = remainder.Build(m_source.sharedMesh.name + "_REMAIN");
                SkinnedMeshCutUvPreview capUvs = null;

                if (m_settings.GenerateCap && partMesh.subMeshCount > m_source.sharedMesh.subMeshCount)
                {
                    capUvs = new SkinnedMeshCutUvPreview(partMesh, m_source.sharedMesh.subMeshCount);
                    capUvs.ApplyToMesh(partMesh, m_settings.UvLayoutScale, m_settings.UvOffset, m_settings.UvIslandTransforms);
                    new SkinnedMeshCutUvPreview(remainderMesh, m_source.sharedMesh.subMeshCount).ApplyToMesh(
                        remainderMesh,
                        m_settings.UvLayoutScale,
                        m_settings.UvOffset,
                        m_settings.UvIslandTransforms);
                }

                List<Vector3> boundary = new List<Vector3>();

                foreach (Segment segment in m_cutSegments.Values)
                {
                    if (segment.Selected)
                    {
                        boundary.Add(m_topologyVertices[segment.Edge.A].Pose);
                        boundary.Add(m_topologyVertices[segment.Edge.B].Pose);
                    }
                }

                // 새로 이은 끝점 사이의 선분도 표시해 미리보기 경계가 실제 캡 형태와 일치하게 한다.
                foreach (Loop loop in m_closedLoops)
                {
                    for (int i = 0; i < loop.Nodes.Count; i++)
                    {
                        Edge closingEdge = new Edge(loop.Nodes[i], loop.Nodes[(i + 1) % loop.Nodes.Count]);
                        if (!m_cutSegments.ContainsKey(closingEdge))
                        {
                            boundary.Add(m_topologyVertices[closingEdge.A].Pose);
                            boundary.Add(m_topologyVertices[closingEdge.B].Pose);
                        }
                    }
                }

                return new Result(
                    partMesh,
                    remainderMesh,
                    boundary.ToArray(),
                    m_closedLoops.Count,
                    m_repairedBoundary.ToArray(),
                    m_maximumRepairedGap,
                    capUvs);
            }
            catch
            {
                if (partMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(partMesh);
                }

                if (remainderMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(remainderMesh);
                }

                throw;
            }
        }

        /// <summary>오류 진단에 표시할 전체 교차 경계의 선분을 반환합니다.</summary>
        public Vector3[] GetDiagnosticBoundary()
        {
            List<Vector3> points = new List<Vector3>();

            foreach (Segment segment in m_cutSegments.Values)
            {
                points.Add(m_topologyVertices[segment.Edge.A].Pose);
                points.Add(m_topologyVertices[segment.Edge.B].Pose);
            }

            return points.ToArray();
        }
    }
}
