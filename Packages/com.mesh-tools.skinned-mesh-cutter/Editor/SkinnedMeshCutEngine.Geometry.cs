using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshCutEngine
    {
        /// <summary>작성 포즈의 정점에서 절단 평면까지 부호 있는 거리를 구합니다.</summary>
        private float SignedDistanceToPlane(Vertex vertex)
        {
            float distance = Vector3.Dot(vertex.Pose - m_settings.Origin, m_planeNormal);

            return Mathf.Abs(distance) <= m_settings.Tolerance ? 0f : distance;
        }

        /// <summary>월드 위치를 절단 평면의 오른쪽·위쪽 좌표로 투영합니다.</summary>
        private Vector2 ProjectOntoCutPlane(Vector3 position)
        {
            Vector3 delta = position - m_settings.Origin;

            return new Vector2(Vector3.Dot(delta, m_planeRight), Vector3.Dot(delta, m_planeUp));
        }

        /// <summary>허용 오차 안의 정점을 공유하는 위상 인덱스를 찾거나 만듭니다.</summary>
        private int GetOrCreateTopologyVertex(Vertex vertex)
        {
            float tolerance = m_settings.Tolerance;
            Vector3Int cell = Vector3Int.RoundToInt(vertex.Pose / tolerance);

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        Vector3Int neighbor = cell + new Vector3Int(x, y, z);

                        if (!m_vertexWeldGrid.TryGetValue(neighbor, out List<int> candidates))
                        {
                            continue;
                        }

                        foreach (int candidate in candidates)
                        {
                            if ((m_topologyVertices[candidate].Pose - vertex.Pose).sqrMagnitude <= tolerance * tolerance)
                            {
                                return candidate;
                            }
                        }
                    }
                }
            }

            int index = m_topologyVertices.Count;
            m_topologyVertices.Add(vertex);

            if (!m_vertexWeldGrid.TryGetValue(cell, out List<int> bucket))
            {
                bucket = new List<int>();
                m_vertexWeldGrid.Add(cell, bucket);
            }

            bucket.Add(index);

            return index;
        }

        /// <summary>원본 모서리와 절단 평면의 교점을 보간하고 캐시에 보관합니다.</summary>
        private Vertex Intersection(Vertex a, Vertex b)
        {
            float da = SignedDistanceToPlane(a);
            float db = SignedDistanceToPlane(b);

            if (da == 0f)
            {
                return a;
            }

            if (db == 0f)
            {
                return b;
            }

            Edge key = new Edge(a.Id, b.Id);

            if (!m_edgeIntersections.TryGetValue(key, out Vertex vertex))
            {
                vertex = Interpolate(a, b, da / (da - db));
                m_edgeIntersections.Add(key, vertex);
            }

            return vertex;
        }

        /// <summary>삼각형의 모든 정점이 허용 오차 안에서 절단 평면 위에 있는지 확인합니다.</summary>
        private bool IsCoplanarTriangle(Triangle triangle)
        {
            return SignedDistanceToPlane(triangle.Vertices[0]) == 0f
                && SignedDistanceToPlane(triangle.Vertices[1]) == 0f
                && SignedDistanceToPlane(triangle.Vertices[2]) == 0f;
        }

        /// <summary>평면과 겹친 원본 면을 그 앞면의 반대쪽 결과에 배정합니다.</summary>
        private int CoplanarTriangleSide(Triangle triangle)
        {
            Vector3 a = triangle.Vertices[0].Pose;
            Vector3 b = triangle.Vertices[1].Pose;
            Vector3 c = triangle.Vertices[2].Pose;
            float handedness = m_source.transform.localToWorldMatrix.determinant < 0f ? -1f : 1f;
            float facing = Vector3.Dot(Vector3.Cross(b - a, c - a), m_planeNormal) * handedness;

            return facing >= 0f ? -1 : 1;
        }

        /// <summary>보호되지 않은 삼각형에서 절단 평면과의 교차 선분을 수집합니다.</summary>
        private void FindIntersections()
        {
            for (int triangleIndex = 0; triangleIndex < m_triangles.Count; triangleIndex++)
            {
                Triangle triangle = m_triangles[triangleIndex];

                if (IsProtected(triangle.Mask))
                {
                    continue;
                }

                List<Vertex> points = new List<Vertex>(3);

                for (int i = 0; i < 3; i++)
                {
                    Vertex a = triangle.Vertices[i];
                    Vertex b = triangle.Vertices[(i + 1) % 3];
                    float da = SignedDistanceToPlane(a);
                    float db = SignedDistanceToPlane(b);
                    Vertex point = null;

                    if (da == 0f)
                    {
                        point = a;
                    }
                    else if (da * db < 0f)
                    {
                        point = Intersection(a, b);
                    }

                    if (point != null
                        && !points.Exists(p => GetOrCreateTopologyVertex(p) == GetOrCreateTopologyVertex(point)))
                    {
                        points.Add(point);
                    }
                }

                if (points.Count == 3)
                {
                    // 면 전체가 평면 위에 있으면 교차 선분으로 취급하지 않는다.
                    // 표면 분류 단계에서 원래 감김 방향과 보호 마스크로 한쪽에만 배정한다.
                    continue;
                }

                if (points.Count != 2)
                {
                    continue;
                }

                Edge edge = new Edge(GetOrCreateTopologyVertex(points[0]), GetOrCreateTopologyVertex(points[1]));

                if (edge.A == edge.B)
                {
                    continue;
                }

                if (!m_cutSegments.TryGetValue(edge, out Segment segment))
                {
                    segment = new Segment(edge);
                    m_cutSegments.Add(edge, segment);
                }

                segment.Triangles.Add(triangleIndex);
            }
        }
    }
}
