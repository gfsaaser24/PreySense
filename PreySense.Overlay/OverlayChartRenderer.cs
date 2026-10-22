using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PreySense.Overlay
{
    public static class OverlayChartRenderer
    {
        private static void FillArea(Graphics g, PointF[] polyPts, PointF[] topLine, PointF[] bottomLine, SolidBrush brush)
        {
            int n = topLine.Length;
            topLine.CopyTo(polyPts, 0);
            for (int i = 0; i < n; i++)
                polyPts[n + i] = bottomLine[n - 1 - i];
            g.FillPolygon(brush, polyPts);
        }

        public static void DrawStackedChart(
            Graphics g,
            int x, int y, int w, int h,
            float sc,
            float[] cpuHistory,
            float[] gpuHistory,
            int historyHead,
            int historyLength,
            PointF[] basePts,
            PointF[] gpuPts,
            PointF[] cpuPts,
            PointF[] polyPts,
            SolidBrush graphGpuBrush,
            Pen graphGpuPen,
            SolidBrush graphCpuBrush,
            Pen graphCpuPen,
            Pen totalPen,
            Pen axPen)
        {
            float peak = 10f;
            for (int i = 0; i < historyLength; i++)
            {
                if (cpuHistory[i] > peak) peak = cpuHistory[i];
                if (gpuHistory[i] > peak) peak = gpuHistory[i];
            }

            float stepX = (float)w / (historyLength - 1);
            int Idx(int i) => (historyHead + i) % historyLength;

            for (int i = 0; i < historyLength; i++)
            {
                int idx = Idx(i);
                float px = x + i * stepX;
                float cpuH = (cpuHistory[idx] / peak) * h;
                float gpuH = (gpuHistory[idx] / peak) * h;

                basePts[i] = new PointF(px, y + h);
                gpuPts[i]  = new PointF(px, y + h - gpuH);
                cpuPts[i]  = new PointF(px, y + h - cpuH);
            }

            var saved = g.Save();
            g.SetClip(new RectangleF(x, y, w, h));

            FillArea(g, polyPts, gpuPts, basePts, graphGpuBrush);
            g.DrawLines(graphGpuPen, gpuPts);
            FillArea(g, polyPts, cpuPts, basePts, graphCpuBrush);
            g.DrawLines(graphCpuPen, cpuPts);

            g.Restore(saved);
            g.DrawLine(axPen, x, y + h, x + w, y + h);
        }

    }
}
