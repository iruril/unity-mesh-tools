using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshCutEngine
    {
        /// <summary>선택한 경계를 지나는 삼각형을 평면 양쪽의 다각형으로 나눕니다.</summary>
        private List<Polygon> BuildClippedPolygons()
        {
            HashSet<int> selected = new HashSet<int>();

            foreach (Segment segment in m_cutSegments.Values)
            {
                if (segment.Selected)
                {
                    selected.UnionWith(segment.Triangles);
                }
            }

            List<Polygon> polygons = new List<Polygon>();

            for (int i = 0; i < m_triangles.Count; i++)
            {
                Triangle triangle = m_triangles[i];

                if (IsCoplanarTriangle(triangle))
                {
                    // 공면을 양쪽으로 복제하면 겹치는 표면이 생기므로 원본 삼각형은 한 번만 보존한다.
                    // 사용자가 지정한 귀속 마스크가 있으면 기존 연결 성분의 마스크 판단을 우선한다.
                    int side = IsProtected(triangle.Mask) ? 0 : CoplanarTriangleSide(triangle);
                    polygons.Add(new Polygon(new List<Vertex>(triangle.Vertices), triangle.Submesh, side, i));
                    continue;
                }

                if (!selected.Contains(i))
                {
                    polygons.Add(new Polygon(new List<Vertex>(triangle.Vertices), triangle.Submesh, 0, i));
                    continue;
                }

                for (int side = -1; side <= 1; side += 2)
                {
                    List<Vertex> clipped = new List<Vertex>(4);

                    for (int edge = 0; edge < 3; edge++)
                    {
                        Vertex a = triangle.Vertices[edge];
                        Vertex b = triangle.Vertices[(edge + 1) % 3];
                        float da = SignedDistanceToPlane(a) * side;
                        float db = SignedDistanceToPlane(b) * side;

                        if (da >= 0f)
                        {
                            clipped.Add(a);
                        }

                        if (da * db < 0f)
                        {
                            clipped.Add(Intersection(a, b));
                        }
                    }

                    if (clipped.Count >= 3 && PolygonArea(clipped) > m_settings.Tolerance * m_settings.Tolerance)
                    {
                        polygons.Add(new Polygon(clipped, triangle.Submesh, side, i));
                    }
                }
            }

            return polygons;
        }

        /// <summary>다각형을 구성하는 삼각형들의 면적을 합산합니다.</summary>
        private static float PolygonArea(List<Vertex> vertices)
        {
            Vector3 sum = Vector3.zero;

            for (int i = 1; i < vertices.Count - 1; i++)
            {
                sum += Vector3.Cross(vertices[i].Pose - vertices[0].Pose, vertices[i + 1].Pose - vertices[0].Pose);
            }

            return sum.magnitude * 0.5f;
        }

        /// <summary>절단 경계를 제외한 연결성과 마스크로 다각형의 귀속을 결정합니다.</summary>
        private bool[] ClassifyOutputPolygons(List<Polygon> polygons)
        {
            DisjointSet cut = new DisjointSet(polygons.Count);
            DisjointSet original = new DisjointSet(polygons.Count);
            Dictionary<Edge, List<SurfaceOwner>> owners = new Dictionary<Edge, List<SurfaceOwner>>();
            int[] triangleOwners = new int[m_triangles.Count];

            for (int i = 0; i < polygons.Count; i++)
            {
                Polygon polygon = polygons[i];
                triangleOwners[polygon.Triangle] = i;

                for (int j = 0; j < polygon.Vertices.Count; j++)
                {
                    Vertex a = polygon.Vertices[j];
                    Vertex b = polygon.Vertices[(j + 1) % polygon.Vertices.Count];
                    int nodeA = GetOrCreateTopologyVertex(a);
                    Edge edge = new Edge(nodeA, GetOrCreateTopologyVertex(b));
                    SurfaceOwner owner = new SurfaceOwner(i, nodeA == edge.A ? a : b, nodeA == edge.A ? b : a);

                    if (!owners.TryGetValue(edge, out List<SurfaceOwner> list))
                    {
                        list = new List<SurfaceOwner>();
                        owners.Add(edge, list);
                    }

                    foreach (SurfaceOwner other in list)
                    {
                        if (!CompatibleWeights(owner.A.Weights, other.A.Weights)
                            || !CompatibleWeights(owner.B.Weights, other.B.Weights))
                        {
                            continue;
                        }

                        original.Join(i, other.Polygon);
                        bool barrier = m_cutSegments.TryGetValue(edge, out Segment segment) && segment.Selected;

                        // 앞뒷면이 포개진 공면은 교차 선분이 없어도 서로 다른 결과에 남아야 한다.
                        // 그렇지 않으면 이미 분리한 양쪽 표면이 공면의 내부 모서리로 다시 연결된다.
                        bool coplanarSeam = polygon.Side != 0 && polygons[other.Polygon].Side != 0
                            && SignedDistanceToPlane(a) == 0f && SignedDistanceToPlane(b) == 0f;
                        barrier |= coplanarSeam;

                        if (!barrier || polygons[other.Polygon].Side == polygon.Side)
                        {
                            cut.Join(i, other.Polygon);
                        }
                    }

                    list.Add(owner);
                }
            }

            Dictionary<int, int> signs = new Dictionary<int, int>();

            for (int i = 0; i < polygons.Count; i++)
            {
                if (polygons[i].Side == 0)
                {
                    continue;
                }

                int component = cut.Find(i);

                if (signs.TryGetValue(component, out int sign) && sign != polygons[i].Side)
                {
                    throw new InvalidOperationException("절단 후에도 양쪽이 연결되어 있습니다. 대상 범위를 확인하세요.");
                }

                signs[component] = polygons[i].Side;
            }

            Dictionary<int, bool> ownership = new Dictionary<int, bool>();

            for (int i = 0; i < polygons.Count; i++)
            {
                SkinnedMeshCutMaskMode mode = m_triangles[polygons[i].Triangle].Mask;

                if (!IsProtected(mode))
                {
                    continue;
                }

                int component = cut.Find(i);
                bool keepPart = mode == SkinnedMeshCutMaskMode.KeepPart;

                if (ownership.TryGetValue(component, out bool existing) && existing != keepPart)
                {
                    throw new InvalidOperationException("연결된 표면에 파츠 포함과 몸체 유지 마스크가 충돌합니다. " + "연결된 메시를 같은 모드로 지정하거나 분리 경계를 조정하세요.");
                }

                if (signs.TryGetValue(component, out int sign)
                    && keepPart != (sign == (m_settings.DetachPositive ? 1 : -1)))
                {
                    throw new InvalidOperationException("브러시의 파츠 귀속이 절단면 방향과 충돌합니다. " + "해당 표면의 마스크 또는 절단 경계를 조정하세요.");
                }

                ownership[component] = keepPart;
            }

            foreach (Loop loop in CutContourCandidates())
            {
                Segment segment = m_cutSegments[new Edge(loop.Nodes[0], loop.Nodes[1])];
                loop.SourceComponent = original.Find(triangleOwners[segment.Triangles[0]]);
            }

            bool[] result = new bool[polygons.Count];
            bool hasPart = false;
            bool hasRemainder = false;

            for (int i = 0; i < result.Length; i++)
            {
                int component = cut.Find(i);
                result[i] = ownership.TryGetValue(component, out bool keepPart)
                    ? keepPart
                    : signs.TryGetValue(component, out int sign) && sign == (m_settings.DetachPositive ? 1 : -1);
                hasPart |= result[i];
                hasRemainder |= !result[i];
            }

            if (!hasPart || !hasRemainder)
            {
                throw new InvalidOperationException("파츠와 남은 몸체를 모두 생성할 수 있는 위치를 지정하세요.");
            }

            return result;
        }

        /// <summary>같은 위치의 정점을 연결할 수 있도록 본 가중치를 비교합니다.</summary>
        private static bool CompatibleWeights(BoneWeight1[] a, BoneWeight1[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            foreach (BoneWeight1 weight in a)
            {
                bool matched = false;

                foreach (BoneWeight1 other in b)
                {
                    if (other.boneIndex == weight.boneIndex
                        && Mathf.Abs(other.weight - weight.weight) <= 0.000001f)
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
