using System.Collections.Generic;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshCutEngine
    {
        private readonly List<Loop> m_openContours = new List<Loop>();

        /// <summary>원본 표면의 연결 파츠를 판별할 모든 닫힌 경계와 열린 절단선을 반환합니다.</summary>
        private IEnumerable<Loop> CutContourCandidates()
        {
            foreach (Loop loop in m_closedLoops)
            {
                yield return loop;
            }

            foreach (Loop contour in m_openContours)
            {
                yield return contour;
            }
        }

        /// <summary>같은 3차원 원본 파츠에서 나온 열린 절단선만 묶어 파츠의 전체 단면을 닫습니다.</summary>
        private void CompletePartContours()
        {
            Dictionary<int, List<Loop>> parts = new Dictionary<int, List<Loop>>();
            foreach (Loop contour in m_openContours)
            {
                if (!parts.TryGetValue(contour.SourceComponent, out List<Loop> contours))
                {
                    contours = new List<Loop>();
                    parts.Add(contour.SourceComponent, contours);
                }

                contours.Add(contour);
            }

            foreach (List<Loop> contours in parts.Values)
            {
                if (TryCompletePartContour(contours, out Loop completed))
                {
                    m_closedLoops.Add(completed);
                    continue;
                }

                // 복잡한 파츠를 하나의 단순 윤곽으로 닫을 수 없으면 기존의 개별 캡 가능 경계만 보존한다.
                foreach (Loop contour in contours)
                {
                    if (IsValidPartContour(contour))
                    {
                        m_closedLoops.Add(contour);
                    }
                }
            }
        }

        /// <summary>원본 파츠의 여러 절단선을 가까운 끝점끼리 이어 교차하지 않는 최소 연결 윤곽을 찾습니다.</summary>
        private bool TryCompletePartContour(List<Loop> contours, out Loop completed)
        {
            completed = null;
            float bestDistance = float.PositiveInfinity;
            for (int start = 0; start < contours.Count; start++)
            {
                for (int reverse = 0; reverse < 2; reverse++)
                {
                    bool[] used = new bool[contours.Count];
                    used[start] = true;
                    List<int> nodes = new List<int>(contours[start].Nodes);
                    if (reverse != 0)
                    {
                        nodes.Reverse();
                    }

                    float distance = 0f;
                    for (int step = 1; step < contours.Count; step++)
                    {
                        int next = -1;
                        bool reverseNext = false;
                        float nearest = float.PositiveInfinity;
                        Vector3 end = m_topologyVertices[nodes[nodes.Count - 1]].Pose;
                        for (int i = 0; i < contours.Count; i++)
                        {
                            if (used[i])
                            {
                                continue;
                            }

                            List<int> candidate = contours[i].Nodes;
                            float first = Vector3.Distance(end, m_topologyVertices[candidate[0]].Pose);
                            float last = Vector3.Distance(end, m_topologyVertices[candidate[candidate.Count - 1]].Pose);
                            float gap = Mathf.Min(first, last);
                            if (gap < nearest)
                            {
                                nearest = gap;
                                next = i;
                                reverseNext = last < first;
                            }
                        }

                        List<int> nextNodes = new List<int>(contours[next].Nodes);
                        if (reverseNext)
                        {
                            nextNodes.Reverse();
                        }

                        nodes.AddRange(nextNodes);
                        used[next] = true;
                        distance += nearest;
                    }

                    distance += Vector3.Distance(
                        m_topologyVertices[nodes[nodes.Count - 1]].Pose, m_topologyVertices[nodes[0]].Pose);
                    Loop candidateLoop = new Loop(nodes) { SourceComponent = contours[start].SourceComponent };
                    if (distance < bestDistance && IsValidPartContour(candidateLoop))
                    {
                        completed = candidateLoop;
                        bestDistance = distance;
                    }
                }
            }

            return completed != null;
        }

        /// <summary>묶은 파츠 윤곽이 실제 면적을 가지며 자기 교차하지 않는지 확인합니다.</summary>
        private bool IsValidPartContour(Loop contour)
        {
            return contour.Nodes.Count >= 3
                && Mathf.Abs(LoopArea(contour)) > m_settings.Tolerance * m_settings.Tolerance
                && IsSimpleBoundary(contour);
        }
    }
}

