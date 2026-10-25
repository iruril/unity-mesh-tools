using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>포즈를 유지한 액터에서 유한 평면과 관절 기준의 스킨 메시 절단을 작성합니다.</summary>
    [MovedFrom(true, "Irony.Dismemberment.Editor", "Irony.SkinnedMeshCutter.Editor", null)]
    public partial class SkinnedMeshCutterWindowBase : EditorWindow
    {
        protected const string DefaultOutputFolder = "Assets/GeneratedSkinnedParts";
        protected static readonly string[] ModeLabels =
        {
            "관절 기반 절단",
            "자유 평면 절단"
        };
        [SerializeField, FormerlySerializedAs("source")]
        protected SkinnedMeshRenderer m_source;
        [SerializeField]
        protected CutMode m_mode = CutMode.PlaneSplit;
        [SerializeField]
        protected Transform m_joint;
        [SerializeField]
        protected Transform m_outputPivotBone;
        [SerializeField]
        protected bool m_includeChildBones = true;
        [SerializeField]
        protected float m_minimumBoneWeight = 0.01f;
        [SerializeField]
        protected Vector3 m_planePosition;
        [SerializeField]
        protected Vector3 m_planeEuler;
        [SerializeField]
        protected Vector2 m_planeSize = Vector2.one;
        [SerializeField]
        protected bool m_detachPositive = true;
        [SerializeField]
        protected bool m_planeInitialized;
        [SerializeField, FormerlySerializedAs("generateCap")]
        protected bool m_generateCap = true;
        [SerializeField]
        protected bool m_closeOpenBoundaries;
        [SerializeField, FormerlySerializedAs("capMaterialOverride")]
        protected Material m_capMaterialOverride;
        [SerializeField]
        protected Material m_partCapMaterial;
        [SerializeField]
        protected Material m_remainingCapMaterial;
        [SerializeField, FormerlySerializedAs("capUvScale")]
        protected float m_capUvScale = 1f;
        [SerializeField]
        protected float m_uvRotation;
        [SerializeField]
        protected Vector2 m_uvOffset;
        [SerializeField]
        protected float m_uvLayoutScale = 1f;
        [SerializeField]
        protected SkinnedMeshCutUvTransform[] m_uvIslandTransforms = Array.Empty<SkinnedMeshCutUvTransform>();
        [SerializeField]
        protected bool m_packUv;
        [SerializeField]
        protected bool m_checkerPreview;
        [SerializeField]
        protected float m_tolerance = 0.00001f;
        [SerializeField]
        protected bool m_repairOpenBoundaries = true;
        [SerializeField]
        protected float m_maximumGapDistance = 0.005f;
        [SerializeField, FormerlySerializedAs("createPreviewObjects")]
        protected bool m_createSceneObjects = true;
        [SerializeField, FormerlySerializedAs("disableSourceRenderer")]
        protected bool m_disableSourceRenderer;
        [SerializeField]
        protected bool m_applyRemainingMesh;
        [SerializeField]
        protected bool m_showScenePreview = true;
        [SerializeField]
        protected bool m_showResult = true;
        [SerializeField]
        protected float m_previewSeparation = 0.06f;
        [SerializeField]
        protected string m_outputFolder = DefaultOutputFolder;
        [SerializeField]
        protected string m_outputName;
        [SerializeField]
        protected AnimationClip m_poseClip;
        [SerializeField]
        protected float m_poseTime;
        [SerializeField]
        protected SkinnedMeshCutPreset m_preset;
        [SerializeField]
        protected Tool m_handleTool = Tool.Move;
        protected Vector2 m_scroll;
        protected string m_message;
        protected MessageType m_messageType;
        protected UnityEngine.Object[] m_lastAssets = Array.Empty<UnityEngine.Object>();
        protected SkinnedMeshCutEngine.Result m_previewResult;
        protected Vector3[] m_previewBoundary = Array.Empty<Vector3>();
        protected string m_previewError;
        protected bool m_previewDirty = true;
        protected double m_previewRequestedAt;
        protected SkinnedMeshCutPreviewStage m_previewStage;
        protected Material m_checkerMaterial;
        protected Texture2D m_checkerTexture;
        protected virtual string WindowTitle => "Skinned Mesh Cutter";
        protected virtual bool PartOnlyAuthoring => false;
        protected virtual bool HasPhysicsIntegration => m_ragdollMode != SkinnedMeshCutRagdollMode.None;
        protected virtual string CurrentPartLabel => string.Empty;
        protected virtual Component SurfaceMaterialOwner => null;

        /// <summary>현재 편집 중인 대상이 일괄 생성 파츠인지 확인합니다.</summary>
        protected virtual bool IsEditingBatchPart()
        {
            return false;
        }

        /// <summary>프로젝트 공통 절단 설정과 저장 동작을 표시합니다.</summary>
        protected virtual void DrawProjectSettings()
        {
        }

        /// <summary>파츠별 출력 경로와 생성 옵션을 표시합니다.</summary>
        protected virtual void DrawCustomPartOutputSettings()
        {
        }

        /// <summary>소스 교체 후 마스크·미리보기·UV 편집 상태를 초기화합니다.</summary>
        protected virtual void OnSourceChanged()
        {
        }

        /// <summary>소스가 비어 있으면 현재 선택한 스킨을 연결합니다.</summary>
        public void UseSelectionIfEmpty()
        {
            if (m_source == null)
            {
                UseSelection();
            }
        }

        /// <summary>모델 내부의 본을 선택하는 필드를 표시합니다.</summary>
        protected virtual Transform DrawModelBoneField(string label, Transform current, bool weightedOnly = false, bool includeParents = false)
        {
            return (Transform)EditorGUILayout.ObjectField(label, current, typeof(Transform), true);
        }

        #region Unity 생명주기

        /// <summary>편집기 이벤트를 연결하고 저장된 설정을 준비합니다.</summary>
        protected virtual void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(460f, 600f);
            m_settingsTab = m_maskBrushEnabled
                ? 2
                : m_source == null ? 0 : Mathf.Clamp(m_settingsTab, 0, SettingsTabLabels.Length - 1);
            SceneView.duringSceneGui += HandleSceneGui;
            Undo.undoRedoPerformed += HandleUndoRedo;
            EditorApplication.update += HandleEditorUpdate;
            MarkPreviewDirty();
        }

        /// <summary>편집기 이벤트 연결과 임시 편집 자원을 정리합니다.</summary>
        protected virtual void OnDisable()
        {
            SceneView.duringSceneGui -= HandleSceneGui;
            Undo.undoRedoPerformed -= HandleUndoRedo;
            EditorApplication.update -= HandleEditorUpdate;
            ClosePosePreview();
            ClearPreview();
            DisposeMaskResources();

            if (m_checkerMaterial != null)
            {
                DestroyImmediate(m_checkerMaterial);
                DestroyImmediate(m_checkerTexture);
            }
        }

        /// <summary>창 종료 시 남은 미리보기 스테이지와 편집 자원을 해제합니다.</summary>
        protected virtual void OnDestroy()
        {
            Undo.ClearUndo(this);
        }

        /// <summary>절단 도구의 설정과 미리보기 편집 화면을 그립니다.</summary>
        protected virtual void OnGUI()
        {
            if (this == null)
            {
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Skinned Mesh Cutter", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string context = m_source != null ? m_source.name : "소스 렌더러를 지정하세요";

                    if (IsEditingBatchPart())
                    {
                        context += " · " + CurrentPartLabel;
                    }

                    EditorGUILayout.LabelField(context, EditorStyles.miniLabel);

                    if (GUILayout.Button("소스 설정", EditorStyles.miniButton, GUILayout.Width(70f)))
                    {
                        SetSettingsTab(0);
                    }
                }

                DrawSettingsTabs();
                DrawPreviewToolbar();
                m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
                EditorGUI.BeginChangeCheck();
                switch (m_settingsTab)
                {
                    case 0:
                        DrawSource();
                        DrawProjectSettings();
                        DrawDefaultsSettings();
                        break;
                    case 1:
                        CutMode mode = (CutMode)GUILayout.Toolbar((int)m_mode, ModeLabels, GUILayout.Height(26f));
                        if (mode != m_mode)
                        {
                            RecordSettings();
                            m_mode = mode;
                            ResetPlane();
                        }

                        DrawPlaneSettings();
                        DrawRagdollSettings();
                        break;
                    case 2:
                        DrawMaskSettings();
                        break;
                    case 3:
                        DrawCapSettings();
                        break;
                    case 4:
                        DrawOutputSettings();
                        DrawPresets();
                        foreach (UnityEngine.Object asset in m_lastAssets)
                        {
                            if (asset != null)
                            {
                                EditorGUILayout.ObjectField(asset, typeof(UnityEngine.Object), false);
                            }
                        }

                        break;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    MarkPreviewDirty();
                }

                EditorGUILayout.EndScrollView();
                string validation = GetValidationError();
                DrawPreviewStatus(validation);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("미리보기 갱신"))
                    {
                        RefreshCutPreview();
                    }

                    using (new EditorGUI.DisabledScope(validation != null))
                    {
                        string saveLabel = IsEditingBatchPart() ? "현재 부위 PART 저장" : "스킨 메시와 Prefab 저장";

                        if (GUILayout.Button(saveLabel, GUILayout.Height(32f)))
                        {
                            HandleGenerate();
                        }
                    }
                }
            }
        }

        #endregion

        #region 창과 소스 설정

        /// <summary>편집 소스를 바꾸고 관련 캐시를 갱신합니다.</summary>
        protected void SetSource(SkinnedMeshRenderer source)
        {
            ClosePosePreview();
            ClearPreview();
            DisposeMaskResources();
            m_source = source;

            if (source == null)
            {
                SetSettingsTab(0);
            }

            m_uvIslandTransforms = Array.Empty<SkinnedMeshCutUvTransform>();
            m_selectedUvIsland = 0;
            OnSourceChanged();
            RefreshSurfaceMaterials();

            if (m_maskSourceMesh != (source != null ? source.sharedMesh : null))
            {
                m_triangleMask = Array.Empty<SkinnedMeshCutMaskMode>();
                m_maskSourceMesh = source != null ? source.sharedMesh : null;
                m_maskTopologyHash = 0;
            }

            m_joint = null;
            m_outputPivotBone = null;
            m_poseClip = null;
            m_poseTime = 0f;
            m_outputName = source != null ? source.gameObject.name : string.Empty;
            ResetPlane();
        }

        /// <summary>현재 선택한 오브젝트에서 편집할 스킨을 찾습니다.</summary>
        protected void UseSelection()
        {
            GameObject selected = Selection.activeGameObject;

            if (selected != null)
            {
                SetSource(selected.GetComponentInChildren<SkinnedMeshRenderer>(true));
            }
        }

        /// <summary>설정 변경을 Undo에 기록하고 미리보기를 갱신 대상으로 표시합니다.</summary>
        protected void RecordSettings()
        {
            if (this != null)
            {
                Undo.RecordObject(this, "Edit cut plane");
            }
        }

        /// <summary>작성 포즈가 적용된 스킨 또는 원본 스킨을 반환합니다.</summary>
        protected SkinnedMeshRenderer AuthoringSource()
        {
            return m_previewStage != null && m_previewStage.Renderer != null
                ? m_previewStage.Renderer
                : m_source;
        }

        /// <summary>현재 편집 기준에서 절단 평면의 월드 위치와 회전을 구합니다.</summary>
        protected Transform PlaneFrame()
        {
            if (m_mode == CutMode.BoneParts && m_joint != null)
            {
                return m_previewStage != null ? m_previewStage.Map(m_joint) : m_joint;
            }

            SkinnedMeshRenderer source = AuthoringSource();

            return source != null ? source.transform : null;
        }

        /// <summary>모델 범위에 맞춰 절단 평면의 위치·회전·크기를 초기화합니다.</summary>
        protected void ResetPlane()
        {
            Transform frame = PlaneFrame();

            if (frame == null)
            {
                m_planeInitialized = false;

                return;
            }

            SkinnedMeshRenderer source = AuthoringSource();
            Vector3 origin = m_mode == CutMode.BoneParts && m_joint != null ? frame.position : source.bounds.center;
            Vector3 direction = frame.right;

            if (m_mode == CutMode.BoneParts && m_joint != null)
            {
                direction = frame.childCount > 0
                    ? frame.GetChild(0).position - frame.position
                    : frame.parent != null ? frame.position - frame.parent.position : frame.up;
            }

            if (direction.sqrMagnitude < 0.000001f)
            {
                direction = frame.up;
            }

            m_planePosition = frame.InverseTransformPoint(origin);
            m_planeEuler = (Quaternion.Inverse(frame.rotation) * Quaternion.LookRotation(direction)).eulerAngles;
            float size = Mathf.Max(source.bounds.size.magnitude, 0.1f);
            m_planeSize = new Vector2(size, size);
            m_planeInitialized = true;
            MarkPreviewDirty();
        }

        /// <summary>창의 편집 값을 절단 엔진 설정으로 구성합니다.</summary>
        protected SkinnedMeshCutEngine.Settings BuildSettings()
        {
            Transform frame = PlaneFrame();

            if (frame == null)
            {
                throw new InvalidOperationException("평면의 기준 Transform을 찾지 못했습니다.");
            }

            HashSet<int> targets = null;

            if (m_mode == CutMode.BoneParts)
            {
                targets = new HashSet<int>();
                Transform[] bones = m_source.bones;

                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == m_joint
                        || (m_includeChildBones && bones[i] != null && bones[i].IsChildOf(m_joint)))
                    {
                        targets.Add(i);
                    }
                }
            }

            return new SkinnedMeshCutEngine.Settings
            {
                Origin = frame.TransformPoint(m_planePosition),
                Rotation = frame.rotation * Quaternion.Euler(m_planeEuler),
                Size = m_planeSize,
                DetachPositive = m_detachPositive,
                TargetBones = targets,
                MinimumBoneWeight = m_minimumBoneWeight,
                GenerateCap = m_generateCap,
                CloseOpenBoundaries = m_closeOpenBoundaries,
                Tolerance = m_tolerance,
                RepairOpenBoundaries = m_repairOpenBoundaries,
                MaximumGapDistance = m_maximumGapDistance,
                UvScale = m_capUvScale,
                UvRotation = m_uvRotation,
                UvOffset = m_uvOffset,
                UvLayoutScale = m_uvLayoutScale,
                UvIslandTransforms = m_uvIslandTransforms,
                PackUv = m_packUv,
                TriangleMask = m_triangleMask
            };
        }

        #endregion

        #region 설정 화면

        /// <summary>소스 스킨과 작성 포즈 선택 화면을 그립니다.</summary>
        protected void DrawSource()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                SkinnedMeshRenderer source = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("소스 렌더러", m_source, typeof(SkinnedMeshRenderer), true);

                if (source != m_source)
                {
                    RecordSettings();
                    SetSource(source);
                }

                if (GUILayout.Button("Hierarchy 선택에서 가져오기"))
                {
                    RecordSettings();
                    UseSelection();
                }

                if (m_source != null && m_source.sharedMesh != null)
                {
                    EditorGUILayout.LabelField($"{m_source.sharedMesh.name} · {m_source.sharedMesh.vertexCount:N0} 정점");

                    if (m_source.sharedMesh.blendShapeCount > 0)
                    {
                        EditorGUILayout.HelpBox("Blend Shape는 출력에 보존되지 않습니다.", MessageType.Warning);
                    }
                }

                EditorGUILayout.HelpBox("준비된 T-pose를 사용하거나, 포즈 클립을 지정해 편집 복제본에서 작업하세요.", MessageType.Info);
                AnimationClip clip = (AnimationClip)EditorGUILayout.ObjectField("작성 포즈 클립 (선택)", m_poseClip, typeof(AnimationClip), false);
                float time = EditorGUILayout.FloatField("포즈 시간 (초)", m_poseTime);

                if (clip != m_poseClip || time != m_poseTime)
                {
                    RecordSettings();
                    m_poseClip = clip;
                    m_poseTime = Mathf.Max(0f, time);

                    if (m_previewStage != null && Finite(m_poseTime))
                    {
                        m_previewStage.SamplePose(m_poseClip, m_poseTime);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(m_source == null || EditorUtility.IsPersistent(m_source)))
                    {
                        if (GUILayout.Button("T-pose 편집 프리뷰 열기"))
                        {
                            OpenPosePreview();
                        }
                    }

                    if (GUILayout.Button("원본 작업 화면으로"))
                    {
                        ClosePosePreview();
                    }
                }
            }
        }

        /// <summary>절단 평면·대상 본·틈 보정 설정을 표시합니다.</summary>
        protected void DrawPlaneSettings()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("절단 평면", EditorStyles.boldLabel);

            if (m_mode == CutMode.BoneParts)
            {
                Transform joint = DrawModelBoneField("기준 관절", m_joint);

                if (joint != m_joint)
                {
                    RecordSettings();
                    m_joint = joint;
                    ResetPlane();
                }
            }

            EditorGUI.BeginChangeCheck();
            Vector3 position = EditorGUILayout.Vector3Field("상대 위치", m_planePosition);
            Vector3 rotation = EditorGUILayout.Vector3Field("상대 회전", m_planeEuler);
            Vector2 size = EditorGUILayout.Vector2Field("폭 / 높이 (월드 단위)", m_planeSize);

            if (EditorGUI.EndChangeCheck())
            {
                RecordSettings();
                m_planePosition = position;
                m_planeEuler = rotation;
                m_planeSize = size;
                MarkPreviewDirty();
            }

            m_detachPositive = EditorGUILayout.Toggle("화살표 방향을 파츠로 분리", m_detachPositive);
            int tool = m_handleTool == Tool.Move ? 0 : m_handleTool == Tool.Rotate ? 1 : 2;
            tool = GUILayout.Toolbar(tool, PlaneToolLabels);
            m_handleTool = tool == 0 ? Tool.Move : tool == 1 ? Tool.Rotate : Tool.Scale;

            if (GUILayout.Button("관절 / 메시 중심에 평면 초기화"))
            {
                RecordSettings();
                ResetPlane();
            }

            EditorGUILayout.Space(6f);
            m_planeAdvancedExpanded = ViewFoldout(m_planeAdvancedExpanded, "고급 · 본 영향과 경계 보정");

            if (m_planeAdvancedExpanded)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (m_mode == CutMode.BoneParts)
                    {
                        m_includeChildBones = EditorGUILayout.Toggle("하위 본을 대상에 포함", m_includeChildBones);
                        m_minimumBoneWeight = EditorGUILayout.Slider("관절 영향 최소값", m_minimumBoneWeight, 0f, 1f);
                        EditorGUILayout.HelpBox("가중치는 대상 단면 선택에만 사용하며, 최종 경계는 평면으로 나눕니다.", MessageType.None);
                    }

                    m_tolerance = EditorGUILayout.FloatField("경계 병합 허용 오차 (월드)", m_tolerance);
                    m_repairOpenBoundaries = EditorGUILayout.Toggle("작은 틈 보정", m_repairOpenBoundaries);

                    using (new EditorGUI.DisabledScope(!m_repairOpenBoundaries))
                    {
                        m_maximumGapDistance = EditorGUILayout.FloatField("보정 허용 거리 (mm)", m_maximumGapDistance * 1000f) / 1000f;
                    }

                    EditorGUILayout.HelpBox(
                        "1 월드 단위 = 1m 기준입니다. 단면의 두 끝점과 원본 경계 간격이 " + "모두 허용 거리 이내일 때 좁은 외피로 연결합니다.",
                        MessageType.None);
                }
            }
        }

        /// <summary>닫힌 절단면의 재질과 UV 생성 설정을 표시합니다.</summary>
        protected void DrawCapSettings()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("절단면 머티리얼 · UV", EditorStyles.boldLabel);
            m_generateCap = EditorGUILayout.Toggle(PartOnlyAuthoring ? "파츠 절단면 메우기" : "양쪽 절단면 메우기", m_generateCap);

            using (new EditorGUI.DisabledScope(!m_generateCap))
            {
                m_closeOpenBoundaries = EditorGUILayout.Toggle(
                    new GUIContent(
                        "열린 절단 경계 닫기",
                        "같은 원본 파츠의 열린 절단선을 모아 캡을 만듭니다. 서로 겹친 별개 파츠는 합치지 않습니다."),
                    m_closeOpenBoundaries);

                if (m_closeOpenBoundaries)
                {
                    EditorGUILayout.HelpBox(
                        "같은 원본 파츠의 앞뒤 절단선을 함께 닫습니다. 가슴·허리·골반이 겹쳐도 캡과 UV는 파츠별로 유지합니다.",
                        MessageType.Info);
                }


                if (PartOnlyAuthoring)
                {
                    Material active = m_partCapMaterial != null ? m_partCapMaterial : m_capMaterialOverride;
                    Material selected = (Material)EditorGUILayout.ObjectField("이 부위 머티리얼 재정의", active, typeof(Material), false);

                    if (selected != active)
                    {
                        m_partCapMaterial = selected;
                        m_capMaterialOverride = null;
                    }
                }
                else
                {
                    m_capMaterialOverride = (Material)EditorGUILayout.ObjectField("공통 절단면 머티리얼", m_capMaterialOverride, typeof(Material), false);
                    m_partCapMaterial = (Material)EditorGUILayout.ObjectField("파츠 쪽 개별 지정", m_partCapMaterial, typeof(Material), false);
                    m_remainingCapMaterial = (Material)EditorGUILayout.ObjectField("몸체 쪽 개별 지정", m_remainingCapMaterial, typeof(Material), false);
                }

                m_uvProjectionExpanded = ViewFoldout(m_uvProjectionExpanded, "UV 투영 · 초기 배치");

                if (m_uvProjectionExpanded)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        m_packUv = EditorGUILayout.Toggle("UV 아일랜드를 0~1에 배치", m_packUv);

                        using (new EditorGUI.DisabledScope(m_packUv))
                        {
                            m_capUvScale = EditorGUILayout.FloatField("월드 단위당 UV 반복", m_capUvScale);
                        }

                        m_uvRotation = EditorGUILayout.FloatField("전체 투영 회전 (도)", m_uvRotation);
                    }
                }

                m_uvOffset = EditorGUILayout.Vector2Field("전체 UV 오프셋", m_uvOffset);
                m_checkerPreview = EditorGUILayout.Toggle("체크무늬로 단면 UV 검사", m_checkerPreview);

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("적용 머티리얼", ResolveCapMaterial(), typeof(Material), false);
                }

                DrawCapUvPreview();
            }

            DrawDefaultsSettings();
        }

        /// <summary>출력 에셋 경로와 씬 반영 옵션을 표시합니다.</summary>
        protected void DrawOutputSettings()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("출력과 저장", EditorStyles.boldLabel);
            Transform pivot = DrawModelBoneField(m_mode == CutMode.BoneParts ? "피봇 위치 본 (필수)" : "피봇 위치 본 (선택)", m_outputPivotBone, true);

            if (pivot != m_outputPivotBone)
            {
                RecordSettings();
                m_outputPivotBone = pivot;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("출력 축 기준 (루트)", m_source != null ? RigRoot(m_source) : null, typeof(Transform), true);
            }

            if (IsEditingBatchPart())
            {
                DrawCustomPartOutputSettings();
            }
            else
            {
                m_outputFolder = EditorGUILayout.TextField("저장 폴더", m_outputFolder);

                if (GUILayout.Button("Project에서 선택한 폴더 사용"))
                {
                    string folder = AssetDatabase.GetAssetPath(Selection.activeObject);

                    if (AssetDatabase.IsValidFolder(folder))
                    {
                        m_outputFolder = folder;
                    }
                }

                m_outputName = EditorGUILayout.TextField("파일 이름", m_outputName);
                m_createSceneObjects = EditorGUILayout.Toggle("결과를 씬에도 생성", m_createSceneObjects);
                m_applyRemainingMesh = EditorGUILayout.Toggle("원본에 남은 메시 적용", m_applyRemainingMesh);

                using (new EditorGUI.DisabledScope(!m_createSceneObjects || m_applyRemainingMesh))
                {
                    m_disableSourceRenderer = EditorGUILayout.Toggle("생성 후 원본 숨기기", m_disableSourceRenderer);
                }
            }

            EditorGUILayout.HelpBox(
                "저장 루트는 위치·회전 0, 스케일 1입니다. 피봇 위치는 지정 본, 축 방향은 " + "원본 액터 루트를 따릅니다. Head 본의 수입 축을 그대로 사용하지 않습니다. " + "목 절단으로 머리를 만들 때는 피봇 위치 본을 Head로 지정하세요. " + "자유 평면에서만 비우면 소스 렌더러 위치를 사용합니다. " + "가중치가 있는 본과 필요한 부모만 저장합니다.",
                MessageType.Info);
        }

        #endregion
        public enum CutMode
        {
            BoneParts = 0,
            PlaneSplit = 1
        }
    }
}
