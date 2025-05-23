// Copyright QUANTOWER LLC. © 2017-2025. All rights reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Chart;    // for IChartWindowCoordinatesConverter & SettingItemDateTime
using TradingPlatform.BusinessLayer.Utils;

namespace Time_Based_Range_Fibs_DTFX
{
    public class Time_Based_Range_Fibs_DTFX : Indicator
    {
        //── Fonts & drawing helpers ───────────────────────────────────────────────
        private Font _emojiFont, _fibLabelFont, _dateFont, _asteriskFont;
        private StringFormat _stringFormat;
        private SolidBrush _dateBrush;

        //── Cached histories ──────────────────────────────────────────────────────
        private HistoricalData _hoursHistory, _minuteHistory;
        private readonly List<HourBar> _hourBars = new();
        private readonly TimeZoneInfo _estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        //── Shared fib percentages ────────────────────────────────────────────────
        private static readonly double[] _fibPcts = { 0.3, 0.5, 0.7 };

        //── Inputs ─────────────────────────────────────────────────────────────────
        [InputParameter("History Lookback (days)", 1)]
        public int HistoryLookbackDays { get; set; } = 5;

        [InputParameter("Morning Session Start Time", 2)]
        private TimeSpan _morningStart = new(9, 0, 0);
        [InputParameter("Morning Session End Time", 3)]
        private TimeSpan _morningEnd = new(10, 0, 0);
        [InputParameter("Afternoon Session Start Time", 4)]
        private TimeSpan _afternoonStart = new(15, 0, 0);
        [InputParameter("Afternoon Session End Time", 5)]
        private TimeSpan _afternoonEnd = new(16, 0, 0);

        [InputParameter("Show Morning Box", 6)] public bool ShowMorningBox { get; set; } = true;
        [InputParameter("Show Afternoon Box", 7)] public bool ShowAfternoonBox { get; set; } = true;

        [InputParameter("Show 30% Retracement", 8)] public bool ShowThirty { get; set; } = true;
        [InputParameter("Show 50% Retracement", 9)] public bool ShowFifty { get; set; } = true;
        [InputParameter("Show 70% Retracement", 10)] public bool ShowSeventy { get; set; } = true;

        [InputParameter("Bullish Box Color", 11)] public Color BullBoxColor { get; set; } = Color.LimeGreen;
        [InputParameter("Bearish Box Color", 12)] public Color BearBoxColor { get; set; } = Color.Red;

        [InputParameter("Fib Line Color", 13)]
        public LineOptions FibLineStyle { get; set; } = new()
        {
            Color = Color.Yellow,
            LineStyle = LineStyle.Dash,
            Width = 1,
            WithCheckBox = false
        };

        [InputParameter("Bullish Inside Bar Color", 14)]
        public Color InsideBullColor { get; set; } = Color.DodgerBlue;
        [InputParameter("Bearish Inside Bar Color", 15)]
        public Color InsideBearColor { get; set; } = Color.Orange;

        [InputParameter("Use Star Marker (otherwise fill)", 16)]
        public bool UseStarMarker { get; set; } = true;
        public const string Star2 = "✱";

        [InputParameter("Max Unmitigated Boxes", 17)]
        public int MaxUnmitigatedBoxes { get; set; } = 5;
        [InputParameter("Max Mitigated Boxes", 18)]
        public int MaxMitigatedBoxes { get; set; } = 5;

        public Time_Based_Range_Fibs_DTFX()
        {
            Name = "Time_Based_Range_Fibs_DTFX3";
            Description = "Session-range boxes + fibs + inside-bar coloring";
            SeparateWindow = false;
        }

        protected override void OnInit()
        {
            base.OnInit();

            //── Initialize fonts & formatting ───────────────────────
            _fibLabelFont = new Font("Segoe UI", 8, FontStyle.Bold);
            _emojiFont = new Font("Segoe UI Emoji", 12, FontStyle.Bold);
            _dateFont = new Font("Segoe UI", 8, FontStyle.Bold);
            _asteriskFont = new Font("Segoe UI Emoji", 12, FontStyle.Bold);
            _stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            _dateBrush = new SolidBrush(Color.White);

            //── Load both H1 & M1 histories ─────────────────────────
            ReloadHistory();

            // Re-load on timeframe changes (and other chart‐level settings)
            if (CurrentChart != null)
                CurrentChart.SettingsChanged += Chart_SettingsChanged;
        }

        protected override void OnClear()
        {
            if (CurrentChart != null)
                CurrentChart.SettingsChanged -= Chart_SettingsChanged;
            base.OnClear();
        }

        private void Chart_SettingsChanged(object sender, ChartEventArgs e)
        {
            ReloadHistory();
            Refresh();
        }

        protected override void OnSettingsUpdated()
        {
            base.OnSettingsUpdated();
            ReloadHistory();
            Refresh();
        }

        private void ReloadHistory()
        {
            // 1h history for session highs/lows
            _hoursHistory = Symbol.GetHistory(
                Period.HOUR1,
                Symbol.HistoryType,
                DateTime.UtcNow.AddDays(-HistoryLookbackDays)
            );
            // 1m history for breakout & inside-bar logic
            _minuteHistory = Symbol.GetHistory(
                this.HistoricalData.Aggregation.GetPeriod,
                Symbol.HistoryType,
                DateTime.UtcNow.AddDays(-HistoryLookbackDays)
            );

            _hourBars.Clear();
            foreach (var item in _hoursHistory)
                if (item is HistoryItemBar bar)
                    _hourBars.Add(new HourBar
                    {
                        Utc = bar.TimeLeft,
                        Est = TimeZoneInfo.ConvertTime(bar.TimeLeft, TimeZoneInfo.Utc, _estZone),
                        High = bar.High,
                        Low = bar.Low,
                        Close = bar.Close
                    });
            _hourBars.Sort((a, b) => a.Utc.CompareTo(b.Utc));
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            base.OnPaintChart(args);
            if (CurrentChart == null) return;

            var gfx = args.Graphics;
            var conv = CurrentChart.MainWindow.CoordinatesConverter;
            var rightEdge = conv.GetTime(CurrentChart.MainWindow.ClientRectangle.Right);

            // 1) Build true 1-minute bars for breakout/inside-bar logic
            var bars = _minuteHistory
                .Cast<HistoryItemBar>()
                .Select(hb => new ChartBar
                {
                    Utc = hb.TimeLeft,
                    X = (float)conv.GetChartX(hb.TimeLeft),
                    High = hb.High,
                    Low = hb.Low,
                    Open = hb.Open,
                    Close = hb.Close
                })
                .ToArray();

            // 2) Define session windows
            var defs = new[] {
                new SessionDef("Morning",   "☀️", _morningStart,   _morningEnd,   () => ShowMorningBox,   Color.Yellow),
                new SessionDef("Afternoon", "🏧", _afternoonStart, _afternoonEnd, () => ShowAfternoonBox, Color.CornflowerBlue)
            };

            // 3) Build session boxes from H1 bars
            var sessions = defs
                .Where(d => d.IsEnabled())
                .SelectMany(d => _hourBars
                    .Where(h => h.Est.TimeOfDay >= d.Start && h.Est.TimeOfDay < d.End)
                    .GroupBy(h => h.Est.Date)
                    .Select(g => MakeSessionBox(g.Key, g.ToList(), d))
                )
                .ToList();

            // 4) Limit & sort
            var unmit = sessions.Where(s => !s.Mitigated)
                                .OrderByDescending(s => s.Date)
                                .Take(MaxUnmitigatedBoxes);
            var mit = sessions.Where(s => s.Mitigated)
                                .OrderByDescending(s => s.Date)
                                .Take(MaxMitigatedBoxes);
            var toDraw = unmit.Concat(mit)
                              .OrderBy(s => s.Date)
                              .ThenBy(s => s.Key);

            // 5) Draw each session
            foreach (var sb in toDraw)
            {
                // a) session box
                float x1 = (float)conv.GetChartX(sb.StartUtc),
                      x2 = (float)conv.GetChartX(sb.Mitigated ? sb.MitigationUtc : rightEdge),
                      y1 = (float)conv.GetChartY(sb.High),
                      y2 = (float)conv.GetChartY(sb.Low);
                var boxCol = sb.BrokeAbove ? BullBoxColor
                           : sb.BrokeBelow ? BearBoxColor
                           : Color.Gray;
                using (var br = new SolidBrush(Color.FromArgb(60, boxCol)))
                    gfx.FillRectangle(br, x1, y1, x2 - x1, y2 - y1);
                using (var pen = new Pen(boxCol, 1))
                    gfx.DrawRectangle(pen, x1, y1, x2 - x1, y2 - y1);

                // b) optimized inside-bar after-breakout
                var sessionBars = bars
                    .Where(b => b.Utc > sb.BreakUtc
                             && b.Utc < (sb.Mitigated ? sb.MitigationUtc : rightEdge))
                    .ToArray();

                int breakIdx = Array.FindIndex(sessionBars,
                    b => sb.BrokeAbove ? b.Close > sb.High : b.Close < sb.Low);
                if (breakIdx >= 0)
                {
                    int mitIdx = -1;
                    if (sb.Mitigated)
                        mitIdx = Array.FindIndex(sessionBars, breakIdx + 1,
                            b => sb.BrokeAbove ? b.Close < sb.Low : b.Close > sb.High);

                    int endIdx = ((mitIdx > 0 ? mitIdx : sessionBars.Length) - 1);
                    for (int j = breakIdx + 1; j <= endIdx; j++)
                    {
                        var cur = sessionBars[j];
                        var prev = sessionBars[j - 1];

                        // strictly inside previous bar
                        if (cur.High >= prev.High || cur.Low <= prev.Low)
                            continue;

                        var ibCol = cur.Close > cur.Open ? InsideBullColor : InsideBearColor;
                        if (UseStarMarker)
                        {
                            float barPx = (sessionBars[j + 1].X - prev.X) * 0.5f;
                            float shiftPx = barPx * 0.4f;
                            float xMark = cur.X + shiftPx;
                            float yMark = (float)conv.GetChartY(cur.High) - _asteriskFont.Height - 2f;
                            using var bsh = new SolidBrush(Color.FromArgb(100, ibCol));
                            gfx.DrawString(Star2, _asteriskFont, bsh, xMark, yMark, _stringFormat);
                        }
                        else
                        {
                            float barPx = (sessionBars[j + 1].X - prev.X) * 0.5f;
                            float shiftPx = barPx * 0.4f;
                            float bx1 = cur.X - barPx + shiftPx,
                                  bx2 = cur.X + barPx + shiftPx;
                            float yHigh = (float)conv.GetChartY(cur.High),
                                  yLow = (float)conv.GetChartY(cur.Low);
                            using var bsh = new SolidBrush(Color.FromArgb(100, ibCol));
                            gfx.FillRectangle(bsh, bx1, yHigh, bx2 - bx1, yLow - yHigh);
                        }
                    }
                }

                // c) Fibonacci levels
                double range = sb.High - sb.Low;
                foreach (var pct in _fibPcts)
                {
                    bool show = (pct == 0.3 && ShowThirty)
                             || (pct == 0.5 && ShowFifty)
                             || (pct == 0.7 && ShowSeventy);
                    if (!show) continue;

                    float yF = (float)conv.GetChartY(sb.High - range * pct);
                    using var fpen = new Pen(FibLineStyle.Color, FibLineStyle.Width)
                    { DashStyle = ConvertLineStyleToDashStyle(FibLineStyle.LineStyle) };
                    gfx.DrawLine(fpen, x1, yF, x2, yF);
                    DrawFibLabel(gfx, $"{(int)(pct * 100)}%", x1 + 2, yF);
                }

                // d) emoji & date
                using (var ebr = new SolidBrush(sb.EmojiColor))
                    gfx.DrawString(sb.Label, _emojiFont, ebr, x1 + 5, y1 - 20, _stringFormat);
                gfx.DrawString(sb.Date.ToString("MM/dd"), _dateFont, _dateBrush,
                               x1 + 5, y1 - 20 + _emojiFont.Height + 2, _stringFormat);
            }
        }

        private SessionBox MakeSessionBox(DateTime date, List<HourBar> sessionBars, SessionDef d)
        {
            double high = sessionBars.Max(x => x.High),
                   low = sessionBars.Min(x => x.Low);

            var brk = _hourBars.FirstOrDefault(h =>
                h.Est.Date == date
                && h.Est.TimeOfDay > d.End
                && (h.Close > high || h.Close < low));
            bool up = brk?.Close > high;
            bool down = brk?.Close < low;
            DateTime breakUtc = brk?.Utc ?? DateTime.MinValue;

            var mit = (up || down)
                ? _hourBars.SkipWhile(h => h.Utc <= breakUtc)
                           .FirstOrDefault(h => up ? h.Close < low : h.Close > high)
                : null;
            bool mitigated = mit != null;
            DateTime mitUtc = mit?.Utc ?? DateTime.MinValue;

            return new SessionBox
            {
                Date = date,
                Key = d.Key,
                Label = d.Label,
                High = high,
                Low = low,
                BrokeAbove = up,
                BrokeBelow = down,
                Mitigated = mitigated,
                StartUtc = TimeZoneInfo.ConvertTimeToUtc(date.Add(d.Start), _estZone),
                BreakUtc = breakUtc,
                MitigationUtc = mitigated ? mitUtc : DateTime.MinValue,
                EmojiColor = d.EmojiColor
            };
        }

        private DashStyle ConvertLineStyleToDashStyle(LineStyle ls) => ls switch
        {
            LineStyle.Solid => DashStyle.Solid,
            LineStyle.Dash => DashStyle.Dash,
            LineStyle.Dot => DashStyle.Dot,
            LineStyle.DashDot => DashStyle.DashDot,
            _ => DashStyle.Solid
        };

        private void DrawFibLabel(Graphics g, string text, float x, float y)
        {
            const int pad = 4, rad = 6;
            var sz = g.MeasureString(text, _fibLabelFont);
            var rect = new RectangleF(x, y - sz.Height / 2, sz.Width + pad * 2, sz.Height);
            using var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, rad, rad, 180, 90);
            path.AddArc(rect.Right - rad, rect.Top, rad, rad, 270, 90);
            path.AddArc(rect.Right - rad, rect.Bottom - rad, rad, rad, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - rad, rad, rad, 90, 90);
            path.CloseFigure();
            using (var bg = new SolidBrush(Color.Gold)) g.FillPath(bg, path);
            using (var p = new Pen(Color.Gold)) g.DrawPath(p, path);
            g.DrawString(text, _fibLabelFont, Brushes.Black, x + pad, y - sz.Height / 2);
        }

        private class SessionDef
        {
            public string Key, Label;
            public TimeSpan Start, End;
            public Func<bool> IsEnabled;
            public Color EmojiColor;
            public SessionDef(string key, string label, TimeSpan start, TimeSpan end, Func<bool> isEnabled, Color emojiColor)
            {
                Key = key;
                Label = label;
                Start = start;
                End = end;
                IsEnabled = isEnabled;
                EmojiColor = emojiColor;
            }
        }

        private class SessionBox
        {
            public DateTime Date, StartUtc, BreakUtc, MitigationUtc;
            public string Key, Label;
            public double High, Low;
            public bool BrokeAbove, BrokeBelow, Mitigated;
            public Color EmojiColor;
        }

        private class HourBar
        {
            public DateTime Utc, Est;
            public double High, Low, Close;
        }

        private class ChartBar
        {
            public DateTime Utc;
            public float X;
            public double High, Low, Open, Close;
        }
    }
}

