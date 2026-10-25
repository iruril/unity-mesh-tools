using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshCutBrushSurface
    {
        private struct PickNode
        {
            public Bounds Bounds;
            public int Start;
            public int Count;
            public int Left;
            public int Right;
        }

        private Bounds[] m_triangleBounds;
        private PickNode[] m_pickNodes;
        private int[] m_pickOrder;
        private int[] m_pickStack;
        private int[] m_queryStack;

        /// <summary>현재 자세에서 피킹 트리의 경계 상자를 갱신합니다.</summary>
        private void RefreshPickingBounds()
        {
            if (m_triangleBounds == null)
            {
                m_triangleBounds = new Bounds[TriangleCount];
            }

            for (int i = 0; i < TriangleCount; i++)
            {
                Bounds bounds = new Bounds(WorldVertices[Triangles[i * 3]], Vector3.zero);
                bounds.Encapsulate(WorldVertices[Triangles[i * 3 + 1]]);
                bounds.Encapsulate(WorldVertices[Triangles[i * 3 + 2]]);
                bounds.Expand(m_visibilityTolerance * 2f);
                m_triangleBounds[i] = bounds;
            }

            if (m_pickNodes == null)
            {
                return;
            }

            // 본이 움직여도 토폴로지는 유지되므로 트리를 다시 만들지 않고 경계만 갱신한다.
            for (int i = m_pickNodes.Length - 1; i >= 0; i--)
            {
                PickNode node = m_pickNodes[i];

                if (node.Count == 0)
                {
                    node.Bounds = m_pickNodes[node.Left].Bounds;
                    node.Bounds.Encapsulate(m_pickNodes[node.Right].Bounds);
                }
                else
                {
                    node.Bounds = PickingBounds(node.Start, node.Count);
                }

                m_pickNodes[i] = node;
            }
        }

        /// <summary>삼각형 범위를 포함하는 피킹 경계 상자를 구합니다.</summary>
        private Bounds PickingBounds(int start, int count)
        {
            Bounds bounds = m_triangleBounds[m_pickOrder[start]];

            for (int i = 1; i < count; i++)
            {
                bounds.Encapsulate(m_triangleBounds[m_pickOrder[start + i]]);
            }

            return bounds;
        }

        /// <summary>삼각형 피킹 트리가 없으면 생성합니다.</summary>
        private void EnsurePickingTree()
        {
            if (m_pickNodes != null)
            {
                return;
            }

            m_pickOrder = new int[TriangleCount];

            for (int i = 0; i < TriangleCount; i++)
            {
                m_pickOrder[i] = i;
            }

            var nodes = new List<PickNode>();
            BuildPickingNode(nodes, 0, TriangleCount);
            m_pickNodes = nodes.ToArray();
            m_pickStack = new int[m_pickNodes.Length];
            m_queryStack = new int[m_pickNodes.Length];
        }

        /// <summary>삼각형 범위를 분할해 피킹 트리 노드를 구성합니다.</summary>
        private int BuildPickingNode(List<PickNode> nodes, int start, int count)
        {
            int index = nodes.Count;
            PickNode node = new PickNode
            {
                Start = start,
                Count = count,
                Bounds = PickingBounds(start, count)
            };
            nodes.Add(node);

            if (count > 8)
            {
                Vector3 size = node.Bounds.size;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Array.Sort(
                    m_pickOrder,
                    start,
                    count,
                    Comparer<int>.Create((a, b) => m_triangleBounds[a].center[axis].CompareTo(m_triangleBounds[b].center[axis])));
                int half = count / 2;
                node.Left = BuildPickingNode(nodes, start, half);
                node.Right = BuildPickingNode(nodes, start + half, count - half);
                node.Count = 0;
                nodes[index] = node;
            }

            return index;
        }
    }
}

