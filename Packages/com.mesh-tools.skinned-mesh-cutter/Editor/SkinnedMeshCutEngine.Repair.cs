using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshCutEngine
    {
        private readonly List<Vector3> m_repairedBoundary = new List<Vector3>();
        private float m_maximumRepairedGap;

        /// <summary>허용 거리 안의 단순 열린 틈에 보정 표면을 추가합니다.</summary>
        private void RepairOpenBoundaries()
        {
            if (!m_settings.RepairOpenBoundaries)
            {
                return;
            }

            float maximum = m_settings.MaximumGapDistance;

            if (maximum <= 0f || float.IsNaN(maximum) || float.IsInfinity(maximum))
            {
                throw new InvalidOperationException("작은 틈 보정 허용 거리는 0보다 큰 유한한 값이어야 합니다.");
            }

            Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();

            foreach (Edge edge in m_cutSegments.Keys)
            {
                AddNeighbor(adjacency, edge.A, edge.B);
                AddNeighbor(adjacency, edge.B, edge.A);
            }

            Dictionary<int, List<SurfaceEdge>> boundary = null;
            HashSet<int> visited = new HashSet<int>();

            foreach (int start in adjacency.Keys)
            {
                if (!visited.Add(start))
                {
                    continue;
                }

                List<int> component = new List<int>();
                List<int> endpoints = new List<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(start);
                bool eligible = true;

                while (queue.Count > 0)
                {
                    int node = queue.Dequeue();
                    component.Add(node);
                    eligible &= adjacency[node].Count <= 2
                        && IsInsideCutRectangle(ProjectOntoCutPlane(m_topologyVertices[node].Pose));

                    if (adjacency[node].Count == 1)
                    {
                        endpoints.Add(node);
                    }

                    foreach (int neighbor in adjacency[node])
                    {
                        if (visited.Add(neighbor))
                        {
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                // 선택한 한 윤곽의 양 끝점만 연결하고, 인접한 별도 표면은 연결하지 않는다.
                float contourGap = endpoints.Count == 2
                    ? Vector3.Distance(m_topologyVertices[endpoints[0]].Pose, m_topologyVertices[endpoints[1]].Pose)
                    : float.PositiveInfinity;

                if (!eligible || endpoints.Count != 2 || !MatchesBoundarySelection(component)
                    || contourGap > maximum)
                {
                    continue;
                }

                if (boundary == null)
                {
                    boundary = FindOpenSurfaceEdges();
                }

                if (!boundary.TryGetValue(endpoints[0], out List<SurfaceEdge> first) || first.Count != 1
                    || !boundary.TryGetValue(endpoints[1], out List<SurfaceEdge> second)
                    || second.Count != 1)
                {
                    continue;
                }

                SurfaceEdge left = first[0];
                SurfaceEdge right = second[0];
                float gap = Mathf.Max(
                    contourGap,
                    Vector3.Distance(left.A.Pose, right.B.Pose),
                    Vector3.Distance(left.B.Pose, right.A.Pose));

                if (ReferenceEquals(left, right) || gap > maximum
                    || SignedDistanceToPlane(left.A) * SignedDistanceToPlane(right.B) <= 0f
                    || SignedDistanceToPlane(left.B) * SignedDistanceToPlane(right.A) <= 0f)
                {
                    continue;
                }

                // 좁은 외피 띠를 추가해 단면 윤곽뿐 아니라 실제 표면도 닫는다.
                // 원본 정점과 가중치를 재사용하고 일반 교차점 보간으로 이 띠를 처리한다.
                SkinnedMeshCutMaskMode mask = left.Mask == SkinnedMeshCutMaskMode.Cut || right.Mask == SkinnedMeshCutMaskMode.Cut
                    ? SkinnedMeshCutMaskMode.Cut
                    : left.Mask;
                AddRepairTriangle(left.B, left.A, right.B, left.Submesh, mask);
                AddRepairTriangle(left.B, right.B, right.A, left.Submesh, mask);
                m_repairedBoundary.Add(m_topologyVertices[endpoints[0]].Pose);
                m_repairedBoundary.Add(m_topologyVertices[endpoints[1]].Pose);
                m_maximumRepairedGap = Mathf.Max(m_maximumRepairedGap, gap);
            }

            if (m_repairedBoundary.Count > 0)
            {
                m_cutSegments.Clear();
                FindIntersections();
            }
        }

        /// <summary>경계 정점의 인접 목록에 연결을 추가합니다.</summary>
        private static void AddNeighbor(Dictionary<int, List<int>> adjacency, int a, int b)
        {
            if (!adjacency.TryGetValue(a, out List<int> neighbors))
            {
                neighbors = new List<int>();
                adjacency.Add(a, neighbors);
            }

            neighbors.Add(b);
        }

        /// <summary>원본 표면에서 한 삼각형만 소유하는 열린 모서리를 찾습니다.</summary>
        private Dictionary<int, List<SurfaceEdge>> FindOpenSurfaceEdges()
        {
            Dictionary<Edge, List<SurfaceEdge>> owners = new Dictionary<Edge, List<SurfaceEdge>>();

            foreach (Triangle triangle in m_triangles)
            {
                for (int i = 0; i < 3; i++)
                {
                    Vertex a = triangle.Vertices[i];
                    Vertex b = triangle.Vertices[(i + 1) % 3];
                    Edge edge = new Edge(GetOrCreateTopologyVertex(a), GetOrCreateTopologyVertex(b));

                    if (!owners.TryGetValue(edge, out List<SurfaceEdge> list))
                    {
                        list = new List<SurfaceEdge>();
                        owners.Add(edge, list);
                    }

                    list.Add(new SurfaceEdge(a, b, triangle.Submesh, triangle.Mask));
                }
            }

            Dictionary<int, List<SurfaceEdge>> result = new Dictionary<int, List<SurfaceEdge>>();

            foreach (List<SurfaceEdge> ownersForEdge in owners.Values)
            {
                if (ownersForEdge.Count != 1)
                {
                    continue;
                }

                SurfaceEdge edge = ownersForEdge[0];

                if (IsProtected(edge.Mask)
                    || SignedDistanceToPlane(edge.A) * SignedDistanceToPlane(edge.B) >= 0f)
                {
                    continue;
                }

                int node = GetOrCreateTopologyVertex(Intersection(edge.A, edge.B));

                if (!result.TryGetValue(node, out List<SurfaceEdge> edges))
                {
                    edges = new List<SurfaceEdge>();
                    result.Add(node, edges);
                }

                edges.Add(edge);
            }

            return result;
        }

        /// <summary>틈 보정용 삼각형과 본 가중치 및 마스크를 등록합니다.</summary>
        private void AddRepairTriangle(Vertex a, Vertex b, Vertex c, int submesh, SkinnedMeshCutMaskMode mask)
        {
            float twiceArea = Vector3.Cross(b.Pose - a.Pose, c.Pose - a.Pose).magnitude;

            if (twiceArea > m_settings.Tolerance * m_settings.Tolerance * 2f)
            {
                m_triangles.Add(new Triangle(a, b, c, submesh, mask));
            }
        }

        private sealed class SurfaceEdge
        {
            public Vertex A { get; }
            public Vertex B { get; }
            public int Submesh { get; }
            public SkinnedMeshCutMaskMode Mask { get; }

            /// <summary>열린 표면 모서리의 끝점과 원본 삼각형을 보관합니다.</summary>
            public SurfaceEdge(Vertex a, Vertex b, int submesh, SkinnedMeshCutMaskMode mask)
            {
                A = a;
                B = b;
                Submesh = submesh;
                Mask = mask;
            }
        }
    }
}
