using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public partial class SkinnedMeshCutterWindowBase
    {
        /// <summary>원본 표면 재질을 보존하고 추가된 절단면에 캡 재질을 적용합니다.</summary>
        protected Material[] ModelOutputMaterials(SkinnedMeshRenderer source, Mesh mesh, Material cap)
        {
            var original = source == m_source ? SurfaceMaterials() : ResolveSurfaceMaterials(source, SurfaceMaterialOwner);
            var materials = new Material[mesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++)
                materials[i] = i < source.sharedMesh.subMeshCount
                    ? original.Length > 0 ? original[Mathf.Min(i, original.Length - 1)] : null : cap;
            return materials;
        }

        /// <summary>원본 계층·렌더러 설정을 보존하며 출력 본을 다시 연결합니다.</summary>
        protected SkinnedMeshRenderer CreateModelOutputRenderer(SkinnedMeshRenderer source,
            SkinnedMeshRenderer author, Mesh mesh, int[] boneIndices, Transform root, Matrix4x4 toOutput,
            Dictionary<Transform, Transform> map, Transform fallbackRootBone, Material cap)
        {
            Transform node = CloneTransformPath(author.transform, root, toOutput, map);
            var output = node.gameObject.AddComponent<SkinnedMeshRenderer>();
            EditorUtility.CopySerialized(author, output);
            output.sharedMesh = mesh;
            output.bones = boneIndices.Select(index => CloneTransformPath(author.bones[index], root, toOutput, map)).ToArray();
            output.rootBone = author.rootBone != null
                ? CloneTransformPath(author.rootBone, root, toOutput, map) : fallbackRootBone;
            output.sharedMaterials = ModelOutputMaterials(source, mesh, cap);
            output.enabled = source.enabled;
            return output;
        }
    }
}
