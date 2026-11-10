using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>Draws and picks collider shapes without modifying scene physics.</summary>
    public sealed class SkinnedMeshCutColliderGizmo
    {
        private const int Segments = 32;
        private readonly Vector3[] m_localEdges;
        private readonly float[] m_capOffsets;
        private readonly Vector3[] m_worldEdges;
        private readonly Vector3 m_center;
        private readonly Vector3 m_boxHalfSize;
        private readonly bool m_isBox;
        private readonly Vector3[] m_meshVertices;
        private readonly int[] m_meshTriangles;
        private readonly float m_radius, m_height;
        private readonly int m_shape, m_direction;
        private Matrix4x4 m_lastMatrix;
        private bool m_hasMatrix;

        public SkinnedMeshCutColliderGizmo(Collider collider)
        {
            var edges = new List<Vector3>();
            var offsets = new List<float>();
            void Edge(Vector3 a, Vector3 b, float capA = 0f, float capB = 0f)
            {
                edges.Add(a); edges.Add(b);
                offsets.Add(capA); offsets.Add(capB);
            }
            void Circle(Vector3 u, Vector3 v, float cap = 0f)
            {
                for (int i = 0; i < Segments; i++)
                {
                    float a = i * 2f * Mathf.PI / Segments, b = (i + 1) * 2f * Mathf.PI / Segments;
                    Edge(u * Mathf.Cos(a) + v * Mathf.Sin(a), u * Mathf.Cos(b) + v * Mathf.Sin(b), cap, cap);
                }
            }
            if (collider is BoxCollider box)
            {
                m_isBox = true; m_center = box.center; m_boxHalfSize = box.size * .5f;
                var corners = new Vector3[8];
                for (int i = 0; i < corners.Length; i++)
                    corners[i] = box.center + Vector3.Scale(box.size * .5f,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                for (int i = 0; i < corners.Length; i++)
                    for (int bit = 1; bit <= 4; bit <<= 1)
                        if ((i & bit) == 0) Edge(corners[i], corners[i | bit]);
            }
            else if (collider is SphereCollider sphere)
            {
                m_shape = 1; m_center = sphere.center; m_radius = sphere.radius;
                Circle(Vector3.right, Vector3.up);
                Circle(Vector3.up, Vector3.forward);
                Circle(Vector3.forward, Vector3.right);
            }
            else if (collider is CapsuleCollider capsule)
            {
                m_shape = 2; m_center = capsule.center; m_radius = capsule.radius;
                m_height = capsule.height; m_direction = capsule.direction;
                Circle(Vector3.right, Vector3.forward, 1f);
                Circle(Vector3.right, Vector3.forward, -1f);
                foreach (Vector3 radial in new[] { Vector3.right, Vector3.forward })
                {
                    Edge(radial, radial, -1f, 1f);
                    Edge(-radial, -radial, -1f, 1f);
                    for (int sign = -1; sign <= 1; sign += 2)
                        for (int i = 0; i < Segments / 2; i++)
                        {
                            float a = i * 2f * Mathf.PI / Segments, b = (i + 1) * 2f * Mathf.PI / Segments;
                            Edge(radial * Mathf.Cos(a) + Vector3.up * (sign * Mathf.Sin(a)),
                                radial * Mathf.Cos(b) + Vector3.up * (sign * Mathf.Sin(b)), sign, sign);
                        }
                }
            }
            else if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
            {
                Mesh mesh = meshCollider.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                m_meshVertices = vertices;
                var pickingTriangles = new List<int>();
                var seen = new HashSet<ulong>();
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    int[] triangles = mesh.GetTriangles(submesh);
                    pickingTriangles.AddRange(triangles);
                    for (int i = 0; i < triangles.Length; i += 3)
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int a = triangles[i + corner], b = triangles[i + (corner + 1) % 3];
                            ulong key = ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                            if (seen.Add(key)) Edge(vertices[a], vertices[b]);
                        }
                }
                m_meshTriangles = pickingTriangles.ToArray();
            }
            m_localEdges = edges.ToArray();
            m_capOffsets = offsets.ToArray();
            m_worldEdges = new Vector3[m_localEdges.Length];
        }

        public void Draw(Matrix4x4 matrix, Color color)
        {
            UpdateEdges(matrix);
            using (new Handles.DrawingScope(color, Matrix4x4.identity)) Handles.DrawLines(m_worldEdges);
        }

        public float DistanceToMouse(Matrix4x4 matrix)
        {
            if (Event.current == null) return float.PositiveInfinity;
            UpdateEdges(matrix);
            float distance = float.PositiveInfinity;
            using (new Handles.DrawingScope(Matrix4x4.identity))
                for (int i = 0; i < m_worldEdges.Length; i += 2)
                    distance = Mathf.Min(distance, HandleUtility.DistanceToLine(m_worldEdges[i], m_worldEdges[i + 1]));
            return distance;
        }

        // Distance is in world units. Primitives return zero from inside; meshes return the first
        // forward triangle surface (the exit for a closed mesh), with both triangle sides pickable.
        public bool Raycast(Matrix4x4 matrix, Ray worldRay, out float distance)
        {
            distance = float.PositiveInfinity;
            if (worldRay.direction.sqrMagnitude == 0f) return false;
            Vector3 direction = worldRay.direction.normalized;
            if (m_shape != 0)
            {
                RoundedShape(matrix, out Vector3 center, out Quaternion rotation, out float radius, out float halfSegment);
                Quaternion inverse = Quaternion.Inverse(rotation);
                Vector3 origin = inverse * (worldRay.origin - center);
                direction = inverse * direction;
                Vector3 nearest = new Vector3(0f, Mathf.Clamp(origin.y, -halfSegment, halfSegment), 0f);
                if ((origin - nearest).sqrMagnitude <= radius * radius) { distance = 0f; return true; }
                distance = Mathf.Min(SphereHit(origin - Vector3.up * halfSegment, direction, radius),
                    SphereHit(origin + Vector3.up * halfSegment, direction, radius));
                float a = direction.x * direction.x + direction.z * direction.z;
                float b = origin.x * direction.x + origin.z * direction.z;
                float c = origin.x * origin.x + origin.z * origin.z - radius * radius;
                float discriminant = b * b - a * c;
                if (halfSegment > 0f && a > 0f && discriminant >= 0f)
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        float hit = (-b + sign * Mathf.Sqrt(discriminant)) / a;
                        if (hit >= 0f && Mathf.Abs(origin.y + hit * direction.y) <= halfSegment)
                            distance = Mathf.Min(distance, hit);
                    }
                return !float.IsPositiveInfinity(distance);
            }
            if (matrix.determinant == 0f) return false;
            Matrix4x4 toLocal = matrix.inverse;
            Vector3 localOrigin = toLocal.MultiplyPoint3x4(worldRay.origin);
            // Do not normalize after inverse scaling: the ray parameter stays a world distance.
            Vector3 localDirection = toLocal.MultiplyVector(direction);
            if (m_isBox)
            {
                Vector3 origin = localOrigin - m_center;
                float near = 0f, far = float.PositiveInfinity;
                for (int axis = 0; axis < 3; axis++)
                {
                    float extent = Mathf.Abs(m_boxHalfSize[axis]);
                    if (localDirection[axis] == 0f)
                    {
                        if (Mathf.Abs(origin[axis]) > extent) return false;
                        continue;
                    }
                    float a = (-extent - origin[axis]) / localDirection[axis];
                    float b = (extent - origin[axis]) / localDirection[axis];
                    near = Mathf.Max(near, Mathf.Min(a, b));
                    far = Mathf.Min(far, Mathf.Max(a, b));
                    if (near > far) return false;
                }
                distance = near;
                return true;
            }
            if (m_meshTriangles == null) return false;
            for (int i = 0; i < m_meshTriangles.Length; i += 3)
            {
                Vector3 vertex = m_meshVertices[m_meshTriangles[i]];
                Vector3 edge1 = m_meshVertices[m_meshTriangles[i + 1]] - vertex;
                Vector3 edge2 = m_meshVertices[m_meshTriangles[i + 2]] - vertex;
                Vector3 p = Vector3.Cross(localDirection, edge2);
                float determinant = Vector3.Dot(edge1, p);
                if (Mathf.Abs(determinant) <= 1e-7f * Mathf.Sqrt(edge1.sqrMagnitude * p.sqrMagnitude)) continue;
                float inverse = 1f / determinant;
                Vector3 offset = localOrigin - vertex;
                float u = Vector3.Dot(offset, p) * inverse;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(offset, edge1);
                float v = Vector3.Dot(localDirection, q) * inverse;
                if (v < 0f || u + v > 1f) continue;
                float hit = Vector3.Dot(edge2, q) * inverse;
                if (hit >= 0f) distance = Mathf.Min(distance, hit);
            }
            return !float.IsPositiveInfinity(distance);
        }

        private static float SphereHit(Vector3 origin, Vector3 direction, float radius)
        {
            float b = Vector3.Dot(origin, direction);
            float discriminant = b * b - origin.sqrMagnitude + radius * radius;
            if (discriminant < 0f) return float.PositiveInfinity;
            float hit = -b - Mathf.Sqrt(discriminant);
            return hit >= 0f ? hit : float.PositiveInfinity;
        }

        private void UpdateEdges(Matrix4x4 matrix)
        {
            if (m_hasMatrix && m_lastMatrix.Equals(matrix)) return;
            m_hasMatrix = true;
            m_lastMatrix = matrix;
            if (m_shape == 0)
            {
                for (int i = 0; i < m_localEdges.Length; i++) m_worldEdges[i] = matrix.MultiplyPoint3x4(m_localEdges[i]);
                return;
            }
            RoundedShape(matrix, out Vector3 center, out Quaternion rotation, out float radius, out float halfSegment);
            // PhysX spheres and capsule caps stay round under nonuniform scale.
            for (int i = 0; i < m_localEdges.Length; i++)
                m_worldEdges[i] = center + rotation * (m_localEdges[i] * radius + Vector3.up * (m_capOffsets[i] * halfSegment));
        }

        private void RoundedShape(Matrix4x4 matrix, out Vector3 center, out Quaternion rotation,
            out float radius, out float halfSegment)
        {
            Vector3 scale = new Vector3(matrix.MultiplyVector(Vector3.right).magnitude,
                matrix.MultiplyVector(Vector3.up).magnitude, matrix.MultiplyVector(Vector3.forward).magnitude);
            center = matrix.MultiplyPoint3x4(m_center);
            radius = m_radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            halfSegment = 0f;
            rotation = matrix.rotation;
            if (m_shape == 2)
            {
                Vector3 localAxis = m_direction == 0 ? Vector3.right : m_direction == 1 ? Vector3.up : Vector3.forward;
                Vector3 axis = matrix.MultiplyVector(localAxis);
                rotation = axis.sqrMagnitude > 0f ? Quaternion.FromToRotation(Vector3.up, axis.normalized) : Quaternion.identity;
                radius = m_radius * Mathf.Max(scale[(m_direction + 1) % 3], scale[(m_direction + 2) % 3]);
                halfSegment = Mathf.Max(0f, m_height * scale[m_direction] * .5f - radius);
            }
        }
    }
}
