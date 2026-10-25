using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshCutEngine
    {
        public sealed class Settings
        {
            public Vector3 Origin { get; set; }
            public Quaternion Rotation { get; set; } = Quaternion.identity;
            public Vector2 Size { get; set; } = new Vector2(10f, 10f);
            public float Tolerance { get; set; } = 0.00001f;
            public bool RepairOpenBoundaries { get; set; }
            public float MaximumGapDistance { get; set; } = 0.005f;
            public bool GenerateCap { get; set; } = true;
            public bool CloseOpenBoundaries { get; set; }
            public bool DetachPositive { get; set; } = true;
            public HashSet<int> TargetBones { get; set; }
            public float MinimumBoneWeight { get; set; } = 0.01f;
            public float UvScale { get; set; } = 1f;
            public float UvRotation { get; set; }
            public Vector2 UvOffset { get; set; }
            public float UvLayoutScale { get; set; } = 1f;
            public SkinnedMeshCutUvTransform[] UvIslandTransforms { get; set; }
            public bool PackUv { get; set; }
            public SkinnedMeshCutMaskMode[] TriangleMask { get; set; }
        }

        public sealed class Result : IDisposable
        {
            public Mesh Part { get; }
            public Mesh Remainder { get; }
            public Vector3[] Boundary { get; }
            public int LoopCount { get; }
            public Vector3[] RepairedBoundary { get; }
            public int RepairCount => RepairedBoundary.Length / 2;
            public float MaximumRepairedGap { get; }
            public SkinnedMeshCutUvPreview CapUvs { get; }

            /// <summary>생성된 양쪽 메시와 경계 및 캡 UV 정보를 보관합니다.</summary>
            public Result(
                Mesh part,
                Mesh remainder,
                Vector3[] boundary,
                int loopCount,
                Vector3[] repairedBoundary,
                float maximumRepairedGap,
                SkinnedMeshCutUvPreview capUvs)
            {
                Part = part;
                Remainder = remainder;
                Boundary = boundary;
                LoopCount = loopCount;
                RepairedBoundary = repairedBoundary;
                MaximumRepairedGap = maximumRepairedGap;
                CapUvs = capUvs;
            }

            /// <summary>이 객체가 소유한 임시 메시와 편집 자원을 해제합니다.</summary>
            public void Dispose()
            {
                if (Part != null && !UnityEditor.AssetDatabase.Contains(Part))
                {
                    UnityEngine.Object.DestroyImmediate(Part);
                }

                if (Remainder != null && !UnityEditor.AssetDatabase.Contains(Remainder))
                {
                    UnityEngine.Object.DestroyImmediate(Remainder);
                }
            }
        }

        private readonly struct Edge : IEquatable<Edge>
        {
            public int A { get; }
            public int B { get; }

            /// <summary>양 끝점의 순서와 무관하게 비교할 모서리를 만듭니다.</summary>
            public Edge(int a, int b)
            {
                A = Mathf.Min(a, b);
                B = Mathf.Max(a, b);
            }

            /// <summary>동일한 끝점으로 구성된 모서리인지 비교합니다.</summary>
            public bool Equals(Edge other)
            {
                return A == other.A && B == other.B;
            }

            /// <summary>동일한 끝점으로 구성된 모서리인지 비교합니다.</summary>
            public override bool Equals(object value)
            {
                return value is Edge edge && Equals(edge);
            }

            /// <summary>모서리를 사전에서 조회할 해시를 반환합니다.</summary>
            public override int GetHashCode()
            {
                return unchecked(A * 397 ^ B);
            }
        }

        private sealed class Segment
        {
            public Edge Edge { get; }
            public List<int> Triangles { get; } = new List<int>();
            public bool Selected { get; set; }

            /// <summary>교차 모서리와 이를 공유하는 원본 삼각형을 보관합니다.</summary>
            public Segment(Edge edge)
            {
                Edge = edge;
            }
        }

        private sealed class Loop
        {
            public List<int> Nodes { get; }
            public int SourceComponent { get; set; }

            /// <summary>캡 후보인 닫힌 경계의 정점 순서를 보관합니다.</summary>
            public Loop(List<int> nodes)
            {
                Nodes = nodes;
            }
        }

        private sealed class Triangle
        {
            public Vertex[] Vertices { get; }
            public int Submesh { get; }
            public SkinnedMeshCutMaskMode Mask { get; set; }

            /// <summary>원본 삼각형의 정점·서브메시·마스크를 보관합니다.</summary>
            public Triangle(
                Vertex a,
                Vertex b,
                Vertex c,
                int submesh,
                SkinnedMeshCutMaskMode mask = SkinnedMeshCutMaskMode.Auto)
            {
                Vertices = new[]
                {
                    a,
                    b,
                    c
                };
                Submesh = submesh;
                Mask = mask;
            }
        }

        private sealed class Polygon
        {
            public List<Vertex> Vertices { get; }
            public int Submesh { get; }
            public int Side { get; }
            public int Triangle { get; }

            /// <summary>클리핑 결과의 정점과 원본 표면 및 평면 방향을 보관합니다.</summary>
            public Polygon(List<Vertex> vertices, int submesh, int side, int triangle)
            {
                Vertices = vertices;
                Submesh = submesh;
                Side = side;
                Triangle = triangle;
            }
        }

        private readonly struct SurfaceOwner
        {
            public int Polygon { get; }
            public Vertex A { get; }
            public Vertex B { get; }

            /// <summary>모서리를 공유하는 출력 다각형과 원본 정점을 연결합니다.</summary>
            public SurfaceOwner(int polygon, Vertex a, Vertex b)
            {
                Polygon = polygon;
                A = a;
                B = b;
            }
        }

        private sealed class DisjointSet
        {
            private readonly int[] m_parents;

            /// <summary>지정한 개수의 독립 연결 집합을 만듭니다.</summary>
            public DisjointSet(int count)
            {
                m_parents = new int[count];

                for (int i = 0; i < count; i++)
                {
                    m_parents[i] = i;
                }
            }

            /// <summary>경로를 압축하며 연결 집합의 대표 인덱스를 찾습니다.</summary>
            public int Find(int value)
            {
                while (m_parents[value] != value)
                {
                    m_parents[value] = m_parents[m_parents[value]];
                    value = m_parents[value];
                }

                return value;
            }

            /// <summary>두 인덱스가 속한 연결 집합을 병합합니다.</summary>
            public void Join(int a, int b)
            {
                m_parents[Find(a)] = Find(b);
            }
        }
    }
}
