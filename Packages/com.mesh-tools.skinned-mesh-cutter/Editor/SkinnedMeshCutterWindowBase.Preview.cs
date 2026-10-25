using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public partial class SkinnedMeshCutterWindowBase
    {
        protected Stage m_previousStage;

        #region 미리보기 수명

        /// <summary>설정 변경 후 절단 미리보기 재계산을 예약합니다.</summary>
        protected void MarkPreviewDirty()
        {
            m_previewDirty = true;
            m_previewRequestedAt = EditorApplication.timeSinceStartup;
            SceneView.RepaintAll();
        }

        /// <summary>기존 절단 미리보기 결과와 UV 선택 상태를 해제합니다.</summary>
        protected void ClearPreview()
        {
            if (m_previewStage != null)
            {
                m_previewStage.SetResult(null, null, null, null);
            }

            m_previewResult?.Dispose();
            m_previewResult = null;
            m_previewBoundary = Array.Empty<Vector3>();
            m_capUvPreview = null;
        }

        /// <summary>예약된 미리보기 갱신과 작성 포즈 동기화를 처리합니다.</summary>
        protected void HandleEditorUpdate()
        {
            if (!ReferenceEquals(m_previewStage, null)
                && (m_previewStage == null || m_previewStage.Renderer == null))
            {
                m_previewStage = null;
                ClearPreview();
                MarkPreviewDirty();
            }

            if (m_previewDirty && !m_maskBrushEnabled && m_source != null && GUIUtility.hotControl == 0
                && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating
                && EditorApplication.timeSinceStartup - m_previewRequestedAt > 0.25d)
            {
                RefreshCutPreview();
            }
        }

        /// <summary>현재 설정으로 절단을 계산하고 미리보기 또는 진단 경계를 갱신합니다.</summary>
        protected void RefreshCutPreview()
        {
            ClearPreview();
            m_previewDirty = false;
            m_previewError = ValidateMaskTopology(true) ?? GetSourceError();

            if (m_previewError != null)
            {
                Repaint();

                return;
            }

            SkinnedMeshCutEngine engine = null;

            try
            {
                engine = new SkinnedMeshCutEngine(AuthoringSource(), BuildSettings());
                m_previewResult = engine.Cut();
                m_previewBoundary = m_previewResult.Boundary;
                m_capUvPreview = m_previewResult.CapUvs;
                UpdateStageResult();
            }
            catch (Exception exception)
            {
                m_previewError = exception.Message;

                if (engine != null)
                {
                    m_previewBoundary = engine.GetDiagnosticBoundary();
                }
            }

            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>격리된 스테이지에서 작성 포즈 미리보기를 엽니다.</summary>
        protected void OpenPosePreview()
        {
            if (m_previewStage != null)
            {
                return;
            }

            m_previousStage = StageUtility.GetCurrentStage();
            m_previewStage = CreateInstance<SkinnedMeshCutPreviewStage>();
            m_previewStage.Initialize(m_source);
            StageUtility.GoToStage(m_previewStage, false);
            m_previewStage.SamplePose(m_poseClip, m_poseTime);
            m_previewStage.Renderer.sharedMaterials = SurfaceMaterials();
            SceneView view = SceneView.lastActiveSceneView;

            if (view != null && m_previewStage.Renderer != null)
            {
                view.Frame(m_previewStage.Renderer.bounds, false);
            }

            MarkPreviewDirty();
        }

        /// <summary>작성 포즈 스테이지를 닫고 편집 기준을 원본으로 복원합니다.</summary>
        protected void ClosePosePreview()
        {
            if (m_previewStage == null)
            {
                return;
            }

            ClearPreview();

            if (StageUtility.GetCurrentStage() == m_previewStage)
            {
                if (m_previousStage != null)
                {
                    StageUtility.GoToStage(m_previousStage, false);
                }
                else
                {
                    StageUtility.GoToMainStage();
                }
            }

            if (m_previewStage != null)
            {
                DestroyImmediate(m_previewStage);
            }

            m_previewStage = null;
            m_previousStage = null;
            MarkPreviewDirty();
        }

        /// <summary>현재 절단 메시지와 표시 재질을 스테이지에 반영합니다.</summary>
        protected void UpdateStageResult()
        {
            if (m_previewStage == null)
            {
                return;
            }

            if (!m_showResult || m_previewResult == null || m_maskBrushEnabled)
            {
                m_previewStage.SetResult(null, null, null, null);

                return;
            }

            Material fallback = m_capMaterialOverride != null
                ? m_capMaterialOverride
                : SkinnedMeshCutDefaults.instance.CapMaterial;

            if (m_checkerPreview || fallback == null)
            {
                EnsureCheckerMaterial();
                fallback = m_checkerMaterial;
            }

            Material part = !m_checkerPreview && m_partCapMaterial != null ? m_partCapMaterial : fallback;
            Material remainder = !m_checkerPreview && m_remainingCapMaterial != null ? m_remainingCapMaterial : fallback;
            m_previewStage.SetResult(
                m_previewResult.Part,
                m_previewResult.Remainder,
                OutputMaterials(m_previewResult.Part, part),
                OutputMaterials(m_previewResult.Remainder, remainder),
                BuildSettings().Rotation * Vector3.forward * m_previewSeparation);
        }

        /// <summary>UV 점검용 체크 무늬 재질과 텍스처를 준비합니다.</summary>
        protected void EnsureCheckerMaterial()
        {
            if (m_checkerMaterial != null)
            {
                return;
            }

            m_checkerTexture = new Texture2D(64, 64)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            m_checkerTexture.wrapMode = TextureWrapMode.Repeat;
            m_checkerTexture.filterMode = FilterMode.Point;
            Color[] pixels = new Color[64 * 64];

            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    pixels[y * 64 + x] = ((x / 8 + y / 8) % 2) == 0 ? Color.white : new Color(0.1f, 0.2f, 0.3f);
                }
            }

            m_checkerTexture.SetPixels(pixels);
            m_checkerTexture.Apply();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            m_checkerMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            m_checkerMaterial.mainTexture = m_checkerTexture;
        }

        #endregion

        #region 씬 핸들

        /// <summary>Undo·Redo 이후 마스크와 절단 미리보기를 다시 계산합니다.</summary>
        protected void HandleUndoRedo()
        {
            m_maskRevision++;
            m_validatedMaskMesh = null;
            DisposeMaskResources();
            m_maskOverlayDirty = true;

            if (m_previewStage != null)
            {
                m_previewStage.SamplePose(m_poseClip, m_poseTime);
            }

            MarkPreviewDirty();
            Repaint();
        }

        /// <summary>Scene 뷰에서 평면 핸들·브러시·경계 표시를 처리합니다.</summary>
        protected void HandleSceneGui(SceneView view)
        {
            if (!m_showScenePreview || m_source == null || !m_planeInitialized
                || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (m_previewStage != null && StageUtility.GetCurrentStage() != m_previewStage)
            {
                return;
            }

            if (m_previewStage == null
                && StageUtility.GetStageHandle(m_source.gameObject) != StageUtility.GetCurrentStageHandle())
            {
                return;
            }

            Transform frame = PlaneFrame();

            if (frame == null)
            {
                return;
            }

            Vector3 position = frame.TransformPoint(m_planePosition);
            Quaternion rotation = frame.rotation * Quaternion.Euler(m_planeEuler);
            Vector3 u = rotation * Vector3.right;
            Vector3 v = rotation * Vector3.up;
            Vector3 normal = rotation * Vector3.forward;
            Vector3 halfU = u * m_planeSize.x * 0.5f;
            Vector3 halfV = v * m_planeSize.y * 0.5f;
            Vector3[] corners =
            {
                position + halfU + halfV,
                position + halfU - halfV,
                position - halfU - halfV,
                position - halfU + halfV
            };

            using (new Handles.DrawingScope())
            {
                Handles.DrawSolidRectangleWithOutline(corners, new Color(1f, 0.5f, 0f, 0.08f), Color.yellow);
                Handles.color = m_previewError == null ? Color.green : Color.red;

                if (m_previewBoundary.Length > 0)
                {
                    Handles.DrawLines(m_previewBoundary);
                }

                if (m_previewResult != null && m_previewResult.RepairCount > 0)
                {
                    Handles.color = new Color(1f, 0.45f, 0f);

                    for (int i = 0; i < m_previewResult.RepairedBoundary.Length; i += 2)
                    {
                        Handles.DrawAAPolyLine(5f, m_previewResult.RepairedBoundary[i], m_previewResult.RepairedBoundary[i + 1]);
                    }
                }

                Handles.color = Color.yellow;
                float handleSize = HandleUtility.GetHandleSize(position) * 0.3f;
                Vector3 arrow = m_detachPositive ? normal : -normal;
                Handles.ArrowHandleCap(0, position, Quaternion.LookRotation(arrow), handleSize, EventType.Repaint);
                Handles.Label(position, m_previewError == null ? "PART →" : "절단 범위를 확인하세요");
                DrawMaskScene(view);

                if (m_maskBrushEnabled)
                {
                    return;
                }

                EditorGUI.BeginChangeCheck();
                Vector3 newPosition = position;
                Quaternion newRotation = rotation;
                Vector2 newSize = m_planeSize;

                if (m_handleTool == Tool.Move)
                {
                    newPosition = Handles.PositionHandle(position, rotation);
                }
                else if (m_handleTool == Tool.Rotate)
                {
                    newRotation = Handles.RotationHandle(rotation, position);
                }
                else
                {
                    Vector3 edgeU = Handles.Slider(position + halfU, u, handleSize * 0.3f, Handles.CubeHandleCap, 0f);
                    Vector3 edgeV = Handles.Slider(position + halfV, v, handleSize * 0.3f, Handles.CubeHandleCap, 0f);
                    newSize.x = Mathf.Max(0.0001f, Vector3.Dot(edgeU - position, u) * 2f);
                    newSize.y = Mathf.Max(0.0001f, Vector3.Dot(edgeV - position, v) * 2f);
                }

                if (EditorGUI.EndChangeCheck())
                {
                    RecordSettings();
                    m_planePosition = frame.InverseTransformPoint(newPosition);
                    m_planeEuler = (Quaternion.Inverse(frame.rotation) * newRotation).eulerAngles;
                    m_planeSize = newSize;
                    MarkPreviewDirty();
                    Repaint();
                }
            }
        }
        #endregion
    }
}

