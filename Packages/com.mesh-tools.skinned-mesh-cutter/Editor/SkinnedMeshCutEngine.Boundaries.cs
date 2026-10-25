using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshCutEngine
    {
        /// <summary>평면 좌표가 유한한 절단 사각형 내부인지 확인합니다.</summary>
        private bool IsInsideCutRectangle(Vector2 point)
        {
            return Mathf.Abs(point.x) <= m_settings.Size.x * 0.5f + m_settings.Tolerance
                && Mathf.Abs(point.y) <= m_settings.Size.y * 0.5f + m_settings.Tolerance;
        }

        /// <summary>경계 선분이 유한한 절단 사각형과 겹치는지 확인합니다.</summary>
        private bool SegmentIntersectsCutRectangle(Vector2 a, Vector2 b)
        {
            float min = 0f;
            float max = 1f;
            Vector2 delta = b - a;
            Vector2 half = m_settings.Size * 0.5f;

            for (int axis = 0; axis < 2; axis++)
            {
                if (Mathf.Abs(delta[axis]) <= m_settings.Tolerance)
                {
                    if (Mathf.Abs(a[axis]) > half[axis])
                    {
                        return false;
                    }

                    continue;
                }

                float first = (-half[axis] - a[axis]) / delta[axis];
                float second = (half[axis] - a[axis]) / delta[axis];
                min = Mathf.Max(min, Mathf.Min(first, second));
                max = Mathf.Min(max, Mathf.Max(first, second));
            }

            return min <= max;
        }

        /// <summary>경계 정점이 선택한 본의 최소 가중치를 만족하는지 확인합니다.</summary>
        private bool MatchesBones(List<int> nodes)
        {
            if (m_settings.TargetBones == null || m_settings.TargetBones.Count == 0)
            {
                return true;
            }

            float total = 0f;

            foreach (int node in nodes)
            {
                foreach (BoneWeight1 weight in m_topologyVertices[node].Weights)
                {
                    if (m_settings.TargetBones.Contains(weight.boneIndex))
                    {
                        total += weight.weight;
                    }
                }
            }

            return total / nodes.Count >= m_settings.MinimumBoneWeight;
        }

        /// <summary>원본 삼각형에 브러시 마스크와 명시적 절단 선택을 적용합니다.</summary>
        private void ReadTriangleMask()
        {
            SkinnedMeshCutMaskMode[] mask = m_settings.TriangleMask;

            if (mask == null || mask.Length == 0)
            {
                return;
            }

            if (mask.Length != m_triangles.Count)
            {
                throw new InvalidOperationException("브러시 마스크와 원본 삼각형 수가 다릅니다. 마스크를 초기화하세요.");
            }

            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i] < SkinnedMeshCutMaskMode.Auto || mask[i] > SkinnedMeshCutMaskMode.KeepRemainder)
                {
                    throw new InvalidOperationException("브러시 마스크에 지원하지 않는 모드가 있습니다.");
                }

                m_triangles[i].Mask = mask[i];
                m_hasExplicitCutMask |= mask[i] == SkinnedMeshCutMaskMode.Cut;
            }
        }

        /// <summary>절단하지 않고 한쪽 결과에 보존할 마스크인지 확인합니다.</summary>
        private static bool IsProtected(SkinnedMeshCutMaskMode mode)
        {
            return mode == SkinnedMeshCutMaskMode.KeepPart || mode == SkinnedMeshCutMaskMode.KeepRemainder;
        }

        /// <summary>경계가 명시적으로 선택한 절단 삼각형을 통과하는지 확인합니다.</summary>
        private bool MatchesCutMask(List<int> nodes)
        {
            if (!m_hasExplicitCutMask)
            {
                return true;
            }

            HashSet<int> component = new HashSet<int>(nodes);

            foreach (Segment segment in m_cutSegments.Values)
            {
                if (component.Contains(segment.Edge.A) && component.Contains(segment.Edge.B)
                    && segment.Triangles.Exists(index => m_triangles[index].Mask == SkinnedMeshCutMaskMode.Cut))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>경계가 본 선택과 브러시 절단 조건을 만족하는지 확인합니다.</summary>
        private bool MatchesBoundarySelection(List<int> nodes)
        {
            return m_hasExplicitCutMask ? MatchesCutMask(nodes) : MatchesBones(nodes);
        }

        /// <summary>선택한 모든 경계를 절단에 사용하고 닫힌 루프만 캡 후보로 등록합니다.</summary>
        private void SelectCutBoundaries()
        {
            Dictionary<int, List<int>> adjacency = BuildBoundaryAdjacency();
            HashSet<int> visited = new HashSet<int>();
            bool hasSelectedBoundary = false;

            foreach (int start in adjacency.Keys)
            {
                if (visited.Contains(start))
                {
                    continue;
                }

                List<int> component = CollectBoundaryComponent(start, adjacency, visited);

                if (!BoundaryTouchesCutRectangle(component, adjacency) || !MatchesBoundarySelection(component))
                {
                    continue;
                }

                foreach (int node in component)
                {
                    if (!IsInsideCutRectangle(ProjectOntoCutPlane(m_topologyVertices[node].Pose)))
                    {
                        throw new InvalidOperationException("평면의 폭·높이가 단면 일부만 덮습니다. 표시 범위를 넓히세요.");
                    }
                }

                // 열린 옷과 분기한 껍데기도 표면을 분리한다. 닫힘 여부는 캡과 UV 생성에만 사용한다.
                hasSelectedBoundary = true;
                bool isClosedLoop = true;

                foreach (int node in component)
                {
                    isClosedLoop &= adjacency[node].Count == 2;

                    foreach (int neighbor in adjacency[node])
                    {
                        m_cutSegments[new Edge(node, neighbor)].Selected = true;
                    }
                }

                if (isClosedLoop)
                {
                    m_closedLoops.Add(new Loop(OrderClosedBoundary(start, adjacency, component.Count)));
                }
                else if (m_settings.GenerateCap && m_settings.CloseOpenBoundaries
                    && TryCollectOpenBoundary(component, adjacency, out Loop openContour))
                {
                    m_openContours.Add(openContour);
                }
            }

            if (!hasSelectedBoundary)
            {
                throw new InvalidOperationException("절단 경계를 찾지 못했습니다. 평면의 위치·폭·높이와 대상 관절을 확인하세요.");
            }
        }

        /// <summary>교차 선분의 양 끝점을 연결해 경계 탐색용 인접 목록을 만듭니다.</summary>
        private Dictionary<int, List<int>> BuildBoundaryAdjacency()
        {
            Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();

            foreach (Edge edge in m_cutSegments.Keys)
            {
                AddNeighbor(adjacency, edge.A, edge.B);
                AddNeighbor(adjacency, edge.B, edge.A);
            }

            return adjacency;
        }

        /// <summary>서로 연결된 경계 정점을 너비 우선으로 수집합니다.</summary>
        private static List<int> CollectBoundaryComponent(int start, Dictionary<int, List<int>> adjacency, HashSet<int> visited)
        {
            List<int> component = new List<int>();
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                component.Add(node);

                foreach (int neighbor in adjacency[node])
                {
                    if (visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return component;
        }

        /// <summary>연결된 경계 선분 중 절단 사각형을 통과하는 선분이 있는지 확인합니다.</summary>
        private bool BoundaryTouchesCutRectangle(List<int> component, Dictionary<int, List<int>> adjacency)
        {
            foreach (int node in component)
            {
                Vector2 point = ProjectOntoCutPlane(m_topologyVertices[node].Pose);

                foreach (int neighbor in adjacency[node])
                {
                    Vector2 other = ProjectOntoCutPlane(m_topologyVertices[neighbor].Pose);

                    if (SegmentIntersectsCutRectangle(point, other))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>모든 정점의 차수가 2인 경계를 캡 둘레 순서로 정렬합니다.</summary>
        private static List<int> OrderClosedBoundary(int start, Dictionary<int, List<int>> adjacency, int expectedCount)
        {
            List<int> ordered = new List<int>();
            int previous = -1;
            int current = start;
            do
            {
                ordered.Add(current);
                List<int> neighbors = adjacency[current];
                int next = neighbors[0] == previous ? neighbors[1] : neighbors[0];
                previous = current;
                current = next;
            }
            while (current != start && ordered.Count <= expectedCount);

            if (current != start || ordered.Count != expectedCount)
            {
                throw new InvalidOperationException("단면 경계를 하나의 닫힌 루프로 구성하지 못했습니다.");
            }

            return ordered;
        }

        /// <summary>분기 없는 열린 절단선을 순서대로 수집하고 원본 파츠별 닫힘 판단을 보류합니다.</summary>
        private bool TryCollectOpenBoundary(
            List<int> component, Dictionary<int, List<int>> adjacency, out Loop loop)
        {
            loop = null;
            int start = -1;
            int endpointCount = 0;

            foreach (int node in component)
            {
                int degree = adjacency[node].Count;
                if (degree > 2)
                {
                    return false;
                }

                if (degree == 1)
                {
                    start = node;
                    endpointCount++;
                }
            }

            if (endpointCount != 2 || component.Count < 2)
            {
                return false;
            }

            List<int> ordered = new List<int>(component.Count);
            int previous = -1;
            int current = start;

            while (current >= 0 && ordered.Count < component.Count)
            {
                ordered.Add(current);
                int next = -1;

                foreach (int neighbor in adjacency[current])
                {
                    if (neighbor != previous)
                    {
                        next = neighbor;
                        break;
                    }
                }

                previous = current;
                current = next;
            }

            if (ordered.Count != component.Count || current >= 0)
            {
                return false;
            }

            // 직선 두 개도 같은 원본 파츠의 앞뒷면이면 하나의 면적 있는 단면을 만들 수 있다.
            loop = new Loop(ordered);
            return true;
        }

        /// <summary>끝점을 이은 경계에 자기 교차나 겹치는 선분이 없는지 확인합니다.</summary>
        private bool IsSimpleBoundary(Loop loop)
        {
            for (int i = 0; i < loop.Nodes.Count; i++)
            {
                Vector2 a = ProjectOntoCutPlane(m_topologyVertices[loop.Nodes[i]].Pose);
                Vector2 b = ProjectOntoCutPlane(m_topologyVertices[loop.Nodes[(i + 1) % loop.Nodes.Count]].Pose);

                for (int j = i + 2; j < loop.Nodes.Count; j++)
                {
                    if (i == 0 && j == loop.Nodes.Count - 1)
                    {
                        continue;
                    }

                    Vector2 c = ProjectOntoCutPlane(m_topologyVertices[loop.Nodes[j]].Pose);
                    Vector2 d = ProjectOntoCutPlane(m_topologyVertices[loop.Nodes[(j + 1) % loop.Nodes.Count]].Pose);
                    if (BoundarySegmentsIntersect(a, b, c, d))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>절단 평면의 두 선분이 허용 오차 안에서 교차하거나 겹치는지 판정합니다.</summary>
        private bool BoundarySegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float tolerance = m_settings.Tolerance;
            if (Mathf.Max(a.x, b.x) < Mathf.Min(c.x, d.x) - tolerance
                || Mathf.Max(c.x, d.x) < Mathf.Min(a.x, b.x) - tolerance
                || Mathf.Max(a.y, b.y) < Mathf.Min(c.y, d.y) - tolerance
                || Mathf.Max(c.y, d.y) < Mathf.Min(a.y, b.y) - tolerance)
            {
                return false;
            }

            float epsilon = tolerance * Mathf.Max((b - a).magnitude, (d - c).magnitude);
            float firstC = BoundaryOrientation(a, b, c);
            float firstD = BoundaryOrientation(a, b, d);
            float secondA = BoundaryOrientation(c, d, a);
            float secondB = BoundaryOrientation(c, d, b);

            return (Mathf.Abs(firstC) <= epsilon || Mathf.Abs(firstD) <= epsilon
                    || (firstC > 0f) != (firstD > 0f))
                && (Mathf.Abs(secondA) <= epsilon || Mathf.Abs(secondB) <= epsilon
                    || (secondA > 0f) != (secondB > 0f));
        }

        /// <summary>평면의 세 점이 이루는 회전 방향을 부호 있는 면적으로 판정합니다.</summary>
        private static float BoundaryOrientation(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }
    }
}
