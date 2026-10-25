using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>동일한 원본 메시의 관절 경로, 상대 평면과 절단면 설정을 재사용합니다.</summary>
    [MovedFrom(true, "Irony.Dismemberment.Editor", "Irony.SkinnedMeshCutter.Editor", null)]
    public sealed class SkinnedMeshCutPreset : ScriptableObject
    {
        [SerializeField]
        private Mesh m_sourceMesh;
        [SerializeField]
        private string m_jointPath;
        [SerializeField]
        private bool m_hasJoint = true;
        [SerializeField]
        private bool m_hasOutputPivotBone;
        [SerializeField]
        private string m_outputPivotPath;
        [SerializeField]
        private SkinnedMeshCutterWindowBase.CutMode m_mode;
        [SerializeField]
        private bool m_includeChildBones;
        [SerializeField]
        private float m_minimumBoneWeight;
        [SerializeField]
        private Vector3 m_planePosition;
        [SerializeField]
        private Vector3 m_planeEuler;
        [SerializeField]
        private Vector2 m_planeSize;
        [SerializeField]
        private bool m_detachPositive;
        [SerializeField]
        private bool m_generateCap;
        [SerializeField]
        private bool m_closeOpenBoundaries;
        [SerializeField]
        private Material m_capMaterialOverride;
        [SerializeField]
        private Material m_partCapMaterial;
        [SerializeField]
        private Material m_remainingCapMaterial;
        [SerializeField]
        private float m_capUvScale;
        [SerializeField]
        private float m_uvRotation;
        [SerializeField]
        private Vector2 m_uvOffset;
        [SerializeField]
        private float m_uvLayoutScale = 1f;
        [SerializeField]
        private SkinnedMeshCutUvTransform[] m_uvIslandTransforms = System.Array.Empty<SkinnedMeshCutUvTransform>();
        [SerializeField]
        private bool m_packUv;
        [SerializeField]
        private float m_tolerance;
        [SerializeField]
        private bool m_repairOpenBoundaries;
        [SerializeField]
        private float m_maximumGapDistance = 0.005f;
        [SerializeField]
        private AnimationClip m_poseClip;
        [SerializeField]
        private float m_poseTime;
        [SerializeField]
        private Mesh m_maskSourceMesh;
        [SerializeField]
        private int m_maskTopologyHash;
        [SerializeField]
        private SkinnedMeshCutMaskMode[] m_triangleMask;
        [SerializeField]
        private SkinnedMeshCutRagdollMode m_ragdollMode;
        [SerializeField]
        private GameObject m_ragdollSource;
        [SerializeField]
        private float m_ragdollMass = 60f;
        [SerializeField]
        private bool m_ragdollTips = true;
        public Mesh SourceMesh => m_sourceMesh;
        public string JointPath => m_jointPath;
        public bool HasJoint => m_hasJoint;
        public bool HasOutputPivotBone => m_hasOutputPivotBone;
        public string OutputPivotPath => m_outputPivotPath;
        public AnimationClip PoseClip => m_poseClip;
        public SkinnedMeshCutRagdollMode RagdollMode => m_ragdollMode;

        /// <summary>프리셋 식별자와 표시 이름을 지정합니다.</summary>
        public void SetIdentity(Mesh mesh, string jointPath, string outputPivotPath = null)
        {
            m_sourceMesh = mesh;
            m_jointPath = jointPath;
            m_hasJoint = jointPath != null;
            m_hasOutputPivotBone = outputPivotPath != null;
            m_outputPivotPath = outputPivotPath;
        }
    }
}
