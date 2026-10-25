using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using UnityEngine.SceneManagement;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshCutEngine
    {
        /// <summary>닫힌 경계의 외곽과 구멍을 묶어 양쪽 절단면을 생성합니다.</summary>
        private void AddCaps(MeshBuilder part, MeshBuilder remainder)
        {
            float sourceHandedness = m_source.transform.localToWorldMatrix.determinant < 0f ? -1f : 1f;
            int[] parents = new int[m_closedLoops.Count];
            float[] areas = new float[m_closedLoops.Count];

            for (int i = 0; i < areas.Length; i++)
            {
                parents[i] = -1;
                areas[i] = Mathf.Abs(LoopArea(m_closedLoops[i]));
            }

            for (int i = 0; i < m_closedLoops.Count; i++)
            {
                float parentArea = float.PositiveInfinity;
                Vector2 point = ProjectOntoCutPlane(m_topologyVertices[m_closedLoops[i].Nodes[0]].Pose);

                for (int j = 0; j < m_closedLoops.Count; j++)
                {
                    if (i == j || areas[j] <= areas[i] || areas[j] >= parentArea
                        || m_closedLoops[i].SourceComponent != m_closedLoops[j].SourceComponent)
                    {
                        continue;
                    }

                    if (InsideLoop(point, m_closedLoops[j]))
                    {
                        parents[i] = j;
                        parentArea = areas[j];
                    }
                }
            }

            List<int> outers = new List<int>();

            for (int i = 0; i < m_closedLoops.Count; i++)
            {
                int depth = 0;
                int ancestor = parents[i];

                while (ancestor >= 0)
                {
                    depth++;
                    ancestor = parents[ancestor];
                }

                if (depth % 2 == 0)
                {
                    outers.Add(i);
                }
            }

            for (int groupIndex = 0; groupIndex < outers.Count; groupIndex++)
            {
                int outerIndex = outers[groupIndex];
                List<int> nodeIndices = new List<int>(m_closedLoops[outerIndex].Nodes);
                List<Vector3> contour = PolygonPoints(m_closedLoops[outerIndex]);
                List<IList<Vector3>> holes = new List<IList<Vector3>>();

                for (int i = 0; i < parents.Length; i++)
                {
                    if (parents[i] == outerIndex)
                    {
                        holes.Add(PolygonPoints(m_closedLoops[i]));
                        nodeIndices.AddRange(m_closedLoops[i].Nodes);
                    }
                }

                List<int> triangles = TriangulateCap(contour, holes);
                List<Vector2> uvs = new List<Vector2>(BuildCapUvs(nodeIndices, groupIndex, outers.Count));
                List<Vertex> vertices = nodeIndices.ConvertAll(index => m_topologyVertices[index]);

                if (m_settings.UvIslandTransforms != null
                    && m_settings.UvIslandTransforms.Length == outers.Count)
                {
                    RefineCapGrid(vertices, uvs, triangles, m_settings.UvIslandTransforms[groupIndex]);
                }

                int[] partIndices = new int[vertices.Count];
                int[] remainderIndices = new int[vertices.Count];
                float partSign = m_settings.DetachPositive ? -1f : 1f;

                for (int i = 0; i < vertices.Count; i++)
                {
                    Vertex boundary = vertices[i];
                    partIndices[i] = part.Add(CapVertex(boundary, uvs[i], partSign));
                    remainderIndices[i] = remainder.Add(CapVertex(boundary, uvs[i], -partSign));
                }

                int capSubmesh = m_source.sharedMesh.subMeshCount;

                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];
                    Vector3 pa = vertices[a].Pose;
                    Vector3 pb = vertices[b].Pose;
                    Vector3 pc = vertices[c].Pose;
                    float winding = Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), m_planeNormal);

                    // 월드에서 정한 앞면을 렌더러의 로컬 삼각형 방향으로 되돌린다.
                    if (winding * partSign * sourceHandedness < 0f)
                    {
                        int swap = b;
                        b = c;
                        c = swap;
                    }

                    part.AddTriangle(partIndices[a], partIndices[b], partIndices[c], capSubmesh);
                    remainder.AddTriangle(remainderIndices[a], remainderIndices[c], remainderIndices[b], capSubmesh);
                }
            }
        }

        /// <summary>절단 평면에 투영한 닫힌 경계의 부호 있는 면적을 구합니다.</summary>
        private float LoopArea(Loop loop)
        {
            double area = 0.0;

            for (int i = 0; i < loop.Nodes.Count; i++)
            {
                Vector2 a = ProjectOntoCutPlane(m_topologyVertices[loop.Nodes[i]].Pose);
                Vector2 b = ProjectOntoCutPlane(m_topologyVertices[loop.Nodes[(i + 1) % loop.Nodes.Count]].Pose);
                area += (double)a.x * b.y - (double)b.x * a.y;
            }

            return (float)(area * 0.5);
        }

        /// <summary>주어진 점이 닫힌 경계 내부에 있는지 판정합니다.</summary>
        private bool InsideLoop(Vector2 point, Loop loop)
        {
            bool inside = false;

            for (int i = 0; i < loop.Nodes.Count; i++)
            {
                Vector2 a = ProjectOntoCutPlane(m_topologyVertices[loop.Nodes[i]].Pose);
                Vector2 b = ProjectOntoCutPlane(m_topologyVertices[loop.Nodes[(i + 1) % loop.Nodes.Count]].Pose);

                if ((a.y > point.y) != (b.y > point.y)
                    && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>경계 정점을 캡 삼각분할용 평면 좌표로 변환합니다.</summary>
        private List<Vector3> PolygonPoints(Loop loop)
        {
            List<Vector3> result = new List<Vector3>();

            foreach (int node in loop.Nodes)
            {
                Vector2 point = ProjectOntoCutPlane(m_topologyVertices[node].Pose);
                result.Add(new Vector3(point.x, 0f, point.y));
            }

            return result;
        }

        /// <summary>외곽 경계와 내부 구멍을 포함하는 캡을 삼각분할합니다.</summary>
        private static List<int> TriangulateCap(List<Vector3> contour, List<IList<Vector3>> holes)
        {
            // 설치된 ProBuilder의 제약 삼각분할을 사용해 오목 경계와 내부 구멍을 함께 보존한다.
            Scene scene = EditorSceneManager.NewPreviewScene();
            GameObject host = new GameObject("Cut cap triangulation")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            SceneManager.MoveGameObjectToScene(host, scene);
            Mesh temporaryMesh = null;

            try
            {
                ProBuilderMesh mesh = host.AddComponent<ProBuilderMesh>();
                ActionResult result = AppendElements.CreateShapeFromPolygon(mesh, contour, 0f, false, holes);
                temporaryMesh = host.GetComponent<MeshFilter>().sharedMesh;

                if (result.status != ActionResult.Status.Success || mesh.faces.Count != 1)
                {
                    throw new InvalidOperationException("절단면 삼각분할 실패: " + result.notification);
                }

                return new List<int>(mesh.faces[0].indexes);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);

                if (temporaryMesh != null && !AssetDatabase.Contains(temporaryMesh))
                {
                    UnityEngine.Object.DestroyImmediate(temporaryMesh);
                }

                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>닫힌 캡 그룹의 크기와 배치 설정으로 UV를 생성합니다.</summary>
        private Vector2[] BuildCapUvs(List<int> nodes, int groupIndex, int groupCount)
        {
            Vector2[] result = new Vector2[nodes.Count];
            Vector3 worldU = m_planeRight;
            Vector3 worldV = m_planeUp;
            float angle = m_settings.UvRotation * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(angle);
            float sine = Mathf.Sin(angle);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < nodes.Count; i++)
            {
                Vector3 delta = m_topologyVertices[nodes[i]].Pose - m_settings.Origin;
                float u = Vector3.Dot(delta, worldU);
                float v = Vector3.Dot(delta, worldV);
                result[i] = new Vector2(u * cosine - v * sine, u * sine + v * cosine);
                min = Vector2.Min(min, result[i]);
                max = Vector2.Max(max, result[i]);
            }

            int grid = Mathf.CeilToInt(Mathf.Sqrt(groupCount));
            Vector2 cell = new Vector2(groupIndex % grid, groupIndex / grid);
            float extent = Mathf.Max(max.x - min.x, max.y - min.y);

            for (int i = 0; i < result.Length; i++)
            {
                if (m_settings.PackUv)
                {
                    Vector2 centered = (result[i] - (min + max) * 0.5f) / Mathf.Max(extent, 0.000001f);
                    result[i] = (cell + new Vector2(0.5f, 0.5f) + centered * 0.9f) / grid;
                }
                else
                {
                    result[i] *= m_settings.UvScale;
                }
            }

            return result;
        }

        /// <summary>절단면의 법선과 UV를 가진 독립 스킨 정점을 만듭니다.</summary>
        private Vertex CapVertex(Vertex boundary, Vector2 uv, float normalSign)
        {
            Matrix4x4 skin = SkinMatrix(boundary.Weights);
            float angle = m_settings.UvRotation * Mathf.Deg2Rad;
            Vector3 tangent = m_planeRight * Mathf.Cos(angle) - m_planeUp * Mathf.Sin(angle);
            Vector3 bindTangent = skin.inverse.MultiplyVector(tangent).normalized;
            Vertex vertex = new Vertex
            {
                Id = boundary.Id,
                Position = boundary.Position,
                Pose = boundary.Pose,
                Weights = boundary.Weights,
                Normal = skin.transpose.MultiplyVector(m_planeNormal * normalSign).normalized,
                Tangent = new Vector4(bindTangent.x, bindTangent.y, bindTangent.z, normalSign),
                Color = boundary.Color,
                Uvs = (Vector4[])boundary.Uvs.Clone()
            };
            vertex.Uvs[0] = new Vector4(uv.x, uv.y, 0f, 0f);

            return vertex;
        }
    }
}
