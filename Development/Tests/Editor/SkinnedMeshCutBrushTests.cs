using System.Reflection;
using MeshTools.Dismemberment.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MeshTools.Dismemberment.Editor.Tests
{
    /// <summary>가려진 파츠 선택과 포즈 캐시 및 반복 칠하기의 회귀를 검증합니다.</summary>
    public sealed class SkinnedMeshCutBrushTests
    {
        private GameObject root;
        private Mesh mesh;
        private SkinnedMeshRenderer skin;
        private Transform bone;
        private Camera camera;
        private SkinnedMeshCutBrushSurface surface;
        private Scene scene;

        /// <summary>앞뒤로 겹치지만 연결되지 않은 두 사각형 스킨을 준비합니다.</summary>
        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Brush regression fixture");
            SceneManager.MoveGameObjectToScene(root, scene);
            bone = new GameObject("Bone").transform;
            bone.SetParent(root.transform, false);
            camera = new GameObject("Camera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
            camera.transform.SetParent(root.transform, false);
            camera.transform.position = new Vector3(0f, 0f, 5f);
            camera.transform.LookAt(Vector3.zero);
            camera.orthographic = true;
            camera.orthographicSize = 2f;
            camera.aspect = 1f;
            mesh = new Mesh();
            mesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 1f), new Vector3(1f, -1f, 1f),
                new Vector3(1f, 1f, 1f), new Vector3(-1f, 1f, 1f),
                new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
            mesh.bindposes = new[] { Matrix4x4.identity };
            BoneWeight[] weights = new BoneWeight[8];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            }

            mesh.boneWeights = weights;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            Vector3[] delta = new Vector3[8];
            for (int i = 0; i < delta.Length; i++)
            {
                delta[i] = Vector3.up;
            }

            mesh.AddBlendShapeFrame("Lift", 100f, delta, new Vector3[8], new Vector3[8]);
            skin = root.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = new[] { bone };
            skin.rootBone = bone;
            surface = new SkinnedMeshCutBrushSurface(skin);
        }

        /// <summary>검증용 메시와 포즈 캐시를 해제합니다.</summary>
        [TearDown]
        public void TearDown()
        {
            surface.Dispose();
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(mesh);
        }

        /// <summary>가시성 토글로 겹친 뒤쪽 파츠의 선택 여부가 달라지는지 확인합니다.</summary>
        [Test]
        public void Rectangle_OcclusionOptionIncludesBackPart()
        {
            Rect rect = new Rect(0.2f, 0.2f, 0.6f, 0.6f);
            CollectionAssert.AreEqual(new[] { 0, 1 }, surface.GetRectangleTriangles(rect, camera, true));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, surface.GetRectangleTriangles(rect, camera, false));
        }

        /// <summary>상자에 걸친 면으로부터 같은 파츠 전체를 선택하되 가려진 별도 파츠는 제외합니다.</summary>
        [Test]
        public void Rectangle_ConnectedSelectionExpandsVisibleSeedOnly()
        {
            Rect rect = new Rect(0.3f, 0.6f, 0.02f, 0.02f);
            Assert.That(surface.GetRectangleTriangles(rect, camera, true), Has.Length.EqualTo(1));
            CollectionAssert.AreEqual(new[] { 0, 1 }, surface.GetRectangleTriangles(rect, camera, true, true));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, surface.GetRectangleTriangles(rect, camera, false, true));
        }

        /// <summary>가려진 표면 포함 설정도 사용자가 명시적으로 숨긴 마스크 면은 선택하지 않습니다.</summary>
        [Test]
        public void Rectangle_ConnectedSelectionExcludesHiddenMask()
        {
            surface.SetHiddenTriangles(new[]
            {
                SkinnedMeshCutMaskMode.Cut, SkinnedMeshCutMaskMode.Auto,
                SkinnedMeshCutMaskMode.Auto, SkinnedMeshCutMaskMode.Auto
            }, true, false, false);
            int[] result = surface.GetRectangleTriangles(new Rect(0.2f, 0.2f, 0.6f, 0.6f), camera, false, true);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, result);
        }

        /// <summary>카메라 이동 후 이전 투영 캐시를 재사용하지 않는지 확인합니다.</summary>
        [Test]
        public void Rectangle_ProjectionCacheTracksCamera()
        {
            Rect rect = new Rect(0.2f, 0.2f, 0.6f, 0.6f);
            Assert.That(surface.GetRectangleTriangles(rect, camera, false), Has.Length.EqualTo(4));
            camera.transform.position += Vector3.right * 10f;
            Assert.That(surface.GetRectangleTriangles(rect, camera, false), Is.Empty);
        }

        /// <summary>근평면을 가로지르는 면도 투영 방향이 뒤집히지 않고 상자와 겹치면 선택합니다.</summary>
        [Test]
        public void Rectangle_PreservesNearPlaneIntersection()
        {
            surface.Dispose();
            camera.orthographic = false;
            camera.nearClipPlane = 0.5f;
            Vector3[] points = mesh.vertices;
            points[0] = new Vector3(-0.1f, -0.1f, 4.8f);
            points[1] = new Vector3(0.1f, -0.1f, 4f);
            points[2] = new Vector3(0f, 0.1f, 4f);
            mesh.vertices = points;
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateBounds();
            surface = new SkinnedMeshCutBrushSurface(skin);
            CollectionAssert.AreEqual(new[] { 0 },
                surface.GetRectangleTriangles(new Rect(0.3f, 0.3f, 0.4f, 0.4f), camera, false));
        }

        /// <summary>정지 상태의 반복 입력은 메시 베이크를 반복하지 않습니다.</summary>
        [Test]
        public void PoseCache_SkipsStationaryPose()
        {
            int version = surface.PoseVersion;
            Assert.That(surface.RefreshPoseIfChanged(), Is.False);
            Assert.That(surface.PoseVersion, Is.EqualTo(version));
        }

        /// <summary>본 이동 시 캐시를 무효화하고 실제 정점 위치를 갱신합니다.</summary>
        [Test]
        public void PoseCache_RefreshesMovedBone()
        {
            Vector3 before = surface.WorldVertices[0];
            bone.localPosition = Vector3.right;
            Assert.That(surface.RefreshPoseIfChanged(), Is.True);
            Assert.That(Vector3.Distance(surface.WorldVertices[0], before + Vector3.right), Is.LessThan(0.0001f));
            Assert.That(surface.RefreshPoseIfChanged(), Is.False);
        }

        /// <summary>블렌드셰이프 변경도 포즈 캐시에 반영합니다.</summary>
        [Test]
        public void PoseCache_RefreshesBlendShape()
        {
            Vector3 before = surface.WorldVertices[0];
            skin.SetBlendShapeWeight(0, 100f);
            Assert.That(surface.RefreshPoseIfChanged(), Is.True);
            Assert.That(Vector3.Distance(surface.WorldVertices[0], before + Vector3.up), Is.LessThan(0.0001f));
            Assert.That(surface.RefreshPoseIfChanged(), Is.False);
        }

        /// <summary>본 배열 교체 시에도 같은 개수의 본이라는 이유로 이전 포즈를 재사용하지 않습니다.</summary>
        [Test]
        public void PoseCache_RefreshesReplacedBone()
        {
            Transform replacement = new GameObject("Replacement").transform;
            replacement.SetParent(root.transform, false);
            replacement.localPosition = Vector3.right;
            skin.bones = new[] { replacement };
            Assert.That(surface.RefreshPoseIfChanged(), Is.True);
            Assert.That(surface.RefreshPoseIfChanged(), Is.False);
        }

        /// <summary>공간 검색이 뒤쪽 면과 삼각형 가장자리를 빠뜨리지 않는지 확인합니다.</summary>
        [Test]
        public void Brush_SpatialQueryKeepsCornerAndOcclusionRules()
        {
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 },
                surface.GetBrushTriangles(0, new Vector3(-1f, -1f, 0.5f), 0.6f, camera, false));
            CollectionAssert.AreEqual(new[] { 0, 1 },
                surface.GetBrushTriangles(0, new Vector3(-1f, -1f, 0.5f), 0.6f, camera, true));
        }

        /// <summary>같은 색을 반복 칠할 때 오버레이와 절단 미리보기를 다시 만들지 않습니다.</summary>
        [Test]
        public void PaintingSameMode_DoesNotInvalidateOverlay()
        {
            SkinnedMeshCutterWindowBase window = ScriptableObject.CreateInstance<SkinnedMeshCutterWindowBase>();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                typeof(SkinnedMeshCutterWindowBase).GetField("m_source", flags).SetValue(window, skin);
                window.PaintMaskTriangles(new[] { 0 }, SkinnedMeshCutMaskMode.Cut);
                FieldInfo dirty = typeof(SkinnedMeshCutterWindowBase).GetField("m_maskOverlayDirty", flags);
                dirty.SetValue(window, false);
                window.PaintMaskTriangles(new[] { 0 }, SkinnedMeshCutMaskMode.Cut);
                Assert.That(dirty.GetValue(window), Is.False);
            }
            finally
            {
                Undo.ClearUndo(window);
                Object.DestroyImmediate(window);
            }
        }

        /// <summary>같은 인덱스 개수로 토폴로지를 바꿔도 프리셋 저장 전에 다시 검증합니다.</summary>
        [Test]
        public void CapturePreset_RevalidatesChangedTriangleIndices()
        {
            SkinnedMeshCutterWindowBase window = ScriptableObject.CreateInstance<SkinnedMeshCutterWindowBase>();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                typeof(SkinnedMeshCutterWindowBase).GetField("m_source", flags).SetValue(window, skin);
                window.PaintMaskTriangles(new[] { 0 }, SkinnedMeshCutMaskMode.Cut);
                int[] indices = mesh.triangles;
                indices[0] = 3;
                mesh.triangles = indices;
                Assert.Throws<System.InvalidOperationException>(() => window.CapturePreset());
            }
            finally
            {
                Undo.ClearUndo(window);
                Object.DestroyImmediate(window);
            }
        }

        /// <summary>마스크 편집 중에는 입력 사이마다 무거운 절단 미리보기를 재생성하지 않습니다.</summary>
        [Test]
        public void EditorUpdate_DefersCutWhileMaskEditing()
        {
            SkinnedMeshCutterWindowBase window = ScriptableObject.CreateInstance<SkinnedMeshCutterWindowBase>();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            System.Type type = typeof(SkinnedMeshCutterWindowBase);
            try
            {
                type.GetField("m_source", flags).SetValue(window, skin);
                type.GetField("m_maskBrushEnabled", flags).SetValue(window, true);
                type.GetField("m_previewDirty", flags).SetValue(window, true);
                type.GetField("m_previewRequestedAt", flags).SetValue(window, -100d);
                type.GetMethod("HandleEditorUpdate", flags).Invoke(window, null);
                Assert.That(type.GetField("m_previewDirty", flags).GetValue(window), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }
    }
}
