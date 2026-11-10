using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MeshTools.Dismemberment.Editor.Tests
{
    /// <summary>외부 Bz 설치 유무와 관계없이 동작하는 실제 래그돌 생성 경로를 검증합니다.</summary>
    public sealed class SkinnedMeshCutRagdollGeneratorTests
    {
        private Scene scene;
        private GameObject root;
        private Animator animator;
        private Avatar avatar;
        private Mesh mesh;
        private SkinnedMeshRenderer source;
        private readonly Dictionary<HumanBodyBones, Transform> bones = new Dictionary<HumanBodyBones, Transform>();

        /// <summary>외부 모델에 의존하지 않는 유효한 휴머노이드와 스킨을 구성합니다.</summary>
        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Ragdoll generator fixture");
            SceneManager.MoveGameObjectToScene(root, scene);
            AddBone(HumanBodyBones.Hips, root.transform, new Vector3(0f, 1f, 0f));
            AddBone(HumanBodyBones.Spine, bones[HumanBodyBones.Hips], new Vector3(0f, 0.2f, 0f));
            AddBone(HumanBodyBones.Chest, bones[HumanBodyBones.Spine], new Vector3(0f, 0.2f, 0f));
            AddBone(HumanBodyBones.Neck, bones[HumanBodyBones.Chest], new Vector3(0f, 0.2f, 0f));
            AddBone(HumanBodyBones.Head, bones[HumanBodyBones.Neck], new Vector3(0f, 0.15f, 0f));
            AddLimbs(true);
            AddLimbs(false);
            HumanDescription description = new HumanDescription
            {
                human = bones.Select(pair => new HumanBone
                {
                    boneName = pair.Value.name,
                    humanName = HumanTrait.BoneName[(int)pair.Key],
                    limit = new HumanLimit { useDefaultValues = true }
                }).ToArray(),
                skeleton = root.GetComponentsInChildren<Transform>().Select(bone => new SkeletonBone
                {
                    name = bone.name,
                    position = bone.localPosition,
                    rotation = bone.localRotation,
                    scale = bone.localScale
                }).ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f
            };
            avatar = AvatarBuilder.BuildHumanAvatar(root, description);
            Assert.That(avatar.isValid && avatar.isHuman, Is.True, "검증용 아바타가 유효해야 합니다.");
            animator = root.AddComponent<Animator>();
            animator.avatar = avatar;
            Transform[] skinBones = bones.Values.ToArray();
            mesh = new Mesh
            {
                vertices = new[] { new Vector3(-0.1f, 1f, 0f), new Vector3(0.1f, 1f, 0f), new Vector3(0f, 1.2f, 0f) },
                triangles = new[] { 0, 1, 2 },
                bindposes = skinBones.Select(bone => bone.worldToLocalMatrix * root.transform.localToWorldMatrix).ToArray(),
                boneWeights = Enumerable.Range(0, 3).Select(_ => new BoneWeight { boneIndex0 = 0, weight0 = 1f }).ToArray()
            };
            mesh.RecalculateBounds();
            source = root.AddComponent<SkinnedMeshRenderer>();
            source.sharedMesh = mesh;
            source.bones = skinBones;
            source.rootBone = bones[HumanBodyBones.Hips];
        }

        /// <summary>생성기가 만든 임시 리그와 검증용 자원을 정리합니다.</summary>
        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Object.DestroyImmediate(avatar);
            UnityEngine.Object.DestroyImmediate(mesh);
            bones.Clear();
        }

        /// <summary>설치된 Bz가 있으면 우선 선택하고 없으면 Unity 내장 생성기를 선택합니다.</summary>
        [Test]
        public void Backend_PrefersInstalledBzOtherwiseUsesUnity()
        {
            Type generator = GeneratorType();
            bool installed = AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                assembly.GetType("BzKovSoft.RagdollHelper.Editor.Ragdoller", false) != null);
            string backend = (string)generator.GetProperty("BackendName", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null);
            TestContext.WriteLine("Selected generator: " + backend);
            Assert.That(backend, Is.EqualTo(installed ? "Bz Ragdoll Helper" : "Unity Ragdoll Builder"));
        }

        /// <summary>자동 선택한 실제 생성기가 복제 리그에 물리를 만들고 질량과 기본값을 보존합니다.</summary>
        [Test]
        public void AutomaticGeneration_CreatesPhysicsOnCloneOnly()
        {
            using (SkinnedMeshCutRagdoll rig = SkinnedMeshCutRagdoll.Prepare(source, source,
                original => original, SkinnedMeshCutRagdollMode.Generate, null, 60f, true))
            {
                Rigidbody[] bodies = rig.Root.GetComponentsInChildren<Rigidbody>(true);
                Assert.That(bodies.Length, Is.GreaterThanOrEqualTo(11));
                Assert.That(bodies.Sum(body => body.mass), Is.EqualTo(60f).Within(0.01f));
                Assert.That(rig.Root.GetComponent<Animator>().enabled, Is.False);
                Assert.That(bodies.All(body => !body.isKinematic && body.useGravity), Is.True);
                Assert.That(root.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            }
        }

        /// <summary>Bz가 설치된 경우에도 Unity 내장 생성기 자체가 독립적으로 실행되는지 검증합니다.</summary>
        [Test]
        public void BuiltInBuilder_CreatesNativeRagdoll()
        {
            MethodInfo method = GeneratorType().GetMethod("GenerateUnity", BindingFlags.Static | BindingFlags.NonPublic);
            try
            {
                method.Invoke(null, new object[] { animator, 60f });
            }
            catch (TargetInvocationException error)
            {
                throw error.InnerException ?? error;
            }

            Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
            Assert.That(bodies.Length, Is.EqualTo(11));
            Assert.That(root.GetComponentsInChildren<CharacterJoint>(true), Has.Length.EqualTo(10));
            Assert.That(bodies.Sum(body => body.mass), Is.EqualTo(60f).Within(0.01f));
        }

        /// <summary>패키지 내부 어댑터를 컴파일 시 외부 어셈블리 참조 없이 찾습니다.</summary>
        private static Type GeneratorType()
        {
            return typeof(SkinnedMeshCutRagdoll).Assembly.GetType("MeshTools.Dismemberment.Editor.SkinnedMeshCutRagdollGenerator", true);
        }

        /// <summary>검증용 휴머노이드의 한 본을 지정한 로컬 위치에 추가합니다.</summary>
        private void AddBone(HumanBodyBones id, Transform parent, Vector3 localPosition)
        {
            Transform bone = new GameObject(id.ToString()).transform;
            bone.SetParent(parent, false);
            bone.localPosition = localPosition;
            bones.Add(id, bone);
        }

        /// <summary>좌우 팔과 다리를 대칭으로 구성해 내장 생성기에 필요한 관절을 제공합니다.</summary>
        private void AddLimbs(bool left)
        {
            float sign = left ? -1f : 1f;
            HumanBodyBones arm = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            HumanBodyBones elbow = left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            HumanBodyBones hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            HumanBodyBones thigh = left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
            HumanBodyBones knee = left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
            HumanBodyBones foot = left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
            AddBone(arm, bones[HumanBodyBones.Chest], new Vector3(sign * 0.25f, 0.1f, 0f));
            AddBone(elbow, bones[arm], new Vector3(sign * 0.3f, 0f, 0f));
            AddBone(hand, bones[elbow], new Vector3(sign * 0.25f, 0f, 0f));
            AddBone(thigh, bones[HumanBodyBones.Hips], new Vector3(sign * 0.1f, -0.1f, 0f));
            AddBone(knee, bones[thigh], new Vector3(0f, -0.4f, 0f));
            AddBone(foot, bones[knee], new Vector3(0f, -0.4f, 0.1f));
        }
    }
}
