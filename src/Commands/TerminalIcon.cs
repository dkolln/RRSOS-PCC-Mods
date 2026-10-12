using System;
using System.Collections.Generic;
using RRSOS.PCC.Shared;
using UnityEngine;

namespace RRSOS.PCC.Commands
{
    /// <summary>
    /// The terminal's build-menu icon: the game's own screen icon with what is on the screen replaced by a green "C:\&gt;" prompt and a cursor block. It is drawn from the
    /// game's icon when the game starts (the game's art is never copied into this project) and saved to CustomIcons\Terminal.png; once that file exists it is used as is.
    /// The screen is found from the picture itself: it is the largest dark area, and its tilt is read from its top edge so the text sits at the same angle.
    /// </summary>
    internal static class TerminalIcon
    {
        // A tiny 5 x 7 pixel font, just the characters of the prompt. Rows from the top; '#' is a lit pixel.
        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
            [':'] = new[] { ".....", "..#..", "..#..", ".....", "..#..", "..#..", "....." },
            ['\\'] = new[] { "#....", "#....", ".#...", "..#..", "...#.", "....#", "....#" },
            ['>'] = new[] { "#....", ".#...", "..#..", "...#.", "..#..", ".#...", "#...." },
            ['_'] = new[] { "###..", "###..", "###..", "###..", "###..", "###..", "###.." },   // the cursor block
        };

        private const string Prompt = "C:\\>_";

        private static readonly Color ScreenColor = new Color(0.03f, 0.045f, 0.04f, 1f);
        private static readonly Color TextColor = new Color(0.45f, 1f, 0.45f, 1f);

        /// <summary>The icon, painted over the source's screen. Null when the screen cannot be found (the plain source icon is used then).</summary>
        public static Sprite Paint(Sprite source)
        {
            try
            {
                var pixels = IconTools.ReadPixels(source, out var w, out var h);

                // the screen: the largest connected area of dark, opaque pixels
                var dark = new bool[w * h];
                for (var i = 0; i < dark.Length; i++)
                {
                    var c = pixels[i];
                    dark[i] = c.a > 0.9f && 0.299f * c.r + 0.587f * c.g + 0.114f * c.b < 0.22f;
                }

                var screen = LargestArea(dark, w, h);
                if (screen == null)
                {
                    ModLog.Note("Terminal icon: no screen found in the source icon; using it as it is.");
                    return null;
                }

                // each row's span across the screen, so what is on the screen (symbols, a globe) counts as part of it
                var minX = new int[h];
                var maxX = new int[h];
                int top = -1, bottom = h, left = w, right = -1;
                for (var y = 0; y < h; y++)
                {
                    minX[y] = w;
                    maxX[y] = -1;
                    for (var x = 0; x < w; x++)
                    {
                        if (!screen[y * w + x])
                            continue;
                        if (x < minX[y]) minX[y] = x;
                        if (x > maxX[y]) maxX[y] = x;
                    }

                    if (maxX[y] >= 0)
                    {
                        top = Math.Max(top, y);
                        bottom = Math.Min(bottom, y);
                        left = Math.Min(left, minX[y]);
                        right = Math.Max(right, maxX[y]);
                    }
                }

                for (var y = bottom; y <= top; y++)
                    for (var x = minX[y]; x <= maxX[y]; x++)
                        pixels[y * w + x] = ScreenColor;

                // the screen's tilt: how much higher its top edge is a quarter of the way in from the right than from the left
                var quarter = (right - left) / 4;
                var edgeLeft = TopEdge(screen, w, h, left + quarter);
                var edgeRight = TopEdge(screen, w, h, right - quarter);
                var slope = edgeLeft < 0 || edgeRight < 0 ? 0f : (edgeRight - edgeLeft) / Math.Max(1f, (right - left) - 2f * quarter);

                // the prompt: as large as fits in most of the screen's width, set at the screen's tilt
                var glyphWidth = 6;                                   // 5 pixels and a gap
                var scale = Math.Max(2, (int)((right - left) * 0.8f / (glyphWidth * Prompt.Length)));
                var originX = left + (int)((right - left) * 0.12f);
                var middle = (top + bottom) / 2;
                var originY = middle + (int)(3.5f * scale);           // the top of the text (the pixel rows count upwards)
                var centerX = (left + right) / 2f;

                for (var n = 0; n < Prompt.Length; n++)
                {
                    var rows = Glyphs[Prompt[n]];
                    for (var gy = 0; gy < rows.Length; gy++)
                    {
                        for (var gx = 0; gx < rows[gy].Length; gx++)
                        {
                            if (rows[gy][gx] != '#')
                                continue;

                            var px = originX + (n * glyphWidth + gx) * scale;
                            var py = originY - gy * scale + (int)(slope * (px - centerX));
                            Block(pixels, w, h, px, py - scale + 1, scale, minX, maxX, bottom, top);
                        }
                    }
                }

                return IconTools.ToSprite(pixels, w, h, source.pixelsPerUnit, "Terminal");
            }
            catch (Exception e)
            {
                ModLog.Note("Terminal icon: could not paint it (" + e.GetType().Name + ": " + e.Message + "); using the source icon as it is.");
                return null;
            }
        }

        /// <summary>A square of lit pixels, kept inside the screen.</summary>
        private static void Block(Color[] pixels, int w, int h, int x0, int y0, int size, int[] minX, int[] maxX, int bottom, int top)
        {
            for (var y = y0; y < y0 + size; y++)
            {
                if (y < bottom || y > top)
                    continue;
                for (var x = x0; x < x0 + size; x++)
                    if (x >= minX[y] && x <= maxX[y])
                        pixels[y * w + x] = TextColor;
            }
        }

        /// <summary>The highest row of the area in this column, or -1.</summary>
        private static int TopEdge(bool[] area, int w, int h, int x)
        {
            for (var y = h - 1; y >= 0; y--)
                if (area[y * w + x])
                    return y;
            return -1;
        }

        /// <summary>The biggest group of touching true pixels, as a mask, or null when there are none.</summary>
        private static bool[] LargestArea(bool[] dark, int w, int h)
        {
            var seen = new bool[dark.Length];
            List<int> best = null;
            var stack = new Stack<int>();
            for (var start = 0; start < dark.Length; start++)
            {
                if (!dark[start] || seen[start])
                    continue;

                var area = new List<int>();
                stack.Push(start);
                seen[start] = true;
                while (stack.Count > 0)
                {
                    var i = stack.Pop();
                    area.Add(i);
                    var x = i % w;
                    var y = i / w;
                    if (x > 0 && dark[i - 1] && !seen[i - 1]) { seen[i - 1] = true; stack.Push(i - 1); }
                    if (x < w - 1 && dark[i + 1] && !seen[i + 1]) { seen[i + 1] = true; stack.Push(i + 1); }
                    if (y > 0 && dark[i - w] && !seen[i - w]) { seen[i - w] = true; stack.Push(i - w); }
                    if (y < h - 1 && dark[i + w] && !seen[i + w]) { seen[i + w] = true; stack.Push(i + w); }
                }

                if (best == null || area.Count > best.Count)
                    best = area;
            }

            if (best == null)
                return null;

            var mask = new bool[dark.Length];
            foreach (var i in best)
                mask[i] = true;
            return mask;
        }
    }
}
