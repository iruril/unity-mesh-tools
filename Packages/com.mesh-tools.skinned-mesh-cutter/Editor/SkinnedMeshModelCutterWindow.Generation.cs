using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshModelCutterWindow
    {
        private Material[] ModelMaterials(SkinnedMeshRenderer source, Mesh mesh, Material cap)
            => ModelOutputMaterials(source, mesh, cap);

        protected override UnityEngine.Object[] GenerateAssetsCore(bool partOnly)
        {
            FlushModelMasks();
            string error = GetValidationError();
            if (error != null) throw new InvalidOperationException(error);
            var sources = SelectedRenderers();
            if (EditorUtility.IsPersistent(m_source) && m_applyRemainingMesh)
                throw new InvalidOperationException("원본에 남은 메시를 적용하려면 Prefab 편집 모드에서 모델을 여세요.");
            var authors = sources.Select(AuthorRenderer).ToArray();
            m_previewStage?.SamplePose(m_poseClip, m_poseTime);
            var results = new List<SkinnedMeshCutEngine.Result>();
            var roots = new List<GameObject>();
            var assets = new List<UnityEngine.Object>();
            var paths = new List<string>();
            var meshes = new List<Mesh>();
            Material generatedCap = null;
            bool keepRoots = false;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
                var settings = BuildSettings();
                for (int i = 0; i < sources.Length; i++)
                    results.Add(SkinnedMeshCutMultiSource.Cut(settings, AuthoringSource(), authors[i], RendererMask(sources[i])));
                if (results.All(result => result.Part == null))
                    throw new InvalidOperationException("선택한 메시에서 분리되는 면이 없습니다.");
                string folder = NormalizeOutputFolder();
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "GeneratedSkinnedParts");
                string stem = folder + "/" + m_outputName.Trim();
                Material common = m_capMaterialOverride != null ? m_capMaterialOverride : SkinnedMeshCutDefaults.instance.CapMaterial;
                bool caps = results.Where((result, i) =>
                    (result.Part != null && result.Part.subMeshCount > sources[i].sharedMesh.subMeshCount)
                    || (!partOnly && result.Remainder != null && result.Remainder.subMeshCount > sources[i].sharedMesh.subMeshCount)).Any();
                if (caps && common == null && (m_partCapMaterial == null || (!partOnly && m_remainingCapMaterial == null)))
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    if (shader == null) throw new InvalidOperationException("절단면 Lit 셰이더를 찾지 못했습니다.");
                    generatedCap = new Material(shader) { name = m_outputName + "_CAP" };
                    common = generatedCap;
                    SaveAsset(common, stem + "_CAP.mat", assets, paths);
                }
                Transform authorRoot = AuthorBone(m_model.transform);
                Quaternion rotation = m_outputRotationOverride ?? authorRoot.rotation;
                Matrix4x4 frame = Matrix4x4.TRS(OutputPivot(AuthoringSource()).position, rotation, Vector3.one);
                using (var ragdoll = SkinnedMeshCutRagdoll.Prepare(m_source, AuthoringSource(), AuthorBone,
                    m_ragdollMode, m_ragdollSource, m_ragdollMass, m_ragdollTips, ModelRenderers()))
                {
                    for (int side = 0; side < (partOnly ? 1 : 2); side++)
                    {
                        string suffix = side == 0 ? "PART" : "REMAIN";
                        var root = new GameObject(m_outputName + "_" + suffix);
                        roots.Add(root);
                        SceneManager.MoveGameObjectToScene(root, authors[0].gameObject.scene);
                        var map = new Dictionary<Transform, Transform>();
                        var model = new GameObject(m_model.name).transform;
                        model.SetParent(root.transform, false);
                        Matrix4x4 local = frame.inverse * authorRoot.localToWorldMatrix;
                        model.localPosition = local.GetColumn(3);
                        model.localRotation = local.rotation;
                        model.localScale = local.lossyScale;
                        map.Add(authorRoot, model);
                        Material cap = side == 0 ? m_partCapMaterial : m_remainingCapMaterial;
                        if (cap == null) cap = common;
                        int outputCount = 0;
                        for (int i = 0; i < sources.Length; i++)
                        {
                            Mesh cut = side == 0 ? results[i].Part : results[i].Remainder;
                            if (cut == null) continue;
                            Mesh mesh = SkinnedMeshCutExportMesh.Create(cut, Matrix4x4.identity, out int[] indices);
                            meshes.Add(mesh);
                            SaveAsset(mesh, stem + "_" + i + "_" + suffix + ".asset", assets, paths);
                            var input = authors[i];
                            var output = CreateModelOutputRenderer(sources[i], input, mesh, indices,
                                root.transform, frame.inverse, map, model, cap);
                            SetOutputBounds(output);
                            outputCount++;
                        }
                        if (outputCount == 0) { DestroyImmediate(root); continue; }
                        if (ragdoll != null)
                        {
                            var plane = new Plane(frame.inverse.MultiplyVector(settings.Rotation * Vector3.forward),
                                frame.inverse.MultiplyPoint3x4(settings.Origin));
                            int start = meshes.Count;
                            var excluded = new HashSet<string>(m_modelSettings.ExcludedColliders);
                            ragdoll.CopyTo(root, map, bone => CloneTransformPath(bone, root.transform, frame.inverse, map), plane, meshes,
                                side == 0 ? collider => !excluded.Contains(ragdoll.ColliderKey(collider)) : (Func<Collider, bool>)null);
                            for (int i = start; i < meshes.Count; i++)
                                SaveAsset(meshes[i], stem + "_" + suffix + "_Collider_" + (i - start) + ".asset", assets, paths);
                        }
                        foreach (var pair in map)
                        {
                            string path = AnimationUtility.CalculateTransformPath(pair.Key, authorRoot);
                            Transform original = path.Length == 0 ? m_model.transform : m_model.transform.Find(path);
                            if (original != null) pair.Value.gameObject.SetActive(original.gameObject.activeSelf);
                        }
                        SavePrefab(root, stem + "_" + suffix + ".prefab", assets, paths);
                        root.transform.SetPositionAndRotation(frame.GetColumn(3), rotation);
                    }
                }
                if (!partOnly && m_applyRemainingMesh)
                {
                    for (int i = 0; i < sources.Length; i++)
                    {
                        Undo.RecordObject(sources[i], "Apply model cut");
                        var remaining = results[i].Remainder;
                        if (remaining == null) sources[i].enabled = false;
                        else
                        {
                            SaveAsset(remaining, stem + "_" + i + "_REMAIN_SOURCE.asset", assets, paths);
                            sources[i].sharedMaterials = ModelMaterials(sources[i], remaining,
                                m_remainingCapMaterial != null ? m_remainingCapMaterial : common);
                            sources[i].sharedMesh = remaining;
                        }
                        RecordRendererChange(sources[i]);
                    }
                }
                else if (!partOnly && m_createSceneObjects && m_disableSourceRenderer)
                    foreach (var source in sources)
                    {
                        Undo.RecordObject(source, "Hide cut model renderer");
                        source.enabled = false;
                        RecordRendererChange(source);
                    }
                if (!partOnly && m_createSceneObjects)
                {
                    foreach (var root in roots.Where(root => root != null))
                    {
                        if (m_applyRemainingMesh && root.name.EndsWith("_REMAIN", StringComparison.Ordinal))
                        { DestroyImmediate(root); continue; }
                        Scene outputScene = m_source.gameObject.scene;
                        if (!outputScene.IsValid() || EditorSceneManager.IsPreviewScene(outputScene))
                            outputScene = SceneManager.GetActiveScene();
                        SceneManager.MoveGameObjectToScene(root, outputScene);
                        Undo.RegisterCreatedObjectUndo(root, "Create model cut");
                    }
                    keepRoots = true;
                }
                AssetDatabase.SaveAssets();
                Undo.CollapseUndoOperations(undoGroup);
                return assets.ToArray();
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                foreach (string path in paths.AsEnumerable().Reverse()) AssetDatabase.DeleteAsset(path);
                throw;
            }
            finally
            {
                if (!keepRoots) foreach (var root in roots) if (root != null) DestroyImmediate(root);
                foreach (var result in results) result.Dispose();
                foreach (var mesh in meshes) if (mesh != null && !AssetDatabase.Contains(mesh)) DestroyImmediate(mesh);
                if (generatedCap != null && !AssetDatabase.Contains(generatedCap)) DestroyImmediate(generatedCap);
            }
        }
    }
}
