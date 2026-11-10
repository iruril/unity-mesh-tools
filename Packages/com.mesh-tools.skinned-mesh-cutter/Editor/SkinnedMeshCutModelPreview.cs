using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>모델 미리보기의 표시 오브젝트와 소유 메시를 함께 정리합니다.</summary>
    public sealed class SkinnedMeshCutModelPreview : IDisposable
    {
        private readonly List<GameObject> m_objects = new List<GameObject>();
        private readonly List<Mesh> m_meshes = new List<Mesh>();

        public void Add(SkinnedMeshRenderer source, SkinnedMeshRenderer author, Mesh mesh,
            Material[] materials, Vector3 separation, string suffix)
        {
            if (mesh == null) return;
            var baked = new Mesh { name = source.name + " " + suffix, hideFlags = HideFlags.HideAndDontSave };
            m_meshes.Add(baked);
            Mesh original = author.sharedMesh;
            try
            {
                author.sharedMesh = mesh;
                author.BakeMesh(baked, true);
            }
            finally { author.sharedMesh = original; }
            var renderer = CreateDisplay(source, author, baked, materials, suffix);
            renderer.transform.localPosition = author.transform.InverseTransformVector(separation);
            renderer.enabled = source.enabled;
        }

        /// <summary>마스크 편집기의 메시를 빌려 표시하며 정리할 때 원본 메시는 유지합니다.</summary>
        public void AddMask(SkinnedMeshRenderer source, SkinnedMeshRenderer author, Mesh mesh, Material[] materials)
            => CreateDisplay(source, author, mesh, materials, "MASK");

        private MeshRenderer CreateDisplay(SkinnedMeshRenderer source, SkinnedMeshRenderer author,
            Mesh mesh, Material[] materials, string suffix)
        {
            var item = new GameObject(source.name + " " + suffix) { hideFlags = HideFlags.HideAndDontSave };
            m_objects.Add(item);
            SceneManager.MoveGameObjectToScene(item, author.gameObject.scene);
            item.transform.SetParent(author.transform, false);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            return renderer;
        }

        public void Clear()
        {
            foreach (var item in m_objects) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            foreach (var mesh in m_meshes) if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            m_objects.Clear();
            m_meshes.Clear();
        }

        public void Dispose() => Clear();
    }
}
