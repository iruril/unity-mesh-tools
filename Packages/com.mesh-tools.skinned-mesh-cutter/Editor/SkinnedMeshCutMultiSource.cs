using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>Cuts model renderers with shared settings while remapping bones and per-renderer masks.</summary>
    public static class SkinnedMeshCutMultiSource
    {
        public static SkinnedMeshCutEngine.Result Cut(SkinnedMeshCutEngine.Settings sharedSettings,
            SkinnedMeshRenderer reference, SkinnedMeshRenderer source, SkinnedMeshCutMaskMode[] triangleMask = null)
        {
            if (sharedSettings == null) throw new ArgumentNullException(nameof(sharedSettings));
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            if (source == null || source.sharedMesh == null || source.sharedMesh.vertexCount == 0)
                throw new InvalidOperationException("절단 대상에 정점이 있는 스킨 메시를 지정하세요.");

            SkinnedMeshCutEngine.Settings settings = CopySettings(sharedSettings);
            if (source != reference)
            {
                settings.TargetBones = RemapTargetBones(sharedSettings.TargetBones, reference.bones, source.bones);
                // 삼각형 번호는 메시마다 다르므로 기준 메시의 브러시를 다른 표면에 적용하지 않습니다.
                settings.TriangleMask = Array.Empty<SkinnedMeshCutMaskMode>();
            }
            if (triangleMask != null) settings.TriangleMask = triangleMask;

            bool noTarget = sharedSettings.TargetBones != null && sharedSettings.TargetBones.Count > 0 &&
                settings.TargetBones.Count == 0;
            if (noTarget)
            {
                // An empty set means unrestricted to the engine. Explicit brush cuts still override this sentinel.
                settings.TargetBones = new HashSet<int> { -1 };
                settings.MinimumBoneWeight = Mathf.Max(settings.MinimumBoneWeight, 0.00001f);
            }

            // 전체가 한쪽에 있는 메시도 원본 스킨과 마스크의 유효성 검사를 거칩니다.
            var engine = new SkinnedMeshCutEngine(source, settings);

            bool hasMask = false;
            bool hasCut = false;
            bool keepPart = settings.TriangleMask != null && settings.TriangleMask.Length > 0;
            bool keepRemainder = keepPart;
            if (settings.TriangleMask != null)
                foreach (SkinnedMeshCutMaskMode mode in settings.TriangleMask)
                {
                    hasMask |= mode != SkinnedMeshCutMaskMode.Auto;
                    hasCut |= mode == SkinnedMeshCutMaskMode.Cut;
                    keepPart &= mode == SkinnedMeshCutMaskMode.KeepPart;
                    keepRemainder &= mode == SkinnedMeshCutMaskMode.KeepRemainder;
                }
            if (keepPart || keepRemainder) return WholeMesh(source.sharedMesh, keepPart);
            if (hasMask && !hasCut && (noTarget || !HasAutomaticCut(source, settings)))
            {
                SkinnedMeshCutEngine.Result protectedSurface = ProtectedSurface(source, settings.TriangleMask, noTarget);
                if (protectedSurface != null) return protectedSurface;
            }
            if (hasMask) return engine.Cut();
            if (noTarget) return WholeMesh(source.sharedMesh, false);

            Vector3[] positions = GetSkinnedPositions(source);
            Vector3 normal = settings.Rotation * Vector3.forward;
            Vector3 right = settings.Rotation * Vector3.right;
            Vector3 up = settings.Rotation * Vector3.up;
            bool positive = false;
            bool negative = false;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            foreach (Vector3 position in positions)
            {
                Vector3 delta = position - settings.Origin;
                float distance = Vector3.Dot(delta, normal);
                positive |= distance > settings.Tolerance;
                negative |= distance < -settings.Tolerance;
                float x = Vector3.Dot(delta, right);
                float y = Vector3.Dot(delta, up);
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }

            if (!positive && !negative)
                throw new InvalidOperationException(source.name + ": 메시 전체가 절단 평면과 겹칩니다. 평면을 이동하세요.");
            if (!positive || !negative)
                return WholeSide(source, settings, positive == settings.DetachPositive);
            if (minX > settings.Size.x * 0.5f + settings.Tolerance ||
                maxX < -settings.Size.x * 0.5f - settings.Tolerance ||
                minY > settings.Size.y * 0.5f + settings.Tolerance ||
                maxY < -settings.Size.y * 0.5f - settings.Tolerance)
                return WholeMesh(source.sharedMesh, false);

            return engine.Cut();
        }

        private static bool HasAutomaticCut(SkinnedMeshRenderer source, SkinnedMeshCutEngine.Settings settings)
        {
            Vector3[] positions = GetSkinnedPositions(source);
            Quaternion toPlane = Quaternion.Inverse(settings.Rotation);
            for (int i = 0; i < positions.Length; i++) positions[i] = toPlane * (positions[i] - settings.Origin);
            bool unrestricted = settings.TargetBones == null || settings.TargetBones.Count == 0
                || settings.MinimumBoneWeight <= 0f;
            var selected = new bool[positions.Length];
            if (!unrestricted)
            {
                var counts = source.sharedMesh.GetBonesPerVertex();
                var weights = source.sharedMesh.GetAllBoneWeights();
                int offset = 0;
                for (int vertex = 0; vertex < counts.Length; vertex++)
                    for (int i = 0; i < counts[vertex]; i++)
                    {
                        BoneWeight1 weight = weights[offset++];
                        selected[vertex] |= weight.weight > 0f && settings.TargetBones.Contains(weight.boneIndex);
                    }
            }

            bool touchesRectangle = false, touchesTarget = unrestricted;
            int triangle = 0;
            float tolerance = settings.Tolerance;
            Vector2 halfSize = settings.Size * 0.5f + Vector2.one * tolerance;
            for (int submesh = 0; submesh < source.sharedMesh.subMeshCount; submesh++)
            {
                int[] indices = source.sharedMesh.GetTriangles(submesh);
                for (int i = 0; i < indices.Length; i += 3, triangle++)
                {
                    if (settings.TriangleMask[triangle] != SkinnedMeshCutMaskMode.Auto) continue;
                    Vector3 a = positions[indices[i]], b = positions[indices[i + 1]], c = positions[indices[i + 2]];
                    Vector3 min = Vector3.Min(a, Vector3.Min(b, c));
                    Vector3 max = Vector3.Max(a, Vector3.Max(b, c));
                    if (min.z > tolerance || max.z < -tolerance) continue;

                    // Boundary selection averages a complete loop. Be conservative across its triangles,
                    // so a protected head surface never replaces a possible automatic neck cut.
                    touchesTarget |= selected[indices[i]] || selected[indices[i + 1]] || selected[indices[i + 2]];
                    touchesRectangle |= min.x <= halfSize.x && max.x >= -halfSize.x
                        && min.y <= halfSize.y && max.y >= -halfSize.y;
                    if (touchesRectangle && touchesTarget) return true;
                }
            }
            return false;
        }

        private static SkinnedMeshCutEngine.Result ProtectedSurface(SkinnedMeshRenderer source,
            SkinnedMeshCutMaskMode[] mask, bool noTarget)
        {
            var partTriangles = new bool[mask.Length];
            bool hasPart = false, hasRemainder = false;
            using (var surface = new SkinnedMeshCutBrushSurface(source))
            {
                var visited = new bool[surface.TriangleCount];
                for (int seed = 0; seed < visited.Length; seed++)
                {
                    if (visited[seed]) continue;
                    bool? owner = null;
                    int[] component = surface.GetConnectedTriangles(seed);
                    foreach (int triangle in component)
                    {
                        visited[triangle] = true;
                        SkinnedMeshCutMaskMode mode = mask[triangle];
                        if (mode == SkinnedMeshCutMaskMode.Auto) continue;
                        bool selected = mode == SkinnedMeshCutMaskMode.KeepPart;
                        if (owner.HasValue && owner.Value != selected)
                            throw new InvalidOperationException(source.name + ": 연결된 표면에 포함·유지 마스크가 충돌합니다. " +
                                "같은 모드를 지정하거나 절단 경계를 추가하세요.");
                        owner = selected;
                    }
                    if (!owner.HasValue)
                    {
                        if (!noTarget) return null;
                        owner = false;
                    }
                    foreach (int triangle in component) partTriangles[triangle] = owner.Value;
                    hasPart |= owner.Value;
                    hasRemainder |= !owner.Value;
                }
            }
            if (!hasPart || !hasRemainder) return WholeMesh(source.sharedMesh, hasPart);

            Mesh part = null, remainder = null;
            try
            {
                part = ExtractProtectedSurface(source.sharedMesh, partTriangles, true);
                remainder = ExtractProtectedSurface(source.sharedMesh, partTriangles, false);
                return new SkinnedMeshCutEngine.Result(part, remainder, Array.Empty<Vector3>(), 0,
                    Array.Empty<Vector3>(), 0f, null);
            }
            catch
            {
                if (part != null) UnityEngine.Object.DestroyImmediate(part);
                if (remainder != null) UnityEngine.Object.DestroyImmediate(remainder);
                throw;
            }
        }

        private static Mesh ExtractProtectedSurface(Mesh source, bool[] partTriangles, bool part)
        {
            var remap = new int[source.vertexCount];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            var vertices = new List<int>();
            var submeshes = new List<int>[source.subMeshCount];
            int triangle = 0;
            for (int submesh = 0; submesh < submeshes.Length; submesh++)
            {
                submeshes[submesh] = new List<int>();
                int[] indices = source.GetTriangles(submesh);
                for (int i = 0; i < indices.Length; i += 3, triangle++)
                {
                    if (partTriangles[triangle] != part) continue;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int original = indices[i + corner];
                        if (remap[original] < 0)
                        {
                            remap[original] = vertices.Count;
                            vertices.Add(original);
                        }
                        submeshes[submesh].Add(remap[original]);
                    }
                }
            }

            var mesh = new Mesh { name = source.name + (part ? "_PART" : "_REMAIN"), indexFormat = source.indexFormat };
            try
            {
                // Copy complete vertex records so UV dimensions, color formats and other attributes stay intact.
                mesh.SetVertexBufferParams(vertices.Count, source.GetVertexAttributes());
                using (var data = Mesh.AcquireReadOnlyMeshData(source))
                {
                    for (int stream = 0; stream < source.vertexBufferCount; stream++)
                    {
                        int stride = source.GetVertexBufferStride(stream);
                        var original = data[0].GetVertexData<byte>(stream);
                        using (var compact = new NativeArray<byte>(vertices.Count * stride, Allocator.Temp))
                        {
                            for (int i = 0; i < vertices.Count; i++)
                                NativeArray<byte>.Copy(original, vertices[i] * stride, compact, i * stride, stride);
                            mesh.SetVertexBufferData(compact, 0, 0, compact.Length, stream, MeshUpdateFlags.DontRecalculateBounds);
                        }
                    }
                }
                var counts = source.GetBonesPerVertex();
                var weights = source.GetAllBoneWeights();
                var offsets = new int[source.vertexCount];
                int offset = 0;
                for (int i = 0; i < offsets.Length; i++)
                {
                    offsets[i] = offset;
                    offset += counts[i];
                }
                var selectedWeights = new List<BoneWeight1>();
                var selectedCounts = new NativeArray<byte>(vertices.Count, Allocator.Temp);
                try
                {
                    for (int i = 0; i < vertices.Count; i++)
                    {
                        int original = vertices[i];
                        selectedCounts[i] = counts[original];
                        for (int j = 0; j < counts[original]; j++) selectedWeights.Add(weights[offsets[original] + j]);
                    }
                    using (var nativeWeights = new NativeArray<BoneWeight1>(selectedWeights.ToArray(), Allocator.Temp))
                        mesh.SetBoneWeights(selectedCounts, nativeWeights);
                }
                finally { selectedCounts.Dispose(); }
                mesh.bindposes = source.bindposes;
                mesh.subMeshCount = submeshes.Length;
                for (int i = 0; i < submeshes.Length; i++) mesh.SetTriangles(submeshes[i], i, false);
                mesh.RecalculateBounds();
                return mesh;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
        }

        private static SkinnedMeshCutEngine.Result WholeSide(SkinnedMeshRenderer source,
            SkinnedMeshCutEngine.Settings settings, bool part)
        {
            if (!part || settings.TargetBones == null || settings.TargetBones.Count == 0)
                return WholeMesh(source.sharedMesh, part);

            // A whole accessory follows its selected bones even outside the finite cut rectangle.
            // Mixed ownership needs a real boundary or an explicit mask, not a whole-mesh guess.
            var counts = source.sharedMesh.GetBonesPerVertex();
            var weights = source.sharedMesh.GetAllBoneWeights();
            bool any = false, all = true;
            int offset = 0;
            for (int vertex = 0; vertex < counts.Length; vertex++)
            {
                float selected = 0f;
                for (int i = 0; i < counts[vertex]; i++)
                {
                    BoneWeight1 weight = weights[offset++];
                    if (settings.TargetBones.Contains(weight.boneIndex)) selected += weight.weight;
                }
                bool matches = selected >= settings.MinimumBoneWeight;
                any |= matches;
                all &= matches;
            }
            if (any && !all)
                throw new InvalidOperationException(source.name + ": 절단 경계 없이 선택 본과 다른 본의 표면이 함께 있습니다. " +
                    "절단면을 조정하거나 기준 메시의 포함·유지 마스크를 지정하세요.");
            return WholeMesh(source.sharedMesh, all);
        }

        private static HashSet<int> RemapTargetBones(HashSet<int> targets, Transform[] referenceBones,
            Transform[] sourceBones)
        {
            if (targets == null || targets.Count == 0) return null;
            var result = new HashSet<int>();
            for (int i = 0; i < sourceBones.Length; i++)
            {
                for (Transform bone = sourceBones[i]; bone != null; bone = bone.parent)
                {
                    int referenceIndex = Array.IndexOf(referenceBones, bone);
                    if (referenceIndex < 0) continue;
                    if (targets.Contains(referenceIndex)) result.Add(i);
                    break;
                }
            }
            return result;
        }

        private static Vector3[] GetSkinnedPositions(SkinnedMeshRenderer source)
        {
            Mesh mesh = source.sharedMesh;
            Transform[] bones = source.bones;
            Matrix4x4[] matrices = mesh.bindposes;
            for (int i = 0; i < matrices.Length; i++) matrices[i] = bones[i].localToWorldMatrix * matrices[i];
            NativeArray<byte> counts = mesh.GetBonesPerVertex();
            NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
            Vector3[] positions = mesh.vertices;
            int quality = source.quality == SkinQuality.Auto ? (int)QualitySettings.skinWeights : (int)source.quality;
            int offset = 0;
            for (int vertex = 0; vertex < positions.Length; vertex++)
            {
                int limit = quality <= 0 ? counts[vertex] : Mathf.Min(quality, counts[vertex]);
                Vector3 position = Vector3.zero;
                float sum = 0f;
                for (int i = 0; i < limit; i++)
                {
                    BoneWeight1 weight = weights[offset + i];
                    position += matrices[weight.boneIndex].MultiplyPoint3x4(positions[vertex]) * weight.weight;
                    sum += weight.weight;
                }
                positions[vertex] = position / sum;
                offset += counts[vertex];
            }
            return positions;
        }

        private static SkinnedMeshCutEngine.Result WholeMesh(Mesh source, bool part)
        {
            Mesh copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name + (part ? "_PART" : "_REMAIN");
            return new SkinnedMeshCutEngine.Result(part ? copy : null, part ? null : copy,
                Array.Empty<Vector3>(), 0, Array.Empty<Vector3>(), 0f, null);
        }

        private static SkinnedMeshCutEngine.Settings CopySettings(SkinnedMeshCutEngine.Settings value)
        {
            return new SkinnedMeshCutEngine.Settings
            {
                Origin = value.Origin, Rotation = value.Rotation, Size = value.Size, Tolerance = value.Tolerance,
                RepairOpenBoundaries = value.RepairOpenBoundaries, MaximumGapDistance = value.MaximumGapDistance,
                GenerateCap = value.GenerateCap, CloseOpenBoundaries = value.CloseOpenBoundaries,
                DetachPositive = value.DetachPositive, TargetBones = value.TargetBones,
                MinimumBoneWeight = value.MinimumBoneWeight, UvScale = value.UvScale,
                UvRotation = value.UvRotation, UvOffset = value.UvOffset, UvLayoutScale = value.UvLayoutScale,
                UvIslandTransforms = value.UvIslandTransforms, PackUv = value.PackUv, TriangleMask = value.TriangleMask
            };
        }
    }
}
