using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    // 볼록 껍질은 최대 정점 64개와 면 124개로, PhysX의 볼록 메시 제한인 255면을 넘지 않는다.
    internal static class SkinnedMeshCutColliderGeometry
    {
        /// <summary>레퍼런스 형상을 보존하고 절단면을 가로지르는 구·캡슐만 볼록 메시로 잘라 복사합니다.</summary>
        internal static Collider CopyClipped(Collider source, Transform target, Plane[] planes, List<Mesh> meshes)
        {
            bool curved = source is SphereCollider || source is CapsuleCollider;
            if (!curved)
            {
                if (IsOutsideRetainedSide(source, target, planes))
                {
                    return null;
                }

                Collider copy = CopyReferenceCollider(source, target);
                if (copy is BoxCollider box)
                {
                    ClipAxisAlignedBox(box, target, planes);
                }

                return copy;
            }

            foreach (Plane plane in planes)
            {
                if (plane.GetDistanceToPoint(CurvedSupport(source, target, plane.normal)) < -0.00001f)
                {
                    return null;
                }
            }

            bool unchanged = planes.All(plane =>
                plane.GetDistanceToPoint(CurvedSupport(source, target, -plane.normal)) >= -0.00001f);
            if (unchanged)
            {
                return CopyReferenceCollider(source, target);
            }

            Vector3[] local = Points(source, target);
            Vector3[] world = local.Select(target.TransformPoint).ToArray();
            Mesh hull = Hull(world.Concat(planes.SelectMany(plane => new[]
            {
                CurvedSupport(source, target, plane.normal),
                CurvedSupport(source, target, -plane.normal)
            })));

            if (hull == null)
            {
                return null;
            }

            try
            {
                foreach (Plane plane in planes)
                {
                    Vector3[] vertices = hull.vertices;

                    if (vertices.All(point => plane.GetDistanceToPoint(point) >= 0f))
                    {
                        continue;
                    }

                    int[] triangles = hull.triangles;
                    var points = new List<Vector3>();

                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            Vector3 a = vertices[triangles[i + j]];
                            Vector3 b = vertices[triangles[i + (j + 1) % 3]];
                            float da = plane.GetDistanceToPoint(a);
                            float db = plane.GetDistanceToPoint(b);

                            if (da >= 0f)
                            {
                                points.Add(a);
                            }

                            if ((da >= 0f) != (db >= 0f))
                            {
                                points.Add(Vector3.LerpUnclamped(a, b, da / (da - db)));
                            }
                        }
                    }

                    // 다음 평면이 새 단면도 자를 수 있도록 중간 절단마다 열린 면을 닫는다.
                    UnityEngine.Object.DestroyImmediate(hull);
                    hull = Hull(points);

                    if (hull == null)
                    {
                        return null;
                    }
                }

                Mesh clippedHull = Hull(hull.vertices.Select(target.InverseTransformPoint));

                if (clippedHull == null)
                {
                    return null;
                }

                clippedHull.name = source.name + "_CutCollider";
                meshes.Add(clippedHull);
                MeshCollider collider = target.gameObject.AddComponent<MeshCollider>();
                collider.convex = true;
                collider.sharedMesh = clippedHull;
                collider.sharedMaterial = source.sharedMaterial;
                collider.isTrigger = source.isTrigger;
                collider.enabled = source.enabled;
                collider.contactOffset = source.contactOffset;
                collider.includeLayers = source.includeLayers;
                collider.excludeLayers = source.excludeLayers;
                collider.layerOverridePriority = source.layerOverridePriority;
                collider.providesContacts = source.providesContacts;

                return collider;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hull);
            }
        }

        /// <summary>래그돌 레퍼런스의 콜라이더 종류와 모든 직렬화 설정을 그대로 복사합니다.</summary>
        private static Collider CopyReferenceCollider(Collider source, Transform target)
        {
            Collider copy = (Collider)target.gameObject.AddComponent(source.GetType());
            EditorUtility.CopySerialized(source, copy);
            return copy;
        }

        /// <summary>레퍼런스 박스·메시 콜라이더 전체가 버리는 쪽에 있는 경우만 제외합니다.</summary>
        private static bool IsOutsideRetainedSide(Collider source, Transform target, Plane[] planes)
        {
            Bounds bounds;
            if (source is BoxCollider box)
            {
                bounds = new Bounds(box.center, box.size);
            }
            else if (source is MeshCollider mesh && mesh.sharedMesh != null)
            {
                // bounds는 Read/Write가 꺼진 원본 메시에서도 읽을 수 있으므로 원본 형상 복사에 정점 추출이 필요 없다.
                bounds = mesh.sharedMesh.bounds;
            }
            else
            {
                return false;
            }

            foreach (Plane plane in planes)
            {
                Vector3 normal = target.localToWorldMatrix.transpose.MultiplyVector(plane.normal);
                Vector3 extents = bounds.extents;
                float radius = Mathf.Abs(normal.x) * extents.x
                    + Mathf.Abs(normal.y) * extents.y + Mathf.Abs(normal.z) * extents.z;
                if (plane.GetDistanceToPoint(target.TransformPoint(bounds.center)) + radius < -0.00001f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>박스는 종류를 유지하고 로컬 축에 맞는 절단일 때만 중심과 크기를 줄입니다.</summary>
        private static void ClipAxisAlignedBox(BoxCollider box, Transform target, Plane[] planes)
        {
            Vector3 minimum = box.center - box.size * 0.5f;
            Vector3 maximum = box.center + box.size * 0.5f;
            bool changed = false;
            foreach (Plane plane in planes)
            {
                Vector3 normal = target.localToWorldMatrix.transpose.MultiplyVector(plane.normal);
                if (!AxisAligned(normal))
                {
                    continue;
                }

                int axis = Mathf.Abs(normal.x) >= Mathf.Abs(normal.y)
                    ? (Mathf.Abs(normal.x) >= Mathf.Abs(normal.z) ? 0 : 2)
                    : (Mathf.Abs(normal.y) >= Mathf.Abs(normal.z) ? 1 : 2);
                float boundary = -plane.GetDistanceToPoint(target.position) / normal[axis];
                if (normal[axis] > 0f && boundary > minimum[axis])
                {
                    minimum[axis] = boundary;
                    changed = true;
                }
                else if (normal[axis] < 0f && boundary < maximum[axis])
                {
                    maximum[axis] = boundary;
                    changed = true;
                }
            }

            if (changed)
            {
                box.center = (minimum + maximum) * 0.5f;
                box.size = Vector3.Max(maximum - minimum, Vector3.one * 0.00001f);
            }
        }

        /// <summary>콜라이더의 로컬 축과 절단 평면이 정렬되어 있는지 확인합니다.</summary>
        private static bool AxisAligned(Vector3 normal)
        {
            normal.Normalize();

            return Mathf.Max(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z)) > .99999f;
        }

        /// <summary>곡면 콜라이더를 클리핑 가능한 볼록 점 집합으로 변환합니다.</summary>
        private static void CurvedShape(
            Collider collider,
            Transform target,
            out Vector3 center,
            out Vector3 axis,
            out float radius,
            out float halfLine)
        {
            Vector3 scale = target.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            halfLine = 0f;
            axis = target.up;

            if (collider is SphereCollider sphere)
            {
                center = target.TransformPoint(sphere.center);
                radius = sphere.radius * Mathf.Max(scale.x, scale.y, scale.z);
            }
            else
            {
                CapsuleCollider capsule = (CapsuleCollider)collider;
                Vector3 localAxis = Vector3.zero;
                localAxis[capsule.direction] = 1f;
                axis = target.rotation * localAxis;
                center = target.TransformPoint(capsule.center);
                radius = capsule.radius * Mathf.Max(scale[(capsule.direction + 1) % 3], scale[(capsule.direction + 2) % 3]);
                halfLine = Mathf.Max(0f, capsule.height * scale[capsule.direction] * .5f - radius);
            }
        }

        /// <summary>구 또는 캡슐에서 주어진 방향의 지지점을 구합니다.</summary>
        private static Vector3 CurvedSupport(Collider collider, Transform target, Vector3 direction)
        {
            CurvedShape(
                collider,
                target,
                out Vector3 center,
                out Vector3 axis,
                out float radius,
                out float halfLine);

            return center + direction.normalized * radius + axis * (Vector3.Dot(direction, axis) >= 0f ? halfLine : -halfLine);
        }

        /// <summary>콜라이더 형상을 구성하는 표본 점을 수집합니다.</summary>
        private static Vector3[] Points(Collider collider, Transform target)
        {
            CurvedShape(
                collider,
                target,
                out Vector3 center,
                out Vector3 axis,
                out float radius,
                out float halfLine);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, axis);
            var points = new List<Vector3>();

            for (int ring = 0; ring <= 6; ring++)
            {
                for (int segment = 0; segment < 12; segment++)
                {
                    float latitude = Mathf.PI * ring / 6f;
                    float longitude = Mathf.PI * 2f * segment / 12f;
                    Vector3 point = new Vector3(
                        Mathf.Sin(latitude) * Mathf.Cos(longitude),
                        Mathf.Cos(latitude),
                        Mathf.Sin(latitude) * Mathf.Sin(longitude)) * radius;
                    point.y += (ring <= 3 ? 1f : -1f) * halfLine;
                    points.Add(target.InverseTransformPoint(center + rotation * point));

                    // 직선 원통 구간을 유지하려면 캡슐 양쪽 반구의 적도 정점이 모두 필요하다.
                    if (ring == 3 && halfLine > 0f)
                    {
                        point.y -= halfLine * 2f;
                        points.Add(target.InverseTransformPoint(center + rotation * point));
                    }
                }
            }

            return points.ToArray();
        }

        private readonly struct HullPoint
        {
            private readonly decimal x, y, z;
            /// <summary>볼록 껍질 계산용 좌표를 저장합니다.</summary>
            internal HullPoint(decimal x, decimal y, decimal z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            internal decimal SquaredLength => x * x + y * y + z * z;

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>
            public static HullPoint operator +(HullPoint a, HullPoint b)
            {
                return new HullPoint(a.x + b.x, a.y + b.y, a.z + b.z);
            }

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>
            public static HullPoint operator -(HullPoint a, HullPoint b)
            {
                return new HullPoint(a.x - b.x, a.y - b.y, a.z - b.z);
            }

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>

            /// <summary>기하 계산에 사용할 좌표 연산 결과를 반환합니다.</summary>
            public static HullPoint operator *(HullPoint a, decimal scale)
            {
                return new HullPoint(a.x * scale, a.y * scale, a.z * scale);
            }

            /// <summary>두 벡터의 외적을 계산합니다.</summary>
            internal static HullPoint Cross(HullPoint a, HullPoint b)
            {
                return new HullPoint(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
            }

            /// <summary>두 벡터의 내적을 계산합니다.</summary>
            internal static decimal Dot(HullPoint a, HullPoint b)
            {
                return a.x * b.x + a.y * b.y + a.z * b.z;
            }
        }

        /// <summary>점 집합의 볼록 껍질을 콜라이더 메시로 구성합니다.</summary>
        internal static Mesh Hull(IEnumerable<Vector3> input)
        {
            Vector3[] inputPoints = input.Distinct().ToArray();

            if (inputPoints.Length < 4)
            {
                return null;
            }

            Bounds inputBounds = new Bounds(inputPoints[0], Vector3.zero);

            foreach (Vector3 point in inputPoints)
            {
                inputBounds.Encapsulate(point);
            }

            float scale = Mathf.Max(inputBounds.size.x, inputBounds.size.y, inputBounds.size.z);

            if (scale < .000001f)
            {
                return null;
            }

            Vector3 origin = inputBounds.center;
            // 가져온 본의 좌표 때문에 껍질 경계가 불안정해지지 않도록 원점 부근의 단위 스케일에서 계산한다.
            var points = inputPoints.Select(point => (point - origin) / scale).Select(point => Vector3Int.RoundToInt(point * 1000000f))
                .Distinct()
                .Select(point => (Vector3)point)
                .ToList();

            if (points.Count < 4)
            {
                return null;
            }

            if (points.Count > 64)
            {
                var reduced = new List<Vector3>
                {
                    points[0]
                };

                while (reduced.Count < 64)
                {
                    Vector3 farthest = points.OrderByDescending(point => reduced.Min(other => (point - other).sqrMagnitude))
                        .First();
                    reduced.Add(farthest);
                    points.Remove(farthest);
                }

                points = reduced;
            }

            HullPoint[] precise = points.Select(point => new HullPoint((decimal)point.x, (decimal)point.y, (decimal)point.z))
                .ToArray();
            int a = 0;
            int b = Enumerable.Range(1, points.Count - 1).OrderByDescending(i => (precise[i] - precise[a]).SquaredLength)
                .First();
            int c = Enumerable.Range(0, points.Count).OrderByDescending(i => HullPoint.Cross(precise[b] - precise[a], precise[i] - precise[a]).SquaredLength)
                .First();
            HullPoint normal = HullPoint.Cross(precise[b] - precise[a], precise[c] - precise[a]);
            int d = Enumerable.Range(0, points.Count).OrderByDescending(i => Math.Abs(HullPoint.Dot(normal, precise[i] - precise[a])))
                .First();

            if (normal.SquaredLength == 0 || HullPoint.Dot(normal, precise[d] - precise[a]) == 0)
            {
                return null;
            }

            HullPoint inside = (precise[a] + precise[b] + precise[c] + precise[d]) * .25m;
            Func<int[], HullPoint> faceNormal = f => HullPoint.Cross(precise[f[1]] - precise[f[0]], precise[f[2]] - precise[f[0]]);
            Func<int, int, int, int[]> face = (x, y, z) => HullPoint.Dot(HullPoint.Cross(precise[y] - precise[x], precise[z] - precise[x]), inside - precise[x]) > 0 ? new[]
            {
                x,
                z,
                y
            }

            : new[]
            {
                x,
                y,
                z
            };
            var faces = new List<int[]>
            {
                face(a, b, c),
                face(a, d, b),
                face(a, c, d),
                face(b, d, c)
            };
            // 양자화된 그리드에서 정확하게 판정하여 거의 같은 평면에 놓인 단면 경계의 일관성을 유지한다.
            // 부동소수점 허용 오차를 쓰면 인접한 면의 판정이 달라져 껍질이 열릴 수 있다.
            for (int i = 0; i < precise.Length; i++)
            {
                int[][] visible = faces.Where(f =>
                {
                    HullPoint n = faceNormal(f);

                    return HullPoint.Dot(n, precise[i] - precise[f[0]]) > 0;
                }).ToArray();
                var edges = new List<Vector2Int>();

                foreach (int[] f in visible)
                {
                    faces.Remove(f);

                    for (int j = 0; j < 3; j++)
                    {
                        var edge = new Vector2Int(f[j], f[(j + 1) % 3]);

                        if (!edges.Remove(new Vector2Int(edge.y, edge.x)))
                        {
                            edges.Add(edge);
                        }
                    }
                }

                foreach (Vector2Int edge in edges)
                {
                    faces.Add(face(edge.x, edge.y, i));
                }
            }

            var edgeCounts = new Dictionary<Vector2Int, int>();

            foreach (int[] f in faces)
            {
                for (int i = 0; i < 3; i++)
                {
                    int x = f[i];
                    int y = f[(i + 1) % 3];
                    var edge = new Vector2Int(Math.Min(x, y), Math.Max(x, y));
                    edgeCounts.TryGetValue(edge, out int count);
                    edgeCounts[edge] = count + 1;
                }
            }

            if (faces.Count > 255 || edgeCounts.Values.Any(count => count != 2))
            {
                throw new InvalidOperationException("닫힌 Convex 콜라이더를 만들지 못했습니다. 콜라이더 또는 절단 위치를 조정하세요.");
            }

            int[] used = faces.SelectMany(f => f).Distinct().ToArray();
            Mesh result = new Mesh();
            result.vertices = used.Select(i => points[i] * (scale / 1000000f) + origin).ToArray();
            result.triangles = faces.SelectMany(f => f.Select(i => Array.IndexOf(used, i))).ToArray();
            result.RecalculateNormals();
            result.RecalculateBounds();

            return result;
        }
    }
}

