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
        private Font _emojiFont, _fibLabelFont, _dateFont, _starFont, _asteriskFont;
        private StringFormat _stringFormat;
        private SolidBrush _dateBrush;

        //–– Historical data
        private HistoricalData _hoursHistory, _minuteHistory;
        private readonly List<HourBar> _hourBars = new();
        private readonly TimeZoneInfo _estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        //–– Cached session boxes
        private List<SessionBox> _allBoxes;

        //–– Inputs
        [InputParameter("History Lookback (days)", 1)]
        public int HistoryLookbackDays { get; set; } = 4;

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

        [InputParameter("Bullish Inside Bar Color", 14)] public Color InsideBullColor { get; set; } = Color.DodgerBlue;
        [InputParameter("Bearish Inside Bar Color", 15)] public Color InsideBearColor { get; set; } = Color.Orange;

        [InputParameter("Max Unmitigated Boxes", 16)] public int MaxUnmitigatedBoxes { get; set; } = 5;
        [InputParameter("Max Mitigated Boxes", 17)] public int MaxMitigatedBoxes { get; set; } = 0;

        [InputParameter("Use Star Marker (otherwise fill)", 18)]
        public bool UseStarMarker { get; set; } = true;
        public const string Star2 = "✱";

        [InputParameter("Show Fib Levels", 19)]
        public bool ShowFibs { get; set; } = true;

        [InputParameter("Morning Box Label", 20)]
        public string MorningLabel { get; set; } = "9";

        [InputParameter("Afternoon Box Label", 21)]
        public string AfternoonLabel { get; set; } = "3";

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
            base.OnInit();

            // init fonts & brushes
            _asteriskFont = new Font("Segoe UI Emoji", 8, FontStyle.Bold);
            _emojiFont = new Font("Segoe UI Emoji", 12, FontStyle.Bold);
            _starFont = new Font("Segoe UI Emoji", 8, FontStyle.Bold);
            _fibLabelFont = new Font("Segoe UI", 8, FontStyle.Bold);
            _dateFont = new Font("Segoe UI", 8, FontStyle.Bold);
            _stringFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            _dateBrush = new SolidBrush(Color.White);

            // fetch histories + build all boxes
            ReloadHistory();
            BuildSessionBoxes();

            // re-build on user-changes
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
            //ReloadHistory();
            BuildSessionBoxes();
            Refresh();
        }

        protected override void OnSettingsUpdated()
        {
            base.OnSettingsUpdated();
            //ReloadHistory();
            BuildSessionBoxes();
            Refresh();
        }

        public override IList<SettingItem> Settings
        {
            get => base.Settings;
            set
            {
                base.Settings = value;
                if (value.TryGetValue("History Lookback (days)", out int hl))
                    HistoryLookbackDays = Math.Clamp(hl, 1, 365);

                //ReloadHistory();
                BuildSessionBoxes();
                Refresh();
            }
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            base.OnPaintChart(args);
            if (CurrentChart == null) return;

            var gfx = args.Graphics;
            var conv = CurrentChart.MainWindow.CoordinatesConverter;
            var rightUtc = conv.GetTime(CurrentChart.MainWindow.ClientRectangle.Right);

            // build minute‐bar array for inside-bar logic
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

            // update today’s sessions in _allBoxes
            var today = TimeZoneInfo.ConvertTime(DateTime.UtcNow, _estZone).Date;
            foreach (var sb in _allBoxes.Where(b => b.Date == today && !b.Mitigated))
            {
                var live = _minuteHistory
                    .OfType<HistoryItemBar>()
                    .Where(hb =>
                    {
                        var est = TimeZoneInfo.ConvertTime(hb.TimeLeft, TimeZoneInfo.Utc, _estZone);
                        return est.Date == today
                            && est.TimeOfDay >= GetDefs().First(d => d.Key == sb.Key).Start
                            && est.TimeOfDay < GetDefs().First(d => d.Key == sb.Key).End;
                    });

                if (live.Any())
                {
                    sb.High = Math.Max(sb.High, live.Max(hb => hb.High));
                    sb.Low = Math.Min(sb.Low, live.Min(hb => hb.Low));
                }
            }

            // choose which boxes to draw
            var unmit = _allBoxes
                .Where(b => !b.Mitigated)
                .OrderByDescending(b => b.Date)
                .Take(MaxUnmitigatedBoxes);

            var mit = _allBoxes
                .Where(b => b.Mitigated)
                .OrderByDescending(b => b.Date)
                .Take(MaxMitigatedBoxes);

            var toDraw = unmit
                .Concat(mit)
                .OrderBy(b => b.Date)
                .ThenBy(b => b.Key);

            var defs = GetDefs();
            foreach (var sb in toDraw)
                DrawSession(sb, defs, gfx, conv, rightUtc, bars);
        }

        private void ReloadHistory()
        {
            _hoursHistory = Symbol.GetHistory(
                Period.HOUR1,
                Symbol.HistoryType,
                DateTime.UtcNow.AddDays(-HistoryLookbackDays)
            );
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

        private void BuildSessionBoxes()
        {
            var defs = GetDefs();
            _allBoxes = new List<SessionBox>();

            foreach (var d in defs)
            {
                if (!d.IsEnabled()) continue;

                var buckets = new Dictionary<DateTime, List<HourBar>>();
                foreach (var h in _hourBars)
                {
                    var tod = h.Est.TimeOfDay;
                    if (tod < d.Start || tod >= d.End) continue;
                    var date = h.Est.Date;
                    if (!buckets.TryGetValue(date, out var list))
                        buckets[date] = list = new List<HourBar>();
                    list.Add(h);
                }

                foreach (var kv in buckets)
                    _allBoxes.Add(MakeSessionBox(kv.Key, kv.Value, d));
            }
        }

        private SessionDef[] GetDefs() => new[]
        {
            new SessionDef("Morning",   MorningLabel,   _morningStart,   _morningEnd,   () => ShowMorningBox,   Color.Yellow),
            new SessionDef("Afternoon", AfternoonLabel, _afternoonStart, _afternoonEnd, () => ShowAfternoonBox, Color.CornflowerBlue)
        };

        private SessionBox MakeSessionBox(DateTime date, List<HourBar> grp, SessionDef d)
        {
            double high = double.MinValue, low = double.MaxValue;
            foreach (var h in grp)
            {
                if (h.High > high) high = h.High;
                if (h.Low < low) low = h.Low;
            }

            HourBar brkBar = null, mitBar = null;
            foreach (var h in _hourBars)
            {
                if (h.Est > date.Add(d.End) && (h.Close > high || h.Close < low))
                { brkBar = h; break; }
            }

            bool up = brkBar != null && brkBar.Close > high;
            bool down = brkBar != null && brkBar.Close < low;
            var breakUtc = brkBar?.Utc ?? DateTime.MinValue;

            if (brkBar != null)
            {
                foreach (var h in _hourBars)
                {
                    if (h.Utc <= breakUtc) continue;
                    if ((up && h.Close < low) ||
                        (down && h.Close > high))
                    { mitBar = h; break; }
                }
            }

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

            // detect true breakout on minute bars
            DateTime breakoutUtc = sb.BreakUtc;
            foreach (var c in bars)
            {
                if (c.Utc > sb.StartUtc && (c.Close > sb.High || c.Close < sb.Low))
                {
                    breakoutUtc = c.Utc;
                    break;
                }
            }

            // inside-bar drawing
            for (int j = 1; j < bars.Length - 1; j++)
            {
                var cur = bars[j];
                var prev = bars[j - 1];
                var next = bars[j + 1];

                if ((!sb.BrokeAbove && !sb.BrokeBelow)
                    || cur.Utc <= sb.BreakUtc
                    || (sb.Mitigated && cur.Utc >= sb.MitigationUtc)
                    || cur.High >= sb.High
                    || cur.Low <= sb.Low)
                    continue;

                if (cur.High < prev.High && cur.Low > prev.Low)
                {
                    var ibCol = cur.Close > cur.Open ? InsideBullColor : InsideBearColor;
                    using var brush = new SolidBrush(Color.FromArgb(100, ibCol));

                    float barPx = (next.X - prev.X) * 0.5f;
                    float shiftPx = barPx * 0.40f;

                    if (UseStarMarker)
                    {
                        float xM = cur.X + shiftPx;
                        float yM = (float)conv.GetChartY(cur.High) - _asteriskFont.Height - 2f;
                        gfx.DrawString(Star2, _asteriskFont, brush, xM, yM, _stringFormat);
                    }
                    else
                    {
                        float bx1 = cur.X - barPx + shiftPx;
                        float bx2 = cur.X + barPx + shiftPx;
                        float yHigh = (float)conv.GetChartY(cur.High);
                        float yLow = (float)conv.GetChartY(cur.Low);
                        gfx.FillRectangle(brush, bx1, yHigh, bx2 - bx1, yLow - yHigh);
                    }
                }
            }

            // Fibonacci levels
            if (ShowFibs)
            {
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
            }

            // Emoji / Number & date
            using (var ebr = new SolidBrush(def.EmojiColor))
                gfx.DrawString(def.Label, _emojiFont, ebr, x1 + 5, y1 - 20, _stringFormat);
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
            public DateTime Utc;
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
