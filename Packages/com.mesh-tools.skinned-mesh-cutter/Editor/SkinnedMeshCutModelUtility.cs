using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    /// <summary>모델 편집기의 메시 선택과 마스크 검증을 공유합니다.</summary>
    public static class SkinnedMeshCutModelUtility
    {
        public static SkinnedMeshRenderer[] GetRenderers(GameObject model)
            => model != null ? model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(skin => skin.sharedMesh != null).ToArray() : Array.Empty<SkinnedMeshRenderer>();

        /// <summary>저장된 메시가 있으면 정확히 일치하는 렌더러를, 없으면 가장 큰 리그를 선택합니다.</summary>
        public static SkinnedMeshRenderer FindReferenceRenderer(GameObject model, Mesh preferredMesh = null)
        {
            var skins = GetRenderers(model);
            return preferredMesh != null ? skins.FirstOrDefault(skin => skin.sharedMesh == preferredMesh)
                : skins.OrderByDescending(skin => skin.bones.Length)
                    .ThenByDescending(skin => skin.sharedMesh.vertexCount).FirstOrDefault();
        }

        public static string RendererKey(GameObject model, SkinnedMeshRenderer skin)
            => AnimationUtility.CalculateTransformPath(skin.transform, model.transform)
                + "|" + Array.IndexOf(skin.GetComponents<SkinnedMeshRenderer>(), skin);

        /// <summary>호출자가 제공한 키를 사용하므로 기존 프리셋의 경로 형식을 유지할 수 있습니다.</summary>
        public static SkinnedMeshRenderer[] SelectRenderers(SkinnedMeshRenderer[] sources, bool all,
            IEnumerable<string> paths, Func<SkinnedMeshRenderer, string> key)
        {
            if (!all)
            {
                var selected = new HashSet<string>(paths);
                sources = sources.Where(skin => selected.Contains(key(skin))).ToArray();
                if (sources.Length != selected.Count)
                    throw new InvalidOperationException("저장한 메시 경로를 찾을 수 없습니다. 절단 대상을 다시 선택하세요.");
            }
            if (sources.Length == 0) throw new InvalidOperationException("절단할 메시를 하나 이상 선택하세요.");
            return sources;
        }

        /// <summary>메시 자산 또는 토폴로지가 바뀐 마스크를 거부하고 기존 버퍼를 반환합니다.</summary>
        public static SkinnedMeshCutMaskMode[] ValidateMask(SkinnedMeshRenderer source, Mesh storedMesh,
            int topologyHash, SkinnedMeshCutMaskMode[] triangles)
        {
            if (triangles != null && triangles.Length > 0 && (storedMesh != source.sharedMesh
                || topologyHash != SkinnedMeshCutBrushSurface.GetTopologyHash(source.sharedMesh)))
                throw new InvalidOperationException(source.name + "의 메시 구조가 바뀌었습니다. 해당 마스크를 초기화하세요.");
            return triangles;
        }
    }
}
