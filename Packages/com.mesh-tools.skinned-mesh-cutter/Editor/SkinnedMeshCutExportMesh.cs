using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>저장 메시의 좌표계를 변환하고 실제 가중치가 있는 본 인덱스만 보존합니다.</summary>
    public static class SkinnedMeshCutExportMesh
    {
        /// <summary>출력 피벗 좌표로 메시를 변환하고 사용 중인 본만 남깁니다.</summary>
        public static Mesh Create(Mesh source, Matrix4x4 toOutput, out int[] sourceBoneIndices)
        {
            if (source == null || !source.isReadable)
            {
                throw new InvalidOperationException("읽기 가능한 절단 메시가 필요합니다.");
            }

            Matrix4x4 inverse = ValidateMatrix(toOutput);
            Matrix4x4[] originalBindposes = source.bindposes;
            NativeArray<byte> counts = source.GetBonesPerVertex();
            NativeArray<BoneWeight1> originalWeights = source.GetAllBoneWeights();

            if (counts.Length != source.vertexCount || originalBindposes.Length == 0)
            {
                throw new InvalidOperationException("저장 메시의 본 가중치와 Bind Pose가 유효하지 않습니다.");
            }

            bool[] used = new bool[originalBindposes.Length];
            int offset = 0;

            for (int vertex = 0; vertex < counts.Length; vertex++)
            {
                bool hasWeight = false;

                for (int j = 0; j < counts[vertex]; j++)
                {
                    if (offset >= originalWeights.Length)
                    {
                        throw new InvalidOperationException("정점별 가중치 수가 전체 스킨 데이터와 다릅니다.");
                    }

                    BoneWeight1 weight = originalWeights[offset++];

                    if (!Finite(weight.weight) || weight.weight < 0f)
                    {
                        throw new InvalidOperationException("유효하지 않은 본 가중치를 저장할 수 없습니다.");
                    }

                    if (weight.weight == 0f)
                    {
                        continue;
                    }

                    if (weight.boneIndex < 0 || weight.boneIndex >= used.Length)
                    {
                        throw new InvalidOperationException("본 가중치가 없는 Bind Pose 인덱스를 참조합니다.");
                    }

                    used[weight.boneIndex] = true;
                    hasWeight = true;
                }

                if (!hasWeight)
                {
                    throw new InvalidOperationException("양수인 본 가중치가 없는 정점을 저장할 수 없습니다.");
                }
            }

            if (offset != originalWeights.Length)
            {
                throw new InvalidOperationException("정점별 가중치 수가 전체 스킨 데이터와 다릅니다.");
            }

            List<int> indices = new List<int>();
            int[] remap = new int[used.Length];
            List<Matrix4x4> bindposes = new List<Matrix4x4>();

            for (int i = 0; i < used.Length; i++)
            {
                remap[i] = -1;

                if (used[i])
                {
                    remap[i] = indices.Count;
                    indices.Add(i);
                    bindposes.Add(originalBindposes[i] * inverse);
                }
            }

            sourceBoneIndices = indices.ToArray();
            byte[] outputCounts = new byte[counts.Length];
            List<BoneWeight1> outputWeights = new List<BoneWeight1>();
            offset = 0;

            for (int vertex = 0; vertex < counts.Length; vertex++)
            {
                for (int j = 0; j < counts[vertex]; j++)
                {
                    BoneWeight1 weight = originalWeights[offset++];

                    if (weight.weight > 0f)
                    {
                        weight.boneIndex = remap[weight.boneIndex];
                        outputWeights.Add(weight);
                        outputCounts[vertex]++;
                    }
                }
            }

            Mesh mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = source.name;

            try
            {
                TransformGeometry(mesh, toOutput, inverse.transpose);
                mesh.bindposes = bindposes.ToArray();

                // 인덱스가 같으면 복제된 가중치를 유지해 Unity의 재정규화로 인한 오차를 피한다.
                if (sourceBoneIndices.Length != originalBindposes.Length
                    || outputWeights.Count != originalWeights.Length)
                {
                    using (NativeArray<byte> nativeCounts = new NativeArray<byte>(outputCounts, Allocator.Temp))
                    using (NativeArray<BoneWeight1> nativeWeights = new NativeArray<BoneWeight1>(outputWeights.ToArray(), Allocator.Temp))
                    {
                        mesh.SetBoneWeights(nativeCounts, nativeWeights);
                    }
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

        /// <summary>메시 좌표 변환 행렬이 유한하고 역변환 가능한지 검증합니다.</summary>
        private static Matrix4x4 ValidateMatrix(Matrix4x4 matrix)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (!Finite(matrix[row, column]))
                    {
                        throw new InvalidOperationException("저장 좌표 변환은 유한한 값이어야 합니다.");
                    }
                }
            }

            if (matrix.m30 != 0f || matrix.m31 != 0f || matrix.m32 != 0f || matrix.m33 != 1f
                || !Finite(matrix.determinant)
                || matrix.determinant == 0f)
            {
                throw new InvalidOperationException("역변환 가능한 아핀 좌표 변환이 필요합니다.");
            }

            Matrix4x4 inverse = matrix.inverse;

            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (!Finite(inverse[row, column]))
                    {
                        throw new InvalidOperationException("저장 좌표 변환을 역산할 수 없습니다.");
                    }
                }
            }

            return inverse;
        }

        /// <summary>값의 모든 성분이 유한한지 확인합니다.</summary>
        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>위치·법선·접선을 출력 피벗 좌표로 변환합니다.</summary>
        private static void TransformGeometry(Mesh mesh, Matrix4x4 matrix, Matrix4x4 normalMatrix)
        {
            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            bool hasNormals = normals.Length == positions.Length;
            bool hasTangents = tangents.Length == positions.Length;
            bool reflected = matrix.determinant < 0f;

            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = matrix.MultiplyPoint3x4(positions[i]);

                if (hasNormals)
                {
                    normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                }

                if (hasTangents)
                {
                    Vector3 tangent = matrix.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z));

                    if (hasNormals)
                    {
                        tangent -= normals[i] * Vector3.Dot(normals[i], tangent);
                    }

                    tangent.Normalize();
                    tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w * (reflected ? -1f : 1f));
                }
            }

            mesh.vertices = positions;

            if (hasNormals)
            {
                mesh.normals = normals;
            }

            if (hasTangents)
            {
                mesh.tangents = tangents;
            }

            if (reflected)
            {
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                    {
                        throw new InvalidOperationException("삼각형 메시만 저장할 수 있습니다.");
                    }

                    int[] triangles = mesh.GetTriangles(submesh);

                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int temporary = triangles[i + 1];
                        triangles[i + 1] = triangles[i + 2];
                        triangles[i + 2] = temporary;
                    }

                    mesh.SetTriangles(triangles, submesh);
                }
            }
        }
    }
}
