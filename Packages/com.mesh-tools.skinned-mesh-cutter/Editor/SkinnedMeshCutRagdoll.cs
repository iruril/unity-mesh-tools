using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MeshTools.Dismemberment.Editor
{
    public enum SkinnedMeshCutRagdollMode
    {
        None,
        Existing,
        Generate
    }

    /// <summary>절단 출력에 사용할 임시 물리 리그와 원본 본 매핑을 관리합니다.</summary>
    public sealed class SkinnedMeshCutRagdoll : IDisposable
    {
        private static readonly HumanBodyBones[] RequiredBones =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Head,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightFoot
        };
        private Scene m_scene;
        private readonly Dictionary<Transform, Rigidbody> m_bodies = new Dictionary<Transform, Rigidbody>();
        public GameObject Root { get; private set; }

        #region 리그 검증과 준비

        /// <summary>선택한 래그돌 모드에 필요한 리그와 질량을 검증합니다.</summary>
        public static string Validate(
            SkinnedMeshRenderer source,
            SkinnedMeshCutRagdollMode mode,
            GameObject existing,
            float mass)
        {
            if (mode == SkinnedMeshCutRagdollMode.None)
            {
                return null;
            }

            if (source == null)
            {
                return "래그돌에 사용할 소스 스킨을 지정하세요.";
            }

            if (mode == SkinnedMeshCutRagdollMode.Existing)
            {
                Transform root = existing != null ? existing.transform : PhysicsRigRoot(source);

                return root.GetComponentsInChildren<Rigidbody>(true).Length == 0
                    ? "복사할 래그돌에 Rigidbody가 없습니다."
                    : null;
            }

            Animator animator = source.GetComponentInParent<Animator>(true);

            if (animator == null || animator.avatar == null || !animator.avatar.isValid
                || !animator.isHuman)
            {
                return "자동 래그돌 생성에는 유효한 Humanoid Animator가 필요합니다. Generic 리그는 기존 래그돌을 지정하세요.";
            }

            if (float.IsNaN(mass) || float.IsInfinity(mass) || mass <= 0f)
            {
                return "래그돌 전체 질량은 0보다 큰 유한한 값이어야 합니다.";
            }

            foreach (HumanBodyBones bone in RequiredBones)
            {
                if (animator.GetBoneTransform(bone) == null)
                {
                    return "Humanoid 본 연결이 없습니다: " + bone;
                }
            }

            if (animator.GetBoneTransform(HumanBodyBones.Chest) == null
                && animator.GetBoneTransform(HumanBodyBones.Spine) == null)
            {
                return "Humanoid Chest 또는 Spine 본이 필요합니다.";
            }

            return null;
        }

        /// <summary>기존 물리 리그를 매핑하거나 작성 포즈에서 임시 래그돌을 생성합니다.</summary>
        public static SkinnedMeshCutRagdoll Prepare(
            SkinnedMeshRenderer source,
            SkinnedMeshRenderer author,
            Func<Transform, Transform> authorBone,
            SkinnedMeshCutRagdollMode mode,
            GameObject existing,
            float mass,
            bool tips)
        {
            if (mode == SkinnedMeshCutRagdollMode.None)
            {
                return null;
            }

            string error = Validate(source, mode, existing, mass);

            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            SkinnedMeshCutRagdoll result = new SkinnedMeshCutRagdoll();

            try
            {
                Transform rig = PhysicsRigRoot(source);
                Transform physicsRoot = existing != null ? existing.transform : rig;

                if (mode == SkinnedMeshCutRagdollMode.Generate)
                {
                    physicsRoot = result.GeneratePhysicsRig(source, rig, authorBone, mass, tips);
                }

                result.RegisterSourceBodies(source, rig, physicsRoot, authorBone);

                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>스킨과 Humanoid 본을 복제한 작성 포즈에서 선택된 래그돌 생성기를 실행합니다.</summary>
        private Transform GeneratePhysicsRig(
            SkinnedMeshRenderer source,
            Transform rig,
            Func<Transform, Transform> authorBone,
            float mass,
            bool tips)
        {
            m_scene = EditorSceneManager.NewPreviewScene();
            Dictionary<Transform, Transform> map = new Dictionary<Transform, Transform>();
            Animator originalAnimator = source.GetComponentInParent<Animator>(true);
            // 무기 리그까지 복제하면 손발 길이 추정에 영향을 주므로 스킨과 아바타 경로만 포함한다.
            HashSet<Transform> wanted = new HashSet<Transform>(source.bones.Where(bone => bone != null));

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform bone = originalAnimator.GetBoneTransform((HumanBodyBones)i);

                if (bone != null)
                {
                    wanted.Add(bone);
                }
            }

            wanted.Add(source.transform);

            foreach (Transform bone in wanted)
            {
                CloneAuthoringPath(bone, rig, authorBone, map);
            }

            Root = map[rig].gameObject;
            Animator animator = Root.AddComponent<Animator>();
            animator.avatar = originalAnimator.avatar;
            animator.applyRootMotion = false;
            SkinnedMeshRenderer renderer = map[source.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = source.sharedMesh;
            renderer.sharedMaterials = source.sharedMaterials;
            renderer.bones = source.bones.Select(bone => bone != null ? map[bone] : null).ToArray();
            renderer.rootBone = source.rootBone != null
                ? CloneAuthoringPath(source.rootBone, rig, authorBone, map)
                : map[rig];
            SkinnedMeshCutDefaults defaults = SkinnedMeshCutDefaults.instance;
            Quaternion authorRotation = Root.transform.rotation;
            // 자동 생성기의 전방 기준을 고정한 뒤 작성 포즈의 월드 회전을 복원한다.
            Root.transform.rotation = Quaternion.identity;
            SkinnedMeshCutRagdollGenerator.Generate(renderer, animator, mass, tips);
            Root.transform.rotation = authorRotation;

            foreach (Rigidbody body in Root.GetComponentsInChildren<Rigidbody>(true))
            {
                defaults.ApplyPhysics(body);
                body.isKinematic = false;
                body.useGravity = true;
            }

            // 준비한 프리팹을 직접 사용해도 애니메이션이 물리 자세를 덮어쓰지 않도록 한다.
            animator.enabled = false;

            return Root.transform;
        }

        /// <summary>작성 포즈의 월드 루트와 로컬 본 변환을 미리보기 씬에 복제합니다.</summary>
        private Transform CloneAuthoringPath(
            Transform original,
            Transform rig,
            Func<Transform, Transform> authorBone,
            Dictionary<Transform, Transform> map)
        {
            if (map.TryGetValue(original, out Transform found))
            {
                return found;
            }

            Transform parent = original != rig ? CloneAuthoringPath(original.parent, rig, authorBone, map) : null;
            Transform node = new GameObject(original.name).transform;
            SceneManager.MoveGameObjectToScene(node.gameObject, m_scene);
            node.SetParent(parent, false);
            Transform posed = authorBone(original);
            node.localPosition = original == rig ? posed.position : posed.localPosition;
            node.localRotation = original == rig ? posed.rotation : posed.localRotation;
            node.localScale = original == rig ? posed.lossyScale : posed.localScale;
            node.gameObject.layer = original.gameObject.layer;
            map.Add(original, node);

            return node;
        }

        /// <summary>소스 스켈레톤 경로와 일치하는 물리 본을 작성 포즈의 본에 연결합니다.</summary>
        private void RegisterSourceBodies(
            SkinnedMeshRenderer source,
            Transform rig,
            Transform physicsRoot,
            Func<Transform, Transform> authorBone)
        {
            HashSet<Transform> skeleton = new HashSet<Transform>();

            foreach (Transform bone in source.bones.Where(bone => bone != null))
            {
                for (Transform current = bone; current != null; current = current.parent)
                {
                    skeleton.Add(current);

                    if (current == rig)
                    {
                        break;
                    }
                }
            }

            foreach (Rigidbody body in physicsRoot.GetComponentsInChildren<Rigidbody>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(body.transform, physicsRoot);
                Transform original = string.IsNullOrEmpty(path) ? rig : rig.Find(path);

                if (original != null && skeleton.Contains(original))
                {
                    m_bodies.Add(authorBone(original), body);
                }
            }

            if (m_bodies.Count == 0)
            {
                throw new InvalidOperationException("소스 스켈레톤과 일치하는 래그돌 본이 없습니다. 같은 리그의 루트 오브젝트를 지정하세요.");
            }
        }

        #endregion

        #region 절단 출력에 물리 복사

        /// <summary>출력 표면에 필요한 강체·잘린 콜라이더·연결 관절을 복사합니다.</summary>
        public void CopyTo(
            GameObject output,
            Dictionary<Transform, Transform> map,
            Func<Transform, Transform> clone,
            Plane cutPlane,
            List<Mesh> meshes)
        {
            SkinnedMeshRenderer skin = output.GetComponent<SkinnedMeshRenderer>();
            Transform[] owners = FindPhysicsOwners(skin, map);
            Dictionary<Transform, List<Vector3>> points = CollectOwnedSurfacePoints(skin, owners);
            Dictionary<Rigidbody, Rigidbody> bodies = new Dictionary<Rigidbody, Rigidbody>();

            foreach (KeyValuePair<Transform, List<Vector3>> pair in points)
            {
                Rigidbody original = m_bodies[pair.Key];
                Rigidbody copy = CopyBody(original, clone(pair.Key), pair.Value, cutPlane, meshes);

                if (copy != null)
                {
                    bodies.Add(original, copy);
                }
            }

            if (bodies.Count == 0)
            {
                throw new InvalidOperationException("절단 결과에 대응하는 래그돌 본이 없습니다.");
            }

            CopyJoints(bodies);
        }

        /// <summary>출력 스킨의 각 본에 대응하는 가장 가까운 상위 물리 본을 찾습니다.</summary>
        private Transform[] FindPhysicsOwners(SkinnedMeshRenderer skin, Dictionary<Transform, Transform> map)
        {
            Dictionary<Transform, Transform> originals = map.ToDictionary(pair => pair.Value, pair => pair.Key);
            Transform[] owners = new Transform[skin.bones.Length];

            for (int i = 0; i < owners.Length; i++)
            {
                Transform current = originals[skin.bones[i]];

                while (current != null && !m_bodies.ContainsKey(current))
                {
                    current = current.parent;
                }

                owners[i] = current;
            }

            return owners;
        }

        /// <summary>현재 출력 자세의 월드 표면 점을 영향을 주는 물리 본별로 수집합니다.</summary>
        private static Dictionary<Transform, List<Vector3>> CollectOwnedSurfacePoints(SkinnedMeshRenderer skin, Transform[] owners)
        {
            Dictionary<Transform, List<Vector3>> points = new Dictionary<Transform, List<Vector3>>();
            Mesh baked = new Mesh();

            try
            {
                skin.BakeMesh(baked, true);
                Vector3[] vertices = baked.vertices;

                // Unity가 소유한 읽기 전용 메시 버퍼이므로 호출자가 Dispose하지 않는다.
                var counts = skin.sharedMesh.GetBonesPerVertex();
                var weights = skin.sharedMesh.GetAllBoneWeights();
                int weightIndex = 0;

                for (int i = 0; i < vertices.Length; i++)
                {
                    for (int j = 0; j < counts[i]; j++)
                    {
                        BoneWeight1 weight = weights[weightIndex++];
                        Transform owner = owners[weight.boneIndex];

                        if (owner == null || weight.weight <= 0.00001f)
                        {
                            continue;
                        }

                        if (!points.TryGetValue(owner, out List<Vector3> list))
                        {
                            list = new List<Vector3>();
                            points.Add(owner, list);
                        }

                        list.Add(skin.transform.TransformPoint(vertices[i]));
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baked);
            }

            return points;
        }

        /// <summary>남은 표면 범위로 콜라이더를 잘라 복사하고 유효한 강체를 반환합니다.</summary>
        private static Rigidbody CopyBody(
            Rigidbody original,
            Transform bone,
            List<Vector3> points,
            Plane cutPlane,
            List<Mesh> meshes)
        {
            Plane[] planes = BuildColliderClipPlanes(points, cutPlane);
            // 저장된 프리팹에서는 attachedRigidbody 대신 부모 계층으로 콜라이더 소유자를 확인한다.
            Collider[] sourceColliders = original.GetComponentsInChildren<Collider>(true).Where(collider => collider.GetComponentInParent<Rigidbody>(true) == original)
                .ToArray();
            int copiedColliders = 0;

            foreach (Collider collider in sourceColliders)
            {
                Transform target = ColliderPath(collider.transform, original.transform, bone);
                target.gameObject.layer = collider.gameObject.layer;

                if (SkinnedMeshCutColliderGeometry.CopyClipped(collider, target, planes, meshes) != null)
                {
                    copiedColliders++;
                }
            }

            if (sourceColliders.Length != 0 && copiedColliders == 0)
            {
                return null;
            }

            Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
            EditorUtility.CopySerialized(original, body);
            bone.gameObject.layer = original.gameObject.layer;

            return body;
        }

        /// <summary>남긴 표면의 방향에 따라 실제 절단 평면만 콜라이더 클리핑에 사용합니다.</summary>
        private static Plane[] BuildColliderClipPlanes(List<Vector3> points, Plane cutPlane)
        {
            // 본별 표면의 AABB로 다시 맞추면 멀쩡한 관절 콜라이더도 잘린 것으로 오인한다.
            // 실제 절단 평면만 적용하고, 마스크로 평면 양쪽을 보존한 부위는 원본을 유지한다.
            List<Plane> planes = new List<Plane>();

            // 마스크 조각은 의도적으로 평면을 넘을 수 있으므로 실제 남긴 영역의 범위를 우선한다.
            if (points.All(point => cutPlane.GetDistanceToPoint(point) >= -0.00001f))
            {
                planes.Add(cutPlane);
            }
            else if (points.All(point => cutPlane.GetDistanceToPoint(point) <= 0.00001f))
            {
                planes.Add(cutPlane.flipped);
            }

            return planes.ToArray();
        }

        /// <summary>양쪽 강체가 모두 출력에 남은 관절만 복사하고 연결을 재설정합니다.</summary>
        private static void CopyJoints(Dictionary<Rigidbody, Rigidbody> bodies)
        {
            foreach (KeyValuePair<Rigidbody, Rigidbody> pair in bodies)
            {
                foreach (Joint original in pair.Key.GetComponents<Joint>())
                {
                    if (original.connectedBody == null
                        || !bodies.TryGetValue(original.connectedBody, out Rigidbody connected))
                    {
                        continue;
                    }

                    Joint joint = (Joint)pair.Value.gameObject.AddComponent(original.GetType());
                    EditorUtility.CopySerialized(original, joint);
                    joint.connectedBody = connected;
                }
            }
        }

        /// <summary>강체 아래의 콜라이더 계층을 출력 본에 재현합니다.</summary>
        private static Transform ColliderPath(Transform original, Transform body, Transform outputBody)
        {
            if (original == body)
            {
                return outputBody;
            }

            Transform parent = ColliderPath(original.parent, body, outputBody);
            Transform node = parent.Find(original.name);

            if (node != null)
            {
                return node;
            }

            node = new GameObject(original.name).transform;
            node.SetParent(parent, false);
            node.localPosition = original.localPosition;
            node.localRotation = original.localRotation;
            node.localScale = original.localScale;
            node.gameObject.layer = original.gameObject.layer;
            node.gameObject.SetActive(original.gameObject.activeSelf);

            return node;
        }

        #endregion
        /// <summary>Animator 또는 스킨 계층에서 물리 리그 루트를 찾습니다.</summary>
        private static Transform PhysicsRigRoot(SkinnedMeshRenderer source)
        {
            // 저장된 프리팹은 activeSelf가 참이어도 씬에서는 비활성 상태이므로 부모 검색에 포함한다.
            Animator animator = source.GetComponentInParent<Animator>(true);

            return animator != null ? animator.transform : SkinnedMeshCutterWindowBase.RigRoot(source);
        }

        /// <summary>생성한 임시 물리 씬을 닫고 본 매핑을 해제합니다.</summary>
        public void Dispose()
        {
            if (m_scene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(m_scene);
            }

            m_bodies.Clear();
        }
    }
}

