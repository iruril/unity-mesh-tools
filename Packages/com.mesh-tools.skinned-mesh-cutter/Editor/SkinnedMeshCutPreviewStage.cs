using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting.APIUpdating;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>원본의 스크립트를 실행하지 않는 본 계층 복제본에서 절단 작성 포즈를 표시합니다.</summary>
    [MovedFrom(true, "Irony.Dismemberment.Editor", "Irony.SkinnedMeshCutter.Editor", null)]
    public sealed class SkinnedMeshCutPreviewStage : PreviewSceneStage
    {
        private SkinnedMeshRenderer m_source;
        private readonly Dictionary<Transform, Transform> m_map = new Dictionary<Transform, Transform>();
        private readonly Dictionary<Transform, PoseData> m_initialPose = new Dictionary<Transform, PoseData>();
        private SkinnedMeshRenderer m_renderer;
        private MeshRenderer m_part;
        private MeshRenderer m_remainder;
        private MeshRenderer m_maskDisplay;
        private Mesh m_bakedPart;
        private Mesh m_bakedRemainder;
        private Animator m_animator;
        private PlayableGraph m_graph;
        public SkinnedMeshRenderer Renderer => m_renderer;

        /// <summary>미리보기 원본과 작성 자세를 연결합니다.</summary>
        public void Initialize(SkinnedMeshRenderer source)
        {
            m_source = source;
        }

        /// <summary>격리된 미리보기 씬에 편집용 리그와 스킨을 준비합니다.</summary>
        protected override bool OnOpenStage()
        {
            if (!base.OnOpenStage() || m_source == null)
            {
                return false;
            }

            Transform[] sourceBones = m_source.bones;

            foreach (Transform bone in sourceBones)
            {
                if (bone != null)
                {
                    ClonePath(bone);
                }
            }

            Transform rendererTransform = ClonePath(m_source.transform);
            Animator sourceAnimator = m_source.GetComponentInParent<Animator>();

            if (sourceAnimator != null)
            {
                foreach (Transform transform in sourceAnimator.GetComponentsInChildren<Transform>(true))
                {
                    ClonePath(transform);
                }

                m_animator = ClonePath(sourceAnimator.transform).gameObject.AddComponent<Animator>();
                m_animator.avatar = sourceAnimator.avatar;
            }
            else
            {
                m_animator = rendererTransform.root.gameObject.AddComponent<Animator>();
            }

            m_animator.runtimeAnimatorController = null;
            m_animator.applyRootMotion = false;
            m_animator.fireEvents = false;
            m_animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            m_renderer = rendererTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
            m_renderer.sharedMesh = m_source.sharedMesh;
            m_renderer.sharedMaterials = m_source.sharedMaterials;
            m_renderer.bones = Array.ConvertAll(sourceBones, Map);
            m_renderer.rootBone = m_source.rootBone != null ? ClonePath(m_source.rootBone) : null;
            m_renderer.localBounds = m_source.localBounds;
            m_renderer.quality = m_source.quality;
            m_renderer.updateWhenOffscreen = true;
            m_part = CreateResultRenderer("Cut PART");
            m_remainder = CreateResultRenderer("Cut REMAIN");
            m_bakedPart = new Mesh
            {
                name = "Cut display PART",
                hideFlags = HideFlags.HideAndDontSave
            };
            m_bakedRemainder = new Mesh
            {
                name = "Cut display REMAIN",
                hideFlags = HideFlags.HideAndDontSave
            };
            m_part.GetComponent<MeshFilter>().sharedMesh = m_bakedPart;
            m_remainder.GetComponent<MeshFilter>().sharedMesh = m_bakedRemainder;
            m_part.enabled = false;
            m_remainder.enabled = false;

            foreach (Transform transform in m_map.Values)
            {
                m_initialPose.Add(transform, new PoseData(transform));
            }

            GameObject lightObject = new GameObject("Cut preview light");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;

            return true;
        }

        /// <summary>미리보기 씬의 소유 메시와 표시 자원을 정리합니다.</summary>
        protected override void OnCloseStage()
        {
            if (m_graph.IsValid())
            {
                m_graph.Destroy();
            }

            m_renderer = null;
            m_part = null;
            m_remainder = null;
            m_maskDisplay = null;

            if (m_bakedPart != null)
            {
                DestroyImmediate(m_bakedPart);
                DestroyImmediate(m_bakedRemainder);
            }

            m_map.Clear();
            m_initialPose.Clear();
            base.OnCloseStage();
        }

        /// <summary>미리보기 스테이지의 헤더 정보를 반환합니다.</summary>
        protected override GUIContent CreateHeaderContent()
        {
            return new GUIContent("Skinned Cut · T-pose 편집 복제본");
        }

        /// <summary>원본 본에 대응하는 미리보기 본을 찾습니다.</summary>
        public Transform Map(Transform original)
        {
            return original != null && m_map.TryGetValue(original, out Transform clone) ? clone : null;
        }

        /// <summary>원본 계층에서 필요한 본까지의 부모 경로를 포함합니다.</summary>
        public Transform IncludePath(Transform original)
        {
            Transform clone = ClonePath(original);

            foreach (Transform transform in m_map.Values)
            {
                if (!m_initialPose.ContainsKey(transform))
                {
                    m_initialPose.Add(transform, new PoseData(transform));
                }
            }

            return clone;
        }

        /// <summary>지정한 애니메이션 시간의 자세를 미리보기 리그에 적용합니다.</summary>
        public void SamplePose(AnimationClip clip, float time)
        {
            if (m_graph.IsValid())
            {
                m_graph.Destroy();
            }

            foreach (KeyValuePair<Transform, PoseData> pair in m_initialPose)
            {
                pair.Value.Apply(pair.Key);
            }

            if (clip == null)
            {
                return;
            }

            m_animator.Rebind();
            m_graph = PlayableGraph.Create("Skinned cut authoring pose");
            m_graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            AnimationClipPlayable playable = AnimationClipPlayable.Create(m_graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            AnimationPlayableOutput.Create(m_graph, "Authoring pose", m_animator).SetSourcePlayable(playable);
            m_graph.Play();
            playable.SetTime(Mathf.Clamp(time, 0f, clip.length));
            playable.SetTime(Mathf.Clamp(time, 0f, clip.length));
            m_graph.Evaluate(0f);
        }

        /// <summary>절단 결과와 재질을 미리보기 렌더러에 반영합니다.</summary>
        public void SetResult(
            Mesh part,
            Mesh remainder,
            Material[] partMaterials,
            Material[] remainingMaterials,
            Vector3 separation = default(Vector3))
        {
            if (m_renderer == null)
            {
                return;
            }

            m_renderer.enabled = part == null;

            if (m_maskDisplay != null)
            {
                m_maskDisplay.enabled = false;
            }

            m_part.enabled = part != null;
            m_remainder.enabled = remainder != null;

            if (part == null || remainder == null)
            {
                return;
            }

            // 벌림 표시는 작성 포즈의 표시용 복사본만 사용하며 출력 스킨 메시에는 영향을 주지 않는다.
            Mesh original = m_renderer.sharedMesh;

            try
            {
                m_renderer.sharedMesh = part;
                m_renderer.BakeMesh(m_bakedPart, true);
                m_bakedPart.RecalculateBounds();
                m_renderer.sharedMesh = remainder;
                m_renderer.BakeMesh(m_bakedRemainder, true);
                m_bakedRemainder.RecalculateBounds();
            }
            finally
            {
                m_renderer.sharedMesh = original;
            }

            m_part.transform.localPosition = m_renderer.transform.InverseTransformVector(separation);
            m_remainder.transform.localPosition = m_renderer.transform.InverseTransformVector(-separation);
            m_part.sharedMaterials = partMaterials ?? m_source.sharedMaterials;
            m_remainder.sharedMaterials = remainingMaterials ?? m_source.sharedMaterials;
        }

        /// <summary>원본 메시가 바뀌면 미리보기 스킨과 결과를 동기화합니다.</summary>
        public void SynchronizeSourceMesh()
        {
            if (m_renderer != null)
            {
                m_renderer.sharedMesh = m_source.sharedMesh;
                m_renderer.sharedMaterials = m_source.sharedMaterials;
            }
        }

        /// <summary>마스크 표시용 메시와 재질을 미리보기 스킨에 적용합니다.</summary>
        public void SetMaskDisplay(Mesh mesh, Material[] materials)
        {
            if (m_maskDisplay == null)
            {
                if (mesh == null)
                {
                    return;
                }

                m_maskDisplay = CreateResultRenderer("Mask editing display");
            }

            m_maskDisplay.enabled = mesh != null;

            if (mesh == null)
            {
                m_renderer.enabled = !m_part.enabled;

                return;
            }

            m_renderer.enabled = false;
            m_part.enabled = m_remainder.enabled = false;
            m_maskDisplay.GetComponent<MeshFilter>().sharedMesh = mesh;
            m_maskDisplay.sharedMaterials = materials;
        }

        /// <summary>필요한 부모 경로를 보존하며 본을 복제합니다.</summary>
        private Transform ClonePath(Transform source)
        {
            if (m_map.TryGetValue(source, out Transform found))
            {
                return found;
            }

            Transform parent = source.parent != null ? ClonePath(source.parent) : null;
            Transform clone = new GameObject(source.name).transform;
            SceneManager.MoveGameObjectToScene(clone.gameObject, scene);
            clone.SetParent(parent, false);
            clone.localPosition = source.localPosition;
            clone.localRotation = source.localRotation;
            clone.localScale = source.localScale;
            m_map.Add(source, clone);

            return clone;
        }

        /// <summary>절단 결과 메시를 표시할 스킨 렌더러를 만듭니다.</summary>
        private MeshRenderer CreateResultRenderer(string name)
        {
            GameObject host = new GameObject(name);
            host.transform.SetParent(m_renderer.transform, false);
            host.AddComponent<MeshFilter>();
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();

            return renderer;
        }

        private readonly struct PoseData
        {
            private readonly Vector3 m_position;
            private readonly Quaternion m_rotation;
            private readonly Vector3 m_scale;
            /// <summary>변경 전 트랜스폼의 로컬 자세를 보관합니다.</summary>
            public PoseData(Transform transform)
            {
                m_position = transform.localPosition;
                m_rotation = transform.localRotation;
                m_scale = transform.localScale;
            }

            /// <summary>보관한 로컬 위치·회전·크기를 복원합니다.</summary>
            public void Apply(Transform transform)
            {
                transform.localPosition = m_position;
                transform.localRotation = m_rotation;
                transform.localScale = m_scale;
            }
        }
    }
}
