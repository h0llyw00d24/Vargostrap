// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Froststrap.Vargostrap.Network;

namespace Froststrap.Vargostrap.UI
{
    /// <summary>
    /// Live latency chart: the last 60 samples as a line, lost samples as red bars along the top and
    /// spikes as orange dots. Drawn directly (no chart library, so it stays trim/AOT friendly).
    /// </summary>
    internal sealed class LatencyChart : Control
    {
        public static readonly StyledProperty<IReadOnlyList<NetworkSample>?> SamplesProperty =
            AvaloniaProperty.Register<LatencyChart, IReadOnlyList<NetworkSample>?>(nameof(Samples));

        public IReadOnlyList<NetworkSample>? Samples
        {
            get => GetValue(SamplesProperty);
            set => SetValue(SamplesProperty, value);
        }

        private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#26000000"));
        private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#30FFFFFF")), 1);
        private static readonly IPen LinePen = new Pen(new SolidColorBrush(Color.Parse("#FFE0A868")), 2, lineJoin: PenLineJoin.Round);
        private static readonly IBrush LossBrush = new SolidColorBrush(Color.Parse("#FFE05555"));
        private static readonly IBrush SpikeBrush = new SolidColorBrush(Color.Parse("#FFFFB020"));
        private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#B0FFFFFF"));

        static LatencyChart()
        {
            AffectsRender<LatencyChart>(SamplesProperty);
        }

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            context.FillRectangle(Background, bounds, 6);

            const double left = 44, top = 10, right = 10, bottom = 10;
            double width = bounds.Width - left - right;
            double height = bounds.Height - top - bottom;
            if (width <= 0 || height <= 0)
                return;

            IReadOnlyList<NetworkSample> samples = Samples ?? [];
            double max = Math.Max(100, samples.Where(s => s.LatencyMs.HasValue).Select(s => s.LatencyMs!.Value).DefaultIfEmpty(0).Max() * 1.2);
            max = Math.Ceiling(max / 50) * 50;

            // grid + scale (4 lines)
            for (int i = 0; i <= 4; i++)
            {
                double y = top + height * i / 4;
                context.DrawLine(GridPen, new Point(left, y), new Point(left + width, y));
                DrawLabel(context, $"{max * (4 - i) / 4:0}", new Point(4, y - 8));
            }

            if (samples.Count == 0)
            {
                DrawLabel(context, "Waiting for samples…", new Point(left + 8, top + height / 2 - 8));
                return;
            }

            double step = width / Math.Max(1, NetworkMonitor.Window - 1);
            double offset = (NetworkMonitor.Window - samples.Count) * step;
            Point? previous = null;

            for (int i = 0; i < samples.Count; i++)
            {
                NetworkSample s = samples[i];
                double x = left + offset + i * step;

                if (s.LatencyMs is double ms)
                {
                    var point = new Point(x, top + height - Math.Min(ms, max) / max * height);
                    if (previous is Point p)
                        context.DrawLine(LinePen, p, point);
                    previous = point;

                    if (s.IsSpike)
                        context.DrawEllipse(SpikeBrush, null, point, 4, 4);
                }
                else
                {
                    context.FillRectangle(LossBrush, new Rect(x - 1.5, top, 3, 10));
                    previous = null; // break the line across lost samples
                }
            }
        }

        private static void DrawLabel(DrawingContext context, string text, Point at)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);
            context.DrawText(formatted, at);
        }
    }
}
