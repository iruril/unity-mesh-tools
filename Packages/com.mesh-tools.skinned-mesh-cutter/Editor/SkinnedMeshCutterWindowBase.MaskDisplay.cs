using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MeshTools.Dismemberment.Editor
{
    public partial class SkinnedMeshCutterWindowBase
    {
        [SerializeField]
        protected int m_maskDisplayMode;
        // 열린 창의 보기 설정을 이전할 때까지 기존 직렬화 토글 값을 유지한다.
        [SerializeField]
        protected bool m_hidePaintedMask;
        [SerializeField]
        protected bool m_hideCutMask;
        [SerializeField]
        protected bool m_hidePartMask;
        [SerializeField]
        protected bool m_hideRemainderMask;
        [SerializeField]
        protected bool m_hideUnassignedMask;
        [SerializeField]
        protected bool m_maskRectangle;
        [SerializeField]
        protected bool m_maskHelpExpanded;
        protected static readonly string[] MaskDisplayLabels =
        {
            "표면",
            "표면 + 와이어",
            "와이어만"
        };
        protected static readonly string[] MaskToolLabels =
        {
            "브러시",
            "사각 영역"
        };
        protected Mesh m_maskDisplayMesh;
        protected Material m_maskDepthMaterial;
        protected Material[] m_maskDepthMaterials;
        protected int[] m_maskWireIndices;
        protected bool m_maskDisplayDirty = true;
        protected Vector2 m_maskRectangleStart;
        protected Vector2 m_maskRectangleEnd;
        protected bool m_maskRectangleDragging;
        protected readonly int[] m_maskCounts = new int[4];
        private int m_displayPoseVersion = -1;
        private int m_visibleMaskRevision = -1;
        private int m_visibleMaskFlags = -1;
        private SkinnedMeshCutBrushSurface m_visibleMaskSurface;

        /// <summary>마스크별 표시·숨김과 투명도 설정을 표시합니다.</summary>
        protected void DrawMaskDisplaySettings()
        {
            UpgradeMaskVisibility();
            EditorGUILayout.LabelField("화면 표시", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!m_maskBrushEnabled))
            {
                m_maskDisplayMode = GUILayout.Toolbar(m_maskDisplayMode, MaskDisplayLabels);
                m_showMask = EditorGUILayout.ToggleLeft("영역별 색상 표시", m_showMask);
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("영역별 표시", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("모두 표시", EditorStyles.miniButtonLeft))
                    {
                        SetMaskVisibility(false, false, false, false);
                    }

                    if (GUILayout.Button("칠한 영역만", EditorStyles.miniButtonMid))
                    {
                        SetMaskVisibility(false, false, false, true);
                    }

                    if (GUILayout.Button("모두 숨김", EditorStyles.miniButtonRight))
                    {
                        SetMaskVisibility(true, true, true, true);
                    }
                }

                System.Array.Clear(m_maskCounts, 0, m_maskCounts.Length);

                if (m_triangleMask != null)
                {
                    foreach (SkinnedMeshCutMaskMode mode in m_triangleMask)
                    {
                        if ((int)mode >= 0 && (int)mode < m_maskCounts.Length)
                        {
                            m_maskCounts[(int)mode]++;
                        }
                    }
                }

                if ((m_triangleMask == null || m_triangleMask.Length == 0) && m_source != null
                    && m_source.sharedMesh != null)
                {
                    for (int submesh = 0; submesh < m_source.sharedMesh.subMeshCount; submesh++)
                    {
                        m_maskCounts[0] += (int)m_source.sharedMesh.GetIndexCount(submesh) / 3;
                    }
                }

                bool cut = DrawMaskVisibilityRow(SkinnedMeshCutMaskMode.Cut, m_hideCutMask);
                bool part = DrawMaskVisibilityRow(SkinnedMeshCutMaskMode.KeepPart, m_hidePartMask);
                bool remainder = DrawMaskVisibilityRow(SkinnedMeshCutMaskMode.KeepRemainder, m_hideRemainderMask);
                bool unassigned = DrawMaskVisibilityRow(SkinnedMeshCutMaskMode.Auto, m_hideUnassignedMask);

                if (cut != m_hideCutMask || part != m_hidePartMask || remainder != m_hideRemainderMask
                    || unassigned != m_hideUnassignedMask)
                {
                    SetMaskVisibility(cut, part, remainder, unassigned);
                }

                if (m_brushSurface != null)
                {
                    int hidden = 0;

                    foreach (bool value in m_brushSurface.HiddenTriangles)
                    {
                        if (value)
                        {
                            hidden++;
                        }
                    }

                    EditorGUILayout.LabelField(
                        $"표시 {m_brushSurface.TriangleCount - hidden:N0}면 · 숨김 {hidden:N0}면",
                        EditorStyles.miniLabel);

                    if (hidden == m_brushSurface.TriangleCount && hidden > 0)
                    {
                        EditorGUILayout.HelpBox("표시할 면이 없습니다. ‘모두 표시’를 누르면 다시 칠할 수 있습니다.", MessageType.None);
                    }
                }

                if (m_hideCutMask || m_hidePartMask || m_hideRemainderMask || m_hideUnassignedMask)
                {
                    EditorGUILayout.HelpBox("숨긴 영역은 선택에서 제외됩니다. 칠하거나 지운 면의 표시 상태는 드래그를 놓으면 반영됩니다.", MessageType.None);
                }
            }

            if (m_maskBrushEnabled && m_previewStage == null && GetSourceError() == null)
            {
                OpenPosePreview();
            }
        }

        /// <summary>한 마스크 종류의 색상과 가시성 조작을 표시합니다.</summary>
        protected bool DrawMaskVisibilityRow(SkinnedMeshCutMaskMode mode, bool hidden)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect swatch = GUILayoutUtility.GetRect(10f, 16f, GUILayout.Width(10f));
                Color color = mode == SkinnedMeshCutMaskMode.Auto ? Color.gray : MaskColor(mode);
                color.a = 1f;
                EditorGUI.DrawRect(new Rect(swatch.x, swatch.y + 3f, 8f, 10f), color);
                EditorGUILayout.LabelField(mode == SkinnedMeshCutMaskMode.Auto ? "미지정 (자동)" : MaskLabels[(int)mode - 1]);
                GUILayout.Label($"{m_maskCounts[(int)mode]:N0}면", EditorStyles.miniLabel, GUILayout.Width(68f));

                return !GUILayout.Toggle(!hidden, hidden ? "숨김" : "표시 중", EditorStyles.miniButton, GUILayout.Width(72f));
            }
        }

        /// <summary>기존 마스크 표시 설정을 현재 형식으로 변환합니다.</summary>
        protected void UpgradeMaskVisibility()
        {
            if (!m_hidePaintedMask)
            {
                return;
            }

            m_hideCutMask = m_hidePartMask = m_hideRemainderMask = true;
            m_hidePaintedMask = false;
        }

        /// <summary>선택한 마스크 종류의 표시 상태를 변경합니다.</summary>
        protected void SetMaskVisibility(bool hideCut, bool hidePart, bool hideRemainder, bool hideUnassigned)
        {
            EndMaskStroke();
            m_hidePaintedMask = false;
            m_hideCutMask = hideCut;
            m_hidePartMask = hidePart;
            m_hideRemainderMask = hideRemainder;
            m_hideUnassignedMask = hideUnassigned;
            m_maskDisplayDirty = m_maskOverlayDirty = true;
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>마스크 표시용 임시 메시와 재질을 해제합니다.</summary>
        protected void DisposeMaskDisplay()
        {
            if (m_previewStage != null)
            {
                m_previewStage.SetMaskDisplay(null, null);
            }

            if (m_maskDisplayMesh != null)
            {
                DestroyImmediate(m_maskDisplayMesh);
            }

            if (m_maskDepthMaterial != null)
            {
                DestroyImmediate(m_maskDepthMaterial);
            }

            m_displayPoseVersion = -1;
            m_visibleMaskSurface = null;
            m_maskDisplayMesh = null;
            m_maskDepthMaterial = null;
            m_maskDepthMaterials = null;
            m_maskWireIndices = null;
            m_maskDisplayDirty = true;
        }

        /// <summary>숨김 설정을 브러시 표면과 미리보기 메시에 반영합니다.</summary>
        protected void UpdateMaskVisibility(SkinnedMeshCutBrushSurface surface)
        {
            UpgradeMaskVisibility();

            // 한 획 중에는 표시를 고정하고, 획 이후에도 마스크와 숨김 설정이 같으면 전체 면 순회를 생략한다.
            int flags = m_maskBrushEnabled
                ? (m_hideCutMask ? 1 : 0) | (m_hidePartMask ? 2 : 0)
                    | (m_hideRemainderMask ? 4 : 0) | (m_hideUnassignedMask ? 8 : 0)
                : 0;
            if (m_maskStrokeGroup >= 0 || (m_visibleMaskSurface == surface
                && m_visibleMaskRevision == m_maskRevision && m_visibleMaskFlags == flags))
            {
                return;
            }

            if (surface.SetHiddenTriangles(m_triangleMask, (flags & 1) != 0, (flags & 2) != 0,
                (flags & 4) != 0, (flags & 8) != 0))
            {
                m_maskDisplayDirty = m_maskOverlayDirty = true;
            }

            m_visibleMaskSurface = surface;
            m_visibleMaskRevision = m_maskRevision;
            m_visibleMaskFlags = flags;
        }

        /// <summary>Scene 뷰에 현재 마스크 표면을 그립니다.</summary>
        protected void DrawMaskDisplay(SkinnedMeshCutBrushSurface surface)
        {
            if (!m_maskBrushEnabled || m_previewStage == null)
            {
                return;
            }

            if (m_maskDisplayMesh == null)
            {
                m_maskDisplayMesh = Instantiate(surface.PoseMesh);
                m_maskDisplayMesh.name = "Cut mask visible surface";
                m_maskDisplayMesh.hideFlags = HideFlags.HideAndDontSave;
                m_maskDisplayMesh.MarkDynamic();
                m_displayPoseVersion = surface.PoseVersion;
                m_maskDisplayDirty = true;
            }

            if (m_displayPoseVersion != surface.PoseVersion)
            {
                m_maskDisplayMesh.vertices = surface.PoseMesh.vertices;
                m_maskDisplayMesh.normals = surface.PoseMesh.normals;
                m_maskDisplayMesh.tangents = surface.PoseMesh.tangents;
                m_maskDisplayMesh.bounds = surface.PoseMesh.bounds;
                m_displayPoseVersion = surface.PoseVersion;
            }

            if (m_maskDisplayDirty)
            {
                var edges = new HashSet<ulong>();
                var wire = new List<int>();
                int triangle = 0;

                for (int submesh = 0; submesh < surface.SourceMesh.subMeshCount; submesh++)
                {
                    var indices = new List<int>();
                    int count = (int)surface.SourceMesh.GetIndexCount(submesh) / 3;

                    for (int i = 0; i < count; i++, triangle++)
                    {
                        if (surface.HiddenTriangles[triangle])
                        {
                            continue;
                        }

                        for (int j = 0; j < 3; j++)
                        {
                            int a = surface.Triangles[triangle * 3 + j];
                            int b = surface.Triangles[triangle * 3 + (j + 1) % 3];
                            indices.Add(a);
                            ulong edge = ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);

                            if (edges.Add(edge))
                            {
                                wire.Add(a);
                                wire.Add(b);
                            }
                        }
                    }

                    m_maskDisplayMesh.SetTriangles(indices, submesh, false);
                }

                m_maskWireIndices = wire.ToArray();
                m_maskDisplayDirty = false;
            }

            Material[] materials = m_previewStage.Renderer.sharedMaterials;

            if (m_maskDisplayMode == 2)
            {
                if (m_maskDepthMaterial == null)
                {
                    m_maskDepthMaterial = new Material(Shader.Find("Hidden/MeshTools/SkinnedCutWireDepth"))
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }

                if (m_maskDepthMaterials == null || m_maskDepthMaterials.Length != materials.Length)
                {
                    m_maskDepthMaterials = new Material[materials.Length];

                    for (int i = 0; i < materials.Length; i++)
                    {
                        m_maskDepthMaterials[i] = m_maskDepthMaterial;
                    }
                }

                materials = m_maskDepthMaterials;
            }

            m_previewStage.SetMaskDisplay(m_maskDisplayMesh, materials);

            if (m_maskDisplayMode > 0 && Event.current != null && Event.current.type == EventType.Repaint)
            {
                CompareFunction previous = Handles.zTest;

                using (new Handles.DrawingScope(new Color(.8f, .95f, 1f, .8f)))
                {
                    Handles.zTest = m_maskVisibleOnly ? CompareFunction.LessEqual : CompareFunction.Always;
                    Handles.DrawLines(surface.WorldVertices, m_maskWireIndices);
                }

                Handles.zTest = previous;
            }
        }

        /// <summary>사각형 선택 입력으로 삼각형 마스크를 변경합니다.</summary>
        protected bool HandleMaskRectangle(SceneView view, SkinnedMeshCutBrushSurface surface, int control)
        {
            if (!m_maskRectangle)
            {
                return false;
            }

            Event current = Event.current;

            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape
                && m_maskRectangleDragging)
            {
                EndMaskStroke();
                current.Use();
                view.Repaint();
            }

            if (current.alt || current.button > 0)
            {
                if (m_maskRectangleDragging && current.type == EventType.MouseUp && current.button == 0)
                {
                    EndMaskStroke();
                    current.Use();
                    view.Repaint();
                }

                return true;
            }

            if (current.type == EventType.MouseDown && current.button == 0
                && HandleUtility.nearestControl == control
                && GUIUtility.hotControl == 0)
            {
                BeginMaskStroke();
                m_maskRectangleStart = m_maskRectangleEnd = current.mousePosition;
                m_maskRectangleDragging = true;
                GUIUtility.hotControl = m_maskHotControl = control;
                current.Use();
            }

            if (m_maskRectangleDragging && current.type == EventType.MouseDrag)
            {
                m_maskRectangleEnd = current.mousePosition;
                current.Use();
                view.Repaint();
            }

            if (m_maskRectangleDragging && current.type == EventType.MouseUp && current.button == 0)
            {
                m_maskRectangleEnd = current.mousePosition;
                Vector3 a = view.camera.WorldToViewportPoint(HandleUtility.GUIPointToWorldRay(m_maskRectangleStart).GetPoint(1f));
                Vector3 b = view.camera.WorldToViewportPoint(HandleUtility.GUIPointToWorldRay(m_maskRectangleEnd).GetPoint(1f));
                Rect rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                PaintMaskTriangles(
                    surface.GetRectangleTriangles(rect, view.camera, m_maskVisibleOnly, m_maskConnected),
                    m_maskErase || current.shift ? SkinnedMeshCutMaskMode.Auto : m_maskPaintMode);
                EndMaskStroke();
                current.Use();
                view.Repaint();
            }

            if (m_maskRectangleDragging && current.type == EventType.Repaint)
            {
                Rect rect = Rect.MinMaxRect(
                    Mathf.Min(m_maskRectangleStart.x, m_maskRectangleEnd.x),
                    Mathf.Min(m_maskRectangleStart.y, m_maskRectangleEnd.y),
                    Mathf.Max(m_maskRectangleStart.x, m_maskRectangleEnd.x),
                    Mathf.Max(m_maskRectangleStart.y, m_maskRectangleEnd.y));
                Handles.BeginGUI();
                EditorGUI.DrawRect(rect, new Color(.1f, .65f, 1f, .15f));
                GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
                Handles.EndGUI();
            }

            return true;
        }
    }
}

