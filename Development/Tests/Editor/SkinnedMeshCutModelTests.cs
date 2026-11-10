using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MeshTools.Dismemberment.Editor.Tests
{
    public sealed class SkinnedMeshCutModelTests
    {
        private Scene scene;
        private GameObject model;
        private Transform bone;
        private SkinnedMeshRenderer[] skins;
        private SkinnedMeshModelCutterWindow window;
        private readonly List<Object> owned = new List<Object>();
        private string folder;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            model = new GameObject("Model");
            SceneManager.MoveGameObjectToScene(model, scene);
            bone = Child(model.transform, "Rig");
            bone = Child(bone, "Bone");
            var visuals = Child(model.transform, "Visuals");
            skins = new[] { CreateSkin(visuals, "Body", -.6f), CreateSkin(visuals, "Clothing", .6f) };
            window = ScriptableObject.CreateInstance<SkinnedMeshModelCutterWindow>();
            window.SetModel(model);
            folder = "Assets/ModelCutTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            Set("m_outputFolder", folder);
            Set("m_outputName", "Cut");
            Set("m_createSceneObjects", false);
            Set("m_planePosition", Vector3.zero);
            Set("m_planeEuler", Vector3.zero);
            Set("m_planeSize", Vector2.one * 10);
            Set("m_generateCap", false);
        }

        [TearDown]
        public void TearDown()
        {
            if (window != null) Object.DestroyImmediate(window);
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
            AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public void RemoveEmptyBodies_CleansOwnedAndIncomingJointsWithoutReparentingTriggers()
        {
            var empty = bone.gameObject.AddComponent<Rigidbody>();
            var trigger = Child(bone, "Trigger").gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            var survivor = Child(bone, "Arm").gameObject.AddComponent<Rigidbody>();
            Child(survivor.transform, "Compound").gameObject.AddComponent<BoxCollider>();
            var hand = Child(survivor.transform, "Hand").gameObject.AddComponent<Rigidbody>();
            hand.gameObject.AddComponent<BoxCollider>();
            var ownedJoint = bone.gameObject.AddComponent<FixedJoint>();
            ownedJoint.connectedBody = survivor;
            var incomingJoint = survivor.gameObject.AddComponent<FixedJoint>();
            incomingJoint.connectedBody = empty;
            var keptJoint = hand.gameObject.AddComponent<FixedJoint>();
            keptJoint.connectedBody = survivor;
            var unrelated = Child(model.transform, "UnrelatedEmpty").gameObject.AddComponent<Rigidbody>();
            int callbacks = 0;

            int removed = SkinnedMeshCutRagdoll.RemoveEmptyBodies(model, new[] { empty, survivor }, body =>
            {
                Assert.That(body, Is.EqualTo(empty));
                Assert.That(body != null, Is.True);
                callbacks++;
            });

            Assert.That(removed, Is.EqualTo(1));
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(empty == null && trigger == null && ownedJoint == null && incomingJoint == null, Is.True);
            Assert.That(survivor != null && unrelated != null && keptJoint != null, Is.True);
            Assert.That(keptJoint.connectedBody, Is.EqualTo(survivor));
            Assert.That(survivor.transform.parent, Is.EqualTo(bone));
            Assert.That(hand.transform.parent, Is.EqualTo(survivor.transform));
        }

        [Test]
        public void RemoveEmptyBodies_RejectsBodiesOutsideOutputBeforeChangingAnything()
        {
            var body = bone.gameObject.AddComponent<Rigidbody>();
            var output = Child(model.transform, "Output").gameObject;
            Assert.Throws<ArgumentException>(() => SkinnedMeshCutRagdoll.RemoveEmptyBodies(output, new[] { body }));
            Assert.That(body != null, Is.True);
        }

        [Test]
        public void DefaultSelection_ExportsSeparateMeshesAndOriginalHierarchy()
        {
            var assets = window.GenerateAssets();
            var part = assets.OfType<GameObject>().Single(value => value.name == "Cut_PART");
            var remainder = assets.OfType<GameObject>().Single(value => value.name == "Cut_REMAIN");
            foreach (var root in new[] { part, remainder })
            {
                Assert.That(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, Is.EqualTo(2));
                foreach (var source in skins)
                {
                    var output = root.transform.Find("Model/Visuals/" + source.name).GetComponent<SkinnedMeshRenderer>();
                    Assert.That(output.bones.Single(), Is.EqualTo(root.transform.Find("Model/Rig/Bone")));
                    Assert.That(output.sharedMesh, Is.Not.SameAs(source.sharedMesh));
                    Assert.That(output.sharedMaterials, Is.EqualTo(source.sharedMaterials));
                }
            }
            Assert.That(skins.All(skin => skin.enabled), Is.True);
        }

        [Test]
        public void SubsetSelection_ExportsOnlySelectedRenderer()
        {
            Settings.AllRenderers = false;
            Settings.RendererPaths = new[] { "Visuals/Clothing|0" };
            var prefab = window.GeneratePartAssets().OfType<GameObject>().Single();
            Assert.That(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single().name, Is.EqualTo("Clothing"));
        }

        [Test]
        public void PerRendererMasks_RoundTripAndControlEachOutputIndependently()
        {
            Settings.Masks = new[] { Mask(skins[0], SkinnedMeshCutMaskMode.KeepPart), Mask(skins[1], SkinnedMeshCutMaskMode.KeepRemainder) };
            var preset = window.CapturePreset();
            owned.Add(preset);
            AssetDatabase.CreateAsset(preset, folder + "/Preset.asset");
            AssetDatabase.SaveAssets();
            Settings.Masks[0].Triangles[0] = SkinnedMeshCutMaskMode.Auto;
            window.ApplyPreset(AssetDatabase.LoadAssetAtPath<SkinnedMeshCutPreset>(folder + "/Preset.asset"));
            Assert.That(Settings.Masks[0].Triangles.All(mode => mode == SkinnedMeshCutMaskMode.KeepPart), Is.True);
            var assets = window.GenerateAssets();
            Assert.That(assets.OfType<GameObject>().Single(value => value.name == "Cut_PART")
                .GetComponentsInChildren<SkinnedMeshRenderer>(true).Single().name, Is.EqualTo("Body"));
            Assert.That(assets.OfType<GameObject>().Single(value => value.name == "Cut_REMAIN")
                .GetComponentsInChildren<SkinnedMeshRenderer>(true).Single().name, Is.EqualTo("Clothing"));
            owned.Remove(preset);
        }

        [Test]
        public void ColliderExclusion_IsPerComponentAndOnlyAffectsPart()
        {
            bone.gameObject.AddComponent<Rigidbody>();
            bone.gameObject.AddComponent<BoxCollider>();
            bone.gameObject.AddComponent<BoxCollider>();
            Set("m_ragdollMode", SkinnedMeshCutRagdollMode.Existing);
            Settings.ExcludedColliders = new[] { "Rig/Bone||0" };
            var assets = window.GenerateAssets();
            Assert.That(assets.OfType<GameObject>().Single(value => value.name == "Cut_PART")
                .GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(1));
            Assert.That(assets.OfType<GameObject>().Single(value => value.name == "Cut_REMAIN")
                .GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(2));
        }

        [Test]
        public void AllCollidersExcluded_CanStillGeneratePart()
        {
            bone.gameObject.AddComponent<Rigidbody>();
            bone.gameObject.AddComponent<BoxCollider>();
            bone.gameObject.AddComponent<BoxCollider>();
            Set("m_ragdollMode", SkinnedMeshCutRagdollMode.Existing);
            Settings.ExcludedColliders = new[] { "Rig/Bone||0", "Rig/Bone||1" };
            var prefab = window.GeneratePartAssets().OfType<GameObject>().Single();
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Joint>(true), Is.Empty);
        }

        [Test]
        public void ExcludedPhysicsBone_RemovesItsBodyAndIncomingJointsButKeepsValidConnections()
        {
            Rigidbody excluded = bone.gameObject.AddComponent<Rigidbody>();
            bone.gameObject.AddComponent<BoxCollider>();
            bone.gameObject.AddComponent<SphereCollider>().isTrigger = true;
            Transform secondBone = Child(bone.parent, "SecondBone");
            Rigidbody second = secondBone.gameObject.AddComponent<Rigidbody>();
            secondBone.gameObject.AddComponent<BoxCollider>();
            secondBone.gameObject.AddComponent<FixedJoint>().connectedBody = excluded;
            skins[1].bones = new[] { secondBone };
            skins[1].rootBone = secondBone;
            skins[1].sharedMesh.bindposes = new[] { secondBone.worldToLocalMatrix * skins[1].transform.localToWorldMatrix };
            Transform thirdBone = Child(bone.parent, "ThirdBone");
            thirdBone.gameObject.AddComponent<Rigidbody>();
            thirdBone.gameObject.AddComponent<BoxCollider>();
            thirdBone.gameObject.AddComponent<FixedJoint>().connectedBody = second;
            var thirdSkin = CreateSkin(skins[1].transform.parent, "ThirdSkin", 1.2f);
            thirdSkin.bones = new[] { thirdBone };
            thirdSkin.rootBone = thirdBone;
            thirdSkin.sharedMesh.bindposes = new[] { thirdBone.worldToLocalMatrix * thirdSkin.transform.localToWorldMatrix };
            Set("m_ragdollMode", SkinnedMeshCutRagdollMode.Existing);
            Settings.ExcludedColliders = new[] { "Rig/Bone||0" };
            var prefab = window.GeneratePartAssets().OfType<GameObject>().Single();
            Assert.That(prefab.transform.Find("Model/Rig/Bone").GetComponent<Rigidbody>(), Is.Null);
            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true).Length, Is.EqualTo(2));
            Assert.That(prefab.transform.Find("Model/Rig/SecondBone").GetComponents<Joint>(), Is.Empty);
            var joint = prefab.GetComponentsInChildren<Joint>(true).Single();
            Assert.That(joint.connectedBody, Is.EqualTo(prefab.transform.Find("Model/Rig/SecondBone").GetComponent<Rigidbody>()));
            Assert.That(excluded, Is.Not.Null);
            Assert.That(second.GetComponent<FixedJoint>().connectedBody, Is.SameAs(excluded));
        }

        [Test]
        public void InvalidSecondaryMask_FailsBeforeCreatingAssets()
        {
            var mask = Mask(skins[1], SkinnedMeshCutMaskMode.KeepPart);
            mask.TopologyHash++;
            Settings.Masks = new[] { mask };
            Assert.Throws<InvalidOperationException>(() => window.GenerateAssets());
            Assert.That(AssetDatabase.FindAssets("", new[] { folder }), Is.Empty);
        }

        [Test]
        public void SecondaryRendererPhysics_IsIncludedWithoutReferenceWeights()
        {
            Transform secondBone = Child(bone.parent, "SecondBone");
            bone.gameObject.AddComponent<Rigidbody>();
            bone.gameObject.AddComponent<BoxCollider>();
            secondBone.gameObject.AddComponent<Rigidbody>();
            secondBone.gameObject.AddComponent<BoxCollider>();
            skins[1].bones = new[] { secondBone };
            skins[1].rootBone = secondBone;
            skins[1].sharedMesh.bindposes = new[] { secondBone.worldToLocalMatrix * skins[1].transform.localToWorldMatrix };
            Set("m_ragdollMode", SkinnedMeshCutRagdollMode.Existing);
            var prefab = window.GeneratePartAssets().OfType<GameObject>().Single();
            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true).Length, Is.EqualTo(2));
        }

        [Test]
        public void EmptySelection_FailsWithoutWritingAssets()
        {
            Settings.AllRenderers = false;
            Assert.Throws<InvalidOperationException>(() => window.GenerateAssets());
            Assert.That(AssetDatabase.FindAssets("", new[] { folder }), Is.Empty);
        }

        [TestCase(typeof(BoxCollider))]
        [TestCase(typeof(SphereCollider))]
        [TestCase(typeof(CapsuleCollider))]
        public void ColliderPicking_HitsInteriorAndRejectsOutside(Type shape)
        {
            var collider = (Collider)bone.gameObject.AddComponent(shape);
            var gizmo = new SkinnedMeshCutColliderGizmo(collider);
            Assert.That(gizmo.Raycast(Matrix4x4.identity, new Ray(new Vector3(0, 0, -5), Vector3.forward),
                out float distance), Is.True);
            Assert.That(distance, Is.GreaterThan(0));
            Assert.That(gizmo.Raycast(Matrix4x4.identity, new Ray(new Vector3(5, 0, -5), Vector3.forward),
                out _), Is.False);
        }

        [Test]
        public void ModelTransform_PreservesRelativeRendererAndBoneTransforms()
        {
            model.transform.SetPositionAndRotation(new Vector3(3, 2, 4), Quaternion.Euler(0, 35, 0));
            model.transform.localScale = new Vector3(2, 1, 3);
            foreach (var skin in skins)
                skin.sharedMesh.bindposes = new[] { bone.worldToLocalMatrix * skin.transform.localToWorldMatrix };
            var prefab = window.GeneratePartAssets().OfType<GameObject>().Single();
            var outputs = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skins.Length; i++)
            {
                var expected = bone.worldToLocalMatrix * skins[i].transform.localToWorldMatrix;
                var actual = outputs[i].bones[0].worldToLocalMatrix * outputs[i].transform.localToWorldMatrix;
                for (int element = 0; element < 16; element++)
                    Assert.That(actual[element], Is.EqualTo(expected[element]).Within(.0001f));
            }
        }

        [Test]
        public void OldSingleRendererPreset_LoadsWithAllRenderersSelected()
        {
            var preset = window.CapturePreset();
            owned.Add(preset);
            preset.ModelSettings = null;
            Settings.AllRenderers = false;
            Settings.RendererPaths = Array.Empty<string>();
            window.ApplyPreset(preset);
            Assert.That(Settings.AllRenderers, Is.True);
            Assert.That(window.GeneratePartAssets().OfType<GameObject>().Single()
                .GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, Is.EqualTo(2));
        }

        private SkinnedMeshCutModelSettings Settings => (SkinnedMeshCutModelSettings)Field("m_modelSettings").GetValue(window);

        private static FieldInfo Field(string name)
        {
            return typeof(SkinnedMeshModelCutterWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? typeof(SkinnedMeshCutterWindowBase).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private void Set(string name, object value) => Field(name).SetValue(window, value);

        private SkinnedMeshCutModelSettings.RendererMask Mask(SkinnedMeshRenderer source, SkinnedMeshCutMaskMode mode)
        {
            return new SkinnedMeshCutModelSettings.RendererMask
            {
                Path = "Visuals/" + source.name + "|0", SourceMesh = source.sharedMesh,
                TopologyHash = SkinnedMeshCutBrushSurface.GetTopologyHash(source.sharedMesh),
                Triangles = Enumerable.Repeat(mode, source.sharedMesh.triangles.Length / 3).ToArray()
            };
        }

        private Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            SceneManager.MoveGameObjectToScene(child.gameObject, scene);
            child.SetParent(parent, false);
            return child;
        }

        private SkinnedMeshRenderer CreateSkin(Transform parent, string name, float x)
        {
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(primitive);
            owned.Add(mesh);
            var node = Child(parent, name);
            node.localPosition = Vector3.right * x;
            var skin = node.gameObject.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = new[] { bone };
            skin.rootBone = bone;
            mesh.bindposes = new[] { bone.worldToLocalMatrix * node.localToWorldMatrix };
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, mesh.vertexCount).ToArray();
            skin.sharedMaterials = new Material[] { null };
            return skin;
        }
    }
}
