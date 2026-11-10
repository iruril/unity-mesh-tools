using System;
using UnityEngine;

namespace MeshTools.Dismemberment.Editor
{
    [Serializable]
    public sealed class SkinnedMeshCutModelSettings
    {
        public bool AllRenderers = true;
        public string[] RendererPaths = Array.Empty<string>();
        public RendererMask[] Masks = Array.Empty<RendererMask>();
        public string[] ExcludedColliders = Array.Empty<string>();

        [Serializable]
        public sealed class RendererMask
        {
            public string Path;
            public Mesh SourceMesh;
            public int TopologyHash;
            public SkinnedMeshCutMaskMode[] Triangles = Array.Empty<SkinnedMeshCutMaskMode>();
        }

        public SkinnedMeshCutModelSettings Clone()
        {
            return new SkinnedMeshCutModelSettings
            {
                AllRenderers = AllRenderers,
                RendererPaths = (string[])RendererPaths.Clone(),
                ExcludedColliders = (string[])ExcludedColliders.Clone(),
                Masks = Array.ConvertAll(Masks, mask => new RendererMask
                {
                    Path = mask.Path, SourceMesh = mask.SourceMesh, TopologyHash = mask.TopologyHash,
                    Triangles = (SkinnedMeshCutMaskMode[])mask.Triangles.Clone()
                })
            };
        }
    }
}
