using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>
    /// Independent per-renderer painting session for custom editor windows. Create with CreateInstance,
    /// bind the authoring renderer, consume changes, and destroy the session when its owner closes.
    /// </summary>
    public class SkinnedMeshCutMaskEditor : SkinnedMeshCutterWindowBase
    {
        private static readonly string[] DisplayLabels = { "표면", "표면 + 와이어", "와이어만" };
        private int m_companionPoseVersion = -1;
        private SkinnedMeshRenderer m_boundSource;

        public SkinnedMeshRenderer Source => m_source;
        public Mesh MaskMesh => m_maskSourceMesh;
        public int TopologyHash => m_maskTopologyHash;
        public SkinnedMeshCutMaskMode[] TriangleMask => m_triangleMask != null
            ? (SkinnedMeshCutMaskMode[])m_triangleMask.Clone() : Array.Empty<SkinnedMeshCutMaskMode>();
        public bool IsPainting => m_maskBrushEnabled;
        public Material[] DisplayMaterials
        {
            get
            {
                Material[] materials = m_source != null ? m_source.sharedMaterials : Array.Empty<Material>();
                if (m_maskDisplayMode != 2) return materials;
                if (m_maskDepthMaterial == null)
                    m_maskDepthMaterial = new Material(Shader.Find("Hidden/MeshTools/SkinnedCutWireDepth"))
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                if (m_maskDepthMaterials == null || m_maskDepthMaterials.Length != materials.Length)
                {
                    m_maskDepthMaterials = new Material[materials.Length];
                    for (int i = 0; i < materials.Length; i++) m_maskDepthMaterials[i] = m_maskDepthMaterial;
                }
                return m_maskDepthMaterials;
            }
        }

        protected override void OnEnable()
        {
            hideFlags = HideFlags.HideAndDontSave;
            Undo.undoRedoPerformed += OnMaskUndoRedo;
            m_previewDirty = false;
        }

        protected override void OnDisable()
        {
            Undo.undoRedoPerformed -= OnMaskUndoRedo;
            DisposeMaskResources();
        }

        protected override void OnGUI() { }

        public void Bind(SkinnedMeshRenderer author, Mesh maskMesh, int topologyHash, SkinnedMeshCutMaskMode[] mask)
        {
            StopPainting();
            DisposeMaskResources();
            Undo.ClearUndo(this);
            m_source = m_boundSource = author;
            m_maskSourceMesh = maskMesh;
            m_maskTopologyHash = topologyHash;
            m_triangleMask = mask != null ? (SkinnedMeshCutMaskMode[])mask.Clone() : Array.Empty<SkinnedMeshCutMaskMode>();
            m_maskRevision++;
            m_previewDirty = false;
        }

        public void RebindSource(SkinnedMeshRenderer author)
        {
            if (m_source == author) return;
            DisposeMaskResources();
            m_source = m_boundSource = author;
            m_companionPoseVersion = -1;
            MarkPreviewDirty();
        }

        public bool ConsumeChanges()
        {
            bool changed = m_previewDirty;
            m_previewDirty = false;
            return changed;
        }

        public void StopPainting()
        {
            EndMaskStroke();
            if (!m_maskBrushEnabled) return;
            m_maskBrushEnabled = false;
            MarkPreviewDirty();
        }

        private void OnMaskUndoRedo()
        {
            // Undo 이전 스테이지의 임시 렌더러 참조로 되돌아가지 않는다.
            m_source = m_boundSource;
            m_maskRevision++;
            DisposeMaskResources();
            MarkPreviewDirty();
        }

        private string MaskError()
        {
            if (m_source == null || m_source.sharedMesh == null) return "마스크를 편집할 메쉬가 없습니다.";
            if (!m_source.sharedMesh.isReadable) return "메쉬의 Read/Write Enabled를 켜세요.";
            string error = GetMaskError();
            if (error != null) return error;
            int triangles = 0;
            for (int i = 0; i < m_source.sharedMesh.subMeshCount; i++)
                triangles += (int)m_source.sharedMesh.GetIndexCount(i) / 3;
            return m_triangleMask != null && m_triangleMask.Length != 0 && m_triangleMask.Length != triangles
                ? "메쉬의 삼각형 수가 저장한 마스크와 다릅니다. 전체 마스크를 지우고 다시 작성하세요." : null;
        }

        public void DrawSettings()
        {
            bool wasChanged = GUI.changed;
            EditorGUI.BeginChangeCheck();
            string error = MaskError();
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
            using (new EditorGUI.DisabledScope(error != null))
            {
                bool painting = GUILayout.Toggle(m_maskBrushEnabled,
                    m_maskBrushEnabled ? "Scene 마스크 편집 종료" : "Scene 마스크 편집 시작", "Button", GUILayout.Height(28f));
                if (painting != m_maskBrushEnabled)
                {
                    EndMaskStroke();
                    m_maskBrushEnabled = painting;
                }
                using (new EditorGUI.DisabledScope(!m_maskBrushEnabled))
                {
                    m_maskPaintMode = (SkinnedMeshCutMaskMode)(GUILayout.Toolbar(
                        Mathf.Clamp((int)m_maskPaintMode - 1, 0, 2), MaskLabels) + 1);
                    bool rectangle = GUILayout.Toolbar(m_maskRectangle ? 1 : 0, MaskToolLabels) == 1;
                    if (rectangle != m_maskRectangle) EndMaskStroke();
                    m_maskRectangle = rectangle;
                    m_maskErase = GUILayout.Toolbar(m_maskErase ? 1 : 0, MaskActionLabels) == 1;
                    m_maskConnected = EditorGUILayout.ToggleLeft("연결된 메쉬 전체 포함", m_maskConnected);
                    if (!m_maskRectangle)
                        using (new EditorGUI.DisabledScope(m_maskConnected))
                            m_maskBrushRadius = Mathf.Max(.0001f, EditorGUILayout.FloatField("브러시 반경 (월드)", m_maskBrushRadius));
                    using (new EditorGUI.DisabledScope(!m_maskRectangle && m_maskConnected))
                        m_maskVisibleOnly = !EditorGUILayout.ToggleLeft("가려진 표면도 포함", !m_maskVisibleOnly);

                    m_maskDisplayMode = GUILayout.Toolbar(Mathf.Clamp(m_maskDisplayMode, 0, 2), DisplayLabels);
                    m_showMask = EditorGUILayout.ToggleLeft("영역별 색상 표시", m_showMask);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("모두 표시")) SetDisplayVisibility(false, false, false, false);
                        if (GUILayout.Button("칠한 영역만")) SetDisplayVisibility(false, false, false, true);
                        if (GUILayout.Button("모두 숨김")) SetDisplayVisibility(true, true, true, true);
                    }
                    Array.Clear(m_maskCounts, 0, m_maskCounts.Length);
                    if (m_triangleMask != null && m_triangleMask.Length > 0)
                    {
                        foreach (var mode in m_triangleMask)
                            if ((int)mode >= 0 && (int)mode < m_maskCounts.Length) m_maskCounts[(int)mode]++;
                    }
                    else if (m_source != null && m_source.sharedMesh != null)
                        for (int i = 0; i < m_source.sharedMesh.subMeshCount; i++)
                            m_maskCounts[0] += (int)m_source.sharedMesh.GetIndexCount(i) / 3;
                    bool cut = DrawMaskVisibilityRow(SkinnedMeshCutMaskMode.Cut, m_hideCutMask);
                    bool part = DrawMaskVisibilityRow(SkinnedMeshCutMaskMode.KeepPart, m_hidePartMask);
                    bool remainder = DrawMaskVisibilityRow(SkinnedMeshCutMaskMode.KeepRemainder, m_hideRemainderMask);
                    bool unassigned = DrawMaskVisibilityRow(SkinnedMeshCutMaskMode.Auto, m_hideUnassignedMask);
                    if (cut != m_hideCutMask || part != m_hidePartMask || remainder != m_hideRemainderMask || unassigned != m_hideUnassignedMask)
                        SetDisplayVisibility(cut, part, remainder, unassigned);
                    EditorGUILayout.HelpBox(m_maskRectangle
                        ? "드래그 후 놓기: 지정 · Esc: 취소 · Shift: 지우기 · Alt: 시점 이동"
                        : "드래그: 칠하기 · Shift: 지우기 · [ / ]: 반경 · Alt: 시점 이동", MessageType.None);
                }
            }
            if (GUILayout.Button("전체 마스크 지우기 (Undo 가능)")) ClearTriangleMask();
            if (EditorGUI.EndChangeCheck())
            {
                m_maskDisplayDirty = m_maskOverlayDirty = true;
                MarkPreviewDirty();
            }
            GUI.changed = wasChanged;
        }

        private void SetDisplayVisibility(bool cut, bool part, bool remainder, bool unassigned)
        {
            SetMaskVisibility(cut, part, remainder, unassigned);
            MarkPreviewDirty();
        }

        // 이 메시는 편집기가 소유한다. 외부 표시는 복제하거나 참조만 하고 해제하지 않는다.
        public Mesh DisplayMesh
        {
            get
            {
                if (!IsPainting || MaskError() != null) return null;
                SkinnedMeshCutBrushSurface surface = BrushSurface();
                surface.RefreshPoseIfChanged();
                UpdateMaskVisibility(surface);
                if (m_maskDisplayMesh == null)
                {
                    m_maskDisplayMesh = Instantiate(surface.PoseMesh);
                    m_maskDisplayMesh.name = "Cut mask visible surface";
                    m_maskDisplayMesh.hideFlags = HideFlags.HideAndDontSave;
                    m_maskDisplayMesh.MarkDynamic();
                    m_companionPoseVersion = surface.PoseVersion;
                    m_maskDisplayDirty = true;
                }
                if (m_companionPoseVersion != surface.PoseVersion)
                {
                    m_maskDisplayMesh.vertices = surface.PoseMesh.vertices;
                    m_maskDisplayMesh.normals = surface.PoseMesh.normals;
                    m_maskDisplayMesh.tangents = surface.PoseMesh.tangents;
                    m_maskDisplayMesh.bounds = surface.PoseMesh.bounds;
                    m_companionPoseVersion = surface.PoseVersion;
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
                            if (surface.HiddenTriangles[triangle]) continue;
                            for (int corner = 0; corner < 3; corner++)
                            {
                                int a = surface.Triangles[triangle * 3 + corner];
                                int b = surface.Triangles[triangle * 3 + (corner + 1) % 3];
                                indices.Add(a);
                                ulong edge = ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                                if (edges.Add(edge)) { wire.Add(a); wire.Add(b); }
                            }
                        }
                        m_maskDisplayMesh.SetTriangles(indices, submesh, false);
                    }
                    m_maskWireIndices = wire.ToArray();
                    m_maskDisplayDirty = false;
                }
                return m_maskDisplayMesh;
            }
        }

        public void DrawScene(SceneView view)
        {
            if (!IsPainting || Event.current == null || MaskError() != null) return;
            DrawMaskScene(view);
            if (DisplayMesh == null || m_maskDisplayMode == 0 || Event.current.type != EventType.Repaint) return;
            CompareFunction previous = Handles.zTest;
            try
            {
                using (new Handles.DrawingScope(new Color(.8f, .95f, 1f, .8f)))
                {
                    Handles.zTest = m_maskVisibleOnly ? CompareFunction.LessEqual : CompareFunction.Always;
                    Handles.DrawLines(m_brushSurface.WorldVertices, m_maskWireIndices);
                }
            }
            finally { Handles.zTest = previous; }
        }
    }
}
