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
        //–– Fonts & formatting
        private Font _emojiFont, _fibLabelFont, _dateFont, _starFont;

        private StringFormat _stringFormat;
        private SolidBrush _dateBrush;

        //–– Hourly history (session high/low)
        private HistoricalData _hoursHistory;
        private readonly List<HourBar> _hourBars = new();
        private readonly TimeZoneInfo _estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        //–– Inputs
        [InputParameter("History Lookback (days)", 1)]
        public int HistoryLookbackDays { get; set; } = 4;
        // right below your other inputs:
        

        [InputParameter("Morning Session Start Time", 2)] private TimeSpan _morningStart = new(9, 0, 0);
        [InputParameter("Morning Session End Time", 3)] private TimeSpan _morningEnd = new(10, 0, 0);
        [InputParameter("Afternoon Session Start Time", 4)] private TimeSpan _afternoonStart = new(15, 0, 0);
        [InputParameter("Afternoon Session End Time", 5)] private TimeSpan _afternoonEnd = new(16, 0, 0);

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
        public Color InsideBullColor { get; set; } = Color.LimeGreen;
        [InputParameter("Bearish Inside Bar Color", 15)]
        public Color InsideBearColor { get; set; } = Color.Red;

        [InputParameter("Max Unmitigated Boxes", 16)] public int MaxUnmitigatedBoxes { get; set; } = 5;
        [InputParameter("Max Mitigated Boxes", 17)] public int MaxMitigatedBoxes { get; set; } = 5;
        // ————————————————————————————————————————————————
        [InputParameter("Use Star Marker (otherwise fill)", 18)]
        public bool UseStarMarker { get; set; } = false;

        // keep the emoji constant
        public const string Star2 = "🌟";

        // ————————————————————————————————————————————————

        //–– Shared fib percentages
        private static readonly double[] _fibPcts = { 0.3, 0.5, 0.7 };

        public Time_Based_Range_Fibs_DTFX()
        {
            Name = "Time_Based_Range_Fibs_DTFX3";
            Description = "Session-range boxes + fibs + inside-bar coloring (only after breakout & re-entry).";
            SeparateWindow = false;
        }

        protected override void OnInit()
        {
            _emojiFont = new Font("Segoe UI Emoji", 12, FontStyle.Bold);
            _starFont = new Font("Segoe UI Emoji", 8, FontStyle.Bold);  // <-- new 8-pt star font
            _fibLabelFont = new Font("Segoe UI", 8, FontStyle.Bold);
            _dateFont = new Font("Segoe UI", 8, FontStyle.Bold);
            _stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            _dateBrush = new SolidBrush(Color.White);

            ReloadHistory();
        }


        public override IList<SettingItem> Settings
        {
            get
            {
                var s = base.Settings;
                var sep = s.FirstOrDefault()?.SeparatorGroup;
                var today = TimeZoneInfo.ConvertTime(DateTime.UtcNow, _estZone).Date;

              

                s.Insert(0, MakePicker("Afternoon Session End Time", today.Add(_afternoonEnd), sep));
                s.Insert(0, MakePicker("Afternoon Session Start Time", today.Add(_afternoonStart), sep));
                s.Insert(0, MakePicker("Morning Session End Time", today.Add(_morningEnd), sep));
                s.Insert(0, MakePicker("Morning Session Start Time", today.Add(_morningStart), sep));

                return s;
            }
            set
            {
                base.Settings = value;
                if (value.TryGetValue("History Lookback (days)", out int hl))
                    HistoryLookbackDays = Math.Clamp(hl, 1, 365);

                foreach (var dt in value.OfType<SettingItemDateTime>())
                {
                    var tod = TimeZoneInfo.ConvertTime((DateTime)dt.Value, _estZone).TimeOfDay;
                    switch (dt.Name)
                    {
                        case "Morning Session Start Time": _morningStart = tod; break;
                        case "Morning Session End Time": _morningEnd = tod; break;
                        case "Afternoon Session Start Time": _afternoonStart = tod; break;
                        case "Afternoon Session End Time": _afternoonEnd = tod; break;
                    }
                }

                ReloadHistory();
                Refresh();
            }
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            base.OnPaintChart(args);
            if (CurrentChart == null) return;

            var gfx = args.Graphics;
            var wnd = CurrentChart.MainWindow;
            var conv = wnd.CoordinatesConverter;
            var rightUtc = conv.GetTime(wnd.ClientRectangle.Right);
            var chartH = this.HistoricalData;

            // 1) Precompute minute bars
            var bars = new ChartBar[chartH.Count];
            for (int i = 0; i < bars.Length; i++)
            {
                var hb = (HistoryItemBar)chartH[i, SeekOriginHistory.Begin];
                var utc = hb.TimeLeft;
                bars[i] = new ChartBar
                {
                    Utc = utc,
                    Est = TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.Utc, _estZone),
                    X = (float)conv.GetChartX(utc),
                    High = hb.High,
                    Low = hb.Low,
                    Open = hb.Open,
                    Close = hb.Close
                };
            }

            // 2) Define sessions
            var defs = new[]
            {
                new SessionDef("Morning",   "☀️", _morningStart,   _morningEnd,   () => ShowMorningBox,   Color.Yellow),
                new SessionDef("Afternoon", "🏧", _afternoonStart, _afternoonEnd, () => ShowAfternoonBox, Color.CornflowerBlue)
            };

            // 3) Build all session boxes
            var sessions = defs
                .Where(d => d.IsEnabled())
                .SelectMany(d => _hourBars
                    .Where(h => h.Est.TimeOfDay >= d.Start && h.Est.TimeOfDay < d.End)
                    .GroupBy(h => h.Est.Date)
                    .Select(g => MakeSessionBox(g.Key, g.ToList(), d))
                )
                .ToList();

            // 4) Apply limits & sort
            var unmit = sessions
                .Where(b => !b.Mitigated)
                .OrderByDescending(b => b.Date)
                .Take(MaxUnmitigatedBoxes);

            var mit = sessions
                .Where(b => b.Mitigated)
                .OrderByDescending(b => b.Date)
                .Take(MaxMitigatedBoxes);

            var toDraw = unmit
                .Concat(mit)
                .OrderBy(b => b.Date)
                .ThenBy(b => b.Key);

            // 5) Draw
            foreach (var sb in toDraw)
                DrawSession(sb, defs, gfx, conv, rightUtc, bars);
        }

        private static SettingItemDateTime MakePicker(string name, DateTime dt, SettingItemSeparatorGroup sep) =>
            new(name, dt) { SeparatorGroup = sep, Format = DatePickerFormat.Time };

        private void ReloadHistory()
        {
            _hoursHistory = Symbol.GetHistory(
                Period.HOUR1,
                Symbol.HistoryType,
                DateTime.UtcNow.AddDays(-HistoryLookbackDays)
            );

            _hourBars.Clear();
            foreach (var item in _hoursHistory)
            {
                if (item is not HistoryItemBar bar) continue;
                var utc = bar.TimeLeft;
                _hourBars.Add(new HourBar
                {
                    Utc = utc,
                    Est = TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.Utc, _estZone),
                    High = bar.High,
                    Low = bar.Low,
                    Close = bar.Close
                });
            }
            _hourBars.Sort((a, b) => a.Utc.CompareTo(b.Utc));
        }

        private SessionBox MakeSessionBox(DateTime date, List<HourBar> grp, SessionDef d)
        {
            double high = grp.Max(x => x.High), low = grp.Min(x => x.Low);

            // Breakout detection (hourly)
            var brk = _hourBars.FirstOrDefault(h =>
                h.Est > date.Add(d.End) && (h.Close > high || h.Close < low)
            );
            bool up = brk?.Close > high;
            bool down = brk?.Close < low;
            var breakUtc = brk?.Utc ?? DateTime.MinValue;

            // Mitigation detection
            var mitBar = (up || down)
                ? _hourBars.SkipWhile(h => h.Utc <= breakUtc)
                           .FirstOrDefault(h => up ? h.Close < low : h.Close > high)
                : null;
            bool mit = mitBar != null;
            var mitUtc = mitBar?.Utc ?? DateTime.MaxValue;

            return new SessionBox
            {
                Date = date,
                Key = d.Key,
                Label = d.Label,
                High = high,
                Low = low,
                BrokeAbove = up,
                BrokeBelow = down,
                Mitigated = mit,
                StartUtc = TimeZoneInfo.ConvertTimeToUtc(date.Add(d.Start), _estZone),
                BreakUtc = breakUtc,
                MitigationUtc = mitUtc
            };
        }

        private void DrawSession(
            SessionBox sb,
            SessionDef[] defs,
            Graphics gfx,
            IChartWindowCoordinatesConverter conv,
            DateTime rightUtc,
            ChartBar[] bars
        )
        {
            var def = defs.First(d => d.Key == sb.Key);

            // Draw session box
            float x1 = (float)conv.GetChartX(sb.StartUtc);
            float x2 = (float)conv.GetChartX(sb.Mitigated ? sb.MitigationUtc : rightUtc);
            float y1 = (float)conv.GetChartY(sb.High);
            float y2 = (float)conv.GetChartY(sb.Low);

            var boxCol = sb.BrokeAbove
                       ? BullBoxColor
                       : sb.BrokeBelow
                       ? BearBoxColor
                       : Color.Gray;
            using (var fill = new SolidBrush(Color.FromArgb(60, boxCol)))
                gfx.FillRectangle(fill, x1, y1, x2 - x1, y2 - y1);
            using (var pen = new Pen(boxCol, 1))
                gfx.DrawRectangle(pen, x1, y1, x2 - x1, y2 - y1);

            // Determine *true* chart‐bar breakout
            DateTime breakoutUtc = sb.BreakUtc;
            for (int k = 0; k < bars.Length; k++)
            {
                var c = bars[k];
                if (c.Utc > sb.StartUtc && (c.Close > sb.High || c.Close < sb.Low))
                {
                    breakoutUtc = c.Utc;
                    break;
                }
            }

            // INSIDE-BAR: either Fill or Star, per InsideBarStyle
            for (int j = 1; j < bars.Length - 1; j++)
            {
                var cur = bars[j];
                var prev = bars[j - 1];
                var next = bars[j + 1];

                // only after breakout & before mitigation, and strictly inside original box
                if ((!sb.BrokeAbove && !sb.BrokeBelow)
                    || cur.Utc <= sb.BreakUtc
                    || (sb.Mitigated && cur.Utc >= sb.MitigationUtc)
                    || cur.High >= sb.High
                    || cur.Low <= sb.Low)
                    continue;

                // strictly inside the previous bar?
                if (cur.High < prev.High && cur.Low > prev.Low)
                {
                    // pick color based on bull/bear
                    var ibCol = cur.Close > cur.Open ? InsideBullColor : InsideBearColor;
                    using var brush = new SolidBrush(Color.FromArgb(100, ibCol));

                    if (UseStarMarker)
                    {
                        // STAR mode
                        // compute the pixel‐width of half a bar & your existing shift
                        float barPx = (next.X - prev.X) * 0.7f;
                        float shiftPx = barPx * 0.40f;

                        // shift the star right by that same amount
                        float xStar = cur.X + shiftPx;
                        float yStar = (float)conv.GetChartY(cur.High)
                                      - _starFont.Height
                                      - 2f;

                        // draw with the smaller star font
                        gfx.DrawString(Star2, _starFont, brush, xStar, yStar, _stringFormat);

                    }
                    else
                    {
                        // FILL mode
                        float barPx = (next.X - prev.X) * 0.5f;
                        float shiftPx = barPx * 0.40f;
                        float bx1 = cur.X - barPx + shiftPx;
                        float bx2 = cur.X + barPx + shiftPx;
                        float yHigh = (float)conv.GetChartY(cur.High);
                        float yLow = (float)conv.GetChartY(cur.Low);
                        gfx.FillRectangle(brush, bx1, yHigh, bx2 - bx1, yLow - yHigh);
                    }

                }
            }


            // Fibonacci levels
            double range = sb.High - sb.Low;
            foreach (var pct in _fibPcts)
            {
                bool show = (pct == 0.3 && ShowThirty)
                         || (pct == 0.5 && ShowFifty)
                         || (pct == 0.7 && ShowSeventy);
                if (!show) continue;

                float yF = (float)conv.GetChartY(sb.High - range * pct);
                using var fpen = new Pen(FibLineStyle.Color, FibLineStyle.Width)
                {
                    DashStyle = FibLineStyle.LineStyle switch
                    {
                        LineStyle.Solid => DashStyle.Solid,
                        LineStyle.Dash => DashStyle.Dash,
                        LineStyle.Dot => DashStyle.Dot,
                        LineStyle.DashDot => DashStyle.DashDot,
                        _ => DashStyle.Solid
                    }
                };
                gfx.DrawLine(fpen, x1, yF, x2, yF);
                DrawFibLabel(gfx, $"{(int)(pct * 100)}%", x1 + 2, yF);
            }

            // Emoji & Date
            using (var ebr = new SolidBrush(def.EmojiColor))
                gfx.DrawString(sb.Label, _emojiFont, ebr, x1 + 5, y1 - 20, _stringFormat);
            gfx.DrawString(
                sb.Date.ToString("MM/dd"),
                _dateFont,
                _dateBrush,
                x1 + 5,
                y1 - 20 + _emojiFont.Height + 2,
                _stringFormat
            );
        }

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

        //── Types ────────────────────────────────────────────────────────────────────────────────────────────

        private class HourBar
        {
            public DateTime Utc, Est;
            public double High, Low, Close;
        }

        private class ChartBar
        {
            public DateTime Utc, Est;
            public float X;
            public double High, Low, Open, Close;
        }

        private class SessionDef
        {
            public string Key, Label;
            public TimeSpan Start, End;
            public Func<bool> IsEnabled;
            public Color EmojiColor;
            public SessionDef(string key, string lbl, TimeSpan st, TimeSpan en, Func<bool> e, Color c)
            {
                Key = key;
                Label = lbl;
                Start = st;
                End = en;
                IsEnabled = e;
                EmojiColor = c;
            }
        }

        private class SessionBox
        {
            public DateTime Date;
            public string Key, Label;
            public double High, Low;
            public bool BrokeAbove, BrokeBelow, Mitigated;
            public DateTime StartUtc, BreakUtc, MitigationUtc;
        }
    }
}
