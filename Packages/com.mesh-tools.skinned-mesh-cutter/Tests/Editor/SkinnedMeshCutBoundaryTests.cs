using System;
using System.Collections.Generic;
using System.Reflection;

using MeshTools.Dismemberment.Editor;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MeshTools.Dismemberment.Editor.Tests
{
    /// <summary>열린 외피와 닫힌 몸체가 섞여도 절단과 캡 UV 집계가 독립적으로 동작하는지 검증합니다.</summary>
    public sealed class SkinnedMeshCutBoundaryTests
    {
        private Scene previewScene;
        private SkinnedMeshRenderer source;
        private Mesh sourceMesh;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        /// <summary>실제 씬과 원본 에셋에 영향을 주지 않는 프리뷰 씬과 단일 본 스킨을 준비합니다.</summary>
        [SetUp]
        public void SetUp()
        {
            previewScene = EditorSceneManager.NewPreviewScene();
            GameObject root = new GameObject("Mesh cutter regression source");
            SceneManager.MoveGameObjectToScene(root, previewScene);
            GameObject bone = new GameObject("Bone");
            SceneManager.MoveGameObjectToScene(bone, previewScene);
            bone.transform.SetParent(root.transform, false);
            source = root.AddComponent<SkinnedMeshRenderer>();
            source.bones = new[] { bone.transform };
            source.rootBone = bone.transform;
            vertices.Clear();
            triangles.Clear();
        }

        /// <summary>검증용 씬과 직접 할당한 메시를 남기지 않고 정리합니다.</summary>
        [TearDown]
        public void TearDown()
        {
            if (previewScene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }

            if (sourceMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(sourceMesh);
            }
        }

        /// <summary>닫힌 몸체의 기존 캡 생성과 단일 UV 조각이 유지되는지 확인합니다.</summary>
        [Test]
        public void ClosedBody_KeepsCapAndUvIsland()
        {
            AppendCube(Vector3.zero);
            CreateSourceMesh();

            using (SkinnedMeshCutEngine.Result result = Cut())
            {
                Assert.That(result.LoopCount, Is.EqualTo(1));
                Assert.That(result.CapUvs, Is.Not.Null);
                Assert.That(result.CapUvs.Islands.Length, Is.EqualTo(1));
                Assert.That(result.Part.subMeshCount, Is.EqualTo(2));
                AssertSkinWeights(result.Part);
                AssertSkinWeights(result.Remainder);
            }
        }

        /// <summary>열린 천만 있는 메시도 캡이나 가짜 UV 조각을 만들지 않고 분리되는지 확인합니다.</summary>
        [Test]
        public void OpenSheet_CutsWithoutCapOrUvIsland()
        {
            AppendSheet(0f);
            CreateSourceMesh();

            using (SkinnedMeshCutEngine.Result result = Cut())
            {
                Assert.That(result.LoopCount, Is.Zero);
                Assert.That(result.CapUvs, Is.Null);
                Assert.That(result.Part.subMeshCount, Is.EqualTo(1));
                Assert.That(result.Remainder.subMeshCount, Is.EqualTo(1));
                Assert.That(result.Part.bounds.min.z, Is.GreaterThanOrEqualTo(-0.00001f));
                Assert.That(result.Remainder.bounds.max.z, Is.LessThanOrEqualTo(0.00001f));
                AssertSkinWeights(result.Part);
                AssertSkinWeights(result.Remainder);
                AssertSurfaceUvs(result.Part);
                AssertSurfaceUvs(result.Remainder);
            }
        }

        /// <summary>몸체의 닫힌 경계만 캡과 UV에 포함되고 열린 옷은 양쪽 출력에 보존되는지 확인합니다.</summary>
        [Test]
        public void ClosedBodyWithOpenClothing_OnlyCapsTheBody()
        {
            AppendCube(Vector3.zero);
            AppendSheet(2f);
            CreateSourceMesh();

            using (SkinnedMeshCutEngine.Result result = Cut())
            {
                Assert.That(result.LoopCount, Is.EqualTo(1));
                Assert.That(result.CapUvs.Islands.Length, Is.EqualTo(1));
                Assert.That(result.Part.bounds.max.y, Is.EqualTo(2f).Within(0.00001f));
                Assert.That(result.Remainder.bounds.max.y, Is.EqualTo(2f).Within(0.00001f));

                Vector3[] positions = result.Part.vertices;
                foreach (int vertex in result.Part.GetTriangles(1))
                {
                    Assert.That(Mathf.Abs(positions[vertex].y), Is.LessThanOrEqualTo(0.50001f));
                }
            }
        }

        /// <summary>열린 옷이 추가되어도 닫힌 두 몸체의 UV 조각 수가 증가하지 않는지 확인합니다.</summary>
        [Test]
        public void MultipleBodiesWithOpenClothing_OnlyCountClosedUvIslands()
        {
            AppendCube(new Vector3(-2f, 0f, 0f));
            AppendCube(new Vector3(2f, 0f, 0f));
            AppendSheet(2f);
            CreateSourceMesh();

            using (SkinnedMeshCutEngine.Result result = Cut())
            {
                Assert.That(result.LoopCount, Is.EqualTo(2));
                Assert.That(result.CapUvs.Islands.Length, Is.EqualTo(2));
            }
        }

        /// <summary>분기하는 얇은 외피도 캡 대상에서 제외하면서 실제 표면은 절단하는지 확인합니다.</summary>
        [Test]
        public void BranchedShell_CutsWithoutInventingACap()
        {
            AppendPanel(Vector2.zero, Vector2.right);
            AppendPanel(Vector2.zero, Vector2.up);
            AppendPanel(Vector2.zero, Vector2.left);
            CreateSourceMesh();

            using (SkinnedMeshCutEngine.Result result = Cut())
            {
                Assert.That(result.LoopCount, Is.Zero);
                Assert.That(result.CapUvs, Is.Null);
                Assert.That(result.Part.vertexCount, Is.GreaterThan(0));
                Assert.That(result.Remainder.vertexCount, Is.GreaterThan(0));
            }
        }

        /// <summary>캡 UV 편집이 열린 옷을 포함한 원본 표면의 UV를 덮어쓰지 않는지 확인합니다.</summary>
        [Test]
        public void CapUvEditing_DoesNotChangeOpenClothingUvs()
        {
            AppendCube(Vector3.zero);
            AppendSheet(2f);
            CreateSourceMesh();

            using (SkinnedMeshCutEngine.Result result = Cut())
            {
                Vector2[] before = result.Part.uv;
                Vector2[] edited = new Vector2[before.Length];
                HashSet<int> capVertices = new HashSet<int>(result.Part.GetTriangles(1));
                result.CapUvs.TransformUvs(edited, 1.5f, new Vector2(0.2f, 0.3f), null);

                for (int vertex = 0; vertex < before.Length; vertex++)
                {
                    if (!capVertices.Contains(vertex))
                    {
                        Assert.That(edited[vertex], Is.EqualTo(before[vertex]));
                    }
                }
            }
        }

        /// <summary>기존의 작은 틈 보정은 열린 경계 허용 여부와 별개로 계속 캡을 복구하는지 확인합니다.</summary>
        [Test]
        public void SmallGapRepair_StillRestoresAClosedCap()
        {
            Vector2[] contour =
            {
                new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f),
                new Vector2(-0.5f, -0.499f)
            };

            for (int edge = 0; edge < contour.Length - 1; edge++)
            {
                AppendPanel(contour[edge], contour[edge + 1]);
            }

            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.RepairOpenBoundaries = true;
            settings.MaximumGapDistance = 0.005f;

            using (SkinnedMeshCutEngine.Result result = Cut(settings))
            {
                Assert.That(result.RepairCount, Is.EqualTo(1));
                Assert.That(result.LoopCount, Is.EqualTo(1));
                Assert.That(result.CapUvs.Islands.Length, Is.EqualTo(1));
            }
        }

        /// <summary>원본과 만나지 않는 절단면은 여전히 잘못된 입력으로 거부하는지 확인합니다.</summary>
        [Test]
        public void PlaneWithoutIntersection_StillFailsValidation()
        {
            AppendCube(Vector3.zero);
            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.Origin = new Vector3(0f, 0f, 2f);

            Assert.Throws<InvalidOperationException>(() => Cut(settings));
        }

        /// <summary>닫힌 경계 일부만 덮는 유한 평면의 안전 검사가 유지되는지 확인합니다.</summary>
        [Test]
        public void PlanePartiallyCoveringClosedBody_StillFailsValidation()
        {
            AppendCube(Vector3.zero);
            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.Size = new Vector2(0.5f, 3f);

            Assert.Throws<InvalidOperationException>(() => Cut(settings));
        }

        /// <summary>캡 생성을 끈 경우에도 열린 천이 정상적으로 분리되는지 확인합니다.</summary>
        [Test]
        public void OpenSheetWithoutCaps_StillCuts()
        {
            AppendSheet(0f);
            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.GenerateCap = false;

            using (SkinnedMeshCutEngine.Result result = Cut(settings))
            {
                Assert.That(result.CapUvs, Is.Null);
                Assert.That(result.Part.vertexCount, Is.GreaterThan(0));
                Assert.That(result.Remainder.vertexCount, Is.GreaterThan(0));
            }
        }

        /// <summary>숨겨진 뒷면이 없는 U자 외피도 옵션을 켜면 끝점을 이어 캡을 생성하는지 확인합니다.</summary>
        [Test]
        public void OpenRobotShell_ClosureOptionCreatesCapAndUv()
        {
            AppendOpenContour(new[]
            {
                new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f)
            });
            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.CloseOpenBoundaries = true;

            using (SkinnedMeshCutEngine.Result result = Cut(settings))
            {
                Assert.That(result.LoopCount, Is.EqualTo(1));
                Assert.That(result.CapUvs.Islands.Length, Is.EqualTo(1));
                AssertCapArea(result.Part, 1f);
                AssertCapArea(result.Remainder, 1f);
                Assert.That(result.Boundary.Length, Is.EqualTo(14));
                AssertSkinWeights(result.Part);
                AssertSkinWeights(result.Remainder);
            }
        }

        /// <summary>닫기 옵션을 끄면 열린 로봇 외피가 기존처럼 캡 집계에서 제외되는지 확인합니다.</summary>
        [Test]
        public void OpenRobotShell_ClosureDisabledKeepsBoundaryOpen()
        {
            AppendOpenContour(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            CreateSourceMesh();

            using (SkinnedMeshCutEngine.Result result = Cut())
            {
                Assert.That(result.LoopCount, Is.Zero);
                Assert.That(result.CapUvs, Is.Null);
            }
        }

        /// <summary>오목한 열린 외피를 닫을 때 실제 오목 형태와 면적을 유지하는지 확인합니다.</summary>
        [Test]
        public void ConcaveOpenShell_ClosurePreservesShape()
        {
            AppendOpenContour(new[]
            {
                Vector2.zero, new Vector2(2f, 0f), new Vector2(2f, 2f), Vector2.one, new Vector2(0f, 2f)
            });
            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.CloseOpenBoundaries = true;

            using (SkinnedMeshCutEngine.Result result = Cut(settings))
            {
                Assert.That(result.LoopCount, Is.EqualTo(1));
                AssertCapArea(result.Part, 3f);
            }
        }

        /// <summary>서로 떨어진 두 열린 파츠의 끝점을 섞지 않고 독립된 캡을 만드는지 확인합니다.</summary>
        [Test]
        public void SeparateOpenShells_CreateSeparateCaps()
        {
            foreach (float offset in new[] { -2f, 2f })
            {
                AppendOpenContour(new[]
                {
                    new Vector2(offset, 0f), new Vector2(offset + 1f, 0f),
                    new Vector2(offset + 1f, 1f), new Vector2(offset, 1f)
                });
            }

            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.CloseOpenBoundaries = true;

            using (SkinnedMeshCutEngine.Result result = Cut(settings))
            {
                Assert.That(result.LoopCount, Is.EqualTo(2));
                Assert.That(result.CapUvs.Islands.Length, Is.EqualTo(2));
                AssertCapArea(result.Part, 2f);
            }
        }

        /// <summary>직선뿐인 얇은 천은 닫기 옵션을 켜도 면적 없는 캡을 만들지 않는지 확인합니다.</summary>
        [Test]
        public void CollinearBoundary_ClosureStillCutsWithoutCap()
        {
            AppendSheet(0f);
            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.CloseOpenBoundaries = true;

            using (SkinnedMeshCutEngine.Result result = Cut(settings))
            {
                Assert.That(result.CapUvs, Is.Null);
                Assert.That(result.Part.vertexCount, Is.GreaterThan(0));
            }
        }

        /// <summary>닫는 선분이 기존 경계와 교차하면 캡만 제외하고 절단은 계속하는지 확인합니다.</summary>
        [Test]
        public void SelfIntersectingBoundary_ClosureDoesNotBlockCut()
        {
            AppendOpenContour(new[]
            {
                Vector2.zero, new Vector2(2f, 2f), new Vector2(0f, 2f),
                new Vector2(2f, 0f), new Vector2(3f, -1f)
            });
            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.CloseOpenBoundaries = true;

            using (SkinnedMeshCutEngine.Result result = Cut(settings))
            {
                Assert.That(result.CapUvs, Is.Null);
                Assert.That(result.Remainder.vertexCount, Is.GreaterThan(0));
            }
        }

        /// <summary>분기 경계를 닫을 수 없어도 표면 절단 자체를 막지 않는지 확인합니다.</summary>
        [Test]
        public void BranchedBoundary_ClosureDoesNotInventCap()
        {
            AppendPanel(Vector2.zero, Vector2.right);
            AppendPanel(Vector2.zero, Vector2.up);
            AppendPanel(Vector2.zero, Vector2.left);
            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.CloseOpenBoundaries = true;

            using (SkinnedMeshCutEngine.Result result = Cut(settings))
            {
                Assert.That(result.CapUvs, Is.Null);
                Assert.That(result.Part.vertexCount, Is.GreaterThan(0));
            }
        }

        /// <summary>절단 평면에 놓인 원본 면이 있어도 다른 표면의 절단을 중단하지 않는지 확인합니다.</summary>
        [Test]
        public void CoplanarFace_PreservesOneOriginalFaceAndAllowsCut()
        {
            AppendCube(Vector3.zero);
            AppendCoplanarTriangle(false);
            CreateSourceMesh();

            using (SkinnedMeshCutEngine.Result result = Cut())
            {
                Assert.That(CountOriginalCoplanarFaces(result.Part), Is.Zero);
                Assert.That(CountOriginalCoplanarFaces(result.Remainder), Is.EqualTo(1));
                Assert.That(result.CapUvs.Islands.Length, Is.EqualTo(1));
            }
        }

        /// <summary>평면과 겹친 원본 면의 반대 감김 방향도 한쪽 결과에 일관되게 배정하는지 확인합니다.</summary>
        [Test]
        public void ReversedCoplanarFace_PreservesWindingOwnership()
        {
            AppendCube(Vector3.zero);
            AppendCoplanarTriangle(true);
            CreateSourceMesh();

            using (SkinnedMeshCutEngine.Result result = Cut())
            {
                Assert.That(CountOriginalCoplanarFaces(result.Part), Is.EqualTo(1));
                Assert.That(CountOriginalCoplanarFaces(result.Remainder), Is.Zero);
            }
        }

        /// <summary>평면과 겹친 면에 지정한 보호 마스크가 자동 귀속보다 우선하는지 확인합니다.</summary>
        [Test]
        public void CoplanarProtectedFace_RespectsPartMask()
        {
            AppendCube(Vector3.zero);
            AppendCoplanarTriangle(false);
            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.TriangleMask = new SkinnedMeshCutMaskMode[triangles.Count / 3];
            settings.TriangleMask[settings.TriangleMask.Length - 1] = SkinnedMeshCutMaskMode.KeepPart;

            using (SkinnedMeshCutEngine.Result result = Cut(settings))
            {
                Assert.That(CountOriginalCoplanarFaces(result.Part), Is.EqualTo(1));
            }
        }

        /// <summary>평면이 원본 모서리를 정확히 지나도 위치를 옮기지 않고 양쪽을 분리하는지 확인합니다.</summary>
        [Test]
        public void PlaneThroughExistingEdges_StillCuts()
        {
            AppendCube(Vector3.zero);
            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.Rotation = Quaternion.FromToRotation(Vector3.forward, new Vector3(1f, 1f, 0f).normalized);

            using (SkinnedMeshCutEngine.Result result = Cut(settings))
            {
                Assert.That(result.LoopCount, Is.EqualTo(1));
                Assert.That(result.Part.vertexCount, Is.GreaterThan(0));
                Assert.That(result.Remainder.vertexCount, Is.GreaterThan(0));
            }
        }

        /// <summary>연속한 절단 프로파일을 두께 없는 외피 패널로 구성합니다.</summary>
        private void AppendOpenContour(Vector2[] contour)
        {
            for (int i = 1; i < contour.Length; i++)
            {
                AppendPanel(contour[i - 1], contour[i]);
            }
        }

        /// <summary>같은 위치의 앞뒷면이 반대 귀속을 가져도 공면 모서리로 다시 합쳐지지 않는지 확인합니다.</summary>
        [Test]
        public void BackToBackCoplanarFaces_StayOnSeparateSides()
        {
            AppendCube(Vector3.zero);
            AppendCoplanarTriangle(false);
            AppendCoplanarTriangle(true);
            CreateSourceMesh();

            using (SkinnedMeshCutEngine.Result result = Cut())
            {
                Assert.That(CountOriginalCoplanarFaces(result.Part), Is.EqualTo(1));
                Assert.That(CountOriginalCoplanarFaces(result.Remainder), Is.EqualTo(1));
            }
        }

        /// <summary>닫기 옵션이 프리셋 저장·복원과 실제 엔진 설정까지 전달되는지 확인합니다.</summary>
        [Test]
        public void ClosureOption_RoundTripsThroughPresetAndEngineSettings()
        {
            AppendCube(Vector3.zero);
            CreateSourceMesh();
            SkinnedMeshCutterWindowBase window = ScriptableObject.CreateInstance<SkinnedMeshCutterWindowBase>();
            SkinnedMeshCutPreset preset = null;
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                Type type = typeof(SkinnedMeshCutterWindowBase);
                type.GetMethod("SetSource", flags).Invoke(window, new object[] { source });
                type.GetField("m_closeOpenBoundaries", flags).SetValue(window, true);
                preset = window.CapturePreset();
                type.GetField("m_closeOpenBoundaries", flags).SetValue(window, false);
                window.ApplyPreset(preset);
                SkinnedMeshCutEngine.Settings settings = (SkinnedMeshCutEngine.Settings)
                    type.GetMethod("BuildSettings", flags).Invoke(window, null);

                Assert.That(settings.CloseOpenBoundaries, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                if (preset != null)
                {
                    UnityEngine.Object.DestroyImmediate(preset);
                }
            }
        }

        /// <summary>몸체와 떨어진 위치에 평면과 정확히 겹치는 검증 삼각형을 추가합니다.</summary>
        private void AppendCoplanarTriangle(bool reverse)
        {
            int first = vertices.Count;
            vertices.Add(new Vector3(2f, 0f, 0f));
            vertices.Add(new Vector3(3f, 0f, 0f));
            vertices.Add(new Vector3(2f, 1f, 0f));
            triangles.AddRange(reverse
                ? new[] { first, first + 2, first + 1 }
                : new[] { first, first + 1, first + 2 });
        }

        /// <summary>원본 표면 서브메시에 검증용 공면 삼각형이 몇 번 남았는지 확인합니다.</summary>
        private static int CountOriginalCoplanarFaces(Mesh mesh)
        {
            int count = 0;
            Vector3[] points = mesh.vertices;
            int[] indices = mesh.GetTriangles(0);
            for (int i = 0; i < indices.Length; i += 3)
            {
                if (points[indices[i]].x >= 2f && points[indices[i + 1]].x >= 2f && points[indices[i + 2]].x >= 2f)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>캡의 삼각형 면적 합으로 오목한 단면과 복수 파츠의 형태를 검증합니다.</summary>
        private static void AssertCapArea(Mesh mesh, float expectedArea)
        {
            Assert.That(mesh.subMeshCount, Is.EqualTo(2));
            Vector3[] points = mesh.vertices;
            int[] indices = mesh.GetTriangles(1);
            float area = 0f;
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = points[indices[i]];
                Vector3 b = points[indices[i + 1]];
                Vector3 c = points[indices[i + 2]];
                area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            }

            Assert.That(area, Is.EqualTo(expectedArea).Within(0.0001f));
        }

        /// <summary>한 파츠의 앞뒤 직선 절단선을 위아래에서 닫아 하나의 캡으로 만드는지 확인합니다.</summary>
        [Test]
        public void OpenEndedPart_JoinsItsOwnFrontAndBackContours()
        {
            AppendOpenTube(-1f, 1f, 0.5f);
            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.CloseOpenBoundaries = true;

            using (SkinnedMeshCutEngine.Result result = Cut(settings))
            {
                Assert.That(result.LoopCount, Is.EqualTo(1));
                Assert.That(result.CapUvs.Islands.Length, Is.EqualTo(1));
                AssertCapArea(result.Part, 2f);
                AssertCapArea(result.Remainder, 2f);
                AssertSkinWeights(result.Part);
                AssertSkinWeights(result.Remainder);
            }
        }

        /// <summary>가슴과 골반에 파고든 허리도 원본 파츠로 유지하여 UV가 총 세 조각이 되는지 확인합니다.</summary>
        [Test]
        public void InterpenetratingRobotParts_CreateThreeIndependentCaps()
        {
            AppendCube(Vector3.up * 1.6f);
            AppendOpenTube(0.8f, 1.3f, 0.3f);
            AppendCube(Vector3.up * 0.5f);
            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.CloseOpenBoundaries = true;

            using (SkinnedMeshCutEngine.Result result = Cut(settings))
            {
                Assert.That(result.LoopCount, Is.EqualTo(3));
                Assert.That(result.CapUvs.Islands.Length, Is.EqualTo(3));
                AssertCapArea(result.Part, 2.3f);
                AssertCapArea(result.Remainder, 2.3f);
                Assert.That(FindCapIslandByHeight(result, 0.8f, 1.3f), Is.GreaterThanOrEqualTo(0));
            }
        }

        /// <summary>허리 UV를 옮겨도 겹쳐 있는 가슴과 골반의 UV가 바뀌지 않는지 확인합니다.</summary>
        [Test]
        public void WaistUv_EditDoesNotMoveChestOrPelvisUv()
        {
            AppendCube(Vector3.up * 1.6f);
            AppendOpenTube(0.8f, 1.3f, 0.3f);
            AppendCube(Vector3.up * 0.5f);
            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.CloseOpenBoundaries = true;

            using (SkinnedMeshCutEngine.Result result = Cut(settings))
            {
                Assert.That(result.CapUvs.Islands.Length, Is.EqualTo(3));
                int waist = FindCapIslandByHeight(result, 0.8f, 1.3f);
                Assert.That(waist, Is.GreaterThanOrEqualTo(0));
                Vector2[] before = result.Part.uv;
                SkinnedMeshCutUvTransform[] edits = new SkinnedMeshCutUvTransform[3];
                edits[waist].Offset = new Vector2(0.25f, -0.2f);
                result.CapUvs.ApplyToMesh(result.Part, 1f, Vector2.zero, edits);
                Vector2[] after = result.Part.uv;
                for (int island = 0; island < 3; island++)
                {
                    foreach (int vertex in result.CapUvs.Islands[island].Vertices)
                    {
                        Vector2 expected = island == waist ? edits[waist].Offset : Vector2.zero;
                        Assert.That(Vector2.Distance(after[vertex] - before[vertex], expected), Is.LessThan(0.00001f));
                    }
                }
            }
        }

        /// <summary>별개 파츠인 두 천 조각을 가깝다는 이유만으로 하나의 캡으로 합치지 않는지 확인합니다.</summary>
        [Test]
        public void SeparateSheets_DoNotBecomeOnePartCap()
        {
            AppendPanel(new Vector2(-0.5f, -1f), new Vector2(-0.5f, 1f));
            AppendPanel(new Vector2(0.5f, -1f), new Vector2(0.5f, 1f));
            CreateSourceMesh();
            SkinnedMeshCutEngine.Settings settings = CreateSettings();
            settings.CloseOpenBoundaries = true;

            using (SkinnedMeshCutEngine.Result result = Cut(settings))
            {
                Assert.That(result.LoopCount, Is.Zero);
                Assert.That(result.CapUvs, Is.Null);
            }
        }

        /// <summary>위아래 끝면 없이 옆면만 이어진 하나의 원본 파츠를 구성합니다.</summary>
        private void AppendOpenTube(float minimumY, float maximumY, float halfWidth)
        {
            int first = vertices.Count;
            vertices.AddRange(new[]
            {
                new Vector3(-halfWidth, minimumY, -halfWidth), new Vector3(halfWidth, minimumY, -halfWidth),
                new Vector3(halfWidth, maximumY, -halfWidth), new Vector3(-halfWidth, maximumY, -halfWidth),
                new Vector3(-halfWidth, minimumY, halfWidth), new Vector3(halfWidth, minimumY, halfWidth),
                new Vector3(halfWidth, maximumY, halfWidth), new Vector3(-halfWidth, maximumY, halfWidth)
            });
            int[] sides =
            {
                0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
                1, 2, 6, 1, 6, 5, 0, 4, 7, 0, 7, 3
            };
            triangles.AddRange(Array.ConvertAll(sides, vertex => first + vertex));
        }

        /// <summary>단면의 실제 정점 높이로 특정 원본 파츠의 UV 조각을 찾습니다.</summary>
        private static int FindCapIslandByHeight(SkinnedMeshCutEngine.Result result, float minimumY, float maximumY)
        {
            Vector3[] points = result.Part.vertices;
            for (int i = 0; i < result.CapUvs.Islands.Length; i++)
            {
                int[] indices = result.CapUvs.Islands[i].Vertices;
                Bounds bounds = new Bounds(points[indices[0]], Vector3.zero);
                foreach (int vertex in indices)
                {
                    bounds.Encapsulate(points[vertex]);
                }

                if (Mathf.Abs(bounds.min.y - minimumY) < 0.00001f
                    && Mathf.Abs(bounds.max.y - maximumY) < 0.00001f)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>모든 검증에서 같은 넓이와 UV 패킹 조건을 사용하는 기본 설정을 만듭니다.</summary>
        private static SkinnedMeshCutEngine.Settings CreateSettings()
        {
            return new SkinnedMeshCutEngine.Settings { PackUv = true };
        }

        /// <summary>열린 천의 출력에서는 마지막 표면 재질을 캡 재질로 바꾸지 않는지 확인합니다.</summary>
        [Test]
        public void OpenSheetOutput_KeepsSurfaceMaterial()
        {
            AppendSheet(0f);
            CreateSourceMesh();

            using (SkinnedMeshCutEngine.Result result = Cut())
            {
                AssertOutputMaterials(result.Part, false);
            }
        }

        /// <summary>닫힌 몸체 출력은 원본 표면 재질 뒤에 캡 재질을 추가하는지 확인합니다.</summary>
        [Test]
        public void ClosedBodyOutput_AppendsCapMaterial()
        {
            AppendCube(Vector3.zero);
            CreateSourceMesh();

            using (SkinnedMeshCutEngine.Result result = Cut())
            {
                AssertOutputMaterials(result.Part, true);
            }
        }

        /// <summary>실제 출력 창의 재질 배열을 검증하고 임시 창과 재질을 정리합니다.</summary>
        private void AssertOutputMaterials(Mesh outputMesh, bool expectCap)
        {
            // 재질 참조만 검증하므로 특정 렌더 파이프라인에 의존하지 않는 내장 셰이더를 사용한다.
            Shader shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);
            Material surface = new Material(shader);
            Material cap = new Material(shader);
            SkinnedMeshCutterWindowBase window = ScriptableObject.CreateInstance<SkinnedMeshCutterWindowBase>();
            try
            {
                // 보호된 출력 API를 호출하되 사용자가 열어 둔 도구 창의 상태는 변경하지 않는다.
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                Type windowType = typeof(SkinnedMeshCutterWindowBase);
                windowType.GetField("m_source", flags).SetValue(window, source);
                windowType.GetField("m_surfaceMaterialMesh", flags).SetValue(window, sourceMesh);
                windowType.GetField("m_surfaceMaterials", flags).SetValue(window, new[] { surface });
                Material[] materials = (Material[])windowType.GetMethod("OutputMaterials", flags)
                    .Invoke(window, new object[] { outputMesh, cap });

                Assert.That(materials.Length, Is.EqualTo(expectCap ? 2 : 1));
                Assert.That(materials[0], Is.SameAs(surface));
                if (expectCap)
                {
                    Assert.That(materials[1], Is.SameAs(cap));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(surface);
                UnityEngine.Object.DestroyImmediate(cap);
            }
        }

        /// <summary>현재 스킨에 설정을 적용해 실제 절단 엔진을 실행합니다.</summary>
        private SkinnedMeshCutEngine.Result Cut(SkinnedMeshCutEngine.Settings settings = null)
        {
            return new SkinnedMeshCutEngine(source, settings ?? CreateSettings()).Cut();
        }

        /// <summary>읽기 가능한 정점·UV와 단일 본 가중치를 가진 검증 메시를 구성합니다.</summary>
        private void CreateSourceMesh()
        {
            sourceMesh = new Mesh { name = "Boundary regression mesh" };
            sourceMesh.SetVertices(vertices);
            sourceMesh.SetTriangles(triangles, 0);
            sourceMesh.uv = vertices.ConvertAll(point => new Vector2(point.x, point.z)).ToArray();
            sourceMesh.bindposes = new[] { Matrix4x4.identity };
            sourceMesh.RecalculateNormals();
            sourceMesh.RecalculateBounds();

            byte[] counts = new byte[vertices.Count];
            BoneWeight1[] weights = new BoneWeight1[vertices.Count];
            for (int vertex = 0; vertex < vertices.Count; vertex++)
            {
                counts[vertex] = 1;
                weights[vertex] = new BoneWeight1 { boneIndex = 0, weight = 1f };
            }

            using (NativeArray<byte> nativeCounts = new NativeArray<byte>(counts, Allocator.Temp))
            using (NativeArray<BoneWeight1> nativeWeights = new NativeArray<BoneWeight1>(weights, Allocator.Temp))
            {
                sourceMesh.SetBoneWeights(nativeCounts, nativeWeights);
            }

            source.sharedMesh = sourceMesh;
        }

        /// <summary>중심 위치를 지정한 닫힌 정육면체를 현재 검증 표면에 추가합니다.</summary>
        private void AppendCube(Vector3 center)
        {
            int start = vertices.Count;
            Vector3[] corners =
            {
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f)
            };
            int[] faces =
            {
                0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5,
                3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3
            };
            vertices.AddRange(Array.ConvertAll(corners, point => center + point));
            triangles.AddRange(Array.ConvertAll(faces, vertex => start + vertex));
        }

        /// <summary>평면 높이를 지정한 열린 천 조각을 검증 표면에 추가합니다.</summary>
        private void AppendSheet(float height)
        {
            AppendPanel(new Vector2(-1f, height), new Vector2(1f, height));
        }

        /// <summary>절단 평면을 가로지르는 열린 사각 외피를 두 삼각형으로 추가합니다.</summary>
        private void AppendPanel(Vector2 start, Vector2 end)
        {
            int first = vertices.Count;
            vertices.Add(new Vector3(start.x, start.y, -1f));
            vertices.Add(new Vector3(end.x, end.y, -1f));
            vertices.Add(new Vector3(end.x, end.y, 1f));
            vertices.Add(new Vector3(start.x, start.y, 1f));
            triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
        }

        /// <summary>절단 후에도 본 인덱스와 정점별 가중치 합이 보존되는지 확인합니다.</summary>
        private static void AssertSkinWeights(Mesh mesh)
        {
            // Get 계열 NativeArray는 메시 소유 버퍼이므로 여기서 해제하지 않습니다.
            NativeArray<byte> counts = mesh.GetBonesPerVertex();
            NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
            Assert.That(counts.Length, Is.EqualTo(mesh.vertexCount));
            int offset = 0;

            foreach (byte count in counts)
            {
                float total = 0f;
                for (int influence = 0; influence < count; influence++)
                {
                    BoneWeight1 weight = weights[offset++];
                    Assert.That(weight.boneIndex, Is.Zero);
                    total += weight.weight;
                }

                Assert.That(total, Is.EqualTo(1f).Within(0.00001f));
            }
        }

        /// <summary>열린 표면의 UV가 캡 패킹의 영향을 받지 않고 보간되는지 확인합니다.</summary>
        private static void AssertSurfaceUvs(Mesh mesh)
        {
            Vector3[] positions = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            for (int vertex = 0; vertex < positions.Length; vertex++)
            {
                Assert.That(uvs[vertex].x, Is.EqualTo(positions[vertex].x).Within(0.00001f));
                Assert.That(uvs[vertex].y, Is.EqualTo(positions[vertex].z).Within(0.00001f));
            }
        }
    }
}
