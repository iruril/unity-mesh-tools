using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public partial class SkinnedMeshCutterWindowBase
    {
        [SerializeField]
        protected Material[] m_surfaceMaterials = Array.Empty<Material>();
        [SerializeField]
        protected Mesh m_surfaceMaterialMesh;

        /// <summary>파츠 종류와 재정의 설정에 따라 캡 재질을 선택합니다.</summary>
        protected Material ResolveCapMaterial(bool remaining = false)
        {
            Material individual = remaining ? m_remainingCapMaterial : m_partCapMaterial;

            if (individual != null)
            {
                return individual;
            }

            return m_capMaterialOverride != null
                ? m_capMaterialOverride
                : SkinnedMeshCutDefaults.instance.CapMaterial;
        }

        /// <summary>원본 스킨의 표면 재질에 사용자 재정의를 적용합니다.</summary>
        public static Material[] ResolveSurfaceMaterials(SkinnedMeshRenderer source, Component owner)
        {
            if (source == null || source.sharedMesh == null)
            {
                return Array.Empty<Material>();
            }

            List<Material[]> candidates = new List<Material[]>();
            bool belongsToOwner = owner == null || source.transform.IsChildOf(owner.transform);

            if (belongsToOwner)
            {
                candidates.Add(source.sharedMaterials);
            }

            if (owner != null)
            {
                candidates.AddRange(owner.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(renderer => renderer.sharedMesh == source.sharedMesh)
                        .Select(renderer => renderer.sharedMaterials));
            }

            if (!belongsToOwner)
            {
                candidates.Add(source.sharedMaterials);
            }

            SkinnedMeshRenderer original = PrefabUtility.GetCorrespondingObjectFromSource(source);

            if (original != null)
            {
                candidates.Add(original.sharedMaterials);
            }

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(source.sharedMesh));

            if (model != null)
            {
                candidates.AddRange(model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(renderer => renderer.sharedMesh == source.sharedMesh)
                        .Select(renderer => renderer.sharedMaterials));
            }

            Material[] materials = new Material[source.sharedMesh.subMeshCount];

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = candidates.Where(candidate => i < candidate.Length).Select(candidate => candidate[i])
                    .FirstOrDefault(material => material != null);
            }

            return materials;
        }

        /// <summary>현재 소스의 표면 재질 목록과 편집 캐시를 동기화합니다.</summary>
        protected void RefreshSurfaceMaterials()
        {
            m_surfaceMaterials = ResolveSurfaceMaterials(m_source, SurfaceMaterialOwner);
            m_surfaceMaterialMesh = m_source != null ? m_source.sharedMesh : null;

            if (m_previewStage != null && m_previewStage.Renderer != null)
            {
                m_previewStage.Renderer.sharedMaterials = m_surfaceMaterials;
            }
        }

        /// <summary>출력과 미리보기에 사용할 표면 재질 배열을 반환합니다.</summary>
        protected Material[] SurfaceMaterials()
        {
            if (m_source != null && m_surfaceMaterialMesh != m_source.sharedMesh)
            {
                RefreshSurfaceMaterials();
            }

            return m_surfaceMaterials;
        }
    }
}
