using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>
    /// Draw order of the flat layers. All layers use ZTest Always, so order comes from the renderer sorting
    /// order and the material render queue, never from depth. Mesh layers use negative orders because the 2D
    /// renderer draws instanced particles at sorting order 0; that keeps particles on top.
    /// </summary>
    public static class RenderLayers
    {
        public const int Building = -40;
        /// <summary>Room heatmap: over the room fills, under the walls.</summary>
        public const int RoomHeat = -36;
        public const int Walls = -33;
        public const int Ducts = -30;
        public const int Overlay = -20;
        public const int Equipment = -10;
        public const int Particles = 0;

        /// <summary>Render queue for a layer (transparent range, increasing with the layer).</summary>
        public static int Queue(int layer) => 3050 + layer;

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
