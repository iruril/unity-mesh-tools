using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshCutEngine
    {
        /// <summary>원본 메시 속성과 현재 자세의 스킨 정점을 읽습니다.</summary>
        private void ReadSource()
        {
            Mesh mesh = m_source.sharedMesh;
            Transform[] bones = m_source.bones;
            Matrix4x4[] bindposes = mesh.bindposes;
            NativeArray<byte> counts = mesh.GetBonesPerVertex();
            NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();

            if (bones.Length == 0 || bones.Length != bindposes.Length || counts.Length != mesh.vertexCount)
            {
                throw new InvalidOperationException("Bones / Bindposes와 정점별 스킨 가중치가 일치해야 합니다.");
            }

            m_skinMatrices = new Matrix4x4[bones.Length];

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null)
                {
                    throw new InvalidOperationException($"소스의 Bones[{i}]가 비어 있습니다.");
                }

                m_skinMatrices[i] = bones[i].localToWorldMatrix * bindposes[i];
            }

            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            Color[] colors = mesh.colors;
            m_hasNormals = normals.Length == mesh.vertexCount;
            m_hasTangents = tangents.Length == mesh.vertexCount;
            m_hasColors = colors.Length == mesh.vertexCount;
            m_uvChannels = new bool[8];
            m_uvDimensions = new int[8];
            List<Vector4>[] uvs = new List<Vector4>[8];

            for (int channel = 0; channel < uvs.Length; channel++)
            {
                uvs[channel] = new List<Vector4>();
                mesh.GetUVs(channel, uvs[channel]);
                m_uvChannels[channel] = uvs[channel].Count == mesh.vertexCount;
                m_uvDimensions[channel] = mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + channel);
            }

            int offset = 0;

            for (int i = 0; i < mesh.vertexCount; i++)
            {
                if (counts[i] == 0)
                {
                    throw new InvalidOperationException($"정점 {i}에 본 가중치가 없습니다.");
                }

                BoneWeight1[] vertexWeights = new BoneWeight1[counts[i]];

                for (int j = 0; j < vertexWeights.Length; j++)
                {
                    BoneWeight1 weight = weights[offset++];

                    if (weight.boneIndex < 0 || weight.boneIndex >= bones.Length || weight.weight < 0f
                        || float.IsNaN(weight.weight)
                        || float.IsInfinity(weight.weight))
                    {
                        throw new InvalidOperationException($"정점 {i}의 본 가중치가 유효하지 않습니다.");
                    }

                    vertexWeights[j] = weight;
                }

                Vertex vertex = new Vertex
                {
                    Id = i,
                    Position = positions[i],
                    Normal = m_hasNormals ? normals[i] : Vector3.up,
                    Tangent = m_hasTangents ? tangents[i] : new Vector4(1f, 0f, 0f, 1f),
                    Color = m_hasColors ? colors[i] : Color.white,
                    Weights = vertexWeights,
                    Uvs = new Vector4[8]
                };

                for (int channel = 0; channel < 8; channel++)
                {
                    if (m_uvChannels[channel])
                    {
                        vertex.Uvs[channel] = uvs[channel][i];
                    }
                }

                vertex.Pose = SkinMatrix(vertex.Weights).MultiplyPoint3x4(vertex.Position);
                m_vertices.Add(vertex);
            }

            if (m_settings.GenerateCap)
            {
                m_uvChannels[0] = true;
                m_uvDimensions[0] = Mathf.Max(m_uvDimensions[0], 2);
            }

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                {
                    throw new InvalidOperationException("삼각형 메시만 지원합니다.");
                }

                int[] indices = mesh.GetTriangles(submesh);

                for (int i = 0; i < indices.Length; i += 3)
                {
                    m_triangles.Add(new Triangle(m_vertices[indices[i]], m_vertices[indices[i + 1]], m_vertices[indices[i + 2]], submesh));
                }
            }
        }

        /// <summary>정점의 전체 본 가중치를 반영하는 스킨 변환 행렬을 구합니다.</summary>
        private Matrix4x4 SkinMatrix(BoneWeight1[] weights)
        {
            Matrix4x4 matrix = Matrix4x4.zero;
            int limit = m_source.quality == SkinQuality.Auto
                ? (int)QualitySettings.skinWeights
                : (int)m_source.quality;
            limit = limit <= 0 ? weights.Length : Mathf.Min(limit, weights.Length);
            float sum = 0f;

            for (int i = 0; i < limit; i++)
            {
                BoneWeight1 weight = weights[i];
                sum += weight.weight;
                Matrix4x4 skin = m_skinMatrices[weight.boneIndex];

                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        matrix[row, column] += skin[row, column] * weight.weight;
                    }
                }
            }

            if (sum <= 0f)
            {
                throw new InvalidOperationException("작성 포즈를 계산할 유효 본 가중치가 없습니다.");
            }

            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    matrix[row, column] /= sum;
                }
            }

            return matrix;
        }

        /// <summary>절단 교점의 자세·원본 좌표·본 가중치·표면 속성을 보간합니다.</summary>
        private Vertex Interpolate(Vertex a, Vertex b, float t)
        {
            Dictionary<int, float> combined = new Dictionary<int, float>();
            AddWeights(combined, a.Weights, 1f - t);
            AddWeights(combined, b.Weights, t);
            List<BoneWeight1> weights = new List<BoneWeight1>();
            float sum = 0f;

            foreach (KeyValuePair<int, float> pair in combined)
            {
                if (pair.Value > 0f)
                {
                    weights.Add(new BoneWeight1 { boneIndex = pair.Key, weight = pair.Value });
                    sum += pair.Value;
                }
            }

            if (weights.Count == 0 || weights.Count > byte.MaxValue)
            {
                throw new InvalidOperationException("보간 정점의 본 가중치를 Unity의 지원 범위로 표현할 수 없습니다.");
            }

            weights.Sort((left, right) => right.weight.CompareTo(left.weight));
            BoneWeight1[] normalized = weights.ToArray();

            for (int i = 0; i < normalized.Length; i++)
            {
                normalized[i].weight /= sum;
            }

            Vector3 pose = Vector3.LerpUnclamped(a.Pose, b.Pose, t);
            Matrix4x4 skin = SkinMatrix(normalized);

            if (Mathf.Abs(skin.determinant) < 0.0000000001f)
            {
                throw new InvalidOperationException("본 변형을 역변환할 수 없습니다. 작성 포즈를 조정하세요.");
            }

            Vertex vertex = new Vertex
            {
                Id = m_vertices.Count,
                Position = skin.inverse.MultiplyPoint3x4(pose),
                Pose = pose,
                Normal = Vector3.LerpUnclamped(a.Normal, b.Normal, t).normalized,
                Tangent = Vector4.LerpUnclamped(a.Tangent, b.Tangent, t),
                Color = Color.LerpUnclamped(a.Color, b.Color, t),
                Weights = normalized,
                Uvs = new Vector4[8]
            };
            Vector3 tangent = new Vector3(vertex.Tangent.x, vertex.Tangent.y, vertex.Tangent.z);
            tangent = (tangent - vertex.Normal * Vector3.Dot(vertex.Normal, tangent)).normalized;
            vertex.Tangent = new Vector4(tangent.x, tangent.y, tangent.z, t < 0.5f ? a.Tangent.w : b.Tangent.w);

            for (int channel = 0; channel < 8; channel++)
            {
                vertex.Uvs[channel] = Vector4.LerpUnclamped(a.Uvs[channel], b.Uvs[channel], t);
            }

            m_vertices.Add(vertex);

            return vertex;
        }

        /// <summary>보간 비율에 따라 본별 가중치를 누적합니다.</summary>
        private static void AddWeights(Dictionary<int, float> target, BoneWeight1[] weights, float factor)
        {
            foreach (BoneWeight1 weight in weights)
            {
                target.TryGetValue(weight.boneIndex, out float existing);
                target[weight.boneIndex] = existing + weight.weight * factor;
            }
        }

        private sealed class Vertex
        {
            public int Id { get; set; }
            // 원본 바인드 좌표를 저장하고 출력 메시에도 이 좌표를 유지한다.
            public Vector3 Position { get; set; }
            // 절단과 거리 판정에 사용하는 현재 작성 포즈의 월드 좌표다.
            public Vector3 Pose { get; set; }
            public Vector3 Normal { get; set; }
            public Vector4 Tangent { get; set; }
            public Color Color { get; set; }
            public Vector4[] Uvs { get; set; }
            public BoneWeight1[] Weights { get; set; }
        }

        private sealed class MeshBuilder
        {
            private readonly SkinnedMeshCutEngine m_engine;
            private readonly List<Vertex> m_output = new List<Vertex>();
            private readonly Dictionary<Vertex, int> m_map = new Dictionary<Vertex, int>();
            private readonly List<int>[] m_indices;

            /// <summary>원본 서브메시와 실제 캡에 필요한 출력 버퍼를 준비합니다.</summary>
            public MeshBuilder(SkinnedMeshCutEngine engine)
            {
                m_engine = engine;
                // 열린 경계만 잘랐다면 빈 캡 서브메시와 UV 그룹을 만들지 않는다.
                bool hasCaps = engine.m_settings.GenerateCap && engine.m_closedLoops.Count > 0;
                int count = engine.m_source.sharedMesh.subMeshCount + (hasCaps ? 1 : 0);
                m_indices = new List<int>[count];

                for (int i = 0; i < count; i++)
                {
                    m_indices[i] = new List<int>();
                }
            }

            /// <summary>출력 메시에서 정점을 재사용하거나 새 인덱스를 등록합니다.</summary>
            public int Add(Vertex vertex)
            {
                if (!m_map.TryGetValue(vertex, out int index))
                {
                    index = m_output.Count;
                    m_output.Add(vertex);
                    m_map.Add(vertex, index);
                }

                return index;
            }

            /// <summary>지정한 서브메시에 삼각형 인덱스를 추가합니다.</summary>
            public void AddTriangle(int a, int b, int c, int submesh)
            {
                m_indices[submesh].Add(a);
                m_indices[submesh].Add(b);
                m_indices[submesh].Add(c);
            }

            /// <summary>클리핑한 볼록 다각형을 삼각형 부채꼴로 출력합니다.</summary>
            public void AddPolygon(List<Vertex> polygon, int submesh)
            {
                int first = Add(polygon[0]);

                for (int i = 1; i < polygon.Count - 1; i++)
                {
                    AddTriangle(first, Add(polygon[i]), Add(polygon[i + 1]), submesh);
                }
            }

            /// <summary>정점 속성과 전체 본 가중치를 보존한 출력 메시를 만듭니다.</summary>
            public Mesh Build(string name)
            {
                Mesh mesh = new Mesh
                {
                    name = name,
                    indexFormat = m_output.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
                };

                try
                {
                    List<Vector3> positions = new List<Vector3>();
                    List<Vector3> normals = new List<Vector3>();
                    List<Vector4> tangents = new List<Vector4>();
                    List<Color> colors = new List<Color>();
                    List<BoneWeight1> weights = new List<BoneWeight1>();
                    byte[] counts = new byte[m_output.Count];

                    for (int i = 0; i < m_output.Count; i++)
                    {
                        Vertex vertex = m_output[i];
                        positions.Add(vertex.Position);
                        normals.Add(vertex.Normal);
                        tangents.Add(vertex.Tangent);
                        colors.Add(vertex.Color);
                        counts[i] = (byte)vertex.Weights.Length;
                        weights.AddRange(vertex.Weights);
                    }

                    mesh.SetVertices(positions);
                    mesh.SetNormals(normals);
                    mesh.SetTangents(tangents);

                    if (m_engine.m_hasColors)
                    {
                        mesh.SetColors(colors);
                    }

                    for (int channel = 0; channel < 8; channel++)
                    {
                        if (!m_engine.m_uvChannels[channel])
                        {
                            continue;
                        }

                        int dimension = m_engine.m_uvDimensions[channel];
                        List<Vector4> uvs = m_output.ConvertAll(vertex => vertex.Uvs[channel]);

                        if (dimension <= 2)
                        {
                            mesh.SetUVs(channel, uvs.ConvertAll(uv => new Vector2(uv.x, uv.y)));
                        }
                        else if (dimension == 3)
                        {
                            mesh.SetUVs(channel, uvs.ConvertAll(uv => new Vector3(uv.x, uv.y, uv.z)));
                        }
                        else
                        {
                            mesh.SetUVs(channel, uvs);
                        }
                    }

                    mesh.bindposes = m_engine.m_source.sharedMesh.bindposes;

                    using (NativeArray<byte> nativeCounts = new NativeArray<byte>(counts, Allocator.Temp))
                    using (NativeArray<BoneWeight1> nativeWeights = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp))
                    {
                        mesh.SetBoneWeights(nativeCounts, nativeWeights);
                    }

                    mesh.subMeshCount = m_indices.Length;

                    for (int i = 0; i < m_indices.Length; i++)
                    {
                        mesh.SetTriangles(m_indices[i], i);
                    }

                    if (!m_engine.m_hasNormals)
                    {
                        mesh.RecalculateNormals();
                    }

                    if (!m_engine.m_hasTangents)
                    {
                        mesh.RecalculateTangents();
                    }

                    mesh.RecalculateBounds();

                    return mesh;
                }
                catch
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                    throw;
                }
            }
        }
    }
}
