using System.IO;
using UnityEngine;

namespace HeadLoss.Editor
{
    /// <summary>
    /// Renders the Steam capsules from the game itself (dev tool, never in a player build): the game camera draws the running
    /// scene into a texture at each Steam size, with the title on top in Unity's default font. No HUD, no outside art.
    /// Call in play mode from the CLI with <c>script eval</c>, after setting up what should be seen (see <see cref="Showcase"/>).
    /// </summary>
    public static class SteamAssets
    {
        /// <summary>Steam's sizes. Zoom is the share of the current view shown (smaller capsules look closer). The hero has no title: Steam lays the logo over it.</summary>
        static readonly (string name, int w, int h, float zoom, TitleKind title)[] Capsules =
        {
            ("header", 920, 430, 0.55f, TitleKind.Wide),
            ("small", 462, 174, 0.34f, TitleKind.Wide),
            ("main", 1232, 706, 0.65f, TitleKind.Wide),
            ("vertical", 748, 896, 0.5f, TitleKind.Tall),
            ("library", 600, 900, 0.5f, TitleKind.Tall),
            ("library-hero", 3840, 1240, 1f, TitleKind.None),
        };

        enum TitleKind { None, Wide, Tall }

        /// <summary>Folder the capsules go to, relative to the project root.</summary>
        public const string CapsuleFolder = "docs/dev/steam/capsules";

        /// <summary>Renders every capsule into <see cref="CapsuleFolder"/>, centred on the camera's current view. Returns the files written.</summary>
        /// <param name="title">The title drawn on them.</param>
        /// <param name="zoomFactor">Share of the current view a capsule at zoom 1 shows; a building needs more than the city.</param>
        public static string RenderCapsules(string title = "Head Loss", float zoomFactor = 1f)
        {
            Camera? cam = Camera.main;
            if (cam == null) return "no camera: enter play mode first";
            Directory.CreateDirectory(CapsuleFolder);
            float baseHalfH = cam.orthographicSize * zoomFactor;
            float baseAspect = cam.aspect;
            float size = cam.orthographicSize;
            var written = new System.Text.StringBuilder();
            foreach ((string name, int w, int h, float zoom, TitleKind kind) in Capsules)
            {
                // The same world area in every shape, scaled by the zoom.
                float aspect = (float)w / h;
                cam.orthographicSize = baseHalfH * zoom * Mathf.Sqrt(baseAspect / aspect);
                string path = $"{CapsuleFolder}/{name}_{w}x{h}.png";
                File.WriteAllBytes(path, Render(cam, w, h, title, kind));
                written.AppendLine(path);
            }
            cam.orthographicSize = size;
            cam.ResetAspect();
            return written.ToString();
        }

        static byte[] Render(Camera cam, int w, int h, string title, TitleKind kind)
        {
            RenderTexture rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 24) { sRGB = true, msaaSamples = 8 });
            RenderTexture? before = RenderTexture.active;
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            cam.Render();
            cam.targetTexture = null;
            RenderTexture.active = rt;
            if (kind != TitleKind.None) DrawTitle(w, h, title, kind, cam.backgroundColor);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(rt);
            return png;
        }

        /// <summary>The title over a soft band of the paper colour, so it stays readable over any part of the city.</summary>
        static void DrawTitle(int w, int h, string title, TitleKind kind, Color paper)
        {
            string[] lines = kind == TitleKind.Tall ? title.Split(' ') : new[] { title };
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // Wide: one line filling most of the width. Tall: the words stacked.
            int px = kind == TitleKind.Tall ? Mathf.RoundToInt(w * 0.24f) : Mathf.RoundToInt(h * 0.27f);
            foreach (string line in lines) font.RequestCharactersInTexture(line, px, FontStyle.Bold);
            float lineHeight = px * 1.05f;
            float block = lineHeight * lines.Length;
            float centreY = kind == TitleKind.Tall ? h * 0.74f : h * 0.5f;

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, w, 0, h);
            var band = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            band.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            band.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            band.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            band.SetInt("_ZWrite", 0);
            band.SetInt("_Cull", 0);
            band.SetPass(0);
            float top = centreY + block * 0.5f + px * 0.12f, bottom = centreY - block * 0.5f - px * 0.12f, feather = px * 0.7f;
            // GL colours are not converted to the render target's colour space.
            Color linearPaper = paper.linear;
            Color solid = linearPaper; solid.a = 0.82f;
            Color clear = linearPaper; clear.a = 0;
            GL.Begin(GL.QUADS);
            Quad(0, w, top, top + feather, solid, clear);
            Quad(0, w, bottom, top, solid, solid);
            Quad(0, w, bottom - feather, bottom, clear, solid);
            GL.End();
            Object.DestroyImmediate(band);

            Color ink = (paper.grayscale > 0.5f ? new Color(0.13f, 0.16f, 0.2f) : new Color(0.93f, 0.95f, 0.97f)).linear;
            font.material.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(ink);
            float y = centreY + block * 0.5f - lineHeight * 0.8f;
            foreach (string line in lines)
            {
                float width = 0;
                foreach (char c in line)
                    if (font.GetCharacterInfo(c, out CharacterInfo ci, px, FontStyle.Bold)) width += ci.advance;
                float x = (w - width) * 0.5f;
                foreach (char c in line)
                {
                    if (!font.GetCharacterInfo(c, out CharacterInfo ci, px, FontStyle.Bold)) continue;
                    GL.TexCoord(ci.uvBottomLeft); GL.Vertex3(x + ci.minX, y + ci.minY, 0);
                    GL.TexCoord(ci.uvTopLeft); GL.Vertex3(x + ci.minX, y + ci.maxY, 0);
                    GL.TexCoord(ci.uvTopRight); GL.Vertex3(x + ci.maxX, y + ci.maxY, 0);
                    GL.TexCoord(ci.uvBottomRight); GL.Vertex3(x + ci.maxX, y + ci.minY, 0);
                    x += ci.advance;
                }
                y -= lineHeight;
            }
            GL.End();
            GL.PopMatrix();
        }

        static void Quad(float x0, float x1, float y0, float y1, Color low, Color high)
        {
            GL.Color(low); GL.Vertex3(x0, y0, 0); GL.Vertex3(x1, y0, 0);
            GL.Color(high); GL.Vertex3(x1, y1, 0); GL.Vertex3(x0, y1, 0);
        }
    }
}
