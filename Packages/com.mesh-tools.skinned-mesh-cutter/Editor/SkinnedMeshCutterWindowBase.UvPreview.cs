using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public partial class SkinnedMeshCutterWindowBase
    {
        protected SkinnedMeshCutUvPreview m_capUvPreview;
        protected Rect m_uvView = new Rect(0, 0, 1, 1);
        protected Vector2 m_uvDragStart;
        protected Vector2 m_uvDragOffset;
        protected Vector2 m_uvDragPivot;
        protected float m_uvDragScale;
        protected float m_uvDragDistance;
        protected bool m_uvDragScaling;
        protected bool m_uvEditIslands;
        protected int m_selectedUvIsland;
        protected Vector2[] m_uvDisplayUvs;
        protected bool m_uvShowRemainder;
        protected readonly Vector3[] m_uvTriangle = new Vector3[3];
        protected readonly Vector3[] m_uvEdge = new Vector3[2];
        protected static readonly string[] UvSideLabels =
        {
            "파츠 절단면",
            "몸체 절단면"
        };
        protected static readonly string[] UvEditLabels =
        {
            "전체 UV",
            "조각별"
        };
        protected readonly Vector2[] m_uvCorners = new Vector2[4];
        protected int UvSelection => m_uvEditIslands ? m_selectedUvIsland : -1;

        /// <summary>현재 배치 설정으로 캡 UV 미리보기와 선택 상태를 갱신합니다.</summary>
        protected void UpdateUvDisplay()
        {
            if (m_capUvPreview == null)
            {
                return;
            }

            if (m_uvDisplayUvs == null || m_uvDisplayUvs.Length != m_capUvPreview.Uvs.Length)
            {
                m_uvDisplayUvs = new Vector2[m_capUvPreview.Uvs.Length];
            }

            m_capUvPreview.TransformUvs(m_uvDisplayUvs, m_uvLayoutScale, m_uvOffset, m_uvIslandTransforms);
            m_selectedUvIsland = Mathf.Clamp(m_selectedUvIsland, 0, Mathf.Max(0, m_capUvPreview.Islands.Length - 1));
        }

        /// <summary>선택 UV 조각의 개별 변형 설정을 가져옵니다.</summary>
        protected SkinnedMeshCutUvTransform UvIslandEdit(int index)
        {
            return m_uvIslandTransforms != null
                && m_uvIslandTransforms.Length == m_capUvPreview.Islands.Length
                ? m_uvIslandTransforms[index]
                : default;
        }

        /// <summary>선택 UV 조각의 변형을 저장하고 미리보기를 갱신합니다.</summary>
        protected void SetUvIslandEdit(int index, SkinnedMeshCutUvTransform edit)
        {
            if (m_uvIslandTransforms == null
                || m_uvIslandTransforms.Length != m_capUvPreview.Islands.Length)
            {
                m_uvIslandTransforms = new SkinnedMeshCutUvTransform[m_capUvPreview.Islands.Length];
            }

            m_uvIslandTransforms[index] = edit;
        }

        /// <summary>선택한 UV 조각을 기준점 주위로 확대·축소합니다.</summary>
        protected void ScaleUvSelection(float scale, float originalScale, Vector2 originalOffset, Vector2 pivot)
        {
            scale = Mathf.Clamp(scale, .001f, 1000f);

            if (UvSelection >= 0)
            {
                SkinnedMeshCutUvTransform edit = UvIslandEdit(UvSelection);
                edit.Scale = scale;
                SetUvIslandEdit(UvSelection, edit);
            }
            else
            {
                m_uvLayoutScale = scale;
                m_uvOffset = originalOffset + (1f - scale / originalScale) * (pivot - m_capUvPreview.Bounds.center - originalOffset);
            }
        }

        /// <summary>캡 UV 조각 선택과 배치 설정을 표시합니다.</summary>
        protected void DrawUvEditSettings(bool hasCap)
        {
            using (new EditorGUI.DisabledScope(!hasCap))
            {
                m_uvEditIslands = GUILayout.Toolbar(m_uvEditIslands ? 1 : 0, UvEditLabels) == 1;

                if (!hasCap)
                {
                    return;
                }

                if (m_uvEditIslands)
                {
                    m_selectedUvIsland = EditorGUILayout.Popup("선택 조각", m_selectedUvIsland, m_capUvPreview.IslandLabels);
                }

                float oldScale = UvSelection >= 0 ? UvIslandEdit(UvSelection).EffectiveScale : m_uvLayoutScale;
                EditorGUI.BeginChangeCheck();
                float scale = EditorGUILayout.FloatField(m_uvEditIslands ? "선택 조각 크기" : "전체 UV 크기", oldScale);

                if (EditorGUI.EndChangeCheck() && !float.IsNaN(scale) && !float.IsInfinity(scale))
                {
                    RecordSettings();
                    ScaleUvSelection(
                        scale,
                        oldScale,
                        m_uvOffset,
                        m_capUvPreview.DisplayBounds(m_uvDisplayUvs, UvSelection).center);
                    UpdateUvDisplay();
                    MarkPreviewDirty();
                }

                if (m_uvEditIslands)
                {
                    SkinnedMeshCutUvTransform edit = UvIslandEdit(UvSelection);
                    EditorGUI.BeginChangeCheck();
                    Vector2 offset = EditorGUILayout.Vector2Field("선택 조각 오프셋", edit.Offset);

                    if (EditorGUI.EndChangeCheck())
                    {
                        RecordSettings();
                        edit.Offset = offset;
                        SetUvIslandEdit(UvSelection, edit);
                        UpdateUvDisplay();
                        MarkPreviewDirty();
                    }

                    DrawUvDeformationSettings();
                }

                if (m_uvIslandTransforms.Length > 0
                    && m_uvIslandTransforms.Length != m_capUvPreview.Islands.Length)
                {
                    EditorGUILayout.HelpBox("절단면 조각 수가 바뀌어 이전 개별 배치는 보류됩니다. 조각을 다시 조정하거나 초기화하세요.", MessageType.Info);
                }

                if (GUILayout.Button(m_uvEditIslands ? "선택 조각 초기화" : "전체 UV 배치 초기화"))
                {
                    RecordSettings();

                    if (UvSelection >= 0)
                    {
                        SetUvIslandEdit(UvSelection, default);
                    }
                    else
                    {
                        m_uvIslandTransforms = System.Array.Empty<SkinnedMeshCutUvTransform>();
                        m_uvLayoutScale = 1f;
                        m_uvOffset = Vector2.zero;
                    }

                    UpdateUvDisplay();
                    MarkPreviewDirty();
                }
            }
        }

        /// <summary>UV 이동·회전·크기 조작에 필요한 상태를 갱신합니다.</summary>
        protected void UpdateUvHandles(Rect canvas)
        {
            Rect bounds = m_capUvPreview.DisplayBounds(m_uvDisplayUvs, UvSelection);
            Vector2 min = SkinnedMeshCutUvPreview.UvToScreen(bounds.min, canvas, m_uvView);
            Vector2 max = SkinnedMeshCutUvPreview.UvToScreen(bounds.max, canvas, m_uvView);
            m_uvCorners[0] = min;
            m_uvCorners[1] = max;
            m_uvCorners[2] = new Vector2(min.x, max.y);
            m_uvCorners[3] = new Vector2(max.x, min.y);
        }

        /// <summary>UV 화면의 선택·이동·회전·확대 입력을 처리합니다.</summary>
        protected void HandleUvInput(Rect canvas, bool hasCap)
        {
            if (!hasCap)
            {
                return;
            }

            UpdateUvHandles(canvas);
            UpdateUvDeformationHandles(canvas);
            int control = GUIUtility.GetControlID("CutCapUvTransform".GetHashCode(), FocusType.Passive, canvas);
            Event current = Event.current;
            bool scaleHandle = false;

            foreach (Vector2 corner in m_uvCorners)
            {
                if (EditingUvGrid)
                {
                    break;
                }

                Rect hit = new Rect(corner - Vector2.one * 6f, Vector2.one * 12f);

                if (canvas.Contains(corner))
                {
                    EditorGUIUtility.AddCursorRect(hit, MouseCursor.ResizeUpLeft);
                }

                scaleHandle |= hit.Contains(current.mousePosition);
            }

            bool rotateHandle = UvSelection >= 0 && !EditingUvGrid
                && Vector2.Distance(current.mousePosition, m_uvRotationHandle) <= 10f;
            int gridHandle = -1;

            if (EditingUvGrid)
            {
                for (int i = 0; i < m_uvGridHandles.Length; i++)
                {
                    Rect hit = new Rect(m_uvGridHandles[i] - Vector2.one * 6f, Vector2.one * 12f);

                    if (canvas.Contains(m_uvGridHandles[i]))
                    {
                        EditorGUIUtility.AddCursorRect(hit, MouseCursor.MoveArrow);
                    }

                    if (hit.Contains(current.mousePosition))
                    {
                        gridHandle = i;
                    }
                }
            }
            else if (UvSelection >= 0)
            {
                EditorGUIUtility.AddCursorRect(
                    new Rect(m_uvRotationHandle - Vector2.one * 10f, Vector2.one * 20f),
                    MouseCursor.RotateArrow);
            }

            if (GUI.enabled && current.type == EventType.MouseDown && current.button == 0
                && canvas.Contains(current.mousePosition))
            {
                Vector2 point = SkinnedMeshCutUvPreview.ScreenToUv(current.mousePosition, canvas, m_uvView);

                if (m_uvEditIslands && !scaleHandle && !rotateHandle && gridHandle < 0)
                {
                    int hit = m_capUvPreview.PickIsland(point, m_uvDisplayUvs, m_selectedUvIsland);

                    if (hit < 0)
                    {
                        return;
                    }

                    m_selectedUvIsland = hit;

                    if (EditingUvGrid)
                    {
                        current.Use();
                        Repaint();

                        return;
                    }
                }

                Undo.RegisterCompleteObjectUndo(this, "Transform cut UV");
                m_uvDragScaling = scaleHandle;
                m_uvDragRotating = rotateHandle;
                m_uvDragGridPoint = gridHandle;

                if (gridHandle >= 0)
                {
                    m_uvGridPoint = gridHandle;
                    m_uvDragGridOffset = UvIslandEdit(UvSelection).GridOffsets[gridHandle];
                }

                m_uvDragStart = current.mousePosition;
                m_uvDragOffset = UvSelection >= 0 ? UvIslandEdit(UvSelection).Offset : m_uvOffset;
                m_uvDragScale = UvSelection >= 0 ? UvIslandEdit(UvSelection).EffectiveScale : m_uvLayoutScale;
                m_uvDragPivot = m_capUvPreview.DisplayBounds(m_uvDisplayUvs, UvSelection).center;

                if (rotateHandle)
                {
                    m_uvDragPivot = UvLayoutPoint(m_capUvPreview.Islands[UvSelection].Bounds.center + UvIslandEdit(UvSelection).Offset);
                    m_uvDragAngle = Mathf.Atan2(point.y - m_uvDragPivot.y, point.x - m_uvDragPivot.x) * Mathf.Rad2Deg;
                }

                m_uvDragDistance = Mathf.Max(.00001f, Vector2.Distance(point, m_uvDragPivot));
                GUIUtility.hotControl = control;
                current.Use();
                Repaint();
            }
            else if (GUIUtility.hotControl == control && current.type == EventType.MouseDrag)
            {
                if (m_uvDragRotating)
                {
                    Vector2 point = SkinnedMeshCutUvPreview.ScreenToUv(current.mousePosition, canvas, m_uvView);
                    float angle = Mathf.Atan2(point.y - m_uvDragPivot.y, point.x - m_uvDragPivot.x) * Mathf.Rad2Deg;
                    SkinnedMeshCutUvTransform edit = UvIslandEdit(UvSelection);
                    edit.Rotation += Mathf.DeltaAngle(m_uvDragAngle, angle);
                    m_uvDragAngle = angle;
                    SetUvIslandEdit(UvSelection, edit);
                }
                else if (m_uvDragGridPoint >= 0)
                {
                    SkinnedMeshCutUvTransform edit = UvIslandEdit(UvSelection);
                    Vector2 delta = SkinnedMeshCutUvPreview.ScreenToUvDelta(current.mousePosition - m_uvDragStart, canvas, m_uvView);
                    edit.GridOffsets[m_uvDragGridPoint] = m_uvDragGridOffset + edit.InverseDelta(delta, m_capUvPreview.Islands[UvSelection].Bounds, m_uvLayoutScale);
                    SetUvIslandEdit(UvSelection, edit);
                }
                else if (m_uvDragScaling)
                {
                    Vector2 point = SkinnedMeshCutUvPreview.ScreenToUv(current.mousePosition, canvas, m_uvView);
                    ScaleUvSelection(
                        m_uvDragScale * Vector2.Distance(point, m_uvDragPivot) / m_uvDragDistance,
                        m_uvDragScale,
                        m_uvDragOffset,
                        m_uvDragPivot);
                }
                else
                {
                    Vector2 delta = SkinnedMeshCutUvPreview.ScreenToUvDelta(current.mousePosition - m_uvDragStart, canvas, m_uvView);

                    if (UvSelection >= 0)
                    {
                        SkinnedMeshCutUvTransform edit = UvIslandEdit(UvSelection);
                        edit.Offset = m_uvDragOffset + delta / m_uvLayoutScale;
                        SetUvIslandEdit(UvSelection, edit);
                    }
                    else
                    {
                        m_uvOffset = m_uvDragOffset + delta;
                    }
                }

                UpdateUvDisplay();
                MarkPreviewDirty();
                Repaint();
                current.Use();
            }
            else if (GUIUtility.hotControl == control && current.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                current.Use();
                MarkPreviewDirty();
            }
        }

        /// <summary>닫힌 캡의 UV 조각과 선택 핸들을 그립니다.</summary>
        protected void DrawCapUvPreview()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("머티리얼 위에서 UV 이동 · 크기 · 회전 · 격자 변형", EditorStyles.boldLabel);

            if (!PartOnlyAuthoring)
            {
                m_uvShowRemainder = GUILayout.Toolbar(m_uvShowRemainder ? 1 : 0, UvSideLabels) == 1;
            }

            Material material = ResolveCapMaterial(!PartOnlyAuthoring && m_uvShowRemainder);
            Texture texture = material != null ? material.mainTexture : null;
            Vector2 tiling = material != null ? material.mainTextureScale : Vector2.one;
            Vector2 offset = material != null ? material.mainTextureOffset : Vector2.zero;
            Color tint = material != null && material.HasProperty("_BaseColor")
                ? material.GetColor("_BaseColor")
                : material != null && material.HasProperty("_Color")
                ? material.GetColor("_Color")
                : Color.white;
            bool hasCap = m_capUvPreview != null && m_capUvPreview.Triangles.Length > 0;
            UpdateUvDisplay();
            DrawUvEditSettings(hasCap);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("0–1 보기"))
                {
                    m_uvView = new Rect(0, 0, 1, 1);
                }

                using (new EditorGUI.DisabledScope(m_capUvPreview == null || m_capUvPreview.Triangles.Length == 0))
                {
                    if (GUILayout.Button("UV 전체 보기"))
                    {
                        Rect bounds = UvEditorBounds();
                        Vector2 center = bounds.center;
                        float size = Mathf.Max(.1f, Mathf.Max(bounds.width, bounds.height) * 1.2f);
                        m_uvView = new Rect(center - Vector2.one * size * .5f, Vector2.one * size);
                    }
                }
            }

            Rect region = GUILayoutUtility.GetRect(0f, 360f, GUILayout.ExpandWidth(true));
            float side = Mathf.Min(region.width, region.height);
            Rect canvas = new Rect(region.x + (region.width - side) * .5f, region.y, side, side);
            EditorGUIUtility.AddCursorRect(canvas, MouseCursor.Pan);
            HandleUvInput(canvas, hasCap);
            Event current = Event.current;
            GUI.BeginGroup(canvas);
            Rect local = new Rect(0, 0, canvas.width, canvas.height);

            if (current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(local, new Color(.12f, .12f, .12f));
                Color oldColor = GUI.color;
                GUI.color = tint;

                if (texture != null)
                {
                    GUI.DrawTextureWithTexCoords(
                        local,
                        texture,
                        SkinnedMeshCutUvPreview.TextureCoordinates(m_uvView, tiling, offset),
                        false);
                }
                else
                {
                    EditorGUI.DrawRect(local, tint);
                }

                GUI.color = oldColor;

                if (hasCap)
                {
                    Color oldHandlesColor = Handles.color;

                    for (int islandIndex = 0; islandIndex < m_capUvPreview.Islands.Length; islandIndex++)
                    {
                        SkinnedMeshCutUvPreview.Island island = m_capUvPreview.Islands[islandIndex];
                        bool selected = UvSelection < 0 || UvSelection == islandIndex;
                        Handles.color = selected ? new Color(.1f, .8f, 1f, .25f) : new Color(1f, .75f, .05f, .12f);

                        for (int i = 0; i < island.Triangles.Length; i += 3)
                        {
                            for (int j = 0; j < 3; j++)
                            {
                                m_uvTriangle[j] = SkinnedMeshCutUvPreview.UvToScreen(m_uvDisplayUvs[island.Triangles[i + j]], local, m_uvView);
                            }

                            Handles.DrawAAConvexPolygon(m_uvTriangle);
                        }

                        Handles.color = selected ? Color.cyan : new Color(1f, .85f, .1f, 1f);

                        for (int i = 0; i < island.Boundary.Length; i += 2)
                        {
                            for (int j = 0; j < 2; j++)
                            {
                                m_uvEdge[j] = SkinnedMeshCutUvPreview.UvToScreen(m_uvDisplayUvs[island.Boundary[i + j]], local, m_uvView);
                            }

                            Handles.DrawAAPolyLine(2f, m_uvEdge);
                        }
                    }

                    Handles.color = oldHandlesColor;
                    UpdateUvHandles(local);

                    if (!EditingUvGrid)
                    {
                        foreach (Vector2 corner in m_uvCorners)
                        {
                            EditorGUI.DrawRect(new Rect(corner - Vector2.one * 4f, Vector2.one * 8f), Color.cyan);
                        }
                    }

                    DrawUvDeformationHandles(local);
                }
            }

            GUI.EndGroup();
            EditorGUILayout.LabelField(
                $"U {m_uvView.xMin:F2}–{m_uvView.xMax:F2}  ·  V {m_uvView.yMin:F2}–{m_uvView.yMax:F2}",
                EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(
                hasCap
                    ? "영역: 이동 · 하늘색 모서리: 크기 · 초록색 원: 회전 · 주황색 격자 점: 자유 변형. " + "겹친 조각은 선택 목록으로 고를 수 있습니다. Ctrl+Z로 되돌립니다."
                    : "절단 평면을 설정하고 미리보기를 갱신하면 실제 절단면의 UV 윤곽이 표시됩니다.",
                MessageType.Info);
        }
    }
}
