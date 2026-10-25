using System;
using System.Collections.Generic;
using System.Reflection;

using MeshTools.Dismemberment.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MeshTools.Dismemberment.Editor.Tests
{
    /// <summary>레퍼런스 래그돌의 콜라이더 종류를 보존하고 실제로 잘린 곡면만 메시로 바꾸는지 검증합니다.</summary>
    public sealed class SkinnedMeshCutColliderTests
    {
        private Scene scene;
        private GameObject source;
        private GameObject target;
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();

        /// <summary>실제 작업 씬에 영향을 주지 않는 콜라이더 복사 대상을 준비합니다.</summary>
        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            source = new GameObject("Reference collider");
            target = new GameObject("Output collider");
            SceneManager.MoveGameObjectToScene(source, scene);
            SceneManager.MoveGameObjectToScene(target, scene);
        }

        /// <summary>검증에만 사용한 물리 오브젝트와 메시를 정리합니다.</summary>
        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (Mesh mesh in ownedMeshes)
            {
                if (mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }

            ownedMeshes.Clear();
        }

        /// <summary>비스듬한 절단이 박스를 콘벡스 메시로 바꾸지 않는지 확인합니다.</summary>
        [Test]
        public void AngledCut_PreservesReferenceBox()
        {
            BoxCollider box = source.AddComponent<BoxCollider>();
            box.center = new Vector3(0.1f, 0.2f, 0.3f);
            box.size = new Vector3(2f, 3f, 4f);
            box.isTrigger = true;
            box.enabled = false;
            Collider copy = Copy(box, new Plane(new Vector3(1f, 1f, 0f).normalized, 0f));

            Assert.That(copy, Is.TypeOf<BoxCollider>());
            Assert.That(((BoxCollider)copy).center, Is.EqualTo(box.center));
            Assert.That(((BoxCollider)copy).size, Is.EqualTo(box.size));
            Assert.That(copy.isTrigger, Is.True);
            Assert.That(copy.enabled, Is.False);
            Assert.That(ownedMeshes, Is.Empty);
        }

        /// <summary>축에 맞는 절단은 메시 변환 없이 박스 중심과 크기만 맞추는지 확인합니다.</summary>
        [Test]
        public void AxisAlignedCut_StillUsesBoxCollider()
        {
            BoxCollider box = source.AddComponent<BoxCollider>();
            box.size = Vector3.one * 2f;
            BoxCollider copy = (BoxCollider)Copy(box, new Plane(Vector3.right, 0f));

            Assert.That(copy.center, Is.EqualTo(Vector3.right * 0.5f));
            Assert.That(copy.size, Is.EqualTo(new Vector3(1f, 2f, 2f)));
            Assert.That(ownedMeshes, Is.Empty);
        }

        /// <summary>기존 비콘벡스 메시의 설정과 메시 참조를 임의로 재생성하지 않는지 확인합니다.</summary>
        [Test]
        public void ExistingMeshCollider_KeepsMeshAndConvexFlag()
        {
            MeshCollider collider = source.AddComponent<MeshCollider>();
            collider.sharedMesh = CreateCubeMesh();
            collider.convex = false;
            MeshCollider copy = (MeshCollider)Copy(collider, new Plane(Vector3.right, 0f));

            Assert.That(copy.sharedMesh, Is.SameAs(collider.sharedMesh));
            Assert.That(copy.convex, Is.False);
            Assert.That(ownedMeshes.Count, Is.EqualTo(1));
        }

        /// <summary>읽기 불가 원본 메시도 정점 추출 없이 레퍼런스 그대로 복사하는지 확인합니다.</summary>
        [Test]
        public void UnreadableReferenceMesh_CopiesWithoutHull()
        {
            MeshCollider collider = source.AddComponent<MeshCollider>();
            collider.sharedMesh = CreateCubeMesh();
            collider.sharedMesh.UploadMeshData(true);

            MeshCollider copy = (MeshCollider)Copy(collider, new Plane(Vector3.right, 10f));
            Assert.That(copy.sharedMesh, Is.SameAs(collider.sharedMesh));
            Assert.That(copy.convex, Is.EqualTo(collider.convex));
        }

        /// <summary>절단면을 만나지 않는 캡슐은 원본 높이와 반지름을 유지하는지 확인합니다.</summary>
        [Test]
        public void UncutCapsule_PreservesPrimitive()
        {
            CapsuleCollider capsule = source.AddComponent<CapsuleCollider>();
            capsule.center = Vector3.forward * 2f;
            capsule.radius = 0.5f;
            capsule.height = 3f;
            CapsuleCollider copy = (CapsuleCollider)Copy(capsule, new Plane(Vector3.forward, 0f));

            Assert.That(copy.center, Is.EqualTo(capsule.center));
            Assert.That(copy.height, Is.EqualTo(capsule.height));
            Assert.That(copy.radius, Is.EqualTo(capsule.radius));
            Assert.That(ownedMeshes, Is.Empty);
        }

        /// <summary>실제로 잘린 구만 콘벡스 메시가 되고 공통 충돌 설정도 보존되는지 확인합니다.</summary>
        [Test]
        public void CutSphere_UsesConvexAndPreservesContactSettings()
        {
            SphereCollider sphere = source.AddComponent<SphereCollider>();
            sphere.radius = 1f;
            sphere.isTrigger = true;
            sphere.enabled = false;
            sphere.contactOffset = 0.02f;
            sphere.includeLayers = 1 << 7;
            sphere.excludeLayers = 1 << 8;
            sphere.layerOverridePriority = 4;
            sphere.providesContacts = true;
            MeshCollider copy = (MeshCollider)Copy(sphere, new Plane(Vector3.right, 0f));

            Assert.That(copy.convex, Is.True);
            Assert.That(copy.isTrigger, Is.EqualTo(sphere.isTrigger));
            Assert.That(copy.enabled, Is.EqualTo(sphere.enabled));
            Assert.That(copy.contactOffset, Is.EqualTo(sphere.contactOffset));
            Assert.That(copy.includeLayers, Is.EqualTo(sphere.includeLayers));
            Assert.That(copy.excludeLayers, Is.EqualTo(sphere.excludeLayers));
            Assert.That(copy.layerOverridePriority, Is.EqualTo(sphere.layerOverridePriority));
            Assert.That(copy.providesContacts, Is.EqualTo(sphere.providesContacts));
            foreach (Vector3 vertex in copy.sharedMesh.vertices)
            {
                Assert.That(vertex.x, Is.GreaterThanOrEqualTo(-0.00001f));
            }
        }

        /// <summary>실제로 잘린 캡슐은 절단 평면 안쪽에 있는 콘벡스 메시로 변환되는지 확인합니다.</summary>
        [Test]
        public void CutCapsule_UsesConvexMesh()
        {
            CapsuleCollider capsule = source.AddComponent<CapsuleCollider>();
            capsule.radius = 0.5f;
            capsule.height = 2f;
            MeshCollider copy = (MeshCollider)Copy(capsule, new Plane(Vector3.up, 0f));

            Assert.That(copy.convex, Is.True);
            foreach (Vector3 vertex in copy.sharedMesh.vertices)
            {
                Assert.That(vertex.y, Is.GreaterThanOrEqualTo(-0.00001f));
            }
        }

        /// <summary>버리는 쪽에만 있는 콜라이더는 출력에 남기지 않는지 확인합니다.</summary>
        [Test]
        public void DiscardedCollider_IsRemoved()
        {
            SphereCollider sphere = source.AddComponent<SphereCollider>();
            sphere.center = Vector3.left * 3f;
            sphere.radius = 1f;

            Assert.That(Copy(sphere, new Plane(Vector3.right, 0f)), Is.Null);
            Assert.That(target.GetComponents<Collider>(), Is.Empty);
        }

        /// <summary>출력 표면보다 큰 정상 캡슐도 본별 메시 AABB 때문에 잘리지 않는지 실제 래그돌 복사로 검증합니다.</summary>
        [Test]
        public void RagdollCopy_DoesNotFitCapsuleToSurfaceBounds()
        {
            Transform bone = new GameObject("Bone").transform;
            bone.SetParent(source.transform, false);
            Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            CapsuleCollider capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            capsule.center = Vector3.forward * 2f;
            capsule.radius = 0.5f;
            capsule.height = 3f;

            Mesh mesh = CreateCubeMesh();
            Vector3[] points = mesh.vertices;
            for (int i = 0; i < points.Length; i++)
            {
                points[i] += Vector3.forward * 2f;
            }

            mesh.vertices = points;
            mesh.bindposes = new[] { Matrix4x4.identity };
            BoneWeight[] weights = new BoneWeight[points.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            }

            mesh.boneWeights = weights;
            mesh.RecalculateBounds();
            SkinnedMeshRenderer skin = source.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = new[] { bone };
            skin.rootBone = bone;

            Transform outputBone = new GameObject("Bone").transform;
            outputBone.SetParent(target.transform, false);
            SkinnedMeshRenderer output = target.AddComponent<SkinnedMeshRenderer>();
            output.sharedMesh = mesh;
            output.bones = new[] { outputBone };
            output.rootBone = outputBone;
            Dictionary<Transform, Transform> map = new Dictionary<Transform, Transform>
            {
                { source.transform, target.transform }, { bone, outputBone }
            };

            using (SkinnedMeshCutRagdoll rig = SkinnedMeshCutRagdoll.Prepare(
                skin, skin, original => original, SkinnedMeshCutRagdollMode.Existing, source, 60f, false))
            {
                rig.CopyTo(target, map, original => map[original], new Plane(Vector3.forward, 0f), ownedMeshes);
            }

            CapsuleCollider copy = outputBone.GetComponent<CapsuleCollider>();
            Assert.That(copy, Is.Not.Null);
            Assert.That(copy.height, Is.EqualTo(3f));
            Assert.That(outputBone.GetComponent<MeshCollider>(), Is.Null);
        }

        /// <summary>동일한 감쇠 설정이 각 Unity 버전의 Rigidbody API에 반영되는지 확인합니다.</summary>
        [Test]
        public void PhysicsDefaults_AppliesVersionSpecificDamping()
        {
            Rigidbody body = source.AddComponent<Rigidbody>();
            SkinnedMeshCutDefaults defaults = SkinnedMeshCutDefaults.instance;
            defaults.ApplyPhysics(body);
#if UNITY_6000_0_OR_NEWER
            Assert.That(body.linearDamping, Is.EqualTo(defaults.Drag));
            Assert.That(body.angularDamping, Is.EqualTo(defaults.AngularDrag));
#else
            Assert.That(body.drag, Is.EqualTo(defaults.Drag));
            Assert.That(body.angularDrag, Is.EqualTo(defaults.AngularDrag));
#endif
            Assert.That(body.interpolation, Is.EqualTo(defaults.Interpolation));
        }


        /// <summary>패키지 내부 콜라이더 복사 경로를 직접 호출해 외부 설정과 구분하여 검증합니다.</summary>
        private Collider Copy(Collider collider, Plane plane)
        {
            Type type = typeof(SkinnedMeshCutRagdoll).Assembly.GetType(
                "MeshTools.Dismemberment.Editor.SkinnedMeshCutColliderGeometry");
            MethodInfo method = type.GetMethod("CopyClipped", BindingFlags.Static | BindingFlags.NonPublic);
            try
            {
                return (Collider)method.Invoke(null, new object[] { collider, target.transform, new[] { plane }, ownedMeshes });
            }
            catch (TargetInvocationException error)
            {
                throw error.InnerException;
            }
        }

        /// <summary>콜라이더와 스킨 복사 검증에 사용할 독립 정육면체 메시를 만듭니다.</summary>
        private Mesh CreateCubeMesh()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = new[]
            {
                new Vector3(-0.5f,-0.5f,-0.5f), new Vector3(0.5f,-0.5f,-0.5f),
                new Vector3(0.5f,0.5f,-0.5f), new Vector3(-0.5f,0.5f,-0.5f),
                new Vector3(-0.5f,-0.5f,0.5f), new Vector3(0.5f,-0.5f,0.5f),
                new Vector3(0.5f,0.5f,0.5f), new Vector3(-0.5f,0.5f,0.5f)
            };
            mesh.triangles = new[] { 0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,1,2,6,1,6,5,3,7,6,3,6,2,0,4,7,0,7,3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            ownedMeshes.Add(mesh);
            return mesh;
        }
    }
}

