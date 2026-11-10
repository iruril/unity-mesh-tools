using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshModelCutterWindow
    {
        private readonly SkinnedMeshCutModelPreview m_modelPreview = new SkinnedMeshCutModelPreview();
        private double m_sourcePreviewAt = -1d;
        private UnityEngine.Object m_sourcePreviewStage;
        private string m_sourcePreviewDisplay;
        private object m_sourcePreviewResult;

        private void ClearModelPreview() => m_modelPreview.Clear();

        protected override void RefreshCutPreview()
        {
            FlushModelMasks();
            ClearPreview();
            m_previewDirty = false;
            m_previewError = GetSourceError();
            if (m_previewError != null) { Repaint(); return; }
            try
            {
                foreach (var skin in ModelRenderers()) AuthorRenderer(skin);
                m_previewStage?.SamplePose(m_poseClip, m_poseTime);
                m_previewResult = SkinnedMeshCutMultiSource.Cut(BuildSettings(), AuthoringSource(), AuthoringSource(), RendererMask(m_source));
                m_previewBoundary = m_previewResult.Boundary;
                m_capUvPreview = m_previewResult.CapUvs;
                UpdateStageResult();
            }
            catch (Exception error) { m_previewError = error.Message; }
            Repaint();
            SceneView.RepaintAll();
        }

        protected override void UpdateStageResult()
        {
            UpdateModelPreview();
        }

        private void UpdateModelPreview()
        {
            if (m_model == null) return;
            if (m_previewStage == null)
            {
                ClearModelPreview();
                m_sourcePreviewStage = null;
                return;
            }
            if ((m_previewDirty && !m_maskBrushEnabled) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string display = m_showResult + ":" + m_selectColliders + ":" + m_maskBrushEnabled + ":" + m_checkerPreview + ":" + m_previewSeparation;
            if (m_sourcePreviewStage == m_previewStage && m_sourcePreviewAt == m_previewRequestedAt
                && m_sourcePreviewDisplay == display && ReferenceEquals(m_sourcePreviewResult, m_previewResult)) return;
            m_sourcePreviewStage = m_previewStage;
            m_sourcePreviewAt = m_previewRequestedAt;
            m_sourcePreviewDisplay = display;
            m_sourcePreviewResult = m_previewResult;
            ClearModelPreview();
            try
            {
                var selected = new HashSet<SkinnedMeshRenderer>(SelectedRenderers());
                var sources = m_model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(skin => skin.sharedMesh != null).ToArray();
                var authors = sources.Select(AuthorRenderer).ToArray();
                m_previewStage.SamplePose(m_poseClip, m_poseTime);
                // The package's single-renderer result is replaced by one display per original renderer.
                m_previewStage.SetResult(null, null, null, null);
                var settings = BuildSettings();
                Material cap = m_partCapMaterial != null ? m_partCapMaterial
                    : m_capMaterialOverride != null ? m_capMaterialOverride
                    : MeshTools.Dismemberment.Editor.SkinnedMeshCutDefaults.instance.CapMaterial;
                if (m_checkerPreview || cap == null) { EnsureCheckerMaterial(); cap = m_checkerMaterial; }
                for (int i = 0; i < sources.Length; i++)
                {
                    var source = sources[i];
                    var author = authors[i];
                    author.enabled = false;
                    if (m_maskBrushEnabled && RendererPath(source) == m_maskRendererPath)
                    {
                        var editor = MaskEditor(source);
                        Mesh displayMesh = editor.DisplayMesh;
                        if (displayMesh != null) AddSourceMaskPreview(source, author, displayMesh, editor.DisplayMaterials);
                        continue;
                    }
                    if (!m_showResult || m_maskBrushEnabled || m_selectColliders || !selected.Contains(source))
                    {
                        AddSourcePreview(source, author, author.sharedMesh, null, Vector3.zero, "MODEL");
                        continue;
                    }
                    using (var result = SkinnedMeshCutMultiSource.Cut(settings, AuthoringSource(), author, RendererMask(source)))
                    {
                        Vector3 separation = settings.Rotation * Vector3.forward * m_previewSeparation;
                        AddSourcePreview(source, author, result.Part, cap, separation, "PART");
                        AddSourcePreview(source, author, result.Remainder,
                            !m_checkerPreview && m_remainingCapMaterial != null ? m_remainingCapMaterial : cap,
                            -separation, "REMAIN");
                    }
                }
                m_previewError = null;
            }
            catch (Exception exception)
            {
                ClearModelPreview();
                m_previewStage.SetResult(null, null, null, null);
                foreach (var source in m_model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (source.sharedMesh != null) AuthorRenderer(source).enabled = source.enabled;
                m_previewError = exception.Message;
            }
            Repaint();
            SceneView.RepaintAll();
        }

        private void AddSourcePreview(SkinnedMeshRenderer source, SkinnedMeshRenderer author, Mesh mesh,
            Material cap, Vector3 separation, string suffix)
        {
            if (mesh != null) m_modelPreview.Add(source, author, mesh,
                ModelOutputMaterials(source, mesh, cap), separation, suffix);
        }

        private void AddSourceMaskPreview(SkinnedMeshRenderer source, SkinnedMeshRenderer author, Mesh mesh, Material[] materials)
            => m_modelPreview.AddMask(source, author, mesh, materials);
    }
}
