using UnityEditor;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace MeshTools.Dismemberment.Editor
{
    [FilePath("ProjectSettings/SkinnedMeshCutDefaults.asset", FilePathAttribute.Location.ProjectFolder)]
    [MovedFrom(true, "Irony.Dismemberment.Editor", "Irony.SkinnedMeshCutter.Editor", null)]
    public sealed class SkinnedMeshCutDefaults : ScriptableSingleton<SkinnedMeshCutDefaults>
    {
        public static System.Func<Material> FallbackMaterialProvider { get; set; }

        [SerializeField]
        private string m_capMaterialGuid;
        [SerializeField]
        private float m_drag = .1f;
        [SerializeField]
        private float m_angularDrag = .3f;
        [SerializeField]
        private RigidbodyInterpolation m_interpolation = RigidbodyInterpolation.Interpolate;
        public Material CapMaterial => string.IsNullOrEmpty(m_capMaterialGuid)
            ? FallbackMaterialProvider?.Invoke()
            : AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(m_capMaterialGuid));
        public float Drag => m_drag;
        public float AngularDrag => m_angularDrag;
        public RigidbodyInterpolation Interpolation => m_interpolation;

        /// <summary>편집기 이벤트를 연결하고 저장된 설정을 준비합니다.</summary>
        private void OnEnable()
        {
            Undo.undoRedoPerformed += SaveDefaults;
        }

        /// <summary>편집기 이벤트 연결과 임시 편집 자원을 정리합니다.</summary>
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= SaveDefaults;
        }

        /// <summary>저장된 물리 기본값을 강체에 적용합니다.</summary>
        public void ApplyPhysics(Rigidbody body)
        {
            // Unity 6의 감쇠 이름과 2022.3의 기존 API를 각각 사용한다.
#if UNITY_6000_0_OR_NEWER
            body.linearDamping = m_drag;
            body.angularDamping = m_angularDrag;
#else
            body.drag = m_drag;
            body.angularDrag = m_angularDrag;
#endif
            body.interpolation = m_interpolation;
        }

        /// <summary>변경한 도구 기본값을 프로젝트 설정에 저장합니다.</summary>
        public void SaveDefaults()
        {
            Save(true);
        }

        /// <summary>절단 도구의 프로젝트 기본값 편집 화면을 그립니다.</summary>
        public bool DrawSettings(bool includePhysics = true)
        {
            EditorGUILayout.LabelField("절단 파츠 생성 기본값 · 프로젝트 공통", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Material material = (Material)EditorGUILayout.ObjectField("기본 절단면 머티리얼", CapMaterial, typeof(Material), false);
            float drag = m_drag;
            float angularDrag = m_angularDrag;
            RigidbodyInterpolation interpolation = m_interpolation;

            if (includePhysics)
            {
                drag = Mathf.Max(0f, EditorGUILayout.FloatField("기본 이동 감쇠", m_drag));
                angularDrag = Mathf.Max(0f, EditorGUILayout.FloatField("기본 회전 감쇠", m_angularDrag));
                interpolation = (RigidbodyInterpolation)EditorGUILayout.EnumPopup("기본 보간", m_interpolation);
            }

            if (!EditorGUI.EndChangeCheck())
            {
                return false;
            }

            if (float.IsNaN(drag) || float.IsInfinity(drag) || float.IsNaN(angularDrag)
                || float.IsInfinity(angularDrag))
            {
                return false;
            }

            Undo.RecordObject(this, "Change cut part defaults");
            m_capMaterialGuid = material != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material))
                : string.Empty;
            m_drag = drag;
            m_angularDrag = angularDrag;
            m_interpolation = interpolation;
            SaveDefaults();

            return true;
        }
    }
}
