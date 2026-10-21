using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LineLogisticsGame.Geometry;
using LineLogisticsGame.Simulation;
using LineLogisticsGame.UI;
using LineLogisticsGame.World;

namespace LineLogisticsGame.Screens
{
    // Statistics over the running game, opened with the Stats button next to Menu. One tab each for:
    //   Lines: every kind of line with an example of it and the total length built
    //   Produced: resources produced, per type, every Statistics.SampleInterval seconds
    //   Consumed: resources consumed by producers and consumer lines together, the same way
    //   Transported: resources transporters delivered, per type, the same way
    //   Trips: deliveries transporters finished, the same way
    //   Points: the points total over time
    // The charts show the last Statistics.MaxSamples samples; totals count everything since the game started.
    // Click a tab or press 1-6 (Left/Right steps through them). Close, Esc or the Stats button closes it.
    public class StatsScreen
    {
        private enum Tab { Lines, Produced, Consumed, Transported, Trips, Points }
        private static readonly string[] tabNames = { "Lines", "Produced", "Consumed", "Transported", "Trips", "Points" };

        private const int Padding = 20, TabWidth = 112, TabHeight = 32, LegendWidth = 130;
        private static readonly Color PanelColor = new Color(36, 36, 44);
        private static readonly Color GridColor = new Color(70, 70, 84);
        private static readonly Color TextColor = new Color(225, 225, 230);

        private readonly TransportSystem transport;
        private readonly LineDrawing drawing;
        private readonly Action close;
        private Tab tab = Tab.Lines;

        private Rectangle panel, content, closeButton;
        private readonly Rectangle[] tabs = new Rectangle[tabNames.Length];
        private int hoveredTab = -1;
        private bool closeHovered;

        // text is drawn after the shapes, in one batch
        private readonly List<(string Text, Vector2 Position, Color Color)> texts = new List<(string, Vector2, Color)>();
        private SpriteFont font;

        public StatsScreen(TransportSystem transport, LineDrawing drawing, Action close)
        {
            this.transport = transport;
            this.drawing = drawing;
            this.close = close;
        }

        public void Layout(int screenWidth, int screenHeight)
        {
            int width = Math.Min(1000, screenWidth - 80), height = Math.Min(680, screenHeight - 80);
            panel = new Rectangle((screenWidth - width) / 2, (screenHeight - height) / 2, width, height);
            for (int i = 0; i < tabs.Length; i++)
                tabs[i] = new Rectangle(panel.X + Padding + i * (TabWidth + 6), panel.Y + Padding, TabWidth, TabHeight);
            closeButton = new Rectangle(panel.Right - Padding - 90, panel.Y + Padding, 90, TabHeight);
            int top = tabs[0].Bottom + 16;
            content = new Rectangle(panel.X + Padding, top, panel.Width - Padding * 2, panel.Bottom - Padding - top);
        }

        public void Update(Input input)
        {
            Vector2 mouse = input.ScreenMousePosition;
            hoveredTab = Array.FindIndex(tabs, t => t.Contains(mouse));
            closeHovered = closeButton.Contains(mouse);

            if (input.LeftPressed)
            {
                if (hoveredTab >= 0) tab = (Tab)hoveredTab;
                else if (closeHovered) close();
            }
            if (input.KeyPressed(Keys.Escape)) close();

            Keys[] tabKeys = { Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6 };
            for (int i = 0; i < tabKeys.Length; i++)
                if (input.KeyPressed(tabKeys[i])) tab = (Tab)i;
            if (input.KeyPressed(Keys.Right)) tab = (Tab)(((int)tab + 1) % tabNames.Length);
            if (input.KeyPressed(Keys.Left)) tab = (Tab)(((int)tab + tabNames.Length - 1) % tabNames.Length);
        }

        public void Draw(SpriteBatch spriteBatch, SpriteFont font)
        {
            this.font = font;
            texts.Clear();

            DrawShape.DrawFilledRectangle(panel, PanelColor);
            DrawShape.DrawRectangle(panel, 1f, GridColor);
            for (int i = 0; i < tabs.Length; i++)
            {
                Button.DrawBox(tabs[i], i == (int)tab || i == hoveredTab);
                TextCentered(tabNames[i], tabs[i].Center.ToVector2(), i == (int)tab ? Color.White : TextColor * 0.7f);
            }
            Button.DrawBox(closeButton, closeHovered);
            TextCentered("Close", closeButton.Center.ToVector2(), Color.White);

            var statistics = transport.Statistics;
            switch (tab)
            {
                case Tab.Lines: DrawLines(); break;
                case Tab.Produced: DrawResourceChart("Resources produced", s => s.Produced); break;
                case Tab.Consumed: DrawResourceChart("Resources consumed by producers and consumers", s => s.Consumed); break;
                case Tab.Transported: DrawResourceChart("Resources transported", s => s.Transported); break;
                case Tab.Trips: DrawTrips(); break;
                case Tab.Points: DrawPoints(); break;
            }

            DrawShape.Flush();
            spriteBatch.Begin();
            foreach (var (text, position, color) in texts)
                spriteBatch.DrawString(font, text, new Vector2(MathF.Round(position.X), MathF.Round(position.Y)), color);
            spriteBatch.End();
        }

        private void Text(string text, Vector2 position, Color color) => texts.Add((text, position, color));
        private void TextCentered(string text, Vector2 center, Color color) => texts.Add((text, TextLayout.Centered(font, text, center), color));
        private void TextRight(string text, Vector2 rightCenter, Color color)
        {
            Vector2 size = font.MeasureString(text);
            texts.Add((text, new Vector2(rightCenter.X - size.X, TextLayout.CenteredY(font, rightCenter.Y)), color));
        }
        private void TextLeft(string text, Vector2 leftCenter, Color color) =>
            texts.Add((text, new Vector2(leftCenter.X, TextLayout.CenteredY(font, leftCenter.Y)), color));

        #region Lines

        // a row for every kind of line that is built: roads, consumers, then producer outputs and inputs by resource
        private void DrawLines()
        {
            var rows = drawing.Segments
                .GroupBy(s => (s.Kind, Type: s.Kind == LineKind.Output || s.Kind == LineKind.Input ? s.Type : 0))
                .OrderBy(g => g.Key.Kind switch { LineKind.Road => 0, LineKind.Consumer => 1, LineKind.Output => 2, _ => 3 })
                .ThenBy(g => g.Key.Type)
                .Select(g => (g.Key.Kind, g.Key.Type, Example: g.First(), Count: g.Count(),
                              Length: g.Sum(s => Vector2.Distance(s.A.position, s.B.position))))
                .ToList();

            int labelX = content.X + 8, exampleX = content.X + 330, exampleWidth = 180;
            int countX = exampleX + exampleWidth + 150, lengthX = content.Right - 8;
            float headerY = content.Y + 10;
            Text("Line", new Vector2(labelX, headerY - 8), TextColor * 0.6f);
            Text("Example", new Vector2(exampleX, headerY - 8), TextColor * 0.6f);
            TextRight("Lines", new Vector2(countX, headerY), TextColor * 0.6f);
            TextRight("Total length", new Vector2(lengthX, headerY), TextColor * 0.6f);
            DrawShape.DrawLine(new Vector2(content.X, headerY + 14), new Vector2(content.Right, headerY + 14), GridColor, 1f);

            if (rows.Count == 0)
            {
                TextCentered("Nothing built yet", content.Center.ToVector2(), TextColor * 0.5f);
                return;
            }

            float top = headerY + 20, totalHeight = content.Bottom - 34 - top;
            float rowHeight = Math.Min(30f, totalHeight / rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var (kind, type, example, count, length) = rows[i];
                float y = top + (i + 0.5f) * rowHeight;
                if (i % 2 == 1) DrawShape.DrawFilledRectangle(new Rectangle(content.X, (int)(y - rowHeight / 2f), content.Width, (int)rowHeight), Color.White * 0.03f);

                float x = labelX;
                if (type > 0)
                {
                    TransportSystem.DrawResourceIcon(new Vector2(x + 9, y), Math.Min(9f, rowHeight / 2f - 2f), type);
                    x += 26;
                }
                TextLeft(LineLabel(kind, type), new Vector2(x, y), TextColor);
                LineDrawing.DrawStyledLine(new Vector2(exampleX + 6, y), new Vector2(exampleX + exampleWidth - 6, y), example.Color, example.Width);
                TextRight(count.ToString("N0"), new Vector2(countX, y), TextColor * 0.8f);
                TextRight(length.ToString("N0"), new Vector2(lengthX, y), Color.White);
            }

            float totalY = content.Bottom - 14;
            DrawShape.DrawLine(new Vector2(content.X, totalY - 18), new Vector2(content.Right, totalY - 18), GridColor, 1f);
            TextLeft("All lines", new Vector2(labelX, totalY), TextColor * 0.8f);
            TextRight(rows.Sum(r => r.Count).ToString("N0"), new Vector2(countX, totalY), TextColor * 0.8f);
            TextRight(rows.Sum(r => r.Length).ToString("N0"), new Vector2(lengthX, totalY), Color.Gold);
        }

        private static string LineLabel(LineKind kind, int type) => kind switch
        {
            LineKind.Road => "Road",
            LineKind.Consumer => "Consumer",
            LineKind.Output => $"Producer output (resource {type})",
            LineKind.Input => $"Producer input (resource {type})",
            _ => kind.ToString()
        };

        #endregion

        #region Charts

        // A line per resource type that has any, of how many there were in every kept sample,
        // with totals since the game started in a legend.
        private void DrawResourceChart(string title, Func<Statistics.Sample, int[]> counts)
        {
            var statistics = transport.Statistics;
            int[] totals = counts(statistics.Totals);
            var types = Enumerable.Range(1, ResourceRecipes.Count).Where(type => totals[type] > 0).ToList();
            Rectangle chart = ChartArea(title, $"per {Statistics.SampleInterval:0} s", true);

            // legend: icon and total for every type
            float legendY = chart.Y + 4;
            Text("Total", new Vector2(chart.Right + 30, chart.Y - 26), TextColor * 0.6f);
            foreach (int type in types)
            {
                TransportSystem.DrawResourceIcon(new Vector2(chart.Right + 40, legendY + 10), 9f, type);
                TextLeft(totals[type].ToString("N0"), new Vector2(chart.Right + 58, legendY + 10), ResourceRecipes.ColorOf(type));
                legendY += 26;
            }

            if (statistics.Samples.Count == 0)
            {
                DrawAxes(chart, 0, 1, 0, 1);
                TextCentered($"Collecting data, the first sample comes after {Statistics.SampleInterval:0} s of play",
                    chart.Center.ToVector2(), TextColor * 0.5f);
                return;
            }

            long max = Math.Max(1, statistics.Samples.Max(s => types.Select(type => counts(s)[type]).DefaultIfEmpty(0).Max()));
            long top = DrawAxes(chart, 0, max, statistics.StartTime, statistics.EndTime);
            foreach (int type in types)
            {
                var points = statistics.Samples.Select((s, i) => (statistics.SampleEnd(i), (float)counts(s)[type])).ToList();
                DrawSeries(chart, points, 0, top, statistics.StartTime, statistics.EndTime, ResourceRecipes.ColorOf(type));
            }
        }

        // how many deliveries transporters finished in every sample
        private void DrawTrips()
        {
            var statistics = transport.Statistics;
            Rectangle chart = ChartArea("Trips taken", $"per {Statistics.SampleInterval:0} s", false);
            TextRight($"Total {statistics.Totals.Trips:N0} trips", new Vector2(chart.Right, chart.Y - 22), transport.TransporterColor);

            if (statistics.Samples.Count == 0)
            {
                DrawAxes(chart, 0, 1, 0, 1);
                TextCentered($"Collecting data, the first sample comes after {Statistics.SampleInterval:0} s of play",
                    chart.Center.ToVector2(), TextColor * 0.5f);
                return;
            }

            long top = DrawAxes(chart, 0, Math.Max(1, statistics.Samples.Max(s => s.Trips)), statistics.StartTime, statistics.EndTime);
            var points = statistics.Samples.Select((s, i) => (statistics.SampleEnd(i), (float)s.Trips)).ToList();
            DrawSeries(chart, points, 0, top, statistics.StartTime, statistics.EndTime, transport.TransporterColor);
        }

        // the points total over the kept samples
        private void DrawPoints()
        {
            var statistics = transport.Statistics;
            Rectangle chart = ChartArea("Points", "total", false);

            if (statistics.Samples.Count == 0)
            {
                TextRight($"Total {statistics.StartPoints:N0}", new Vector2(chart.Right, chart.Y - 22), Color.Gold);
                DrawAxes(chart, 0, 1, 0, 1);
                TextCentered($"Collecting data, the first sample comes after {Statistics.SampleInterval:0} s of play",
                    chart.Center.ToVector2(), TextColor * 0.5f);
                return;
            }

            // like the other charts, only finished samples: it changes once every sample, not every frame
            var points = new List<(float Time, float Value)> { (statistics.StartTime, statistics.StartPoints) };
            points.AddRange(statistics.Samples.Select((s, i) => (statistics.SampleEnd(i), (float)s.Points)));
            float duration = statistics.EndTime - statistics.StartTime;

            long total = statistics.Samples[^1].Points, earned = total - statistics.StartPoints;
            TextRight($"Total {total:N0}   |   earned {earned:N0} in {FormatTime(duration)}   |   {earned / duration * 60f:N0} per minute",
                new Vector2(chart.Right, chart.Y - 22), Color.Gold);

            long min = points.Min(p => (long)p.Value), max = points.Max(p => (long)p.Value);
            // zoom in when the total doesn't change much; go below zero when penalties took it there
            long bottom = min > 0 && min * 2 > max ? NiceFloor(min) : min < 0 ? -NiceCeiling(-min) : 0;
            long top = DrawAxes(chart, bottom, Math.Max(max, bottom + 1), statistics.StartTime, statistics.EndTime);
            DrawSeries(chart, points, bottom, top, statistics.StartTime, statistics.EndTime, Color.Gold);
        }

        // the plot area under a title, leaving room for the value labels on the left and, when asked, a legend on the right
        private Rectangle ChartArea(string title, string unit, bool legend)
        {
            Text(title, new Vector2(content.X, content.Y), Color.White);
            Text(unit, new Vector2(content.X, content.Y + font.LineSpacing + 2), TextColor * 0.5f);
            int left = content.X + 70, top = content.Y + font.LineSpacing * 2 + 18;
            int right = content.Right - (legend ? LegendWidth : 10);
            return new Rectangle(left, top, right - left, content.Bottom - 30 - top);
        }

        // Grid lines with values on the left and times (since the game started) along the bottom, from start to end;
        // returns the value at the top of the chart.
        private long DrawAxes(Rectangle chart, long bottom, long max, float start, float end)
        {
            float duration = end - start;
            const int steps = 4;
            long step = NiceCeiling(Math.Max(1, (max - bottom + steps - 1) / steps));
            long top = bottom + step * steps;
            for (int i = 0; i <= steps; i++)
            {
                float y = chart.Bottom - chart.Height * i / (float)steps;
                DrawShape.DrawLine(new Vector2(chart.Left, y), new Vector2(chart.Right, y), i == 0 ? TextColor * 0.5f : GridColor * 0.6f, 1f);
                TextRight((bottom + step * i).ToString("N0"), new Vector2(chart.Left - 8, y), TextColor * 0.6f);
            }
            DrawShape.DrawLine(new Vector2(chart.Left, chart.Top), new Vector2(chart.Left, chart.Bottom), TextColor * 0.5f, 1f);

            // a time label every "nice" interval, about every 120 pixels
            float[] intervals = { 10, 30, 60, 120, 300, 600, 1200, 1800, 3600, 7200 };
            float every = intervals.FirstOrDefault(s => chart.Width * s / duration >= 120f, intervals[^1]);
            for (float t = MathF.Ceiling(start / every - 0.001f) * every; t <= end + 0.01f; t += every)
            {
                float x = chart.Left + chart.Width * (t - start) / duration;
                DrawShape.DrawLine(new Vector2(x, chart.Bottom), new Vector2(x, chart.Bottom + 5), TextColor * 0.5f, 1f);
                TextCentered(FormatTime(t), new Vector2(x, chart.Bottom + 16), TextColor * 0.6f);
            }
            return top;
        }

        private static void DrawSeries(Rectangle chart, List<(float Time, float Value)> points, long bottom, long top, float start, float end, Color color)
        {
            Vector2 At((float Time, float Value) p) =>
                new Vector2(chart.Left + chart.Width * (p.Time - start) / (end - start), chart.Bottom - chart.Height * (p.Value - bottom) / (top - bottom));

            for (int i = 1; i < points.Count; i++)
                DrawShape.DrawLine(At(points[i - 1]), At(points[i]), color, 2f);
            // dots while there are few enough to tell apart
            if (points.Count <= chart.Width / 12)
                foreach (var p in points) DrawShape.DrawFilledCircle(At(p), 3f, 0, color, color);
        }

        // the smallest 1, 2 or 5 times a power of ten that is at least value
        private static long NiceCeiling(long value)
        {
            long power = 1;
            while (true)
            {
                foreach (long m in new long[] { 1, 2, 5 })
                    if (m * power >= value) return m * power;
                power *= 10;
            }
        }

        // the largest 1, 2 or 5 times a power of ten that is at most value
        private static long NiceFloor(long value)
        {
            long nice = 1;
            while (NiceCeiling(nice + 1) <= value) nice = NiceCeiling(nice + 1);
            return nice;
        }

        private static string FormatTime(float seconds)
        {
            int total = (int)MathF.Round(seconds);
            return total >= 3600 ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}" : $"{total / 60}:{total % 60:00}";
        }

        #endregion
    }
}
