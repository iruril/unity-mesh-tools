using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace MeshTools.Dismemberment.Editor
{
    [Serializable]
    [MovedFrom(true, "Irony.Dismemberment.Editor", "Irony.SkinnedMeshCutter.Editor", null)]
    public struct SkinnedMeshCutUvTransform
    {
        public Vector2 Offset;
        public float Scale;
        public float Rotation;
        public Vector2 AxisScale;
        public int GridColumns, GridRows;
        public Vector2[] GridOffsets;
        public float EffectiveScale => Scale > 0f ? Scale : 1f;
        public Vector2 EffectiveAxisScale => new Vector2(AxisScale.x > 0f ? AxisScale.x : 1f, AxisScale.y > 0f ? AxisScale.y : 1f);
        public bool HasGrid => GridColumns >= 2 && GridRows >= 2 && GridColumns <= 9 && GridRows <= 9
            && GridOffsets != null
            && GridOffsets.Length == GridColumns * GridRows;

        /// <summary>격자 좌표에 저장된 UV 변위 값을 반환합니다.</summary>
        public Vector2 GridOffset(Vector2 normalized)
        {
            if (!HasGrid)
            {
                return Vector2.zero;
            }

            float x = Mathf.Clamp01(normalized.x) * (GridColumns - 1);
            float y = Mathf.Clamp01(normalized.y) * (GridRows - 1);
            int column = Mathf.Min((int)x, GridColumns - 2);
            int row = Mathf.Min((int)y, GridRows - 2);
            int index = row * GridColumns + column;

            return Vector2.Lerp(
                Vector2.Lerp(GridOffsets[index], GridOffsets[index + 1], x - column),
                Vector2.Lerp(GridOffsets[index + GridColumns], GridOffsets[index + GridColumns + 1], x - column),
                y - row);
        }

        /// <summary>기존 변형을 보간해 새로운 해상도의 UV 격자를 만듭니다.</summary>
        public void ResizeGrid(int columns, int rows)
        {
            columns = Mathf.Clamp(columns, 2, 9);
            rows = Mathf.Clamp(rows, 2, 9);
            Vector2[] offsets = new Vector2[columns * rows];

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    offsets[y * columns + x] = GridOffset(new Vector2((float)x / (columns - 1), (float)y / (rows - 1)));
                }
            }

            GridColumns = columns;
            GridRows = rows;
            GridOffsets = offsets;
        }

        /// <summary>격자 제어점의 원래 위치와 변위를 합산합니다.</summary>
        public Vector2 GridPoint(int index)
        {
            return new Vector2(
                (float)(index % GridColumns) / (GridColumns - 1),
                (float)(index / GridColumns) / (GridRows - 1));
        }

        /// <summary>UV 점에 격자 변형·회전·크기·이동을 적용합니다.</summary>
        public Vector2 TransformPoint(Vector2 point, Rect bounds)
        {
            Vector2 normalized = new Vector2(
                Mathf.InverseLerp(bounds.xMin, bounds.xMax, point.x),
                Mathf.InverseLerp(bounds.yMin, bounds.yMax, point.y));
            point += Vector2.Scale(GridOffset(normalized), bounds.size);
            Vector2 delta = point - bounds.center;
            Vector2 scale = EffectiveAxisScale * EffectiveScale;
            // 변형이 없는 경우 기존 UV 채널과 프리셋을 포함한 원본 값을 정확히 유지한다.
            point += Vector2.Scale(delta, scale - Vector2.one);

            if (Rotation != 0f)
            {
                point = bounds.center + Rotate(point - bounds.center, Rotation);
            }

            return point + Offset;
        }

        /// <summary>표시 좌표 이동량을 UV 조각의 로컬 변위로 역변환합니다.</summary>
        public Vector2 InverseDelta(Vector2 delta, Rect bounds, float layoutScale)
        {
            delta = Rotate(delta, -Rotation) / (Mathf.Max(.001f, layoutScale) * EffectiveScale);
            Vector2 size = Vector2.Scale(bounds.size, EffectiveAxisScale);

            return new Vector2(delta.x / Mathf.Max(.000001f, size.x), delta.y / Mathf.Max(.000001f, size.y));
        }

        /// <summary>UV 벡터를 지정한 각도만큼 회전합니다.</summary>
        public static Vector2 Rotate(Vector2 point, float degrees)
        {
            float angle = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(angle);
            float s = Mathf.Sin(angle);

            return new Vector2(point.x * c - point.y * s, point.x * s + point.y * c);
        }
    }
}
