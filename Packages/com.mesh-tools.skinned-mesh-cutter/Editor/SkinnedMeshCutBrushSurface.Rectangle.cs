using System.Collections.Generic;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshCutBrushSurface
    {
        private Vector3[] m_viewportVertices;
        private int m_projectedPoseVersion = -1;
        private Matrix4x4 m_projectionMatrix;
        private Matrix4x4 m_viewMatrix;

        /// <summary>화면 선택 사각형과 겹치는 표시 중인 삼각형을 수집합니다.</summary>
        public int[] GetRectangleTriangles(Rect viewportRect, Camera camera, bool visibleOnly)
        {
            return GetRectangleTriangles(viewportRect, camera, visibleOnly, false);
        }

        /// <summary>상자에 걸친 면을 선택하고 필요하면 원본의 연결된 파츠 전체로 확장합니다.</summary>
        public int[] GetRectangleTriangles(Rect viewportRect, Camera camera, bool visibleOnly, bool connected)
        {
            List<int> selected = new List<int>();
            if (camera == null || viewportRect.width <= 0f || viewportRect.height <= 0f)
            {
                return selected.ToArray();
            }

            RefreshProjectedVertices(camera);
            List<Vector3> polygon = new List<Vector3>(8);
            List<Vector3> clipped = new List<Vector3>(8);
            Vector3 cameraPosition = camera.transform.position;
            Vector3 cameraForward = camera.transform.forward;
            float near = camera.nearClipPlane;

            for (int triangle = 0; triangle < TriangleCount; triangle++)
            {
                if (HiddenTriangles[triangle])
                {
                    continue;
                }

                Vector3 a = m_viewportVertices[Triangles[triangle * 3]];
                Vector3 b = m_viewportVertices[Triangles[triangle * 3 + 1]];
                Vector3 c = m_viewportVertices[Triangles[triangle * 3 + 2]];
                polygon.Clear();

                if (a.z >= near && b.z >= near && c.z >= near)
                {
                    // 대부분의 면은 정점 투영 캐시와 화면 경계만으로 빠르게 제외할 수 있다.
                    if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < viewportRect.xMin
                        || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > viewportRect.xMax
                        || Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < viewportRect.yMin
                        || Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > viewportRect.yMax)
                    {
                        continue;
                    }

                    polygon.Add(a);
                    polygon.Add(b);
                    polygon.Add(c);
                }
                else
                {
                    if (a.z < near && b.z < near && c.z < near)
                    {
                        continue;
                    }

                    // 근평면을 가로지르면 투영 전에 월드 공간에서 잘라 원근 투영의 뒤집힘을 방지한다.
                    for (int i = 0; i < 3; i++)
                    {
                        polygon.Add(WorldVertices[Triangles[triangle * 3 + i]]);
                    }

                    Clip(polygon, clipped, cameraForward, -Vector3.Dot(cameraPosition, cameraForward) - near);
                    for (int i = 0; i < polygon.Count; i++)
                    {
                        polygon[i] = camera.WorldToViewportPoint(polygon[i]);
                    }
                }

                Clip(polygon, clipped, Vector3.right, -viewportRect.xMin);
                Clip(polygon, clipped, Vector3.left, viewportRect.xMax);
                Clip(polygon, clipped, Vector3.up, -viewportRect.yMin);
                Clip(polygon, clipped, Vector3.down, viewportRect.yMax);

                if (polygon.Count < 3)
                {
                    continue;
                }

                if (!visibleOnly)
                {
                    selected.Add(triangle);
                    continue;
                }

                Vector3 center = Vector3.zero;
                foreach (Vector3 point in polygon)
                {
                    center += point;
                }

                center /= polygon.Count;
                bool visible = RayHitsTriangle(camera, center, triangle);
                for (int i = 0; !visible && i < polygon.Count; i++)
                {
                    visible = RayHitsTriangle(camera, Vector3.Lerp(center, polygon[i], 0.95f), triangle);
                }

                if (visible)
                {
                    selected.Add(triangle);
                }
            }

            if (!connected || selected.Count == 0)
            {
                return selected.ToArray();
            }

            // 가시성은 상자 안의 시작 면에 적용하고, 그 면과 이어진 파츠는 상자 밖과 뒷면까지 포함한다.
            HashSet<int[]> groups = new HashSet<int[]>();
            foreach (int triangle in selected)
            {
                groups.Add(m_connected[triangle]);
            }

            selected.Clear();
            foreach (int[] group in groups)
            {
                foreach (int triangle in group)
                {
                    if (!HiddenTriangles[triangle])
                    {
                        selected.Add(triangle);
                    }
                }
            }

            selected.Sort();
            return selected.ToArray();
        }

        /// <summary>포즈나 카메라가 바뀔 때만 각 정점의 화면 위치를 갱신합니다.</summary>
        private void RefreshProjectedVertices(Camera camera)
        {
            Matrix4x4 projection = camera.projectionMatrix;
            Matrix4x4 view = camera.worldToCameraMatrix;
            if (m_projectedPoseVersion == PoseVersion && projection.Equals(m_projectionMatrix)
                && view.Equals(m_viewMatrix))
            {
                return;
            }

            if (m_viewportVertices == null || m_viewportVertices.Length != WorldVertices.Length)
            {
                m_viewportVertices = new Vector3[WorldVertices.Length];
            }

            for (int i = 0; i < WorldVertices.Length; i++)
            {
                m_viewportVertices[i] = camera.WorldToViewportPoint(WorldVertices[i]);
            }

            m_projectedPoseVersion = PoseVersion;
            m_projectionMatrix = projection;
            m_viewMatrix = view;
        }

        /// <summary>광선이 삼각형 내부를 통과하는지 판정합니다.</summary>
        private bool RayHitsTriangle(Camera camera, Vector3 viewport, int triangle)
        {
            return Raycast(camera.ViewportPointToRay(viewport), out int hit, out _, out _) && hit == triangle;
        }

        /// <summary>법선과 거리로 정의한 한 경계를 기준으로 다각형을 자릅니다.</summary>
        private static void Clip(List<Vector3> polygon, List<Vector3> scratch, Vector3 normal, float offset)
        {
            scratch.Clear();
            if (polygon.Count == 0)
            {
                return;
            }

            Vector3 previous = polygon[polygon.Count - 1];
            float previousDistance = Vector3.Dot(previous, normal) + offset;
            foreach (Vector3 current in polygon)
            {
                float currentDistance = Vector3.Dot(current, normal) + offset;
                if ((previousDistance >= 0f) != (currentDistance >= 0f))
                {
                    scratch.Add(Vector3.LerpUnclamped(previous, current,
                        previousDistance / (previousDistance - currentDistance)));
                }

                if (currentDistance >= 0f)
                {
                    scratch.Add(current);
                }

                previous = current;
                previousDistance = currentDistance;
            }

            polygon.Clear();
            polygon.AddRange(scratch);
        }
    }
}

