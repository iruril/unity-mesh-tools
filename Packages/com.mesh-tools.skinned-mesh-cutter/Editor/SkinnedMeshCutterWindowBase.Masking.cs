using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MeshTools.Dismemberment.Editor
{
    public partial class SkinnedMeshCutterWindowBase
    {
        protected static readonly string[] MaskLabels =
        {
            "평면 절단",
            "파츠에 포함",
            "몸체에 유지"
        };
        protected static readonly string[] MaskActionLabels =
        {
            "칠하기",
            "지우기 · 자동 복원"
        };
        [SerializeField]
        protected SkinnedMeshCutMaskMode[] m_triangleMask = Array.Empty<SkinnedMeshCutMaskMode>();
        [SerializeField]
        protected Mesh m_maskSourceMesh;
        [SerializeField]
        protected int m_maskTopologyHash;
        [SerializeField]
        protected bool m_maskBrushEnabled;
        [SerializeField]
        protected SkinnedMeshCutMaskMode m_maskPaintMode = SkinnedMeshCutMaskMode.Cut;
        [SerializeField]
        protected float m_maskBrushRadius = 0.03f;
        [SerializeField]
        protected bool m_maskErase;
        [SerializeField]
        protected bool m_maskConnected;
        [SerializeField]
        protected bool m_maskVisibleOnly = true;
        [SerializeField]
        protected bool m_showMask = true;
        protected SkinnedMeshCutBrushSurface m_brushSurface;
        protected SkinnedMeshRenderer m_brushSource;
        protected Mesh m_maskOverlay;
        protected Material m_maskMaterial;
        protected int[] m_overlayTriangles = Array.Empty<int>();
        protected bool m_maskOverlayDirty = true;
        protected int m_maskStrokeGroup = -1;
        protected int m_maskHotControl;
        protected Vector3? m_previousMaskHit;
        protected int m_maskRevision;
        private Vector3[] m_overlayVertices;
        private int m_overlayPoseVersion = -1;
        private Mesh m_validatedMaskMesh;
        private int m_validatedMeshDirtyCount;
        private int m_validatedMaskHash;
        private string m_maskValidationError;

        /// <summary>브러시 모드와 크기 및 표면 선택 설정을 표시합니다.</summary>
        protected void DrawMaskSettings()
        {
            bool wasChanged = GUI.changed;
            EditorGUI.BeginChangeCheck();
            bool enabled = GUILayout.Toggle(
                m_maskBrushEnabled,
                m_maskBrushEnabled ? "Scene 마스크 편집 중 · 클릭하여 종료" : "Scene 마스크 편집 시작",
                "Button",
                GUILayout.Height(28f));

            if (enabled != m_maskBrushEnabled)
            {
                EndMaskStroke();
                m_maskBrushEnabled = enabled;
                m_showScenePreview |= enabled;
                UpdateStageResult();
                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(!m_maskBrushEnabled))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("칠할 영역", EditorStyles.boldLabel);
                    int mode = Mathf.Clamp((int)m_maskPaintMode - 1, 0, 2);
                    m_maskPaintMode = (SkinnedMeshCutMaskMode)(GUILayout.Toolbar(mode, MaskLabels, GUILayout.Height(24f)) + 1);
                    string description = m_maskPaintMode == SkinnedMeshCutMaskMode.Cut
                        ? "빨강 · 평면과 만나는 이 영역을 절단합니다."
                        : m_maskPaintMode == SkinnedMeshCutMaskMode.KeepPart
                        ? "파랑 · 자르지 않고 분리 파츠에 포함합니다."
                        : "초록 · 자르지 않고 몸체에 남깁니다.";
                    EditorGUILayout.HelpBox(description, MessageType.None);
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("칠하기 도구", EditorStyles.boldLabel);
                    int tool = GUILayout.Toolbar(m_maskRectangle ? 1 : 0, MaskToolLabels);

                    if (m_maskRectangle != (tool == 1))
                    {
                        EndMaskStroke();
                    }

                    m_maskRectangle = tool == 1;
                    m_maskErase = GUILayout.Toolbar(m_maskErase ? 1 : 0, MaskActionLabels) == 1;

                    m_maskConnected = EditorGUILayout.ToggleLeft(
                        new GUIContent("연결된 메시 전체 포함", "선택된 면과 이어진 원본 파츠를 상자 밖과 뒷면까지 포함합니다."),
                        m_maskConnected);

                    if (!m_maskRectangle)
                    {

                        using (new EditorGUI.DisabledScope(m_maskConnected))
                        {
                            m_maskBrushRadius = Mathf.Max(0.0001f, EditorGUILayout.FloatField("브러시 반경 (월드)", m_maskBrushRadius));
                        }
                    }

                    using (new EditorGUI.DisabledScope(!m_maskRectangle && m_maskConnected))
                    {
                        m_maskVisibleOnly = !EditorGUILayout.ToggleLeft(
                            new GUIContent("가려진 표면도 포함", "소스 메시에서 다른 면 뒤에 가려진 파츠도 선택합니다. 표시 설정에서 숨긴 영역은 제외됩니다."),
                            !m_maskVisibleOnly);
                    }

                    EditorGUILayout.HelpBox(
                        m_maskRectangle
                            ? "드래그 후 놓기: 일괄 지정 · Esc: 취소 · Shift: 지우기 · Alt: 시점 이동"
                            : "드래그: 칠하기 · Shift: 지우기 · [ / ]: 반경 · Alt: 시점 이동",
                        MessageType.None);
                }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawMaskDisplaySettings();
            }

            m_maskHelpExpanded = EditorGUILayout.Foldout(m_maskHelpExpanded, "사용법 · 마스크 초기화", true);

            if (m_maskHelpExpanded)
            {
                EditorGUILayout.HelpBox(
                    "목은 평면 절단, 긴 머리카락은 파츠에 포함으로 지정하세요. " + "평면 절단 영역이 있으면 해당 닫힌 단면만 자릅니다. 미지정 면은 자동 판정합니다. " + "표시 여부는 편집 화면에만 적용됩니다. 한 번의 드래그는 Ctrl+Z 한 번으로 되돌립니다.",
                    MessageType.None);

                if (GUILayout.Button("전체 마스크 지우기 (Undo 가능)"))
                {
                    ClearTriangleMask();
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }

            GUI.changed = wasChanged;
        }

        /// <summary>삼각형 마스크와 원본 메시의 구조가 일치하는지 캐시를 통해 검증합니다.</summary>
        protected string GetMaskError()
        {
            return ValidateMaskTopology(false);
        }

        /// <summary>메시 변경 시 해시를 갱신하고 획 시작과 저장 직전에는 인덱스를 다시 검증합니다.</summary>
        protected string ValidateMaskTopology(bool force)
        {
            if (m_triangleMask == null || m_triangleMask.Length == 0)
            {
                return null;
            }

            Mesh mesh = m_source != null ? m_source.sharedMesh : null;
            if (mesh == null || !mesh.isReadable || mesh != m_maskSourceMesh)
            {
                return "원본 메시의 삼각형 구성이 마스크 작성 이후 변경됐습니다. 전체 마스크를 지우고 다시 작성하세요.";
            }

            int dirtyCount = EditorUtility.GetDirtyCount(mesh);
            if (force || mesh != m_validatedMaskMesh || dirtyCount != m_validatedMeshDirtyCount
                || m_maskTopologyHash != m_validatedMaskHash)
            {
                // GUI 이벤트마다 전체 인덱스 배열을 읽지 않는다. 내보내기와 새 획에서는 캐시를 신뢰하지 않는다.
                m_maskValidationError = SkinnedMeshCutBrushSurface.GetTopologyHash(mesh) == m_maskTopologyHash
                    ? null : "원본 메시의 삼각형 구성이 마스크 작성 이후 변경됐습니다. 전체 마스크를 지우고 다시 작성하세요.";
                m_validatedMaskMesh = mesh;
                m_validatedMeshDirtyCount = dirtyCount;
                m_validatedMaskHash = m_maskTopologyHash;
            }

            return m_maskValidationError;
        }

        /// <summary>현재 스킨과 자세에 대응하는 브러시 표면 캐시를 반환합니다.</summary>
        protected SkinnedMeshCutBrushSurface BrushSurface()
        {
            SkinnedMeshRenderer source = AuthoringSource();

            if (m_brushSurface == null || m_brushSource != source
                || m_brushSurface.SourceMesh != source.sharedMesh)
            {
                m_brushSurface?.Dispose();
                m_brushSource = source;
                m_brushSurface = new SkinnedMeshCutBrushSurface(source);
                DisposeMaskDisplay();
                m_maskOverlayDirty = true;
            }

            return m_brushSurface;
        }

        /// <summary>프로젝트 변경으로 무효해진 소스와 브러시 캐시를 갱신합니다.</summary>
        protected void OnProjectChange()
        {
            DisposeMaskResources();
            m_validatedMaskMesh = null;
            MarkPreviewDirty();
        }

        /// <summary>브러시 표면 캐시와 임시 메시를 해제합니다.</summary>
        protected void DisposeMaskResources()
        {
            EndMaskStroke();
            DisposeMaskDisplay();
            m_brushSurface?.Dispose();
            m_brushSurface = null;
            m_brushSource = null;

            if (m_maskOverlay != null)
            {
                DestroyImmediate(m_maskOverlay);
            }

            if (m_maskMaterial != null)
            {
                DestroyImmediate(m_maskMaterial);
            }

            m_maskOverlay = null;
            m_maskMaterial = null;
            m_maskOverlayDirty = true;
        }

        /// <summary>브러시 획의 Undo 기록과 삼각형 선택을 시작합니다.</summary>
        protected void BeginMaskStroke()
        {
            if (m_maskStrokeGroup >= 0)
            {
                return;
            }

            string error = ValidateMaskTopology(true);
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            Undo.IncrementCurrentGroup();
            m_maskStrokeGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paint skinned cut mask");
            Undo.RegisterCompleteObjectUndo(this, "Paint skinned cut mask");
        }

        /// <summary>브러시 획을 확정하고 미리보기를 갱신합니다.</summary>
        protected void EndMaskStroke()
        {
            if (m_maskHotControl != 0 && GUIUtility.hotControl == m_maskHotControl)
            {
                GUIUtility.hotControl = 0;
            }

            m_maskHotControl = 0;
            m_maskRectangleDragging = false;
            m_previousMaskHit = null;

            if (m_maskStrokeGroup >= 0)
            {
                Undo.CollapseUndoOperations(m_maskStrokeGroup);
                m_maskStrokeGroup = -1;
                Undo.IncrementCurrentGroup();
            }
        }

        /// <summary>선택한 삼각형에 현재 브러시 마스크를 적용합니다.</summary>
        public void PaintMaskTriangles(int[] triangles, SkinnedMeshCutMaskMode mode)
        {
            if (triangles == null || triangles.Length == 0)
            {
                return;
            }

            string error = GetMaskError();

            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            SkinnedMeshCutBrushSurface surface = BrushSurface();

            if (mode < SkinnedMeshCutMaskMode.Auto || mode > SkinnedMeshCutMaskMode.KeepRemainder
                || Array.Exists(triangles, index => index < 0 || index >= surface.TriangleCount))
            {
                throw new ArgumentOutOfRangeException(nameof(triangles));
            }

            bool changed = false;
            foreach (int triangle in triangles)
            {
                SkinnedMeshCutMaskMode current = m_triangleMask != null && m_triangleMask.Length != 0
                    ? m_triangleMask[triangle] : SkinnedMeshCutMaskMode.Auto;
                changed |= current != mode;
            }

            if (!changed)
            {
                return;
            }

            bool standalone = m_maskStrokeGroup < 0;

            if (standalone)
            {
                BeginMaskStroke();
            }

            if (m_triangleMask == null || m_triangleMask.Length == 0)
            {
                m_triangleMask = new SkinnedMeshCutMaskMode[surface.TriangleCount];
                m_maskSourceMesh = surface.SourceMesh;
                m_maskTopologyHash = surface.TopologyHash;
            }

            foreach (int triangle in triangles)
            {
                m_triangleMask[triangle] = mode;
            }

            m_maskRevision++;
            m_maskOverlayDirty = true;
            MarkPreviewDirty();
            Repaint();

            if (standalone)
            {
                EndMaskStroke();
            }
        }

        /// <summary>모든 삼각형을 자동 분류 마스크로 되돌립니다.</summary>
        public void ClearTriangleMask()
        {
            DisposeMaskResources();
            Undo.RegisterCompleteObjectUndo(this, "Clear skinned cut mask");
            m_triangleMask = Array.Empty<SkinnedMeshCutMaskMode>();
            m_maskSourceMesh = m_source != null ? m_source.sharedMesh : null;
            m_maskTopologyHash = 0;
            m_maskOverlayDirty = true;
            MarkPreviewDirty();
            Repaint();
        }

        /// <summary>마스크 종류에 대응하는 표시 색상을 반환합니다.</summary>
        protected static Color MaskColor(SkinnedMeshCutMaskMode mode)
        {
            return mode == SkinnedMeshCutMaskMode.Cut
                ? new Color(1f, 0.15f, 0.1f, 0.4f)
                : mode == SkinnedMeshCutMaskMode.KeepPart
                ? new Color(0.1f, 0.45f, 1f, 0.4f)
                : new Color(0.1f, 0.9f, 0.3f, 0.4f);
        }

        /// <summary>Scene 뷰에서 브러시 입력과 마스크 미리보기를 처리합니다.</summary>
        protected void DrawMaskScene(SceneView view)
        {
            if ((!m_maskBrushEnabled
                    && (!m_showMask || m_triangleMask == null || m_triangleMask.Length == 0))
                || m_source.sharedMesh == null
                || !m_source.sharedMesh.isReadable
                || GetMaskError() != null)
            {
                return;
            }

            SkinnedMeshCutBrushSurface surface = BrushSurface();
            surface.RefreshPoseIfChanged();
            UpdateMaskVisibility(surface);

            if (Event.current.type == EventType.Repaint)
            {
                DrawMaskDisplay(surface);
            }

            if (m_showMask
                && (m_maskBrushEnabled || m_previewStage == null || !m_showResult || m_previewResult == null)
                && Event.current.type == EventType.Repaint)
            {
                DrawMaskOverlay(surface);
            }

            if (!m_maskBrushEnabled)
            {
                return;
            }

            Event current = Event.current;
            int control = GUIUtility.GetControlID("Skinned cut mask".GetHashCode(), FocusType.Passive);

            if (current.type == EventType.Layout && !current.alt)
            {
                HandleUtility.AddDefaultControl(control);
            }

            if (HandleMaskRectangle(view, surface, control))
            {
                return;
            }

            if (current.type == EventType.MouseUp && current.button == 0 && m_maskHotControl != 0)
            {
                EndMaskStroke();
                current.Use();

                return;
            }

            if (current.alt || current.button > 0)
            {
                return;
            }

            if (current.type == EventType.KeyDown && !current.control && !current.command
                && (current.keyCode == KeyCode.LeftBracket || current.keyCode == KeyCode.RightBracket))
            {
                m_maskBrushRadius = Mathf.Max(0.0001f, m_maskBrushRadius * (current.keyCode == KeyCode.LeftBracket ? 0.8f : 1.25f));
                current.Use();
                Repaint();
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);

            if (!surface.Raycast(ray, out int triangle, out Vector3 hit, out Vector3 normal))
            {
                m_previousMaskHit = null;

                return;
            }

            Handles.color = m_maskErase || current.shift ? Color.white : MaskColor(m_maskPaintMode);
            Handles.DrawWireDisc(hit, normal, m_maskBrushRadius);

            if (current.type == EventType.MouseMove)
            {
                view.Repaint();
            }

            bool down = current.type == EventType.MouseDown && current.button == 0;
            bool drag = current.type == EventType.MouseDrag && current.button == 0 && m_maskHotControl != 0;

            if ((!down && !drag) || (down && HandleUtility.nearestControl != control)
                || (GUIUtility.hotControl != 0 && GUIUtility.hotControl != m_maskHotControl))
            {
                return;
            }

            if (down)
            {
                BeginMaskStroke();
                GUIUtility.hotControl = control;
                m_maskHotControl = control;
            }

            SkinnedMeshCutMaskMode mode = m_maskErase || current.shift ? SkinnedMeshCutMaskMode.Auto : m_maskPaintMode;

            if (m_maskConnected)
            {
                PaintMaskTriangles(surface.GetConnectedTriangles(triangle), mode);
            }
            else
            {
                Vector3 start = m_previousMaskHit ?? hit;
                int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(start, hit) / (m_maskBrushRadius * 0.4f)), 1, 64);
                HashSet<int> selected = new HashSet<int>();

                for (int step = 1; step <= steps; step++)
                {
                    Vector3 center = Vector3.Lerp(start, hit, (float)step / steps);
                    selected.UnionWith(surface.GetBrushTriangles(triangle, center, m_maskBrushRadius, view.camera, m_maskVisibleOnly));
                }

                int[] triangles = new int[selected.Count];
                selected.CopyTo(triangles);
                PaintMaskTriangles(triangles, mode);
            }

            m_previousMaskHit = hit;
            current.Use();
            view.Repaint();
        }

        /// <summary>선택한 삼각형의 마스크 색상을 덧그립니다.</summary>
        protected void DrawMaskOverlay(SkinnedMeshCutBrushSurface surface)
        {
            if (m_triangleMask == null || m_triangleMask.Length == 0)
            {
                return;
            }

            if (m_maskOverlay == null)
            {
                m_maskOverlay = new Mesh
                {
                    name = "Cut mask overlay",
                    hideFlags = HideFlags.HideAndDontSave
                };
                m_maskOverlay.indexFormat = IndexFormat.UInt32;
                m_maskOverlay.MarkDynamic();
            }

            if (m_maskMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/MeshTools/SkinnedCutMask");

                if (shader == null)
                {
                    return;
                }

                m_maskMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            if (m_maskOverlayDirty)
            {
                List<int> triangles = new List<int>();
                List<Color> colors = new List<Color>();

                for (int i = 0; i < m_triangleMask.Length; i++)
                {
                    if (m_triangleMask[i] == SkinnedMeshCutMaskMode.Auto || surface.HiddenTriangles[i])
                    {
                        continue;
                    }

                    for (int corner = 0; corner < 3; corner++)
                    {
                        triangles.Add(surface.Triangles[i * 3 + corner]);
                        colors.Add(MaskColor(m_triangleMask[i]));
                    }
                }

                m_overlayTriangles = triangles.ToArray();
                m_maskOverlay.Clear();
                m_overlayVertices = new Vector3[m_overlayTriangles.Length];
                m_maskOverlay.vertices = m_overlayVertices;
                m_overlayPoseVersion = -1;
                m_maskOverlay.SetColors(colors);
                int[] indices = new int[m_overlayTriangles.Length];

                for (int i = 0; i < indices.Length; i++)
                {
                    indices[i] = i;
                }

                m_maskOverlay.triangles = indices;
                m_maskOverlayDirty = false;
            }

            if (m_overlayPoseVersion != surface.PoseVersion)
            {
                for (int i = 0; i < m_overlayVertices.Length; i++)
                {
                    m_overlayVertices[i] = surface.WorldVertices[m_overlayTriangles[i]];
                }

                m_maskOverlay.vertices = m_overlayVertices;
                m_maskOverlay.RecalculateBounds();
                m_overlayPoseVersion = surface.PoseVersion;
            }

            if (m_maskMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(m_maskOverlay, Matrix4x4.identity);
            }
        }
    }
}

