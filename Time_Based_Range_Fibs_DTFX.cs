// Copyright QUANTOWER LLC. © 2017-2025. All rights reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Xml.Linq;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Chart;
using TradingPlatform.BusinessLayer.Utils;

namespace Time_Based_Range_Fibs_DTFX
{
    public class SessionBoxesWithMitigation : Indicator
    {
        //── Fonts & formatting ───────────────────────────────────────────────────────
        private Font _dateFont;
        private Font _labelFont;
        private StringFormat _stringFormat;

        //── User inputs ─────────────────────────────────────────────────────────────
        [InputParameter("History Lookback (days)", 1)]
        public int HistoryLookbackDays { get; set; } = 10;

        [InputParameter("Show Morning Box", 2)]
        public bool ShowMorningBox { get; set; } = true;
        [InputParameter("Morning Start (EST)", 3)]
        public TimeSpan MorningStart { get; set; } = new(9, 0, 0);
        [InputParameter("Morning End (EST)", 4)]
        public TimeSpan MorningEnd { get; set; } = new(10, 0, 0);
        [InputParameter("Morning Default Color", 5)]
        public Color MorningDefaultColor { get; set; } = Color.FromArgb(60, Color.LimeGreen);

        [InputParameter("Show Afternoon Box", 6)]
        public bool ShowAfternoonBox { get; set; } = true;
        [InputParameter("Afternoon Start (EST)", 7)]
        public TimeSpan AfternoonStart { get; set; } = new(15, 0, 0);
        [InputParameter("Afternoon End (EST)", 8)]
        public TimeSpan AfternoonEnd { get; set; } = new(16, 0, 0);
        [InputParameter("Afternoon Default Color", 9)]
        public Color AfternoonDefaultColor { get; set; } = Color.FromArgb(60, Color.CornflowerBlue);

        [InputParameter("Max Unmitigated Boxes", 10)]
        public int MaxUnmitigatedBoxes { get; set; } = 5;
        [InputParameter("Max Mitigated Boxes", 11)]
        public int MaxMitigatedBoxes { get; set; } = 5;

        [InputParameter("Bullish Break Color", 12)]
        public Color BullBoxColor { get; set; } = Color.LimeGreen;
        [InputParameter("Bearish Break Color", 13)]
        public Color BearBoxColor { get; set; } = Color.Red;

        //── Fibonacci master toggle & inputs ────────────────────────────────────────
        [InputParameter("Show Fibs", 14)]
        public bool ShowFibs { get; set; } = true;
        [InputParameter("Show 30% Retracement", 15)]
        public bool ShowThirty { get; set; } = true;
        [InputParameter("Show 50% Retracement", 16)]
        public bool ShowFifty { get; set; } = true;
        [InputParameter("Show 70% Retracement", 17)]
        public bool ShowSeventy { get; set; } = true;
        [InputParameter("Fib Line Style", 18)]
        public LineOptions FibLineStyle { get; set; } = new LineOptions()
        {
            Color = Color.Yellow,
            LineStyle = LineStyle.Dash,
            Width = 1,
            WithCheckBox = false
        };

        [InputParameter("Morning Box Label", 19)]
        public string MorningLabel { get; set; } = "9";
        [InputParameter("Afternoon Box Label", 20)]
        public string AfternoonLabel { get; set; } = "3";

        private static readonly double[] _fibPcts = { 0.3, 0.5, 0.7 };

        //── Internal storage ─────────────────────────────────────────────────────────
        private HistoricalData _hoursHistory;
        private readonly List<HourBar> _hourBars = new();
        private readonly List<SessionBox> _allBoxes = new();
        private readonly TimeZoneInfo _estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        public SessionBoxesWithMitigation()
        {
            Name = "Time_Based_Range_Fibs_DTFX3";
            Description = "Session boxes with mitigation, optional fibs, time‐labels & date.";
            SeparateWindow = false;
        }

        protected override void OnInit()
        {
            base.OnInit();

            //── fonts & formatting ────────────────────────────────────────────────
            _dateFont = new Font("Segoe UI", 8, FontStyle.Bold);
            _labelFont = new Font("Segoe UI", 8, FontStyle.Bold);
            _stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            ReloadHistory();
            BuildHourBars();
            BuildSessionBoxes();
        }

        protected override void OnSettingsUpdated()
        {
            base.OnSettingsUpdated();
            ReloadHistory();
            BuildHourBars();
            BuildSessionBoxes();
            Refresh();
        }

        private void ReloadHistory()
        {
            _hoursHistory = Symbol.GetHistory(
                Period.HOUR1,
                Symbol.HistoryType,
                DateTime.UtcNow.AddDays(-HistoryLookbackDays)
            );
        }

        private void BuildHourBars()
        {
            _hourBars.Clear();
            foreach (var item in _hoursHistory)
                if (item is HistoryItemBar hb)
                    _hourBars.Add(new HourBar
                    {
                        Utc = hb.TimeLeft,
                        EstTime = TimeZoneInfo.ConvertTime(hb.TimeLeft, TimeZoneInfo.Utc, _estZone),
                        High = hb.High,
                        Low = hb.Low,
                        Close = hb.Close
                    });
            _hourBars.Sort((a, b) => a.Utc.CompareTo(b.Utc));
        }

        private void BuildSessionBoxes()
        {
            _allBoxes.Clear();

            var byDate = _hourBars
                .GroupBy(h => h.EstTime.Date)
                .OrderByDescending(g => g.Key)
                .Take(HistoryLookbackDays);

            foreach (var grp in byDate)
            {
                var date = grp.Key;
                var list = grp.ToList();

                if (ShowMorningBox)
                    TryAddSession(list, date, MorningStart, MorningEnd, MorningDefaultColor, MorningLabel);

                if (ShowAfternoonBox)
                    TryAddSession(list, date, AfternoonStart, AfternoonEnd, AfternoonDefaultColor, AfternoonLabel);
            }
        }

        private void TryAddSession(
            List<HourBar> bars,
            DateTime date,
            TimeSpan start,
            TimeSpan end,
            Color defaultColor,
            string label
        )
        {
            double high = double.MinValue, low = double.MaxValue;
            foreach (var h in bars)
            {
                var tod = h.EstTime.TimeOfDay;
                if (tod >= start && tod < end)
                {
                    high = Math.Max(high, h.High);
                    low = Math.Min(low, h.Low);
                }
            }
            if (high == double.MinValue)
                return;

            var sb = new SessionBox
            {
                Date = date,
                Label = label,
                DefaultColor = defaultColor,
                High = high,
                Low = low,
                StartUtc = TimeZoneInfo.ConvertTimeToUtc(date + start, _estZone),
                EndUtc = TimeZoneInfo.ConvertTimeToUtc(date + end, _estZone)
            };

            // breakout & mitigation
            var after = _hourBars.Where(h => h.Utc > sb.EndUtc);
            var brk = after.FirstOrDefault(h => h.Close > high || h.Close < low);
            if (brk != null)
            {
                sb.BrokeAbove = brk.Close > high;
                sb.BrokeBelow = brk.Close < low;
                sb.BreakUtc = brk.Utc;
                var mit = after
                    .Where(h => h.Utc > sb.BreakUtc)
                    .FirstOrDefault(h => sb.BrokeAbove ? h.Close < low : h.Close > high);
                if (mit != null)
                {
                    sb.Mitigated = true;
                    sb.MitigationUtc = mit.Utc;
                }
            }

            _allBoxes.Add(sb);
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            var gfx = args.Graphics;
            var conv = CurrentChart.MainWindow.CoordinatesConverter;
            var rightUtc = conv.GetTime(CurrentChart.MainWindow.ClientRectangle.Right);

            var unmit = _allBoxes.Where(s => !s.Mitigated)
                                 .OrderByDescending(s => s.Date)
                                 .Take(MaxUnmitigatedBoxes);
            var mit = _allBoxes.Where(s => s.Mitigated)
                                 .OrderByDescending(s => s.Date)
                                 .Take(MaxMitigatedBoxes);

            foreach (var sb in unmit.Concat(mit).OrderBy(s => s.Date))
            {
                // box extents
                DateTime boxEnd = sb.Mitigated ? sb.MitigationUtc : rightUtc;
                float x1 = (float)conv.GetChartX(sb.StartUtc),
                      x2 = (float)conv.GetChartX(boxEnd),
                      y1 = (float)conv.GetChartY(sb.High),
                      y2 = (float)conv.GetChartY(sb.Low);

                // color by breakout
                Color col = sb.BrokeAbove ? BullBoxColor
                          : sb.BrokeBelow ? BearBoxColor
                          : sb.DefaultColor;

                using (var fill = new SolidBrush(Color.FromArgb(60, col)))
                    gfx.FillRectangle(fill, x1, y1, x2 - x1, y2 - y1);
                using (var pen = new Pen(col, 1))
                    gfx.DrawRectangle(pen, x1, y1, x2 - x1, y2 - y1);

                //── draw time‐label in session‐specific color ─────────────────────────
                var lblColor = sb.Label == MorningLabel
                    ? Color.Yellow
                    : Color.CornflowerBlue;
                using var lblBrush = new SolidBrush(lblColor);
                gfx.DrawString(sb.Label, _labelFont, lblBrush, x1 + 5, y1 - 20, _stringFormat);

                // draw date below it
                gfx.DrawString(sb.Date.ToString("MM/dd"), _dateFont, Brushes.White,
                               x1 + 5, y1 - 20 + _labelFont.Height + 2, _stringFormat);

                //── fibs (master toggle) ─────────────────────────────────────────────
                if (ShowFibs)
                {
                    double range = sb.High - sb.Low;
                    foreach (var pct in _fibPcts)
                    {
                        bool show = (pct == 0.3 && ShowThirty)
                                 || (pct == 0.5 && ShowFifty)
                                 || (pct == 0.7 && ShowSeventy);
                        if (!show) continue;

                        float yF = (float)conv.GetChartY(sb.High - pct * range);
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
            }
        }

        private void DrawFibLabel(Graphics g, string text, float x, float y)
        {
            const int pad = 4, rad = 6;
            var sz = g.MeasureString(text, _dateFont);
            var rect = new RectangleF(x, y - sz.Height / 2, sz.Width + pad * 2, sz.Height);
            using var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, rad, rad, 180, 90);
            path.AddArc(rect.Right - rad, rect.Top, rad, rad, 270, 90);
            path.AddArc(rect.Right - rad, rect.Bottom - rad, rad, rad, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - rad, rad, rad, 90, 90);
            path.CloseFigure();
            using var bg = new SolidBrush(Color.Gold); g.FillPath(bg, path);
            using var pen = new Pen(Color.Gold); g.DrawPath(pen, path);
            g.DrawString(text, _dateFont, Brushes.Black, x + pad, y - sz.Height / 2);
        }

        //── Helper types ────────────────────────────────────────────────────────────
        private class HourBar
        {
            public DateTime Utc, EstTime;
            public double High, Low, Close;
        }

        private class SessionBox
        {
            public DateTime Date, StartUtc, EndUtc, BreakUtc, MitigationUtc;
            public double High, Low;
            public bool BrokeAbove, BrokeBelow, Mitigated;
            public Color DefaultColor;
            public string Label;
        }
    }
}
