using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>실제 절단면 서브메시에서 채움 삼각형과 외곽·구멍 경계를 가져온다.</summary>
    public sealed class SkinnedMeshCutUvPreview
    {
        public readonly Vector2[] Uvs;
        public readonly int[] Triangles;
        public readonly int[] Boundary;
        public readonly Rect Bounds;
        public readonly Island[] Islands;
        public readonly string[] IslandLabels;

        public sealed class Island
        {
            public int[] Vertices;
            public int[] Triangles;
            public int[] Boundary;
            public Rect Bounds;
        }

        /// <summary>실제 캡 삼각형만 연결해 UV 조각과 경계를 구성합니다.</summary>
        public SkinnedMeshCutUvPreview(Mesh mesh, int capSubmesh)
        {
            Uvs = mesh.uv;
            Triangles = mesh.GetTriangles(capSubmesh);
            Dictionary<ulong, int> edges = new Dictionary<ulong, int>();
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < Triangles.Length; i += 3)
            {
                for (int j = 0; j < 3; j++)
                {
                    int a = Triangles[i + j];
                    int b = Triangles[i + (j + 1) % 3];
                    min = Vector2.Min(min, Uvs[a]);
                    max = Vector2.Max(max, Uvs[a]);
                    ulong edge = ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                    edges.TryGetValue(edge, out int count);
                    edges[edge] = count + 1;
                }
            }

            List<int> boundary = new List<int>();

            foreach (KeyValuePair<ulong, int> edge in edges)
            {
                if (edge.Value == 1)
                {
                    boundary.Add((int)(edge.Key >> 32));
                    boundary.Add((int)(edge.Key & uint.MaxValue));
                }
            }

            Boundary = boundary.ToArray();
            Bounds = Triangles.Length > 0 ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : new Rect(0, 0, 1, 1);
            // UV 좌표가 겹쳐도 메시의 연결 관계로 조각을 구분한다. 내부 구멍은 같은 조각에 속한다.
            int[] parents = Enumerable.Range(0, Uvs.Length).ToArray();

            for (int i = 0; i < Triangles.Length; i += 3)
            {
                for (int j = 1; j < 3; j++)
                {
                    int a = Root(parents, Triangles[i]);
                    int b = Root(parents, Triangles[i + j]);
                    parents[Mathf.Max(a, b)] = Mathf.Min(a, b);
                }
            }

            var groups = new SortedDictionary<int, List<int>>();

            for (int i = 0; i < Triangles.Length; i += 3)
            {
                int root = Root(parents, Triangles[i]);

                if (!groups.TryGetValue(root, out List<int> indices))
                {
                    groups.Add(root, indices = new List<int>());
                }

                indices.AddRange(new[] { Triangles[i], Triangles[i + 1], Triangles[i + 2] });
            }

            Islands = groups.Select(group => new Island
            {
                Triangles = group.Value.ToArray(),
                Vertices = group.Value.Distinct().ToArray(),
                Boundary = Boundary.Where(vertex => Root(parents, vertex) == group.Key).ToArray(),
                Bounds = BoundsOf(Uvs, group.Value)
            })
                .ToArray();
            IslandLabels = Enumerable.Range(1, Islands.Length).Select(index => "UV 조각 " + index).ToArray();
        }

        /// <summary>UV 정점 연결 집합의 대표 인덱스를 찾습니다.</summary>
        private static int Root(int[] parents, int vertex)
        {
            while (parents[vertex] != vertex)
            {
                parents[vertex] = parents[parents[vertex]];
                vertex = parents[vertex];
            }

            return vertex;
        }

        /// <summary>전체 배치와 조각별 변형을 적용한 UV 배열을 계산합니다.</summary>
        public void TransformUvs(Vector2[] output, float scale, Vector2 offset, SkinnedMeshCutUvTransform[] edits)
        {
            Array.Copy(Uvs, output, Uvs.Length);

            for (int i = 0; i < Islands.Length; i++)
            {
                SkinnedMeshCutUvTransform edit = edits != null && edits.Length == Islands.Length ? edits[i] : default;

                foreach (int vertex in Islands[i].Vertices)
                {
                    Vector2 point = edit.TransformPoint(Uvs[vertex], Islands[i].Bounds);
                    output[vertex] = point + (point - Bounds.center) * (Mathf.Max(.001f, scale) - 1f) + offset;
                }
            }
        }

        /// <summary>캡 정점의 UV와 접선을 갱신하고 원본 표면 UV는 보존합니다.</summary>
        public void ApplyToMesh(Mesh mesh, float scale, Vector2 offset, SkinnedMeshCutUvTransform[] edits)
        {
            Vector2[] output = new Vector2[Uvs.Length];
            TransformUvs(output, scale, offset, edits);
            int dimension = mesh.GetVertexAttributeDimension(UnityEngine.Rendering.VertexAttribute.TexCoord0);

            if (dimension <= 2)
            {
                mesh.uv = output;
            }
            else
            {
                var extended = new List<Vector4>();
                mesh.GetUVs(0, extended);

                foreach (Island island in Islands)
                {
                    foreach (int vertex in island.Vertices)
                    {
                        Vector4 uv = extended[vertex];
                        uv.x = output[vertex].x;
                        uv.y = output[vertex].y;
                        extended[vertex] = uv;
                    }
                }

                if (dimension == 3)
                {
                    mesh.SetUVs(0, extended.ConvertAll(uv => new Vector3(uv.x, uv.y, uv.z)));
                }
                else
                {
                    mesh.SetUVs(0, extended);
                }
            }

            if (edits != null && edits.Length == Islands.Length)
            {
                for (int i = 0; i < edits.Length; i++)
                {
                    if (edits[i].Rotation != 0f || edits[i].HasGrid || edits[i].EffectiveAxisScale != Vector2.one)
                    {
                        UpdateCapTangents(mesh, output, Islands[i]);
                    }
                }
            }
        }

        /// <summary>변형된 캡 UV 방향에 맞춰 접선과 손잡이 방향을 갱신합니다.</summary>
        private static void UpdateCapTangents(Mesh mesh, Vector2[] uvs, Island island)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            Vector3[] directions = new Vector3[vertices.Length];
            Vector3[] bitangents = new Vector3[vertices.Length];
            int[] indices = island.Triangles;

            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i];
                int b = indices[i + 1];
                int c = indices[i + 2];
                Vector3 ab = vertices[b] - vertices[a];
                Vector3 ac = vertices[c] - vertices[a];
                Vector2 uvB = uvs[b] - uvs[a];
                Vector2 uvC = uvs[c] - uvs[a];
                float determinant = Cross(uvB, uvC);

                if (Mathf.Abs(determinant) < 1e-12f)
                {
                    continue;
                }

                Vector3 tangent = (ab * uvC.y - ac * uvB.y) / determinant;
                Vector3 bitangent = (ac * uvB.x - ab * uvC.x) / determinant;
                directions[a] += tangent;
                directions[b] += tangent;
                directions[c] += tangent;
                bitangents[a] += bitangent;
                bitangents[b] += bitangent;
                bitangents[c] += bitangent;
            }

            foreach (int vertex in island.Vertices)
            {
                Vector3 direction = directions[vertex] - normals[vertex] * Vector3.Dot(normals[vertex], directions[vertex]);

                if (direction.sqrMagnitude < 1e-12f)
                {
                    continue;
                }

                direction.Normalize();
                float sign = Vector3.Dot(Vector3.Cross(normals[vertex], direction), bitangents[vertex]) < 0f ? -1f : 1f;
                tangents[vertex] = new Vector4(direction.x, direction.y, direction.z, sign);
            }

            mesh.tangents = tangents;
        }

        /// <summary>모든 UV 조각을 포함하는 표시 범위를 반환합니다.</summary>
        public Rect DisplayBounds(Vector2[] uvs, int island = -1)
        {
            return BoundsOf(uvs, island >= 0 ? Islands[island].Vertices : Triangles);
        }

        /// <summary>지정한 UV 정점 집합의 경계 사각형을 계산합니다.</summary>
        private static Rect BoundsOf(Vector2[] uvs, IEnumerable<int> vertices)
        {
            Vector2 min = Vector2.positiveInfinity;
            Vector2 max = Vector2.negativeInfinity;

            foreach (int vertex in vertices)
            {
                min = Vector2.Min(min, uvs[vertex]);
                max = Vector2.Max(max, uvs[vertex]);
            }

            return float.IsInfinity(min.x)
                ? new Rect(0, 0, 1, 1)
                : Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>UV 좌표를 포함하는 가장 앞의 캡 조각을 찾습니다.</summary>
        public int PickIsland(Vector2 point, Vector2[] uvs, int preferred = -1)
        {
            if (preferred >= 0 && preferred < Islands.Length && Contains(preferred, point, uvs))
            {
                return preferred;
            }

            for (int i = Islands.Length - 1; i >= 0; i--)
            {
                if (Contains(i, point, uvs))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>UV 점이 삼각형 내부에 있는지 확인합니다.</summary>
        private bool Contains(int island, Vector2 point, Vector2[] uvs)
        {
            int[] indices = Islands[island].Triangles;

            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector2 a = uvs[indices[i]];
                Vector2 b = uvs[indices[i + 1]];
                Vector2 c = uvs[indices[i + 2]];
                float area = Cross(b - a, c - a);

                if (Mathf.Abs(area) < 1e-10f)
                {
                    continue;
                }

                float u = Cross(b - a, point - a) / area;
                float v = Cross(point - a, c - a) / area;

                if (u >= 0f && v >= 0f && u + v <= 1f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>두 벡터의 외적을 계산합니다.</summary>
        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        /// <summary>화면 위치를 UV 편집 좌표로 변환합니다.</summary>
        public static Vector2 ScreenToUv(Vector2 point, Rect viewport, Rect uvView)
        {
            return uvView.position + new Vector2(
                (point.x - viewport.x) / viewport.width * uvView.width,
                (viewport.yMax - point.y) / viewport.height * uvView.height);
        }

        /// <summary>화면 이동량을 UV 편집 좌표의 이동량으로 변환합니다.</summary>
        public static Vector2 ScreenToUvDelta(Vector2 delta, Rect viewport, Rect uvView)
        {
            return new Vector2(delta.x / viewport.width * uvView.width, -delta.y / viewport.height * uvView.height);
        }

        /// <summary>UV 편집 좌표를 화면 위치로 변환합니다.</summary>
        public static Vector2 UvToScreen(Vector2 uv, Rect viewport, Rect uvView)
        {
            return new Vector2(
                viewport.x + (uv.x - uvView.x) / uvView.width * viewport.width,
                viewport.yMax - (uv.y - uvView.y) / uvView.height * viewport.height);
        }

        /// <summary>UV 표시 범위에 대응하는 체크 텍스처 좌표를 구합니다.</summary>
        public static Rect TextureCoordinates(Rect uvView, Vector2 tiling, Vector2 offset)
        {
            return new Rect(Vector2.Scale(uvView.position, tiling) + offset, Vector2.Scale(uvView.size, tiling));
        }
    }
}
