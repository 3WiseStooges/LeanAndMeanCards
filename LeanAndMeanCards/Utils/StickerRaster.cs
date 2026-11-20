using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LeanAndMeanCards.Utils
{
    /// <summary>
    /// Tiny raster kit for the in-world sticker sprites (Dynamite's stick, Bozo's shoes):
    /// shapes as signed distances in texture pixels (y up), sampled 4x4 per pixel so the
    /// edges come out smooth without shipping a PNG.
    /// </summary>
    internal static class StickerRaster
    {
        internal static readonly Color Ink = new Color(0.05f, 0.04f, 0.07f, 1f);
        internal static readonly Color Cream = new Color(1f, 0.93f, 0.82f, 1f);

        internal static Texture2D NewTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Object.DontDestroyOnLoad(tex);
            return tex;
        }

        /// <summary>4x4 samples per pixel, straight alpha out.</summary>
        internal static Texture2D Supersampled(int w, int h, Func<float, float, Color> sample)
        {
            var tex = NewTexture(w, h);
            const int n = 4;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (var sy = 0; sy < n; sy++)
                for (var sx = 0; sx < n; sx++)
                {
                    var c = sample(x + (sx + 0.5f) / n, y + (sy + 0.5f) / n);
                    r += c.r * c.a;
                    g += c.g * c.a;
                    b += c.b * c.a;
                    a += c.a;
                }

                tex.SetPixel(x, y, a <= 0f ? Color.clear : new Color(r / a, g / a, b / a, a / (n * n)));
            }

            tex.Apply();
            return tex;
        }

        /// <summary>The shared outline: ink first, then a thin cream sticker rim.</summary>
        internal static Color Outline(float edge, float ink = 3f, float rim = 2.5f)
        {
            if (edge <= ink) return Ink;
            if (edge <= ink + rim) return Cream;
            return Color.clear;
        }

        internal static float Dist(float x, float y, float cx, float cy)
        {
            var dx = x - cx;
            var dy = y - cy;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Approximate signed distance to an ellipse: the implicit value over its gradient.
        /// Plain (r - 1) * ry swells sideways on a flat ellipse and bloats the outline.
        /// </summary>
        internal static float EllipseDist(float x, float y, float cx, float cy, float rx, float ry)
        {
            var nx = (x - cx) / rx;
            var ny = (y - cy) / ry;
            var r = Mathf.Sqrt(nx * nx + ny * ny);
            if (r < 1e-4f) return -Mathf.Min(rx, ry);
            var grad = Mathf.Sqrt(nx * nx / (rx * rx) + ny * ny / (ry * ry)) / r;
            return (r - 1f) / grad;
        }

        /// <summary>Signed distance to a rounded box: negative inside.</summary>
        internal static float RoundBoxDist(float x, float y, float x0, float y0, float x1, float y1, float r)
        {
            var qx = Mathf.Abs(x - (x0 + x1) * 0.5f) - ((x1 - x0) * 0.5f - r);
            var qy = Mathf.Abs(y - (y0 + y1) * 0.5f) - ((y1 - y0) * 0.5f - r);
            var ox = Mathf.Max(qx, 0f);
            var oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        internal static float SegDist(float x, float y, float ax, float ay, float bx, float by)
        {
            var vx = bx - ax;
            var vy = by - ay;
            var len = vx * vx + vy * vy;
            var t = len <= 0f ? 0f : Mathf.Clamp01(((x - ax) * vx + (y - ay) * vy) / len);
            return Dist(x, y, ax + vx * t, ay + vy * t);
        }
    }
}
