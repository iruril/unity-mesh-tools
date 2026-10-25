using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>프로젝트에 설치된 Bz 또는 Unity 내장 생성기를 외부 소스 포함 없이 호출합니다.</summary>
    internal static class SkinnedMeshCutRagdollGenerator
    {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type BzGenerator = FindType("BzKovSoft.RagdollHelper.Editor.Ragdoller");
        private static readonly Type BzProperties = FindType("BzKovSoft.RagdollHelper.Editor.RagdollProperties");
        internal static bool HasBzHelper => BzGenerator != null && BzProperties != null;
        internal static string BackendName => HasBzHelper ? "Bz Ragdoll Helper" : "Unity Ragdoll Builder";

        /// <summary>현재 에디터에 설치된 생성기를 선택해 복제된 리그에만 물리를 생성합니다.</summary>
        internal static void Generate(SkinnedMeshRenderer renderer, Animator animator, float mass, bool tips)
        {
            if (HasBzHelper)
            {
                GenerateBz(renderer, mass, tips);
            }
            else
            {
                GenerateUnity(animator, mass);
            }
        }

        /// <summary>선택적 외부 패키지를 어셈블리 참조 없이 찾아 프로젝트의 설치 방식 차이를 허용합니다.</summary>
        private static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>설치된 Bz의 생성 API에 기본 물리 설정을 전달합니다.</summary>
        private static void GenerateBz(SkinnedMeshRenderer renderer, float mass, bool tips)
        {
            SkinnedMeshCutDefaults defaults = SkinnedMeshCutDefaults.instance;
            object settings = Activator.CreateInstance(BzProperties, true);
            SetField(settings, "rigidDrag", defaults.Drag);
            SetField(settings, "rigidAngularDrag", defaults.AngularDrag);
            SetField(settings, "createTips", tips);
            SetField(settings, "isKinematic", false);
            SetField(settings, "useGravity", true);
            ConstructorInfo constructor = BzGenerator.GetConstructor(InstanceMembers, null,
                new[] { typeof(Transform), typeof(Vector3) }, null);
            MethodInfo generate = BzGenerator.GetMethod("ApplyRagdoll", InstanceMembers, null,
                new[] { typeof(float), BzProperties }, null);
            if (constructor == null || generate == null)
            {
                throw new InvalidOperationException("설치된 Bz Ragdoll Helper의 생성 API를 찾을 수 없습니다.");
            }

            try
            {
                object generator = constructor.Invoke(new object[] { renderer.transform, renderer.transform.root.forward });
                generate.Invoke(generator, new[] { (object)mass, settings });
            }
            catch (TargetInvocationException error)
            {
                throw new InvalidOperationException("Bz Ragdoll Helper 생성 실패: " + error.InnerException?.Message,
                    error.InnerException ?? error);
            }
        }

        /// <summary>Unity에 포함된 실제 Ragdoll Builder를 호출해 외부 패키지 없는 생성 경로를 제공합니다.</summary>
        internal static void GenerateUnity(Animator animator, float mass)
        {
            Type type = FindType("UnityEditor.RagdollBuilder")
                ?? Type.GetType("UnityEditor.RagdollBuilder, UnityEditor.PhysicsModule", false);
            if (type == null)
            {
                throw new InvalidOperationException("이 Unity 에디터에서 내장 Ragdoll Builder를 찾을 수 없습니다.");
            }

            // 2022.3은 ScriptableWizard, Unity 6은 일반 관리 객체이므로 실제 에디터 형식에 맞춘다.
            object wizard = typeof(ScriptableObject).IsAssignableFrom(type)
                ? ScriptableObject.CreateInstance(type) : Activator.CreateInstance(type, true);
            if (wizard is UnityEngine.Object editorObject)
            {
                editorObject.hideFlags = HideFlags.HideAndDontSave;
            }
            try
            {
                SetBone(wizard, "pelvis", animator, HumanBodyBones.Hips);
                SetBone(wizard, "leftHips", animator, HumanBodyBones.LeftUpperLeg);
                SetBone(wizard, "leftKnee", animator, HumanBodyBones.LeftLowerLeg);
                SetBone(wizard, "leftFoot", animator, HumanBodyBones.LeftFoot);
                SetBone(wizard, "rightHips", animator, HumanBodyBones.RightUpperLeg);
                SetBone(wizard, "rightKnee", animator, HumanBodyBones.RightLowerLeg);
                SetBone(wizard, "rightFoot", animator, HumanBodyBones.RightFoot);
                SetBone(wizard, "leftArm", animator, HumanBodyBones.LeftUpperArm);
                SetBone(wizard, "leftElbow", animator, HumanBodyBones.LeftLowerArm);
                SetBone(wizard, "rightArm", animator, HumanBodyBones.RightUpperArm);
                SetBone(wizard, "rightElbow", animator, HumanBodyBones.RightLowerArm);
                SetBone(wizard, "head", animator, HumanBodyBones.Head);
                Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest)
                    ?? animator.GetBoneTransform(HumanBodyBones.Spine);
                SetField(wizard, "middleSpine", chest);
                SetField(wizard, "totalMass", mass);

                // 위저드를 화면에 열지 않으므로 축 계산과 본 검증을 명시적으로 실행한다.
                Invoke(wizard, "CalculateAxes");
                string error = (string)Invoke(wizard, "CheckConsistency");
                if (!string.IsNullOrEmpty(error))
                {
                    throw new InvalidOperationException("Unity Ragdoll Builder 본 검증 실패: " + error);
                }

                Invoke(wizard, "OnWizardCreate");
            }
            finally
            {
                // 내장 생성기의 Undo 기록은 임시 리그에만 속한다. 사용자 오브젝트의 Undo는 건드리지 않는다.
                foreach (Transform bone in animator.GetComponentsInChildren<Transform>(true))
                {
                    foreach (Component component in bone.GetComponents<Component>())
                    {
                        Undo.ClearUndo(component);
                    }

                    Undo.ClearUndo(bone.gameObject);
                }

                if (wizard is UnityEngine.Object objectToDestroy)
                {
                    UnityEngine.Object.DestroyImmediate(objectToDestroy);
                }
            }
        }

        /// <summary>Unity 위저드의 본 슬롯을 휴머노이드 아바타의 실제 트랜스폼으로 채웁니다.</summary>
        private static void SetBone(object target, string name, Animator animator, HumanBodyBones bone)
        {
            SetField(target, name, animator.GetBoneTransform(bone));
        }

        /// <summary>호출 대상의 알려진 설정 필드를 확인한 뒤 값을 전달합니다.</summary>
        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, InstanceMembers);
            if (field == null)
            {
                throw new InvalidOperationException(target.GetType().Name + "의 설정 필드를 찾을 수 없습니다: " + name);
            }

            field.SetValue(target, value);
        }

        /// <summary>내장 위저드의 단계별 메서드를 호출하고 실제 예외 원인을 전달합니다.</summary>
        private static object Invoke(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(name, InstanceMembers, null, Type.EmptyTypes, null);
            if (method == null)
            {
                throw new InvalidOperationException("Unity Ragdoll Builder의 실행 메서드를 찾을 수 없습니다: " + name);
            }

            try
            {
                return method.Invoke(target, null);
            }
            catch (TargetInvocationException error)
            {
                throw new InvalidOperationException("Unity Ragdoll Builder 실행 실패: " + error.InnerException?.Message,
                    error.InnerException ?? error);
            }
        }
    }
}
