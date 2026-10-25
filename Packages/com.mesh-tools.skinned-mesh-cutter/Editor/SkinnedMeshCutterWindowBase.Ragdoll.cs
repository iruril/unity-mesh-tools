using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public partial class SkinnedMeshCutterWindowBase
    {
        [SerializeField]
        protected SkinnedMeshCutRagdollMode m_ragdollMode;
        [SerializeField]
        protected GameObject m_ragdollSource;
        [SerializeField]
        protected float m_ragdollMass = 60f;
        [SerializeField]
        protected bool m_ragdollTips = true;
        [SerializeField]
        private bool m_ragdollExpanded = true;
        protected SkinnedMeshCutRagdoll m_preparedRagdoll;
        private readonly List<Mesh> m_colliderMeshes = new List<Mesh>();
        private static readonly string[] RagdollLabels =
        {
            "사용 안 함 · 기존 출력 유지",
            "기존 래그돌 사용",
            "자동 래그돌 생성 후 절단"
        };
        protected virtual string[] RagdollModeLabels => RagdollLabels;

        /// <summary>래그돌 복사·생성 모드와 물리 설정을 표시합니다.</summary>
        protected void DrawRagdollSettings()
        {
            m_ragdollExpanded = ViewFoldout(m_ragdollExpanded, "절단 전 래그돌 준비");

            if (!m_ragdollExpanded)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                var mode = (SkinnedMeshCutRagdollMode)EditorGUILayout.Popup("물리 준비 방식", (int)m_ragdollMode, RagdollModeLabels);
                GameObject source = m_ragdollSource;
                float mass = m_ragdollMass;
                bool tips = m_ragdollTips;

                if (mode == SkinnedMeshCutRagdollMode.Existing)
                {
                    source = (GameObject)EditorGUILayout.ObjectField("래그돌 원본 프리팹", source, typeof(GameObject), false);
                    EditorGUILayout.HelpBox("비우면 소스 모델의 물리를 사용합니다. 다른 프리팹은 같은 본 경로의 루트를 지정하세요.", MessageType.None);
                }

                if (mode == SkinnedMeshCutRagdollMode.Generate)
                {
                    mass = EditorGUILayout.FloatField("전체 질량 (kg)", mass);
                    EditorGUILayout.LabelField("사용 중인 생성기", SkinnedMeshCutRagdollGenerator.BackendName);
                    using (new EditorGUI.DisabledScope(!SkinnedMeshCutRagdollGenerator.HasBzHelper))
                    {
                        tips = EditorGUILayout.Toggle("손·발 물리 포함 (Bz)", tips);
                    }

                    if (!SkinnedMeshCutRagdollGenerator.HasBzHelper)
                    {
                        EditorGUILayout.HelpBox("Bz Ragdoll Helper가 없어 Unity 내장 Ragdoll Builder를 사용합니다. "
                            + "내장 생성기는 기본 관절 구성을 생성하며 별도 손·발 강체는 추가하지 않습니다.", MessageType.None);
                    }
                }

                if (EditorGUI.EndChangeCheck())
                {
                    RecordSettings();
                    m_ragdollMode = mode;
                    m_ragdollSource = source;
                    m_ragdollMass = mass;
                    m_ragdollTips = tips;
                }

                if (mode == SkinnedMeshCutRagdollMode.None)
                {
                    return;
                }

                EditorGUILayout.HelpBox(
                    "래그돌의 Rigidbody·관절·콜라이더 설정을 보존합니다. 실제 절단면에 걸친 구·캡슐과 비스듬하게 잘린 Box만 Convex 메시로 바꿉니다. " + "Box는 로컬 축에 맞는 절단만 크기를 줄이고, 잘리지 않은 콜라이더와 기존 MeshCollider는 원본 형상을 유지합니다.",
                    MessageType.None);
                string error = SkinnedMeshCutRagdoll.Validate(m_source, mode, source, mass);

                if (error != null)
                {
                    EditorGUILayout.HelpBox(error, MessageType.Warning);
                }

                if (mode == SkinnedMeshCutRagdollMode.Generate)
                {
                    EditorGUILayout.HelpBox("Humanoid의 T-pose에서 준비하세요. 먼저 프리팹을 만들면 콜라이더를 직접 편집한 뒤 재사용할 수 있습니다.", MessageType.None);

                    using (new EditorGUI.DisabledScope(error != null))
                    {
                        if (GUILayout.Button("래그돌 편집용 프리팹 먼저 만들기"))
                        {
                            string path = EditorUtility.SaveFilePanelInProject("래그돌 프리팹 저장", m_source.name + "_RAGDOLL", "prefab", "콜라이더 편집용 래그돌 저장 위치");

                            if (!string.IsNullOrEmpty(path))
                            {
                                try
                                {
                                    GameObject prefab = CreateRagdollPrefab(AssetDatabase.GenerateUniqueAssetPath(path));
                                    RecordSettings();
                                    m_ragdollSource = prefab;
                                    m_ragdollMode = SkinnedMeshCutRagdollMode.Existing;
                                    Selection.activeObject = prefab;
                                    EditorGUIUtility.PingObject(prefab);
                                    m_message = "래그돌 프리팹을 만들었습니다. 프리팹을 열어 콜라이더를 조정한 뒤 현재 절단 설정으로 생성하세요.";
                                    m_messageType = MessageType.Info;
                                }
                                catch (Exception exception)
                                {
                                    m_message = exception.Message;
                                    m_messageType = MessageType.Error;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>현재 작성 포즈의 물리 리그를 프리팹으로 저장합니다.</summary>
        public GameObject CreateRagdollPrefab(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                throw new InvalidOperationException("기존 에셋을 덮어쓸 수 없습니다: " + path);
            }

            using (SkinnedMeshCutRagdoll ragdoll = SkinnedMeshCutRagdoll.Prepare(
                m_source,
                AuthoringSource(),
                RagdollAuthorBone,
                SkinnedMeshCutRagdollMode.Generate,
                null,
                m_ragdollMass,
                m_ragdollTips))
            {
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(ragdoll.Root, path);

                if (prefab == null)
                {
                    throw new InvalidOperationException("래그돌 프리팹 저장 실패: " + path);
                }

                return prefab;
            }
        }

        /// <summary>원본 본을 현재 작성 포즈의 본으로 변환합니다.</summary>
        private Transform RagdollAuthorBone(Transform original)
        {
            return m_previewStage != null ? m_previewStage.IncludePath(original) : original;
        }

        /// <summary>준비된 래그돌을 출력 메시 범위에 맞춰 복사합니다.</summary>
        private void CopyPreparedRagdoll(GameObject root, Dictionary<Transform, Transform> map, Matrix4x4 frame, string suffix)
        {
            if (m_preparedRagdoll == null)
            {
                return;
            }

            SkinnedMeshCutEngine.Settings settings = BuildSettings();
            Plane plane = new Plane(
                frame.inverse.MultiplyVector(settings.Rotation * Vector3.forward),
                frame.inverse.MultiplyPoint3x4(settings.Origin));
            int start = m_colliderMeshes.Count;
            m_preparedRagdoll.CopyTo(
                root,
                map,
                bone => CloneTransformPath(bone, root.transform, frame.inverse, map),
                plane,
                m_colliderMeshes);

            for (int i = start; i < m_colliderMeshes.Count; i++)
            {
                m_colliderMeshes[i].name = m_outputName + "_" + suffix + "_Collider_" + (i - start);
            }
        }
    }
}

