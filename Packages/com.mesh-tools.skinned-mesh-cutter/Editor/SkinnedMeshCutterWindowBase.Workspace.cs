using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public partial class SkinnedMeshCutterWindowBase
    {
        [SerializeField]
        protected int m_settingsTab = 1;
        [SerializeField]
        protected bool m_planeAdvancedExpanded;
        [SerializeField]
        protected bool m_defaultsExpanded;
        [SerializeField]
        protected bool m_uvProjectionExpanded;
        protected static readonly string[] SettingsTabs =
        {
            "소스·세트",
            "절단",
            "마스크",
            "단면·UV",
            "출력"
        };
        protected static readonly string[] PlaneToolLabels =
        {
            "이동",
            "회전",
            "폭·높이"
        };
        protected virtual string[] SettingsTabLabels => SettingsTabs;

        /// <summary>편집 탭을 전환하고 필요한 미리보기를 갱신합니다.</summary>
        protected void SetSettingsTab(int tab)
        {
            tab = Mathf.Clamp(tab, 0, SettingsTabLabels.Length - 1);

            if (tab == m_settingsTab)
            {
                return;
            }

            EndMaskStroke();

            if (tab != 2 && m_maskBrushEnabled)
            {
                m_maskBrushEnabled = false;
                UpdateStageResult();
            }

            m_settingsTab = tab;
            m_scroll = Vector2.zero;

            if (Event.current != null)
            {
                GUI.FocusControl(null);
            }

            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>절단·마스크·UV·출력 설정 탭을 표시합니다.</summary>
        protected void DrawSettingsTabs()
        {
            bool changed = GUI.changed;
            SetSettingsTab(GUILayout.Toolbar(m_settingsTab, SettingsTabLabels, GUILayout.Height(28f)));
            GUI.changed = changed;
        }

        /// <summary>보기 설정의 접기 상태와 내용을 표시합니다.</summary>
        protected static bool ViewFoldout(bool expanded, string label)
        {
            bool changed = GUI.changed;
            bool result = EditorGUILayout.Foldout(expanded, label, true);
            GUI.changed = changed;

            return result;
        }

        /// <summary>작성 포즈와 표시 모드를 조작하는 도구 모음을 그립니다.</summary>
        protected void DrawPreviewToolbar()
        {
            bool changed = GUI.changed;
            EditorGUI.BeginChangeCheck();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(m_maskBrushEnabled))
                {
                    m_showScenePreview = GUILayout.Toggle(m_showScenePreview, "평면·경계", GUILayout.Width(84f));
                    m_showResult = GUILayout.Toggle(m_showResult, "절단 결과", GUILayout.Width(84f));

                    using (new EditorGUI.DisabledScope(!m_showResult))
                    {
                        GUILayout.Label("벌림", GUILayout.Width(26f));
                        m_previewSeparation = Mathf.Max(0f, EditorGUILayout.FloatField(m_previewSeparation, GUILayout.Width(54f)));
                    }
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(m_source == null || m_source.sharedMesh == null
                        || (!PartOnlyAuthoring && EditorUtility.IsPersistent(m_source))))
                {
                    if (GUILayout.Button("Scene 보기", GUILayout.Width(82f)))
                    {
                        SceneView.GetWindow<SceneView>();

                        if (m_previewStage == null)
                        {
                            OpenPosePreview();
                        }
                        else
                        {
                            StageUtility.GoToStage(m_previewStage, false);
                        }

                        FrameCorpseModel();
                        Focus();
                    }
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                UpdateStageResult();
                SceneView.RepaintAll();
            }

            GUI.changed = changed;
        }

        /// <summary>미리보기 카메라가 현재 모델 범위를 비추도록 맞춥니다.</summary>
        protected void FrameCorpseModel()
        {
            SkinnedMeshRenderer source = AuthoringSource();
            Mesh baked = new Mesh();

            try
            {
                source.BakeMesh(baked, true);
                Vector3[] vertices = baked.vertices;

                if (vertices.Length == 0)
                {
                    return;
                }

                Bounds bounds = new Bounds(source.transform.TransformPoint(vertices[0]), Vector3.zero);

                foreach (Vector3 vertex in vertices)
                {
                    bounds.Encapsulate(source.transform.TransformPoint(vertex));
                }

                bounds.Expand(m_previewSeparation * 2f);
                SceneView.GetWindow<SceneView>().Frame(bounds, true);
            }
            finally
            {
                DestroyImmediate(baked);
            }
        }

        /// <summary>닫힌 단면 수와 절단 진단 상태를 표시합니다.</summary>
        protected void DrawPreviewStatus(string validation, bool includeCut = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("재생을 종료한 뒤 사용할 수 있습니다.", MessageType.Info);
            }
            else if (validation != null)
            {
                EditorGUILayout.HelpBox(validation, MessageType.Warning);
            }

            if (includeCut && m_previewError != null && m_previewError != validation)
            {
                EditorGUILayout.HelpBox(m_previewError, MessageType.Warning);
            }
            else if (includeCut && validation == null)
            {
                if (m_previewDirty)
                {
                    EditorGUILayout.LabelField("미리보기 갱신 대기 중…", EditorStyles.miniLabel);
                }
                else if (m_previewResult != null)
                {
                    EditorGUILayout.LabelField($"닫힌 단면 {m_previewResult.LoopCount}개 · 스킨 가중치 보존", EditorStyles.miniLabel);
                }
            }

            if (includeCut && m_previewResult != null && m_previewResult.RepairCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"작은 틈 {m_previewResult.RepairCount}곳 보정 · " + $"최대 {m_previewResult.MaximumRepairedGap * 1000f:F2}mm (주황색)",
                    MessageType.Info);
            }

            if (!string.IsNullOrEmpty(m_message))
            {
                EditorGUILayout.HelpBox(m_message, m_messageType);
            }
        }

        /// <summary>프로젝트 공통 기본값의 접기 영역을 표시합니다.</summary>
        protected void DrawDefaultsSettings()
        {
            EditorGUILayout.Space(6f);
            m_defaultsExpanded = ViewFoldout(m_defaultsExpanded, "생성 기본값 · 프로젝트 공통");

            if (m_defaultsExpanded)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (SkinnedMeshCutDefaults.instance.DrawSettings(HasPhysicsIntegration))
                    {
                        MarkPreviewDirty();
                    }
                }
            }
        }
    }
}
