using System;
using System.Collections.Generic;
using System.Linq;
using AgentSpace.Core.Models;

namespace AgentSpace.Core.Services
{
    /// <summary>
    /// Calculates optimal grid layout for window arrangement.
    ///
    /// Algorithm:
    ///   Input: n=windows, screenW, screenH, gap
    ///   Output: list of (x, y, w, h) rects in screen coordinates (physical pixels)
    ///
    ///   1. Find best grid dims (rows x cols) closest to square
    ///      - iterate rows from 1 to n
    ///      - cols = ceil(n / rows)
    ///      - verify cols * rows >= n
    ///   2. Calculate cellW = (screenW - gap*(cols+1)) / cols
    ///   3. Calculate cellH = (screenH - gap*(rows+1)) / rows
    ///   4. Place each window left-to-right, top-to-bottom
    ///   5. Left-align incomplete last row
    /// </summary>
    public static class ArrangeService
    {
        public static List<NativeRect> CalculateGrid(int windowCount, int screenW, int screenH, int gap)
        {
            var result = new List<NativeRect>();

            if (windowCount <= 0)
                return result;

            // Step 1: Find best rows × cols (closest to square)
            int bestRows = 1;
            int bestCols = windowCount;
            double bestAspectDiff = double.MaxValue;

            for (int rows = 1; rows <= windowCount; rows++)
            {
                int cols = (int)Math.Ceiling((double)windowCount / rows);
                // Grid should be at least as wide as needed
                if (cols * rows < windowCount)
                    continue;

                // Prefer grids close to square (aspect ratio 1:1)
                double cellAspect = (double)cols / rows;
                double aspectDiff = Math.Abs(cellAspect - 1.0);
                if (aspectDiff < bestAspectDiff)
                {
                    bestAspectDiff = aspectDiff;
                    bestRows = rows;
                    bestCols = cols;
                }
            }

            // Step 2: Calculate cell dimensions
            int cellW = (screenW - gap * (bestCols + 1)) / bestCols;
            int cellH = (screenH - gap * (bestRows + 1)) / bestRows;

            // Step 3 & 4: Place windows left-to-right, top-to-bottom
            int windowIndex = 0;
            for (int row = 0; row < bestRows; row++)
            {
                int colsInThisRow = bestCols;
                // Left-align incomplete last row
                if (row == bestRows - 1)
                {
                    int remaining = windowCount - windowIndex;
                    colsInThisRow = Math.Min(bestCols, remaining);
                }

                for (int col = 0; col < colsInThisRow && windowIndex < windowCount; col++)
                {
                    int x = gap + col * (cellW + gap);
                    int y = gap + row * (cellH + gap);

                    result.Add(new NativeRect
                    {
                        Left = x,
                        Top = y,
                        Right = x + cellW,
                        Bottom = y + cellH
                    });

                    windowIndex++;
                }
            }

            return result;
        }

        /// <summary>
        /// Arranges all ContainerWindows in the application by grid.
        /// Uses AppSettings.ArrangeGap for spacing.
        /// </summary>
        public static void ArrangeAll(
            IReadOnlyList<(IntPtr Hwnd, double DipLeft, double DipTop, double DipWidth, double DipHeight)> windows,
            int screenW,
            int screenH,
            int gap,
            double dpiScaleX,
            double dpiScaleY,
            Action<IntPtr, int, int, int, int> setPosition)
        {
            if (windows.Count == 0)
                return;

            var gridRects = CalculateGrid(windows.Count, screenW, screenH, gap);

            for (int i = 0; i < windows.Count && i < gridRects.Count; i++)
            {
                var (hwnd, dipLeft, dipTop, dipWidth, dipHeight) = windows[i];
                var rect = gridRects[i];

                // Convert physical pixel rect to DIPs for the container API
                int dipX = (int)(rect.Left / dpiScaleX);
                int dipY = (int)(rect.Top / dpiScaleY);
                int dipW = (int)(rect.Width / dpiScaleX);
                int dipH = (int)(rect.Height / dpiScaleY);

                try
                {
                    setPosition(hwnd, dipX, dipY, dipW, dipH);
                }
                catch (Exception)
                {
                    // Window may have been closed mid-arrange — skip
                }
            }
        }
    }
}