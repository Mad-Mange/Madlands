using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// The developer menu's look, drawn in code (no image assets): gold section icons as vector shapes, a
    /// brushed-metal panel texture and the hazard stripe along its edge.
    /// </summary>
    public static class DeveloperMenuArt
    {
        public enum Icon { Crown, Player, Gear, Paw, Bug, Pin, Scroll, Wrench }

        private static readonly Color Gold = new Color(0.91f, 0.77f, 0.42f);
        private static readonly Color GoldDark = new Color(0.45f, 0.33f, 0.12f);
        private static Texture2D _stripes, _metal;

        /// <summary>A square element that draws one icon in gold.</summary>
        public static VisualElement MakeIcon(Icon icon, float size = 20f)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList("dev-icon");
            element.style.width = size;
            element.style.height = size;
            element.generateVisualContent += ctx => Draw(ctx.painter2D, icon, element.contentRect.size);
            return element;
        }

        private static void Draw(Painter2D p, Icon icon, Vector2 size)
        {
            float s = Mathf.Min(size.x, size.y);
            if (s <= 0f) return;
            p.fillColor = Gold;
            p.strokeColor = Gold;
            p.lineWidth = Mathf.Max(1.5f, s * 0.11f);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            Vector2 P(float x, float y) => new Vector2(x * s, y * s);
            switch (icon)
            {
                case Icon.Crown:
                    p.BeginPath();
                    p.MoveTo(P(0.08f, 0.78f));
                    p.LineTo(P(0.08f, 0.3f));
                    p.LineTo(P(0.3f, 0.52f));
                    p.LineTo(P(0.5f, 0.18f));
                    p.LineTo(P(0.7f, 0.52f));
                    p.LineTo(P(0.92f, 0.3f));
                    p.LineTo(P(0.92f, 0.78f));
                    p.ClosePath();
                    p.Fill();
                    p.BeginPath();
                    p.MoveTo(P(0.08f, 0.9f));
                    p.LineTo(P(0.92f, 0.9f));
                    p.Stroke();
                    break;
                case Icon.Player:
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.3f), s * 0.2f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                    p.BeginPath();
                    p.MoveTo(P(0.12f, 0.95f));
                    p.BezierCurveTo(P(0.12f, 0.55f), P(0.88f, 0.55f), P(0.88f, 0.95f));
                    p.ClosePath();
                    p.Fill();
                    break;
                case Icon.Gear:
                    p.BeginPath();
                    const int teeth = 8;
                    for (int i = 0; i < teeth * 4; i++)
                    {
                        float a = (i / (float)(teeth * 4)) * Mathf.PI * 2f;
                        float r = (i % 4 < 2 ? 0.48f : 0.36f) * s;
                        var point = new Vector2(0.5f * s + Mathf.Cos(a) * r, 0.5f * s + Mathf.Sin(a) * r);
                        if (i == 0) p.MoveTo(point);
                        else p.LineTo(point);
                    }
                    p.ClosePath();
                    p.Fill();
                    p.fillColor = GoldDark;
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.5f), s * 0.15f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                    break;
                case Icon.Paw:
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.66f), s * 0.22f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                    foreach (var (x, y) in new[] { (0.16f, 0.42f), (0.36f, 0.2f), (0.64f, 0.2f), (0.84f, 0.42f) })
                    {
                        p.BeginPath();
                        p.Arc(P(x, y), s * 0.11f, Angle.Degrees(0f), Angle.Degrees(360f));
                        p.Fill();
                    }
                    break;
                case Icon.Bug:
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.58f), s * 0.24f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.26f), s * 0.12f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                    p.BeginPath();
                    foreach (var (y, dy) in new[] { (0.46f, -0.1f), (0.6f, 0f), (0.74f, 0.1f) })
                    {
                        p.MoveTo(P(0.3f, y));
                        p.LineTo(P(0.08f, y + dy));
                        p.MoveTo(P(0.7f, y));
                        p.LineTo(P(0.92f, y + dy));
                    }
                    p.Stroke();
                    break;
                case Icon.Pin:
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.38f), s * 0.3f, Angle.Degrees(150f), Angle.Degrees(390f));
                    p.LineTo(P(0.5f, 0.96f));
                    p.ClosePath();
                    p.Fill();
                    p.fillColor = GoldDark;
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.38f), s * 0.12f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                    break;
                case Icon.Scroll:
                    p.BeginPath();
                    p.MoveTo(P(0.2f, 0.08f));
                    p.LineTo(P(0.82f, 0.08f));
                    p.LineTo(P(0.82f, 0.92f));
                    p.LineTo(P(0.2f, 0.92f));
                    p.ClosePath();
                    p.Fill();
                    p.strokeColor = GoldDark;
                    p.lineWidth = Mathf.Max(1f, s * 0.07f);
                    p.BeginPath();
                    for (int i = 0; i < 4; i++)
                    {
                        p.MoveTo(P(0.32f, 0.26f + i * 0.17f));
                        p.LineTo(P(0.7f, 0.26f + i * 0.17f));
                    }
                    p.Stroke();
                    break;
                case Icon.Wrench:
                    p.lineWidth = s * 0.18f;
                    p.BeginPath();
                    p.MoveTo(P(0.2f, 0.82f));
                    p.LineTo(P(0.62f, 0.4f));
                    p.Stroke();
                    p.lineWidth = s * 0.12f;
                    p.BeginPath();
                    p.Arc(P(0.7f, 0.3f), s * 0.2f, Angle.Degrees(110f), Angle.Degrees(380f));
                    p.Stroke();
                    break;
            }
        }

        /// <summary>Yellow and black diagonal stripes, tiled along the menu's edge.</summary>
        public static Texture2D HazardStripes()
        {
            if (_stripes != null) return _stripes;
            const int size = 32;
            _stripes = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "DevMenuStripes" };
            var yellow = new Color(0.86f, 0.66f, 0.12f);
            var black = new Color(0.08f, 0.07f, 0.06f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Worn paint: a little noise so the stripes do not look printed.
                float wear = Mathf.PerlinNoise(x * 0.31f, y * 0.29f) * 0.25f;
                var c = ((x + y) / 8) % 2 == 0 ? yellow : black;
                _stripes.SetPixel(x, y, Color.Lerp(c, new Color(0.22f, 0.2f, 0.18f), wear));
            }
            _stripes.Apply();
            return _stripes;
        }

        /// <summary>A dark, slightly scratched metal plate.</summary>
        public static Texture2D Metal()
        {
            if (_metal != null) return _metal;
            const int size = 128;
            _metal = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "DevMenuMetal" };
            var random = new System.Random(7);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float grain = Mathf.PerlinNoise(x * 0.9f, y * 0.06f) * 0.05f + Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.04f;
                float v = 0.1f + grain;
                _metal.SetPixel(x, y, new Color(v, v * 0.98f, v * 0.95f));
            }
            for (int i = 0; i < 26; i++)
            {
                int x0 = random.Next(size), y0 = random.Next(size), length = 6 + random.Next(22);
                float slope = (float)(random.NextDouble() - 0.5) * 0.6f;
                for (int k = 0; k < length; k++)
                {
                    int x = (x0 + k) % size, y = ((int)(y0 + k * slope) % size + size) % size;
                    var c = _metal.GetPixel(x, y);
                    _metal.SetPixel(x, y, c + new Color(0.05f, 0.05f, 0.05f));
                }
            }
            _metal.Apply();
            return _metal;
        }

        /// <summary>Four small rivets in the corners of an element.</summary>
        public static void AddRivets(VisualElement element)
        {
            foreach (var corner in new[] { "dev-rivet-tl", "dev-rivet-tr", "dev-rivet-bl", "dev-rivet-br" })
            {
                var rivet = new VisualElement { pickingMode = PickingMode.Ignore };
                rivet.AddToClassList("dev-rivet");
                rivet.AddToClassList(corner);
                element.Add(rivet);
            }
        }
    }
}
