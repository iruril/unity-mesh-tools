using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MeshTools.Dismemberment.Editor
{
    public partial class SkinnedMeshCutterWindowBase
    {
        protected Quaternion? m_outputRotationOverride;

        #region 입력 검증

        /// <summary>현재 설정에서 생성을 막는 첫 번째 오류를 반환합니다.</summary>
        protected string GetValidationError()
        {
            string sourceError = GetSourceError();

            if (sourceError != null)
            {
                return sourceError;
            }

            string ragdollError = SkinnedMeshCutRagdoll.Validate(m_source, m_ragdollMode, m_ragdollSource, m_ragdollMass);

            if (ragdollError != null)
            {
                return ragdollError;
            }

            if (m_ragdollMode != SkinnedMeshCutRagdollMode.None && m_applyRemainingMesh
                && !IsEditingBatchPart())
            {
                return "래그돌 절단은 독립 PART/REMAIN 프리팹으로 출력합니다. 출력 탭에서 '원본에 남은 메시 적용'을 끄세요.";
            }

            if (m_mode == CutMode.BoneParts && m_outputPivotBone == null)
            {
                return "출력 피봇 본을 지정하세요. 목을 절단해 머리를 만들 때는 Head 본을 지정합니다.";
            }

            if (IsEditingBatchPart())
            {
                return null;
            }

            string folder = NormalizeOutputFolder();

            if ((folder != "Assets" && !folder.StartsWith("Assets/", StringComparison.Ordinal))
                || (folder != DefaultOutputFolder && !AssetDatabase.IsValidFolder(folder)))
            {
                return "Assets 아래의 기존 폴더를 지정하세요. 기본 폴더는 자동 생성됩니다.";
            }

            if (string.IsNullOrWhiteSpace(m_outputName)
                || m_outputName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return "파일 이름에 빈 값이나 파일 이름 금지 문자를 사용할 수 없습니다.";
            }

            return null;
        }

        /// <summary>스킨 메시와 본 연결 및 읽기 가능 상태를 검증합니다.</summary>
        protected string GetSourceError()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return "재생을 종료한 뒤 사용하세요.";
            }

            if (m_source == null || m_source.sharedMesh == null)
            {
                return "씬 또는 Prefab 편집 모드의 SkinnedMeshRenderer를 지정하세요.";
            }

            if ((EditorUtility.IsPersistent(m_source) || !m_source.gameObject.scene.IsValid())
                && !(m_previewStage != null && m_previewStage.Renderer != null))
            {
                return "Prefab 에셋은 편집 모드로 열거나 씬에 배치한 뒤 지정하세요.";
            }

            if (!m_source.sharedMesh.isReadable)
            {
                return "모델 Import Settings에서 Read/Write Enabled를 켜세요.";
            }

            if (m_mode == CutMode.BoneParts
                && (m_joint == null
                    || !Array.Exists(
                    m_source.bones,
                    bone => bone == m_joint || (m_includeChildBones && bone != null && bone.IsChildOf(m_joint)))))
            {
                return "소스 스켈레톤의 관절을 지정하세요.";
            }

            if (m_poseClip != null && (m_previewStage == null || m_previewStage.Renderer == null))
            {
                return "지정한 포즈를 사용하려면 T-pose 편집 프리뷰를 여세요.";
            }

            if (m_outputPivotBone != null
                && !Array.Exists(m_source.bones, bone => bone == m_outputPivotBone))
            {
                return "출력 피봇은 소스 스켈레톤의 본을 지정하세요.";
            }

            if (!m_planeInitialized || !Finite(m_planePosition) || !Finite(m_planeEuler)
                || !Positive(m_planeSize.x)
                || !Positive(m_planeSize.y)
                || !Positive(m_tolerance))
            {
                return "평면 위치·회전은 유한한 값, 폭·높이와 허용 오차는 0보다 큰 값이어야 합니다.";
            }

            if (!Finite(m_minimumBoneWeight) || m_minimumBoneWeight < 0f || m_minimumBoneWeight > 1f
                || !Finite(m_poseTime)
                || m_poseTime < 0f)
            {
                return "관절 영향 값은 0~1, 포즈 시간은 0 이상의 유한한 값이어야 합니다.";
            }

            if (m_repairOpenBoundaries && !Positive(m_maximumGapDistance))
            {
                return "작은 틈 보정 허용 거리는 0보다 큰 유한한 값이어야 합니다.";
            }

            if (m_generateCap
                && (!Positive(m_capUvScale) || !Finite(m_uvRotation) || !Finite(m_uvOffset.x)
                    || !Finite(m_uvOffset.y)))
            {
                return "UV 반복은 0보다 커야 하며 UV 회전과 오프셋은 유한한 값이어야 합니다.";
            }

            return GetMaskError();
        }

        /// <summary>값의 모든 성분이 유한한지 확인합니다.</summary>
        protected static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>값의 모든 성분이 유한한지 확인합니다.</summary>
        protected static bool Finite(Vector3 value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z);
        }

        /// <summary>값이 유한한 양수인지 확인합니다.</summary>
        protected static bool Positive(float value)
        {
            return Finite(value) && value > 0f;
        }

        /// <summary>출력 경로를 정규화하고 Assets 내부인지 검증합니다.</summary>
        protected string NormalizeOutputFolder()
        {
            return (m_outputFolder ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
        }

        #endregion

        #region 생성과 저장

        /// <summary>생성 요청을 처리하고 결과 또는 오류를 사용자에게 표시합니다.</summary>
        protected void HandleGenerate()
        {
            try
            {
                m_lastAssets = GenerateForCurrentContext();
                m_message = GenerationSuccessMessage(m_lastAssets);
                m_messageType = MessageType.Info;
                EditorGUIUtility.PingObject(m_lastAssets[0]);
                MarkPreviewDirty();
            }
            catch (Exception exception)
            {
                m_message = "생성 실패: " + exception.Message;
                m_messageType = MessageType.Error;
                Debug.LogException(exception, m_source);
            }
        }

        /// <summary>현재 파츠 편집 문맥에 맞춰 출력 에셋을 생성합니다.</summary>
        protected virtual UnityEngine.Object[] GenerateForCurrentContext()
        {
            return GenerateAssets();
        }

        /// <summary>생성한 에셋 경로를 포함하는 완료 메시지를 만듭니다.</summary>
        protected virtual string GenerationSuccessMessage(UnityEngine.Object[] assets)
        {
            return $"{assets.Length}개 에셋을 저장했습니다. 양쪽 Prefab에 독립 본 계층이 포함됩니다.";
        }

        /// <summary>현재 설정으로 파츠와 남은 몸체 에셋을 생성합니다.</summary>
        public UnityEngine.Object[] GenerateAssets()
        {
            return GenerateAssetsCore(false);
        }

        /// <summary>일괄 편집용 단일 파츠 에셋을 생성합니다.</summary>
        public UnityEngine.Object[] GeneratePartAssets()
        {
            return GenerateAssetsCore(true);
        }

        /// <summary>출력 폴더와 현재 설정을 검증한 뒤 생성을 실행합니다.</summary>
        protected UnityEngine.Object[] GenerateAssetsCore(bool partOnly)
        {
            m_validatedMaskMesh = null;
            string error = GetValidationError();

            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            SkinnedMeshRenderer author = AuthoringSource();

            using (SkinnedMeshCutRagdoll ragdoll = SkinnedMeshCutRagdoll.Prepare(
                m_source,
                author,
                RagdollAuthorBone,
                m_ragdollMode,
                m_ragdollSource,
                m_ragdollMass,
                m_ragdollTips))
            {
                m_preparedRagdoll = ragdoll;

                try
                {
                    return GeneratePreparedAssets(partOnly, author);
                }
                finally
                {
                    m_preparedRagdoll = null;

                    foreach (Mesh mesh in m_colliderMeshes)
                    {
                        if (mesh != null && !AssetDatabase.Contains(mesh))
                        {
                            DestroyImmediate(mesh);
                        }
                    }

                    m_colliderMeshes.Clear();
                }
            }
        }

        /// <summary>절단 결과를 메시·재질·래그돌·프리팹 에셋으로 저장합니다.</summary>
        private UnityEngine.Object[] GeneratePreparedAssets(bool partOnly, SkinnedMeshRenderer author)
        {
            using (SkinnedMeshCutEngine.Result result = new SkinnedMeshCutEngine(author, BuildSettings()).Cut())
            {
                string folder = NormalizeOutputFolder();

                if (!AssetDatabase.IsValidFolder(folder))
                {
                    AssetDatabase.CreateFolder("Assets", "GeneratedSkinnedParts");
                }

                List<string> createdPaths = new List<string>();
                List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
                List<GameObject> roots = new List<GameObject>();
                Material generatedMaterial = null;
                Mesh partMesh = null;
                Mesh remainingMesh = null;
                bool keepRoots = false;
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Generate skinned cut");

                try
                {
                    string stem = folder + "/" + m_outputName.Trim();
                    Transform pivot = OutputPivot(author);
                    Transform actorRoot = RigRoot(m_source);
                    Quaternion outputRotation = m_previewStage != null ? m_previewStage.Map(actorRoot).rotation : actorRoot.rotation;
                    outputRotation = m_outputRotationOverride ?? outputRotation;
                    Matrix4x4 pivotFrame = Matrix4x4.TRS(pivot.position, outputRotation, Vector3.one);
                    Matrix4x4 toOutput = pivotFrame.inverse * author.transform.localToWorldMatrix;
                    partMesh = SkinnedMeshCutExportMesh.Create(result.Part, toOutput, out int[] partBones);
                    SaveAsset(partMesh, stem + "_PART.asset", assets, createdPaths);
                    int[] remainingBones = null;

                    if (!partOnly)
                    {
                        remainingMesh = SkinnedMeshCutExportMesh.Create(result.Remainder, toOutput, out remainingBones);
                        SaveAsset(remainingMesh, stem + "_REMAIN.asset", assets, createdPaths);
                    }

                    Material common = m_capMaterialOverride != null
                        ? m_capMaterialOverride
                        : SkinnedMeshCutDefaults.instance.CapMaterial;
                    bool hasCaps = result.Part.subMeshCount > m_source.sharedMesh.subMeshCount;

                    if (hasCaps && common == null
                        && (m_partCapMaterial == null || (!partOnly && m_remainingCapMaterial == null)))
                    {
                        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

                        if (shader == null)
                        {
                            throw new InvalidOperationException("절단면 Lit 셰이더를 찾지 못했습니다.");
                        }

                        generatedMaterial = new Material(shader)
                        {
                            name = m_outputName + "_CAP"
                        };
                        common = generatedMaterial;
                        SaveAsset(common, stem + "_CAP.mat", assets, createdPaths);
                    }

                    Material partCap = m_partCapMaterial != null ? m_partCapMaterial : common;
                    Material remainingCap = m_remainingCapMaterial != null ? m_remainingCapMaterial : common;
                    GameObject part = CreateSkinnedOutput(author, partMesh, partBones, partCap, pivotFrame, "PART");
                    roots.Add(part);
                    GameObject remainder = null;

                    if (!partOnly)
                    {
                        remainder = CreateSkinnedOutput(author, remainingMesh, remainingBones, remainingCap, pivotFrame, "REMAIN");
                        roots.Add(remainder);
                    }

                    foreach (GameObject root in roots)
                    {
                        Scene outputScene = m_source.gameObject.scene.IsValid() ? m_source.gameObject.scene : author.gameObject.scene;

                        if (outputScene.IsValid())
                        {
                            SceneManager.MoveGameObjectToScene(root, outputScene);
                        }
                    }

                    foreach (Mesh colliderMesh in m_colliderMeshes)
                    {
                        SaveAsset(colliderMesh, folder + "/" + colliderMesh.name + ".asset", assets, createdPaths);
                    }

                    SavePrefab(part, stem + "_PART.prefab", assets, createdPaths);

                    if (remainder != null)
                    {
                        SavePrefab(remainder, stem + "_REMAIN.prefab", assets, createdPaths);
                    }

                    foreach (GameObject root in roots)
                    {
                        root.transform.SetPositionAndRotation(pivot.position, outputRotation);
                    }

                    if (!partOnly && m_applyRemainingMesh)
                    {
                        SaveAsset(result.Remainder, stem + "_REMAIN_SOURCE.asset", assets, createdPaths);
                        Undo.RecordObject(m_source, "Apply remaining skinned mesh");
                        m_source.sharedMesh = result.Remainder;
                        m_source.sharedMaterials = OutputMaterials(result.Remainder, remainingCap);
                        RecordRendererChange(m_source);

                        if (m_previewStage != null)
                        {
                            m_previewStage.SynchronizeSourceMesh();
                        }
                    }
                    else if (!partOnly && m_createSceneObjects && m_disableSourceRenderer)
                    {
                        Undo.RecordObject(m_source, "Hide original renderer");
                        m_source.enabled = false;
                        RecordRendererChange(m_source);
                    }

                    if (!partOnly && m_createSceneObjects)
                    {
                        foreach (GameObject root in roots)
                        {
                            Undo.RegisterCreatedObjectUndo(root, "Create skinned cut output");
                        }

                        // 원본에 남은 메시를 적용하면 중복 몸체는 생성하지 않는다.
                        if (m_applyRemainingMesh)
                        {
                            Undo.DestroyObjectImmediate(remainder);
                        }

                        keepRoots = true;
                    }

                    AssetDatabase.SaveAssets();
                    Undo.CollapseUndoOperations(undoGroup);

                    return assets.ToArray();
                }
                catch
                {
                    keepRoots = false;
                    Undo.RevertAllDownToGroup(undoGroup);

                    for (int i = createdPaths.Count - 1; i >= 0; i--)
                    {
                        AssetDatabase.DeleteAsset(createdPaths[i]);
                    }

                    throw;
                }
                finally
                {
                    if (!keepRoots)
                    {
                        foreach (GameObject root in roots)
                        {
                            if (root != null)
                            {
                                DestroyImmediate(root);
                            }
                        }
                    }

                    if (generatedMaterial != null && !AssetDatabase.Contains(generatedMaterial))
                    {
                        DestroyImmediate(generatedMaterial);
                    }

                    if (partMesh != null && !AssetDatabase.Contains(partMesh))
                    {
                        DestroyImmediate(partMesh);
                    }

                    if (remainingMesh != null && !AssetDatabase.Contains(remainingMesh))
                    {
                        DestroyImmediate(remainingMesh);
                    }
                }
            }
        }

        /// <summary>중복되지 않는 경로에 에셋을 저장하고 실패 시 정리 목록에 등록합니다.</summary>
        protected static void SaveAsset(
            UnityEngine.Object asset,
            string desiredPath,
            List<UnityEngine.Object> assets,
            List<string> createdPaths)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(desiredPath);
            createdPaths.Add(path);
            AssetDatabase.CreateAsset(asset, path);

            if (!AssetDatabase.Contains(asset))
            {
                throw new InvalidOperationException("에셋을 저장하지 못했습니다: " + path);
            }

            assets.Add(asset);
        }

        /// <summary>생성 오브젝트를 프리팹으로 저장하고 출력 목록에 등록합니다.</summary>
        protected static void SavePrefab(
            GameObject root,
            string desiredPath,
            List<UnityEngine.Object> assets,
            List<string> createdPaths)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(desiredPath);
            createdPaths.Add(path);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);

            if (prefab == null)
            {
                throw new InvalidOperationException("스킨 Prefab을 저장하지 못했습니다: " + path);
            }

            assets.Add(prefab);
        }

        /// <summary>씬 또는 프리팹의 렌더러 변경을 저장 대상으로 기록합니다.</summary>
        protected static void RecordRendererChange(Renderer renderer)
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(renderer);
        }

        #endregion

        #region 독립 스켈레톤 출력

        /// <summary>사용자 지정 본 또는 리그 루트에서 출력 피벗을 선택합니다.</summary>
        protected Transform OutputPivot(SkinnedMeshRenderer author)
        {
            Transform pivot = m_outputPivotBone;

            return pivot != null
                ? (m_previewStage != null ? m_previewStage.Map(pivot) : pivot)
                : author.transform;
        }

        /// <summary>절단 메시와 대응 본 계층으로 출력 스킨을 만듭니다.</summary>
        protected GameObject CreateSkinnedOutput(
            SkinnedMeshRenderer author,
            Mesh mesh,
            int[] sourceBoneIndices,
            Material cap,
            Matrix4x4 pivotFrame,
            string suffix)
        {
            GameObject root = new GameObject(m_outputName + "_" + suffix);

            try
            {
                root.layer = author.gameObject.layer;
                Dictionary<Transform, Transform> map = new Dictionary<Transform, Transform>();
                Transform[] sourceBones = author.bones;
                Transform[] bones = new Transform[sourceBoneIndices.Length];

                for (int i = 0; i < bones.Length; i++)
                {
                    bones[i] = CloneTransformPath(sourceBones[sourceBoneIndices[i]], root.transform, pivotFrame.inverse, map);
                }

                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.bones = bones;
                Transform common = sourceBones[sourceBoneIndices[0]];

                foreach (int index in sourceBoneIndices)
                {
                    while (common != null && sourceBones[index] != common && !sourceBones[index].IsChildOf(common))
                    {
                        common = common.parent;
                    }
                }

                renderer.rootBone = common != null ? map[common] : root.transform;
                renderer.sharedMaterials = OutputMaterials(mesh, cap);
                renderer.quality = author.quality;
                renderer.updateWhenOffscreen = author.updateWhenOffscreen;
                renderer.shadowCastingMode = author.shadowCastingMode;
                renderer.receiveShadows = author.receiveShadows;
                renderer.lightProbeUsage = author.lightProbeUsage;
                renderer.reflectionProbeUsage = author.reflectionProbeUsage;
                ConfigureGeneratedOutput(root, author, map, pivotFrame, suffix);
                SetOutputBounds(renderer);

                return root;
            }
            catch
            {
                DestroyImmediate(root);
                throw;
            }
        }

        /// <summary>생성 오브젝트에 물리 설정과 추가 구성 요소를 적용합니다.</summary>
        protected virtual void ConfigureGeneratedOutput(
            GameObject root,
            SkinnedMeshRenderer author,
            Dictionary<Transform, Transform> map,
            Matrix4x4 pivotFrame,
            string suffix)
        {
            CopyPreparedRagdoll(root, map, pivotFrame, suffix);
        }

        /// <summary>출력 루트에 필요한 트랜스폼 경로를 복제합니다.</summary>
        protected static Transform CloneTransformPath(
            Transform source,
            Transform parent,
            Matrix4x4 toOutput,
            Dictionary<Transform, Transform> map)
        {
            if (map.TryGetValue(source, out Transform found))
            {
                return found;
            }

            Transform clonedParent = source.parent != null ? CloneTransformPath(source.parent, parent, toOutput, map) : parent;
            Transform clone = new GameObject(source.name).transform;
            clone.gameObject.layer = source.gameObject.layer;
            clone.SetParent(clonedParent, false);
            clone.localPosition = source.parent != null ? source.localPosition : toOutput.MultiplyPoint3x4(source.position);
            clone.localRotation = source.parent != null ? source.localRotation : toOutput.rotation * source.rotation;
            clone.localScale = source.localScale;
            map.Add(source, clone);

            return clone;
        }

        /// <summary>작성 포즈를 포함하도록 출력 스킨의 로컬 범위를 계산합니다.</summary>
        public static void SetOutputBounds(SkinnedMeshRenderer renderer)
        {
            Mesh baked = new Mesh();

            try
            {
                renderer.BakeMesh(baked, true);
                Vector3[] vertices = baked.vertices;
                Matrix4x4 toRootBone = renderer.rootBone.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                Bounds bounds = new Bounds(toRootBone.MultiplyPoint3x4(vertices[0]), Vector3.zero);

                foreach (Vector3 vertex in vertices)
                {
                    bounds.Encapsulate(toRootBone.MultiplyPoint3x4(vertex));
                }

                renderer.localBounds = bounds;
            }
            finally
            {
                DestroyImmediate(baked);
            }
        }

        /// <summary>원본 표면 재질을 보존하고 실제 캡이 있을 때만 캡 재질을 추가합니다.</summary>
        protected Material[] OutputMaterials(Mesh mesh, Material cap)
        {
            Material[] original = SurfaceMaterials();
            Material[] materials = new Material[mesh.subMeshCount];

            for (int i = 0; i < original.Length && i < materials.Length; i++)
            {
                materials[i] = original[i];
            }

            if (mesh.subMeshCount > m_source.sharedMesh.subMeshCount)
            {
                materials[materials.Length - 1] = cap;
            }

            return materials;
        }
        #endregion
    }
}

