using System;
using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public partial class SkinnedMeshCutterWindowBase
    {
        protected static readonly string[] PresetFields =
        {
            nameof(m_mode),
            nameof(m_includeChildBones),
            nameof(m_minimumBoneWeight),
            nameof(m_planePosition),
            nameof(m_planeEuler),
            nameof(m_planeSize),
            nameof(m_detachPositive),
            nameof(m_generateCap),
            nameof(m_closeOpenBoundaries),
            nameof(m_capMaterialOverride),
            nameof(m_partCapMaterial),
            nameof(m_remainingCapMaterial),
            nameof(m_capUvScale),
            nameof(m_uvRotation),
            nameof(m_uvOffset),
            nameof(m_uvLayoutScale),
            nameof(m_uvIslandTransforms),
            nameof(m_packUv),
            nameof(m_tolerance),
            nameof(m_repairOpenBoundaries),
            nameof(m_maximumGapDistance),
            nameof(m_poseClip),
            nameof(m_poseTime),
            nameof(m_maskSourceMesh),
            nameof(m_maskTopologyHash),
            nameof(m_triangleMask),
            nameof(m_ragdollMode),
            nameof(m_ragdollSource),
            nameof(m_ragdollMass),
            nameof(m_ragdollTips)
        };

        /// <summary>저장된 절단 프리셋의 선택·저장·적용 화면을 그립니다.</summary>
        protected void DrawPresets()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("관절 · 평면 프리셋", EditorStyles.boldLabel);
            m_preset = (SkinnedMeshCutPreset)EditorGUILayout.ObjectField("저장한 설정", m_preset, typeof(SkinnedMeshCutPreset), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(m_source == null || m_source.sharedMesh == null || GetMaskError() != null))
                {
                    if (GUILayout.Button("현재 설정을 새 프리셋으로 저장"))
                    {
                        string path = EditorUtility.SaveFilePanelInProject("Save cut preset", m_source.name + "_CutPreset", "asset", "절단 설정 저장 위치");

                        if (!string.IsNullOrEmpty(path))
                        {
                            SkinnedMeshCutPreset preset = CapturePreset();
                            AssetDatabase.CreateAsset(preset, AssetDatabase.GenerateUniqueAssetPath(path));
                            AssetDatabase.SaveAssets();
                            m_preset = preset;
                        }
                    }
                }

                using (new EditorGUI.DisabledScope(m_preset == null || m_source == null))
                {
                    if (GUILayout.Button("불러오기"))
                    {
                        try
                        {
                            ApplyPreset(m_preset);
                        }
                        catch (Exception exception)
                        {
                            m_message = exception.Message;
                            m_messageType = MessageType.Error;
                        }
                    }
                }
            }
        }

        /// <summary>현재 편집 설정을 저장 가능한 프리셋으로 복사합니다.</summary>
        public SkinnedMeshCutPreset CapturePreset()
        {
            string error = ValidateMaskTopology(true);

            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            SkinnedMeshCutPreset preset = CreateInstance<SkinnedMeshCutPreset>();
            Transform rig = RigRoot(m_source);
            string path = m_joint != null ? AnimationUtility.CalculateTransformPath(m_joint, rig) : null;
            string pivotPath = m_outputPivotBone != null
                ? AnimationUtility.CalculateTransformPath(m_outputPivotBone, rig)
                : null;
            preset.SetIdentity(m_source.sharedMesh, path, pivotPath);

            using (SerializedObject source = new SerializedObject(this))
            using (SerializedObject destination = new SerializedObject(preset))
            {
                foreach (string name in PresetFields)
                {
                    destination.CopyFromSerializedProperty(source.FindProperty(name));
                }

                destination.ApplyModifiedPropertiesWithoutUndo();
            }

            return preset;
        }

        /// <summary>저장된 프리셋을 현재 리그와 창 설정에 적용합니다.</summary>
        public void ApplyPreset(SkinnedMeshCutPreset preset)
        {
            if (preset.SourceMesh != m_source.sharedMesh)
            {
                throw new InvalidOperationException("이 프리셋은 다른 원본 메시의 설정입니다.");
            }

            Transform rig = RigRoot(m_source);
            Transform joint = !preset.HasJoint
                ? null
                : string.IsNullOrEmpty(preset.JointPath) ? rig : rig.Find(preset.JointPath);

            if (preset.HasJoint && joint == null)
            {
                throw new InvalidOperationException("저장한 관절 경로를 찾지 못했습니다: " + preset.JointPath);
            }

            Transform pivot = preset.HasOutputPivotBone
                ? (string.IsNullOrEmpty(preset.OutputPivotPath) ? rig : rig.Find(preset.OutputPivotPath))
                : null;

            if (preset.HasOutputPivotBone && pivot == null)
            {
                throw new InvalidOperationException("저장한 출력 피봇 본을 찾지 못했습니다: " + preset.OutputPivotPath);
            }

            RecordSettings();

            using (SerializedObject source = new SerializedObject(preset))
            using (SerializedObject destination = new SerializedObject(this))
            {
                foreach (string name in PresetFields)
                {
                    destination.CopyFromSerializedProperty(source.FindProperty(name));
                }

                destination.ApplyModifiedProperties();
            }

            m_joint = m_mode == CutMode.BoneParts ? joint : null;
            m_outputPivotBone = pivot;
            m_maskRevision++;
            m_maskOverlayDirty = true;
            m_planeInitialized = true;

            if (m_previewStage != null)
            {
                m_previewStage.SamplePose(m_poseClip, m_poseTime);
            }

            MarkPreviewDirty();
        }

        /// <summary>스킨의 본 경로를 포함하는 리그 루트를 찾습니다.</summary>
        public static Transform RigRoot(SkinnedMeshRenderer source)
        {
            Animator animator = source.GetComponentInParent<Animator>();

            if (animator != null)
            {
                return animator.transform;
            }

            Transform root = source.transform;

            foreach (Transform bone in source.bones)
            {
                while (bone != null && root.parent != null && !bone.IsChildOf(root))
                {
                    root = root.parent;
                }
            }

            return root;
        }
    }
}

