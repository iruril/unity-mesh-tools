using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshModelCutterWindow
    {
        private sealed class ColliderTarget
        {
            internal string Key;
            internal Transform Bone;
            internal Matrix4x4 Offset;
            internal SkinnedMeshCutColliderGizmo Gizmo;
        }

        private readonly List<ColliderTarget> m_colliderTargets = new List<ColliderTarget>();
        private bool m_selectColliders;
        private int m_colliderControl;
        private SkinnedMeshCutRagdoll m_colliderPreview;

        protected override void DrawRagdollSettings()
        {
            var previousMode = m_ragdollMode;
            var previousSource = m_ragdollSource;
            float previousMass = m_ragdollMass;
            bool previousTips = m_ragdollTips;
            base.DrawRagdollSettings();
            if (previousMode != m_ragdollMode || previousSource != m_ragdollSource
                || previousMass != m_ragdollMass || previousTips != m_ragdollTips) StopColliderSelection();
            if (m_ragdollMode == SkinnedMeshCutRagdollMode.None) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(m_selectColliders ? "콜라이더 선택 끝내기" : "씬에서 콜라이더 선택"))
                    ColliderAction(() => { if (m_selectColliders) StopColliderSelection(); else StartColliderSelection(); });
                if (GUILayout.Button("모두 포함"))
                {
                    RecordSettings();
                    m_modelSettings.ExcludedColliders = Array.Empty<string>();
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("모두 제외")) ColliderAction(() =>
                {
                    StartColliderSelection();
                    RecordSettings();
                    m_modelSettings.ExcludedColliders = m_colliderTargets.Select(target => target.Key).ToArray();
                    SceneView.RepaintAll();
                });
            }
            EditorGUILayout.HelpBox("초록은 포함, 빨강은 제외입니다. 내부나 선을 클릭하면 콜라이더 하나가 전환됩니다. Esc로 종료합니다. 선택은 PART 출력에 적용됩니다.", MessageType.None);
        }

        private void ColliderAction(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                StopColliderSelection();
                m_message = error.Message;
                m_messageType = MessageType.Error;
            }
        }

        private void StartColliderSelection()
        {
            StopColliderSelection();
            StopModelPainting();
            OpenPosePreview();
            m_colliderPreview = SkinnedMeshCutRagdoll.Prepare(m_source, AuthoringSource(), AuthorBone,
                m_ragdollMode, m_ragdollSource, m_ragdollMass, m_ragdollTips, ModelRenderers());
            if (m_colliderPreview == null) return;
            foreach (var pair in m_colliderPreview.Bodies)
                foreach (var collider in pair.Value.GetComponentsInChildren<Collider>(true))
                {
                    if (collider.isTrigger || collider.GetComponentInParent<Rigidbody>(true) != pair.Value) continue;
                    m_colliderTargets.Add(new ColliderTarget
                    {
                        Key = m_colliderPreview.ColliderKey(pair.Key, pair.Value, collider), Bone = pair.Key,
                        Offset = pair.Value.transform.worldToLocalMatrix * collider.transform.localToWorldMatrix,
                        Gizmo = new SkinnedMeshCutColliderGizmo(collider)
                    });
                }
            m_selectColliders = true;
            MarkPreviewDirty();
        }

        private void StopColliderSelection()
        {
            m_selectColliders = false;
            if (m_colliderControl != 0 && GUIUtility.hotControl == m_colliderControl) GUIUtility.hotControl = 0;
            m_colliderControl = 0;
            m_colliderTargets.Clear();
            m_colliderPreview?.Dispose();
            m_colliderPreview = null;
            MarkPreviewDirty();
        }

        private void DrawColliderScene(SceneView view)
        {
            Event input = Event.current;
            if (input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape)
            {
                StopColliderSelection();
                input.Use();
                return;
            }
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (input.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            Ray ray = HandleUtility.GUIPointToWorldRay(input.mousePosition);
            int picked = -1;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < m_colliderTargets.Count; i++)
            {
                var target = m_colliderTargets[i];
                if (target.Bone == null) continue;
                var matrix = target.Bone.localToWorldMatrix * target.Offset;
                if (target.Gizmo.Raycast(matrix, ray, out float hit) && hit < distance)
                {
                    picked = i;
                    distance = hit;
                }
            }
            if (picked < 0)
            {
                distance = 8f;
                for (int i = 0; i < m_colliderTargets.Count; i++)
                {
                    var target = m_colliderTargets[i];
                    if (target.Bone == null) continue;
                    float hit = target.Gizmo.DistanceToMouse(target.Bone.localToWorldMatrix * target.Offset);
                    if (hit < distance) { picked = i; distance = hit; }
                }
            }
            var previousDepth = Handles.zTest;
            try
            {
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                if (input.type == EventType.Repaint)
                    for (int i = 0; i < m_colliderTargets.Count; i++)
                    {
                        var target = m_colliderTargets[i];
                        if (target.Bone == null) continue;
                        bool excluded = m_modelSettings.ExcludedColliders.Contains(target.Key);
                        target.Gizmo.Draw(target.Bone.localToWorldMatrix * target.Offset,
                            i == picked ? Color.yellow : excluded ? Color.red : Color.green);
                    }
                if (input.type == EventType.MouseDown && input.button == 0 && !input.alt
                    && !UnityEditor.Tools.viewToolActive && GUIUtility.hotControl == 0 && picked >= 0)
                {
                    RecordSettings();
                    var excluded = new HashSet<string>(m_modelSettings.ExcludedColliders);
                    string key = m_colliderTargets[picked].Key;
                    if (!excluded.Add(key)) excluded.Remove(key);
                    m_modelSettings.ExcludedColliders = excluded.OrderBy(value => value, StringComparer.Ordinal).ToArray();
                    GUIUtility.hotControl = m_colliderControl = control;
                    input.Use();
                    Repaint();
                }
                if (input.type == EventType.MouseUp && input.button == 0 && m_colliderControl != 0)
                {
                    GUIUtility.hotControl = m_colliderControl = 0;
                    input.Use();
                }
            }
            finally { Handles.zTest = previousDepth; }
            if (input.type == EventType.MouseMove) view.Repaint();
        }
    }
}
