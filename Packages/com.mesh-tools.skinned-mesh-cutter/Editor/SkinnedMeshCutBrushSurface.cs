using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>원본 스킨을 수정하지 않고 현재 포즈에서 브러시 선택과 연결 영역을 계산합니다.</summary>
    public sealed partial class SkinnedMeshCutBrushSurface : IDisposable
    {
        private readonly SkinnedMeshRenderer m_source;
        private readonly Mesh m_baked;
        private readonly int[][] m_connected;
        private float m_visibilityTolerance;
        private readonly List<Vector3> m_poseVertices = new List<Vector3>();
        private Transform[] m_poseBones;
        private Matrix4x4[] m_boneMatrices;
        private float[] m_blendWeights;
        private Matrix4x4 m_sourceMatrix;
        private int m_meshDirtyCount;
        public int PoseVersion { get; private set; }
        public Mesh SourceMesh { get; }
        public int TriangleCount => Triangles.Length / 3;
        public int TopologyHash { get; }
        public Vector3[] WorldVertices { get; private set; }
        public int[] Triangles { get; }
        public bool[] HiddenTriangles { get; }
        public Mesh PoseMesh => m_baked;

        /// <summary>원본 스킨의 삼각형 연결 정보와 브러시 판정 자원을 준비합니다.</summary>
        public SkinnedMeshCutBrushSurface(SkinnedMeshRenderer source)
        {
            if (source == null || source.sharedMesh == null || !source.sharedMesh.isReadable)
            {
                throw new InvalidOperationException("브러시에는 읽기 가능한 스킨 메시가 필요합니다.");
            }

            m_source = source;
            SourceMesh = source.sharedMesh;
            List<int> triangles = new List<int>();

            for (int submesh = 0; submesh < SourceMesh.subMeshCount; submesh++)
            {
                if (SourceMesh.GetTopology(submesh) != MeshTopology.Triangles)
                {
                    throw new InvalidOperationException("브러시는 삼각형 메시만 지원합니다.");
                }

                triangles.AddRange(SourceMesh.GetTriangles(submesh));
            }

            Triangles = triangles.ToArray();
            HiddenTriangles = new bool[TriangleCount];
            TopologyHash = GetTopologyHash(SourceMesh);
            m_connected = BuildConnectedTriangles();
            m_baked = new Mesh
            {
                name = "Skinned cut brush pose",
                hideFlags = HideFlags.HideAndDontSave
            };

            try
            {
                RefreshPose();
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(m_baked);
                throw;
            }
        }

        /// <summary>메시의 정점과 인덱스 구조를 비교할 해시를 계산합니다.</summary>
        public static int GetTopologyHash(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable)
            {
                return 0;
            }

            unchecked
            {
                int hash = mesh.vertexCount * 397 ^ mesh.subMeshCount;

                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    hash = hash * 397 ^ (int)mesh.GetTopology(submesh);
                    int[] indices = mesh.GetIndices(submesh);
                    hash = hash * 397 ^ indices.Length;

                    foreach (int index in indices)
                    {
                        hash = hash * 397 ^ index;
                    }
                }

                return hash;
            }
        }

        /// <summary>본·블렌드셰이프·소스 변환이 달라진 경우에만 포즈를 다시 베이크합니다.</summary>
        public bool RefreshPoseIfChanged()
        {
            if (m_source == null || m_source.sharedMesh != SourceMesh)
            {
                throw new InvalidOperationException("브러시 소스 메시가 변경되었습니다. 선택 정보를 다시 생성하세요.");
            }

            Transform[] bones = m_source.bones;
            bool changed = m_poseBones == null || bones.Length != m_poseBones.Length
                || !m_sourceMatrix.Equals(m_source.transform.localToWorldMatrix)
                || m_meshDirtyCount != EditorUtility.GetDirtyCount(SourceMesh)
                || m_blendWeights.Length != SourceMesh.blendShapeCount;

            for (int i = 0; !changed && i < bones.Length; i++)
            {
                Matrix4x4 matrix = bones[i] != null ? bones[i].localToWorldMatrix : Matrix4x4.identity;
                changed = bones[i] != m_poseBones[i] || !matrix.Equals(m_boneMatrices[i]);
            }

            for (int i = 0; !changed && i < m_blendWeights.Length; i++)
            {
                changed = m_source.GetBlendShapeWeight(i) != m_blendWeights[i];
            }

            if (!changed)
            {
                return false;
            }

            RefreshPose();
            return true;
        }

        /// <summary>현재 본 자세로 베이크한 정점과 피킹 범위를 강제로 갱신합니다.</summary>
        public void RefreshPose()
        {
            if (m_source == null || m_source.sharedMesh != SourceMesh)
            {
                throw new InvalidOperationException("브러시 소스 메시가 변경되었습니다. 선택 정보를 다시 생성하세요.");
            }

            m_source.BakeMesh(m_baked, true);
            m_baked.GetVertices(m_poseVertices);

            if (WorldVertices == null || WorldVertices.Length != m_poseVertices.Count)
            {
                WorldVertices = new Vector3[m_poseVertices.Count];
            }

            Bounds bounds = new Bounds();
            m_sourceMatrix = m_source.transform.localToWorldMatrix;

            for (int i = 0; i < m_poseVertices.Count; i++)
            {
                Vector3 point = m_sourceMatrix.MultiplyPoint3x4(m_poseVertices[i]);
                WorldVertices[i] = point;

                if (i == 0)
                {
                    bounds = new Bounds(point, Vector3.zero);
                }
                else
                {
                    bounds.Encapsulate(point);
                }
            }

            // 입력 이벤트마다 전체 메시를 베이크하지 않도록 실제 포즈 상태를 기록한다.
            m_poseBones = m_source.bones;
            m_boneMatrices = new Matrix4x4[m_poseBones.Length];
            for (int i = 0; i < m_poseBones.Length; i++)
            {
                m_boneMatrices[i] = m_poseBones[i] != null
                    ? m_poseBones[i].localToWorldMatrix : Matrix4x4.identity;
            }

            m_blendWeights = new float[SourceMesh.blendShapeCount];
            for (int i = 0; i < m_blendWeights.Length; i++)
            {
                m_blendWeights[i] = m_source.GetBlendShapeWeight(i);
            }

            m_meshDirtyCount = EditorUtility.GetDirtyCount(SourceMesh);
            m_visibilityTolerance = Mathf.Max(bounds.size.magnitude * 0.000001f, 0.000001f);
            RefreshPickingBounds();
            PoseVersion++;
        }

        /// <summary>화면 광선과 처음 만나는 표시 중인 삼각형을 찾습니다.</summary>
        public bool Raycast(Ray ray, out int triangle, out Vector3 hit, out Vector3 normal)
        {
            triangle = -1;
            hit = Vector3.zero;
            normal = Vector3.zero;

            if (TriangleCount == 0)
            {
                return false;
            }

            float closest = float.PositiveInfinity;
            EnsurePickingTree();
            int stackSize = 0;
            m_pickStack[stackSize++] = 0;

            while (stackSize > 0)
            {
                PickNode node = m_pickNodes[m_pickStack[--stackSize]];

                if (!node.Bounds.IntersectRay(ray, out float entry) || entry > closest)
                {
                    continue;
                }

                if (node.Count == 0)
                {
                    m_pickStack[stackSize++] = node.Left;
                    m_pickStack[stackSize++] = node.Right;
                    continue;
                }

                for (int candidate = node.Start; candidate < node.Start + node.Count; candidate++)
                {
                    int i = m_pickOrder[candidate];

                    if (HiddenTriangles[i])
                    {
                        continue;
                    }

                    Vector3 a = WorldVertices[Triangles[i * 3]];
                    Vector3 b = WorldVertices[Triangles[i * 3 + 1]];
                    Vector3 c = WorldVertices[Triangles[i * 3 + 2]];
                    Vector3 ab = b - a;
                    Vector3 ac = c - a;
                    Vector3 cross = Vector3.Cross(ray.direction, ac);
                    float determinant = Vector3.Dot(ab, cross);

                    if (Mathf.Abs(determinant) <= 0.000000000001f)
                    {
                        continue;
                    }

                    float inverse = 1f / determinant;
                    Vector3 offset = ray.origin - a;
                    float u = Vector3.Dot(offset, cross) * inverse;

                    if (u < -0.000001f || u > 1.000001f)
                    {
                        continue;
                    }

                    Vector3 q = Vector3.Cross(offset, ab);
                    float v = Vector3.Dot(ray.direction, q) * inverse;

                    if (v < -0.000001f || u + v > 1.000001f)
                    {
                        continue;
                    }

                    float distance = Vector3.Dot(ac, q) * inverse;

                    if (distance < 0f || distance > closest || (distance == closest && i >= triangle))
                    {
                        continue;
                    }

                    closest = distance;
                    triangle = i;
                    hit = ray.GetPoint(distance);
                    normal = Vector3.Cross(ab, ac).normalized;
                }
            }

            return triangle >= 0;
        }

        /// <summary>시작 삼각형과 연결된 표면의 삼각형을 반환합니다.</summary>
        public int[] GetConnectedTriangles(int seed)
        {
            return seed >= 0 && seed < TriangleCount
                ? Array.FindAll(m_connected[seed], triangle => !HiddenTriangles[triangle])
                : Array.Empty<int>();
        }

        /// <summary>편집 중 숨길 삼각형을 지정하고 표시 캐시를 갱신합니다.</summary>
        public bool SetHiddenTriangles(SkinnedMeshCutMaskMode[] mask, bool hidePainted)
        {
            return SetHiddenTriangles(mask, hidePainted, hidePainted, hidePainted);
        }

        /// <summary>편집 중 숨길 삼각형을 지정하고 표시 캐시를 갱신합니다.</summary>
        public bool SetHiddenTriangles(
            SkinnedMeshCutMaskMode[] mask,
            bool hideCut,
            bool hidePart,
            bool hideRemainder,
            bool hideUnassigned = false)
        {
            bool changed = false;
            bool unpainted = mask == null || mask.Length == 0;
            bool validMask = mask != null && mask.Length == TriangleCount;

            for (int i = 0; i < TriangleCount; i++)
            {
                SkinnedMeshCutMaskMode mode = validMask ? mask[i] : SkinnedMeshCutMaskMode.Auto;
                bool hidden = (validMask || unpainted)
                    && ((mode == SkinnedMeshCutMaskMode.Cut && hideCut)
                        || (mode == SkinnedMeshCutMaskMode.KeepPart && hidePart)
                        || (mode == SkinnedMeshCutMaskMode.KeepRemainder && hideRemainder)
                        || (mode == SkinnedMeshCutMaskMode.Auto && hideUnassigned));
                changed |= HiddenTriangles[i] != hidden;
                HiddenTriangles[i] = hidden;
            }

            return changed;
        }

        /// <summary>브러시 반경과 가시성 조건을 만족하는 삼각형을 수집합니다.</summary>
        public int[] GetBrushTriangles(int seed, Vector3 center, float radius, Camera camera, bool visibleOnly)
        {
            if (seed < 0 || seed >= TriangleCount || radius <= 0f || float.IsNaN(radius)
                || float.IsInfinity(radius))
            {
                return Array.Empty<int>();
            }

            List<int> result = new List<int>();
            float radiusSquared = radius * radius;

            EnsurePickingTree();
            int stackSize = 0;
            m_queryStack[stackSize++] = 0;

            while (stackSize > 0)
            {
                PickNode node = m_pickNodes[m_queryStack[--stackSize]];
                if (node.Bounds.SqrDistance(center) > radiusSquared)
                {
                    continue;
                }

                if (node.Count == 0)
                {
                    m_queryStack[stackSize++] = node.Left;
                    m_queryStack[stackSize++] = node.Right;
                    continue;
                }

                for (int candidate = node.Start; candidate < node.Start + node.Count; candidate++)
                {
                    int i = m_pickOrder[candidate];
                    if (HiddenTriangles[i] || m_triangleBounds[i].SqrDistance(center) > radiusSquared)
                    {
                        continue;
                    }

                    Vector3 point = ClosestTrianglePoint(
                        center,
                        WorldVertices[Triangles[i * 3]],
                        WorldVertices[Triangles[i * 3 + 1]],
                        WorldVertices[Triangles[i * 3 + 2]]);

                    if ((point - center).sqrMagnitude > radiusSquared
                        || (visibleOnly && camera != null && !VisibleFromCamera(point, camera)))
                    {
                        continue;
                    }

                    result.Add(i);
                }
            }

            // 가시성 광선은 별도 스택을 사용하므로 브러시 트리 순회를 덮어쓰지 않는다.
            result.Sort();
            return result.ToArray();
        }

        /// <summary>삼각형이 다른 표면에 가려지지 않았는지 확인합니다.</summary>
        private bool VisibleFromCamera(Vector3 point, Camera camera)
        {
            Vector3 viewport = camera.WorldToViewportPoint(point);

            if (viewport.z <= camera.nearClipPlane)
            {
                return false;
            }

            Ray ray = camera.ViewportPointToRay(viewport);

            return Raycast(ray, out int triangle, out Vector3 hit, out Vector3 normal)
                && (hit - point).sqrMagnitude <= m_visibilityTolerance * m_visibilityTolerance;
        }

        /// <summary>정점 위치와 본 가중치가 이어지는 삼각형을 연결합니다.</summary>
        private int[][] BuildConnectedTriangles()
        {
            int[] vertices = CanonicalVertices();
            int[] parents = new int[TriangleCount];
            Dictionary<Edge, int> owners = new Dictionary<Edge, int>();

            for (int triangle = 0; triangle < TriangleCount; triangle++)
            {
                parents[triangle] = triangle;

                for (int i = 0; i < 3; i++)
                {
                    Edge edge = new Edge(vertices[Triangles[triangle * 3 + i]], vertices[Triangles[triangle * 3 + (i + 1) % 3]]);

                    if (edge.A == edge.B)
                    {
                        continue;
                    }

                    if (owners.TryGetValue(edge, out int other))
                    {
                        parents[Find(parents, triangle)] = Find(parents, other);
                    }
                    else
                    {
                        owners.Add(edge, triangle);
                    }
                }
            }

            Dictionary<int, List<int>> groups = new Dictionary<int, List<int>>();

            for (int triangle = 0; triangle < TriangleCount; triangle++)
            {
                int root = Find(parents, triangle);

                if (!groups.TryGetValue(root, out List<int> group))
                {
                    group = new List<int>();
                    groups.Add(root, group);
                }

                group.Add(triangle);
            }

            int[][] connected = new int[TriangleCount][];

            foreach (List<int> group in groups.Values)
            {
                int[] indices = group.ToArray();

                foreach (int triangle in indices)
                {
                    connected[triangle] = indices;
                }
            }

            return connected;
        }

        /// <summary>같은 위치와 본 가중치를 가진 정점을 대표 인덱스로 묶습니다.</summary>
        private int[] CanonicalVertices()
        {
            Vector3[] positions = SourceMesh.vertices;
            NativeArray<byte> counts = SourceMesh.GetBonesPerVertex();
            NativeArray<BoneWeight1> weights = SourceMesh.GetAllBoneWeights();
            int[] offsets = new int[positions.Length + 1];

            for (int i = 0; i < positions.Length; i++)
            {
                offsets[i + 1] = offsets[i] + (counts.Length == positions.Length ? counts[i] : 0);
            }

            float tolerance = Mathf.Max(SourceMesh.bounds.size.magnitude * 0.0000001f, 0.0000001f);
            Dictionary<Vector3Int, List<int>> buckets = new Dictionary<Vector3Int, List<int>>();
            int[] result = new int[positions.Length];

            for (int i = 0; i < positions.Length; i++)
            {
                Vector3Int cell = Vector3Int.RoundToInt(positions[i] / tolerance);
                int representative = -1;

                for (int x = -1; x <= 1 && representative < 0; x++)
                {
                    for (int y = -1; y <= 1 && representative < 0; y++)
                    {
                        for (int z = -1; z <= 1 && representative < 0; z++)
                        {
                            Vector3Int neighbor = cell + new Vector3Int(x, y, z);

                            if (!buckets.TryGetValue(neighbor, out List<int> candidates))
                            {
                                continue;
                            }

                            foreach (int candidate in candidates)
                            {
                                if ((positions[i] - positions[candidate]).sqrMagnitude <= tolerance * tolerance
                                    && SameWeights(i, candidate, offsets, weights))
                                {
                                    representative = candidate;
                                    break;
                                }
                            }
                        }
                    }
                }

                result[i] = representative >= 0 ? representative : i;

                if (representative < 0)
                {
                    if (!buckets.TryGetValue(cell, out List<int> candidates))
                    {
                        candidates = new List<int>();
                        buckets.Add(cell, candidates);
                    }

                    candidates.Add(i);
                }
            }

            return result;
        }

        /// <summary>두 정점의 본 인덱스와 가중치가 일치하는지 확인합니다.</summary>
        private static bool SameWeights(int a, int b, int[] offsets, NativeArray<BoneWeight1> weights)
        {
            int count = offsets[a + 1] - offsets[a];

            if (count != offsets[b + 1] - offsets[b])
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                BoneWeight1 left = weights[offsets[a] + i];
                bool found = false;

                for (int j = 0; j < count; j++)
                {
                    BoneWeight1 right = weights[offsets[b] + j];

                    if (left.boneIndex == right.boneIndex && Mathf.Abs(left.weight - right.weight) <= 0.000001f)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>경로를 압축하며 연결 집합의 대표 인덱스를 찾습니다.</summary>
        private static int Find(int[] parents, int value)
        {
            while (parents[value] != value)
            {
                parents[value] = parents[parents[value]];
                value = parents[value];
            }

            return value;
        }

        /// <summary>삼각형 위에서 주어진 점과 가장 가까운 점을 구합니다.</summary>
        private static Vector3 ClosestTrianglePoint(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;

            if (Vector3.Cross(ab, ac).sqrMagnitude <= 0.00000000000000000001f)
            {
                Vector3 first = ClosestSegmentPoint(point, a, b);
                Vector3 second = ClosestSegmentPoint(point, b, c);
                Vector3 third = ClosestSegmentPoint(point, c, a);
                Vector3 closest = (first - point).sqrMagnitude <= (second - point).sqrMagnitude ? first : second;

                return (third - point).sqrMagnitude < (closest - point).sqrMagnitude ? third : closest;
            }

            Vector3 ap = point - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);

            if (d1 <= 0f && d2 <= 0f)
            {
                return a;
            }

            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);

            if (d3 >= 0f && d4 <= d3)
            {
                return b;
            }

            float vc = d1 * d4 - d3 * d2;

            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                return a + ab * (d1 / (d1 - d3));
            }

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);

            if (d6 >= 0f && d5 <= d6)
            {
                return c;
            }

            float vb = d5 * d2 - d1 * d6;

            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                return a + ac * (d2 / (d2 - d6));
            }

            float va = d3 * d6 - d5 * d4;

            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            }

            float inverse = 1f / (va + vb + vc);

            return a + ab * (vb * inverse) + ac * (vc * inverse);
        }

        /// <summary>선분 위에서 주어진 점과 가장 가까운 점을 구합니다.</summary>
        private static Vector3 ClosestSegmentPoint(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 delta = b - a;
            float length = delta.sqrMagnitude;

            return length > 0f ? a + delta * Mathf.Clamp01(Vector3.Dot(point - a, delta) / length) : a;
        }

        /// <summary>이 객체가 소유한 임시 메시와 편집 자원을 해제합니다.</summary>
        public void Dispose()
        {
            if (m_baked != null)
            {
                UnityEngine.Object.DestroyImmediate(m_baked);
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
    }
}

