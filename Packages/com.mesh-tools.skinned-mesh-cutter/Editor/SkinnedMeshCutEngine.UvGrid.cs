using System.Collections.Generic;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshCutEngine
    {
        /// <summary>UV 변형 격자 경계에 맞춰 캡 삼각형을 세분화합니다.</summary>
        private void RefineCapGrid(
            List<Vertex> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            SkinnedMeshCutUvTransform edit)
        {
            if (!edit.HasGrid)
            {
                return;
            }

            Vector2 min = Vector2.positiveInfinity;
            Vector2 max = Vector2.negativeInfinity;

            foreach (Vector2 uv in uvs)
            {
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }

            Vector2 size = max - min;

            if (size.x < .000001f || size.y < .000001f)
            {
                return;
            }

            var normalized = uvs.ConvertAll(uv => new Vector2((uv.x - min.x) / size.x, (uv.y - min.y) / size.y));
            var edgeCounts = new Dictionary<ulong, int>();
            var samples = new List<Vector2>();

            for (int i = 0; i < triangles.Count; i += 3)
            {
                // 그리드 선 사이의 좁은 조각도 표본에 포함하도록 무게중심을 추가한다.
                samples.Add((normalized[triangles[i]] + normalized[triangles[i + 1]] + normalized[triangles[i + 2]]) / 3f);

                for (int j = 0; j < 3; j++)
                {
                    int a = triangles[i + j];
                    int b = triangles[i + (j + 1) % 3];
                    ulong key = ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                    edgeCounts.TryGetValue(key, out int count);
                    edgeCounts[key] = count + 1;
                }
            }

            var boundary = new List<Vector2>();

            foreach (var edge in edgeCounts)
            {
                if (edge.Value == 1)
                {
                    boundary.Add(normalized[(int)(edge.Key >> 32)]);
                    boundary.Add(normalized[(int)(edge.Key & uint.MaxValue)]);
                }
            }

            int columns = (edit.GridColumns - 1) * 2;
            int rows = (edit.GridRows - 1) * 2;

            for (int y = 1; y < rows; y++)
            {
                for (int x = 1; x < columns; x++)
                {
                    samples.Add(new Vector2((float)x / columns, (float)y / rows));
                }
            }

            foreach (Vector2 point in samples)
            {
                bool skip = normalized.Exists(uv => (uv - point).sqrMagnitude < 1e-10f);

                // 몸체와 단면의 이음매 및 구멍 경계는 원래 위치와 밀폐 상태를 유지한다.
                for (int i = 0; !skip && i < boundary.Count; i += 2)
                {
                    Vector2 edge = boundary[i + 1] - boundary[i];
                    float t = Mathf.Clamp01(Vector2.Dot(point - boundary[i], edge) / Mathf.Max(1e-12f, edge.sqrMagnitude));
                    skip = (point - boundary[i] - edge * t).sqrMagnitude < 1e-10f;
                }

                if (skip)
                {
                    continue;
                }

                int added = -1;
                int originalCount = triangles.Count;

                for (int i = originalCount - 3; i >= 0; i -= 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];
                    Vector2 pa = normalized[a];
                    Vector2 pb = normalized[b];
                    Vector2 pc = normalized[c];
                    float area = UvCross(pb - pa, pc - pa);

                    if (Mathf.Abs(area) < 1e-10f)
                    {
                        continue;
                    }

                    float wb = UvCross(point - pa, pc - pa) / area;
                    float wc = UvCross(pb - pa, point - pa) / area;
                    float wa = 1f - wb - wc;
                    const float tolerance = .00001f;

                    if (wa < -tolerance || wb < -tolerance || wc < -tolerance)
                    {
                        continue;
                    }

                    if (added < 0)
                    {
                        float ab = Mathf.Max(0f, wa) + Mathf.Max(0f, wb);
                        Vertex vertex = Interpolate(vertices[a], vertices[b], Mathf.Clamp01(wb / Mathf.Max(1e-10f, ab)));
                        vertex = Interpolate(vertex, vertices[c], Mathf.Clamp01(wc));
                        added = vertices.Count;
                        vertices.Add(vertex);
                        uvs.Add(min + Vector2.Scale(point, size));
                        normalized.Add(point);
                    }

                    triangles.RemoveRange(i, 3);

                    if (wc > tolerance)
                    {
                        triangles.AddRange(new[] { a, b, added });
                    }

                    if (wa > tolerance)
                    {
                        triangles.AddRange(new[] { b, c, added });
                    }

                    if (wb > tolerance)
                    {
                        triangles.AddRange(new[] { c, a, added });
                    }
                }
            }
        }

        /// <summary>UV 평면에서 두 벡터의 부호 있는 외적을 구합니다.</summary>
        private static float UvCross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }
    }
}
