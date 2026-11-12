using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    public sealed partial class SkinnedMeshCutRagdoll
    {
        #region 원본 리그의 물리 구성

        /// <summary>
        /// 준비한 물리를 원본 본에 연결하고 원본 강체 목록을 반환합니다. 기존 강체·관절 설정과
        /// 콜라이더 참조를 보존하며, 콜라이더가 없는 강체에만 준비한 형상을 추가합니다. Undo를 지원합니다.
        /// </summary>
        public Rigidbody[] ApplyToSource()
        {
            Dictionary<Rigidbody, Rigidbody> bodies = new Dictionary<Rigidbody, Rigidbody>();
            foreach (KeyValuePair<Transform, Rigidbody> pair in m_bodies)
            {
                Rigidbody body = pair.Key.GetComponent<Rigidbody>();
                if (body == null)
                {
                    body = Undo.AddComponent<Rigidbody>(pair.Key.gameObject);
                    EditorUtility.CopySerialized(pair.Value, body);
                }
                bodies.Add(pair.Value, body);
            }
            foreach (KeyValuePair<Rigidbody, Rigidbody> pair in bodies)
            {
                Rigidbody original = pair.Key;
                Rigidbody body = pair.Value;
                bool hasCollider = body.GetComponentsInChildren<Collider>(true)
                    .Any(collider => collider.GetComponentInParent<Rigidbody>(true) == body);
                if (!hasCollider)
                {
                    foreach (Collider collider in original.GetComponentsInChildren<Collider>(true))
                    {
                        if (collider.GetComponentInParent<Rigidbody>(true) != original)
                        {
                            continue;
                        }
                        Transform target = ColliderPath(collider.transform, original.transform, body.transform, true);
                        Collider copy = (Collider)Undo.AddComponent(target.gameObject, collider.GetType());
                        EditorUtility.CopySerialized(collider, copy);
                    }
                }
                foreach (Joint originalJoint in original.GetComponents<Joint>())
                {
                    Rigidbody connected = originalJoint.connectedBody;
                    if (connected != null && !bodies.ContainsKey(connected))
                    {
                        throw new InvalidOperationException("원본에 연결할 관절의 상대 본이 없습니다: " + body.name);
                    }
                    Joint joint = body.GetComponent<Joint>();
                    if (joint == null)
                    {
                        joint = (Joint)Undo.AddComponent(body.gameObject, originalJoint.GetType());
                        EditorUtility.CopySerialized(originalJoint, joint);
                        joint.connectedBody = connected != null ? bodies[connected] : null;
                    }
                    else if (joint.connectedBody == null && connected != null)
                    {
                        Undo.RecordObject(joint, "래그돌 관절 연결");
                        joint.connectedBody = bodies[connected];
                    }
                }
            }
            return bodies.Values.ToArray();
        }

        #endregion
    }
}
