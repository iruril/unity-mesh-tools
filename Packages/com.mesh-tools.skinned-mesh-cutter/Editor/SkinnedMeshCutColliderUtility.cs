using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>원본 콜라이더를 보존해 복사하고 고정 부속물의 길이를 반영합니다.</summary>
    public static class SkinnedMeshCutColliderUtility
    {
        #region 콜라이더 복사와 부속물 반영

        /// <summary>같은 로컬 좌표계를 사용하는 대상의 콜라이더를 원본 구성으로 교체합니다.</summary>
        public static void CopyFrom(Transform source, Transform target)
        {
            if (source == null || target == null || source == target)
            {
                throw new ArgumentException("서로 다른 원본과 대상 Transform이 필요합니다.");
            }

            foreach (Collider collider in target.GetComponents<Collider>())
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
            foreach (Collider collider in source.GetComponents<Collider>())
            {
                Copy(collider, target);
            }
        }

        /// <summary>콜라이더 종류와 직렬화 설정을 같은 로컬 좌표계의 대상에 새 컴포넌트로 복사합니다.</summary>
        public static Collider Copy(Collider source, Transform target)
        {
            if (source == null || target == null)
            {
                throw new ArgumentException("원본 콜라이더와 대상 Transform이 필요합니다.");
            }
            Collider copy = (Collider)target.gameObject.AddComponent(source.GetType());
            EditorUtility.CopySerialized(source, copy);
            return copy;
        }

        /// <summary>
        /// 기준 Transform의 로컬 좌표로 부속물 범위를 구해 박스 또는 캡슐에 반영합니다.
        /// 캡슐은 반지름을 유지하고 길이와 중심만 확장하며, 기존 범위를 줄이지 않습니다.
        /// </summary>
        public static void IncludeAttachment(
            Collider collider,
            Transform reference,
            IEnumerable<Renderer> renderers)
        {
            if (reference == null)
            {
                throw new ArgumentNullException(nameof(reference));
            }
            IncludeAttachment(collider, reference.worldToLocalMatrix, renderers);
        }

        /// <summary>렌더러 월드 좌표를 콜라이더 로컬 좌표로 변환하는 행렬로 부속물 범위를 반영합니다.</summary>
        public static void IncludeAttachment(
            Collider collider,
            Matrix4x4 worldToReference,
            IEnumerable<Renderer> renderers)
        {
            if (collider == null || renderers == null)
            {
                throw new ArgumentNullException("콜라이더와 부속물 렌더러가 필요합니다.");
            }
            CapsuleCollider capsule = collider as CapsuleCollider;
            BoxCollider box = collider as BoxCollider;
            if (capsule == null && box == null)
            {
                throw new ArgumentException("부속물 길이 반영은 CapsuleCollider와 BoxCollider를 지원합니다.");
            }

            Bounds bounds = box != null
                ? new Bounds(box.center, box.size)
                : new Bounds(capsule.center, Vector3.one * capsule.height);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                {
                    continue;
                }
                Bounds local = renderer.localBounds;
                Matrix4x4 toReference = worldToReference * renderer.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = local.center + Vector3.Scale(local.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));
                    bounds.Encapsulate(toReference.MultiplyPoint3x4(point));
                }
            }

            if (box != null)
            {
                box.center = bounds.center;
                box.size = bounds.size;
                return;
            }
            int axis = capsule.direction;
            Vector3 center = capsule.center;
            center[axis] = bounds.center[axis];
            capsule.center = center;
            capsule.height = Mathf.Max(capsule.height, bounds.size[axis]);
        }

        #endregion
    }
}
