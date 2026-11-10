using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>모델의 메시별 절단, 마스크와 물리 선택을 공통 프리셋으로 작성합니다.</summary>
    public sealed partial class SkinnedMeshModelCutterWindow : SkinnedMeshCutterWindowBase
    {
        [SerializeField] private GameObject m_model;
        [SerializeField] private SkinnedMeshCutModelSettings m_modelSettings = new SkinnedMeshCutModelSettings();
        [SerializeField] private string m_maskRendererPath;
        private readonly Dictionary<string, SkinnedMeshCutMaskEditor> m_maskEditors = new Dictionary<string, SkinnedMeshCutMaskEditor>();
        private readonly Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer> m_authorRenderers = new Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer>();
        private SkinnedMeshCutPreviewStage m_authorStage;

        protected override string WindowTitle => "Skeletal Mesh Cutter";

        [MenuItem("Tools/Skeletal Mesh Cutter")]
        public static void OpenModelCutter()
        {
            var window = GetWindow<SkinnedMeshModelCutterWindow>();
            window.UseSelectionIfEmpty();
            window.Show();
        }

        public void SetModel(GameObject model)
        {
            var reference = model != null ? model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(skin => skin.sharedMesh != null).OrderByDescending(skin => skin.bones.Length)
                .ThenByDescending(skin => skin.sharedMesh.vertexCount).FirstOrDefault() : null;
            SetSource(reference);
            m_model = model;
            m_outputName = model != null ? model.name : string.Empty;
            if (m_source != null && EditorUtility.IsPersistent(m_source)) OpenPosePreview();
        }

        protected override void UseSelection()
        {
            if (Selection.activeGameObject != null) SetModel(Selection.activeGameObject);
        }

        protected override void OnSourceChanged()
        {
            DisposeModelEditors();
            ClearModelPreview();
            StopColliderSelection();
            m_model = m_source != null ? RigRoot(m_source).gameObject : null;
            m_modelSettings = new SkinnedMeshCutModelSettings();
            m_maskRendererPath = null;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EditorApplication.update += UpdateModelEditors;
            Undo.undoRedoPerformed += ReloadModelEditors;
        }

        protected override void OnDisable()
        {
            EditorApplication.update -= UpdateModelEditors;
            Undo.undoRedoPerformed -= ReloadModelEditors;
            FlushModelMasks();
            DisposeModelEditors();
            StopColliderSelection();
            ClearModelPreview();
            base.OnDisable();
        }

        private SkinnedMeshRenderer[] ModelRenderers()
        {
            return m_model != null ? m_model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(skin => skin.sharedMesh != null).ToArray() : Array.Empty<SkinnedMeshRenderer>();
        }

        private string RendererPath(SkinnedMeshRenderer skin)
        {
            return AnimationUtility.CalculateTransformPath(skin.transform, m_model.transform)
                + "|" + Array.IndexOf(skin.GetComponents<SkinnedMeshRenderer>(), skin);
        }

        private string RendererLabel(SkinnedMeshRenderer skin)
        {
            string path = AnimationUtility.CalculateTransformPath(skin.transform, m_model.transform);
            if (path.Length == 0) path = skin.name;
            var components = skin.GetComponents<SkinnedMeshRenderer>();
            return components.Length > 1 ? path + " (메시 " + (Array.IndexOf(components, skin) + 1) + ")" : path;
        }

        private SkinnedMeshRenderer[] SelectedRenderers()
        {
            var sources = ModelRenderers();
            if (sources.Select(RendererPath).Distinct().Count() != sources.Length)
                throw new InvalidOperationException("메시 경로가 중복됩니다. 같은 부모 아래의 모델 오브젝트 이름을 구분하세요.");
            if (!m_modelSettings.AllRenderers)
            {
                var paths = new HashSet<string>(m_modelSettings.RendererPaths);
                sources = sources.Where(skin => paths.Contains(RendererPath(skin))).ToArray();
                if (sources.Length != paths.Count)
                    throw new InvalidOperationException("저장한 메시 경로를 찾을 수 없습니다. 절단 대상을 다시 선택하세요.");
            }
            if (sources.Length == 0) throw new InvalidOperationException("절단할 메시를 하나 이상 선택하세요.");
            return sources;
        }

        protected override void DrawSource()
        {
            var model = (GameObject)EditorGUILayout.ObjectField("모델 루트", m_model, typeof(GameObject), true);
            if (model != m_model) { RecordSettings(); SetModel(model); }
            base.DrawSource();
            if (m_model == null) return;
            var skins = ModelRenderers();
            bool all = EditorGUILayout.ToggleLeft("모든 하위 메시 선택", m_modelSettings.AllRenderers);
            if (all != m_modelSettings.AllRenderers)
            {
                RecordSettings();
                m_modelSettings.AllRenderers = all;
                m_modelSettings.RendererPaths = all ? Array.Empty<string>() : skins.Select(RendererPath).ToArray();
            }
            foreach (var skin in skins)
            {
                string path = RendererPath(skin);
                bool included = m_modelSettings.AllRenderers || m_modelSettings.RendererPaths.Contains(path);
                bool next = EditorGUILayout.ToggleLeft(RendererLabel(skin), included);
                if (next == included) continue;
                RecordSettings();
                var selected = new HashSet<string>(m_modelSettings.AllRenderers
                    ? skins.Select(RendererPath) : m_modelSettings.RendererPaths);
                if (next) selected.Add(path); else selected.Remove(path);
                m_modelSettings.AllRenderers = false;
                m_modelSettings.RendererPaths = selected.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            }
        }

        private Transform AuthorBone(Transform source)
            => m_previewStage != null ? m_previewStage.IncludePath(source) : source;

        private SkinnedMeshRenderer AuthorRenderer(SkinnedMeshRenderer source)
        {
            if (m_previewStage == null) return source;
            if (source == m_source) return m_previewStage.Renderer;
            if (m_authorStage != m_previewStage)
            {
                m_authorRenderers.Clear();
                m_authorStage = m_previewStage;
            }
            if (m_authorRenderers.TryGetValue(source, out var cached) && cached != null) return cached;
            Transform node = AuthorBone(source.transform);
            var skin = node.gameObject.AddComponent<SkinnedMeshRenderer>();
            EditorUtility.CopySerialized(source, skin);
            skin.bones = source.bones.Select(AuthorBone).ToArray();
            skin.rootBone = source.rootBone != null ? AuthorBone(source.rootBone) : null;
            m_authorRenderers.Add(source, skin);
            return skin;
        }

        private SkinnedMeshCutMaskEditor MaskEditor(SkinnedMeshRenderer source)
        {
            string path = RendererPath(source);
            if (!m_maskEditors.TryGetValue(path, out var editor))
            {
                var mask = m_modelSettings.Masks.FirstOrDefault(item => item.Path == path);
                editor = CreateInstance<SkinnedMeshCutMaskEditor>();
                editor.Bind(AuthorRenderer(source), mask != null ? mask.SourceMesh : source == m_source ? m_maskSourceMesh : source.sharedMesh,
                    mask != null ? mask.TopologyHash : source == m_source ? m_maskTopologyHash : 0,
                    mask != null ? mask.Triangles : source == m_source ? m_triangleMask : null);
                m_maskEditors.Add(path, editor);
            }
            else editor.RebindSource(AuthorRenderer(source));
            return editor;
        }

        protected override void DrawMaskSettings()
        {
            var skins = ModelRenderers();
            if (skins.Length == 0) return;
            int index = Array.FindIndex(skins, skin => RendererPath(skin) == m_maskRendererPath);
            int selected = EditorGUILayout.Popup("마스크 편집 메시", Mathf.Max(0, index), skins.Select(RendererLabel).ToArray());
            string path = RendererPath(skins[selected]);
            if (path != m_maskRendererPath)
            {
                StopModelPainting();
                m_maskRendererPath = path;
            }
            var editor = MaskEditor(skins[selected]);
            editor.DrawSettings();
            if (editor.IsPainting && m_previewStage == null)
            {
                OpenPosePreview();
                editor.RebindSource(AuthorRenderer(skins[selected]));
            }
            UpdateModelEditors();
        }

        private void FlushModelMasks()
        {
            foreach (var pair in m_maskEditors)
            {
                if (!pair.Value.ConsumeChanges()) continue;
                var editor = pair.Value;
                var mask = new SkinnedMeshCutModelSettings.RendererMask
                {
                    Path = pair.Key, SourceMesh = editor.MaskMesh, TopologyHash = editor.TopologyHash, Triangles = editor.TriangleMask
                };
                m_modelSettings.Masks = m_modelSettings.Masks.Where(item => item.Path != pair.Key).Append(mask).ToArray();
                if (m_source != null && pair.Key == RendererPath(m_source))
                {
                    m_maskSourceMesh = mask.SourceMesh;
                    m_maskTopologyHash = mask.TopologyHash;
                    m_triangleMask = (SkinnedMeshCutMaskMode[])mask.Triangles.Clone();
                    m_maskRevision++;
                }
                MarkPreviewDirty();
            }
        }

        private void StopModelPainting()
        {
            foreach (var editor in m_maskEditors.Values) editor.StopPainting();
            FlushModelMasks();
            m_maskBrushEnabled = false;
        }

        private void DisposeModelEditors()
        {
            foreach (var editor in m_maskEditors.Values) if (editor != null) DestroyImmediate(editor);
            m_maskEditors.Clear();
        }

        private void ReloadModelEditors()
        {
            // Mask editor Undo is owned by its own object. Let it publish the restored mask first.
            EditorApplication.delayCall += PublishModelUndo;
        }

        private void PublishModelUndo()
        {
            if (this == null) return;
            FlushModelMasks();
            MarkPreviewDirty();
        }

        private void UpdateModelEditors()
        {
            if (m_settingsTab != 2) StopModelPainting();
            FlushModelMasks();
            m_maskBrushEnabled = m_maskEditors.Values.Any(editor => editor.IsPainting);
            if (m_selectColliders && (m_settingsTab != 1 || m_previewStage == null)) StopColliderSelection();
            if (m_maskBrushEnabled || m_selectColliders) UpdateStageResult();
        }

        private SkinnedMeshCutMaskMode[] RendererMask(SkinnedMeshRenderer source)
        {
            var mask = m_modelSettings.Masks.FirstOrDefault(item => item.Path == RendererPath(source));
            if (mask == null) return source == m_source ? m_triangleMask : null;
            if (mask.Triangles.Length > 0 && (mask.SourceMesh != source.sharedMesh
                || mask.TopologyHash != SkinnedMeshCutBrushSurface.GetTopologyHash(source.sharedMesh)))
                throw new InvalidOperationException(source.name + "의 메시 구조가 바뀌었습니다. 해당 마스크를 초기화하세요.");
            return mask.Triangles;
        }

        public override SkinnedMeshCutPreset CapturePreset()
        {
            FlushModelMasks();
            foreach (var skin in ModelRenderers()) RendererMask(skin);
            var preset = base.CapturePreset();
            preset.ModelSettings = m_modelSettings.Clone();
            return preset;
        }

        public override void ApplyPreset(SkinnedMeshCutPreset preset)
        {
            base.ApplyPreset(preset);
            DisposeModelEditors();
            StopColliderSelection();
            m_modelSettings = preset.ModelSettings != null ? preset.ModelSettings.Clone() : new SkinnedMeshCutModelSettings();
        }

        protected override void HandleSceneGui(SceneView view)
        {
            if (m_previewStage != null && StageUtility.GetCurrentStage() != m_previewStage) return;
            if (m_selectColliders) { DrawColliderScene(view); return; }
            if (m_maskBrushEnabled)
            {
                var skin = ModelRenderers().FirstOrDefault(source => RendererPath(source) == m_maskRendererPath);
                if (skin != null) MaskEditor(skin).DrawScene(view);
                FlushModelMasks();
                return;
            }
            base.HandleSceneGui(view);
        }
    }
}
