using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>
    /// Draw order of the flat layers. All layers use ZTest Always, so order comes from the material render
    /// queue and the renderer sorting order, never from depth.
    /// </summary>
    public static class RenderLayers
    {
        public const int Building = 0;
        public const int Ducts = 10;
        public const int Overlay = 20;
        public const int Equipment = 30;
        public const int Particles = 40;

        /// <summary>Render queue for a layer (transparent range).</summary>
        public static int Queue(int layer) => 3000 + layer;

        /// <summary>Adds a mesh renderer for <paramref name="mesh"/> on <paramref name="go"/> at the given layer.</summary>
        public static MeshRenderer Attach(GameObject go, Mesh mesh, Material material, int layer)
        {
            if (!go.TryGetComponent(out MeshFilter filter)) filter = go.AddComponent<MeshFilter>();
            if (!go.TryGetComponent(out MeshRenderer renderer)) renderer = go.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = layer;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }
    }
}
