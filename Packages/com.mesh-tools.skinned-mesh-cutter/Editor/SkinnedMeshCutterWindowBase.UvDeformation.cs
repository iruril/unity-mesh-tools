using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public partial class SkinnedMeshCutterWindowBase
    {
        protected bool m_uvEditGrid;
        protected bool m_uvDragRotating;
        protected int m_uvGridPoint;
        protected int m_uvDragGridPoint = -1;
        protected float m_uvDragAngle;
        protected Vector2 m_uvRotationHandle;
        protected Vector2 m_uvRotationPivot;
        protected Vector2 m_uvDragGridOffset;
        protected Vector2[] m_uvGridHandles;

        /// <summary>캡 UV 점을 현재 배치의 표시 좌표로 변환합니다.</summary>
        protected Vector2 UvLayoutPoint(Vector2 point)
        {
            return point + (point - m_capUvPreview.Bounds.center) * (m_uvLayoutScale - 1f) + m_uvOffset;
        }

        /// <summary>선택 UV 조각의 변형 격자 설정을 표시합니다.</summary>
        protected void DrawUvDeformationSettings()
        {
            SkinnedMeshCutUvTransform edit = UvIslandEdit(UvSelection);
            EditorGUI.BeginChangeCheck();
            float angle = EditorGUILayout.FloatField("선택 조각 회전 (°)", edit.Rotation);
            Vector2 axes = EditorGUILayout.Vector2Field("가로 / 세로 배율", edit.EffectiveAxisScale);

            if (EditorGUI.EndChangeCheck() && FiniteUvValue(angle) && FiniteUvValue(axes.x)
                && FiniteUvValue(axes.y))
            {
                RecordSettings();
                edit.Rotation = angle;
                edit.AxisScale = new Vector2(Mathf.Clamp(axes.x, .001f, 1000f), Mathf.Clamp(axes.y, .001f, 1000f));
                SetUvIslandEdit(UvSelection, edit);
                UpdateUvDisplay();
                MarkPreviewDirty();
            }

            if (!edit.HasGrid)
            {
                if (GUILayout.Button("변형 격자 추가 (3 × 3)"))
                {
                    RecordSettings();
                    edit.ResizeGrid(3, 3);
                    SetUvIslandEdit(UvSelection, edit);
                    m_uvEditGrid = true;
                    m_uvGridPoint = 4;
                    MarkPreviewDirty();
                }

                return;
            }

            m_uvEditGrid = EditorGUILayout.Toggle("격자 점 편집", m_uvEditGrid);

            if (!m_uvEditGrid)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            int columns = EditorGUILayout.IntSlider("가로 제어점 수", edit.GridColumns, 2, 9);
            int rows = EditorGUILayout.IntSlider("세로 제어점 수", edit.GridRows, 2, 9);

            if (EditorGUI.EndChangeCheck())
            {
                RecordSettings();
                edit.ResizeGrid(columns, rows);
                SetUvIslandEdit(UvSelection, edit);
                MarkPreviewDirty();
            }

            m_uvGridPoint = Mathf.Clamp(m_uvGridPoint, 0, edit.GridOffsets.Length - 1);
            EditorGUI.BeginChangeCheck();
            Vector2 point = EditorGUILayout.Vector2Field(
                $"선택 점 ({m_uvGridPoint % edit.GridColumns + 1}, {m_uvGridPoint / edit.GridColumns + 1})",
                edit.GridPoint(m_uvGridPoint) + edit.GridOffsets[m_uvGridPoint]);

            if (EditorGUI.EndChangeCheck() && FiniteUvValue(point.x) && FiniteUvValue(point.y))
            {
                RecordSettings();
                edit.GridOffsets[m_uvGridPoint] = point - edit.GridPoint(m_uvGridPoint);
                SetUvIslandEdit(UvSelection, edit);
                UpdateUvDisplay();
                MarkPreviewDirty();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("격자 변형 초기화"))
                {
                    RecordSettings();
                    edit.GridOffsets = new Vector2[edit.GridOffsets.Length];
                    SetUvIslandEdit(UvSelection, edit);
                    UpdateUvDisplay();
                    MarkPreviewDirty();
                }

                if (GUILayout.Button("격자 제거"))
                {
                    RecordSettings();
                    edit.GridColumns = edit.GridRows = 0;
                    edit.GridOffsets = null;
                    SetUvIslandEdit(UvSelection, edit);
                    UpdateUvDisplay();
                    MarkPreviewDirty();
                }
            }

            EditorGUILayout.HelpBox(
                "주황색 점을 각각 드래그해 자유롭게 변형합니다. 점 수 변경 시 변형을 새 격자로 보간합니다. 점을 교차시키면 텍스처가 접힙니다.",
                MessageType.None);
        }

        /// <summary>UV 편집 값이 유한한지 확인합니다.</summary>
        protected static bool FiniteUvValue(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        protected bool EditingUvGrid => UvSelection >= 0 && m_uvEditGrid && UvIslandEdit(UvSelection).HasGrid;

        /// <summary>UV 편집 화면에 필요한 표시 범위를 계산합니다.</summary>
        protected Rect UvEditorBounds()
        {
            Rect bounds = m_capUvPreview.DisplayBounds(m_uvDisplayUvs);
            Vector2 min = bounds.min;
            Vector2 max = bounds.max;

            for (int island = 0; island < m_capUvPreview.Islands.Length; island++)
            {
                SkinnedMeshCutUvTransform edit = UvIslandEdit(island);

                if (!edit.HasGrid)
                {
                    continue;
                }

                Rect raw = m_capUvPreview.Islands[island].Bounds;

                for (int i = 0; i < edit.GridOffsets.Length; i++)
                {
                    Vector2 point = UvLayoutPoint(edit.TransformPoint(raw.min + Vector2.Scale(edit.GridPoint(i), raw.size), raw));
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>선택 UV 조각의 변형 격자 핸들을 갱신합니다.</summary>
        protected void UpdateUvDeformationHandles(Rect canvas)
        {
            if (UvSelection < 0)
            {
                return;
            }

            SkinnedMeshCutUvTransform edit = UvIslandEdit(UvSelection);
            Rect bounds = m_capUvPreview.Islands[UvSelection].Bounds;
            m_uvRotationPivot = SkinnedMeshCutUvPreview.UvToScreen(UvLayoutPoint(bounds.center + edit.Offset), canvas, m_uvView);
            float top = Mathf.Min(m_uvCorners[0].y, m_uvCorners[1].y);
            m_uvRotationHandle = new Vector2(
                Mathf.Clamp(m_uvRotationPivot.x, canvas.xMin + 10f, canvas.xMax - 10f),
                Mathf.Clamp(top - 24f, canvas.yMin + 10f, canvas.yMax - 10f));

            if (!EditingUvGrid)
            {
                return;
            }

            if (m_uvGridHandles == null || m_uvGridHandles.Length != edit.GridOffsets.Length)
            {
                m_uvGridHandles = new Vector2[edit.GridOffsets.Length];
            }

            for (int i = 0; i < m_uvGridHandles.Length; i++)
            {
                Vector2 point = bounds.min + Vector2.Scale(edit.GridPoint(i), bounds.size);
                m_uvGridHandles[i] = SkinnedMeshCutUvPreview.UvToScreen(UvLayoutPoint(edit.TransformPoint(point, bounds)), canvas, m_uvView);
            }
        }

        /// <summary>UV 편집 화면에 변형 격자와 조작점을 그립니다.</summary>
        protected void DrawUvDeformationHandles(Rect canvas)
        {
            if (UvSelection < 0)
            {
                return;
            }

            UpdateUvDeformationHandles(canvas);
            Color previous = Handles.color;

            if (EditingUvGrid)
            {
                SkinnedMeshCutUvTransform edit = UvIslandEdit(UvSelection);
                Handles.color = new Color(1f, .65f, .15f, .85f);

                for (int i = 0; i < m_uvGridHandles.Length; i++)
                {
                    m_uvEdge[0] = m_uvGridHandles[i];

                    if (i % edit.GridColumns < edit.GridColumns - 1)
                    {
                        m_uvEdge[1] = m_uvGridHandles[i + 1];
                        Handles.DrawAAPolyLine(1.5f, m_uvEdge);
                    }

                    if (i + edit.GridColumns < m_uvGridHandles.Length)
                    {
                        m_uvEdge[1] = m_uvGridHandles[i + edit.GridColumns];
                        Handles.DrawAAPolyLine(1.5f, m_uvEdge);
                    }
                }

                for (int i = 0; i < m_uvGridHandles.Length; i++)
                {
                    EditorGUI.DrawRect(
                        new Rect(m_uvGridHandles[i] - Vector2.one * 4f, Vector2.one * 8f),
                        i == m_uvGridPoint ? Color.white : new Color(1f, .65f, .15f));
                }
            }
            else
            {
                Handles.color = Color.green;
                m_uvEdge[0] = m_uvRotationPivot;
                m_uvEdge[1] = m_uvRotationHandle;
                Handles.DrawAAPolyLine(1.5f, m_uvEdge);
                Handles.DrawWireDisc(m_uvRotationHandle, Vector3.forward, 6f);
                EditorGUI.DrawRect(new Rect(m_uvRotationPivot - Vector2.one * 2f, Vector2.one * 4f), Color.green);
            }

            Handles.color = previous;
        }
    }
}
