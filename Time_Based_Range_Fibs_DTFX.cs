// Copyright QUANTOWER LLC. © 2017-2025. All rights reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
            Description = "Session boxes with mitigation, optional fibs, time-labels & date.";
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

            ReloadHistory();     // loads raw UTC into _hoursHistory
            BuildHourBars();     // converts TimeLeft→UTC & EST once
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
            // fetch only UTC timestamps here
            _hoursHistory = Symbol.GetHistory(
                Period.HOUR1,
                Symbol.HistoryType,
                DateTime.UtcNow.AddDays(-HistoryLookbackDays)
            );
        }

        private void BuildHourBars()
        {
            _hourBars.Clear();
            foreach (HistoryItem item in _hoursHistory)
            {
                if (item is HistoryItemBar hb)
                {
                    // convert once: store both Utc and EstTime
                    _hourBars.Add(new HourBar
                    {
                        Utc = hb.TimeLeft,
                        EstTime = TimeZoneInfo.ConvertTime(hb.TimeLeft, TimeZoneInfo.Utc, _estZone),
                        High = hb.High,
                        Low = hb.Low,
                        Close = hb.Close
                    });
                }
            }
            _hourBars.Sort((a, b) => a.Utc.CompareTo(b.Utc));
        }

        private void BuildSessionBoxes()
        {
            _allBoxes.Clear();

            // bucket hours by EST date (no further conversions)
            var buckets = new Dictionary<DateTime, List<HourBar>>();
            foreach (var h in _hourBars)
            {
                var date = h.EstTime.Date;
                if (!buckets.TryGetValue(date, out var list))
                    buckets[date] = list = new List<HourBar>();
                list.Add(h);
            }

            // process most recent N days
            var dates = new List<DateTime>(buckets.Keys);
            dates.Sort((a, b) => b.CompareTo(a));  // descending
            int used = 0;
            foreach (var date in dates)
            {
                if (used++ >= HistoryLookbackDays) break;
                var list = buckets[date];
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
            // find session high/low by ESTTime
            foreach (var h in bars)
            {
                var tod = h.EstTime.TimeOfDay;
                if (tod >= start && tod < end)
                {
                    if (h.High > high) high = h.High;
                    if (h.Low < low) low = h.Low;
                }
            }
            if (high == double.MinValue) return;

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

            // breakout & mitigation using precomputed _hourBars
            for (int i = 0; i < _hourBars.Count; i++)
            {
                var h = _hourBars[i];
                if (h.Utc <= sb.EndUtc) continue;
                if (h.Close > high || h.Close < low)
                {
                    sb.BrokeAbove = (h.Close > high);
                    sb.BrokeBelow = (h.Close < low);
                    sb.BreakUtc = h.Utc;
                    for (int j = i + 1; j < _hourBars.Count; j++)
                    {
                        var m = _hourBars[j];
                        if ((sb.BrokeAbove && m.Close < low) ||
                            (sb.BrokeBelow && m.Close > high))
                        {
                            sb.Mitigated = true;
                            sb.MitigationUtc = m.Utc;
                            break;
                        }
                    }
                    break;
                }
            }

            _allBoxes.Add(sb);
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            var gfx = args.Graphics;
            var conv = CurrentChart.MainWindow.CoordinatesConverter;
            var rightUtc = conv.GetTime(CurrentChart.MainWindow.ClientRectangle.Right);

            // pick unmitigated then mitigated (manual, no LINQ)
            int maxUn = Math.Min(MaxUnmitigatedBoxes, _allBoxes.Count);
            int maxMi = Math.Min(MaxMitigatedBoxes, _allBoxes.Count);
            var buffer = new SessionBox[maxUn + maxMi];
            int idx = 0;
            for (int i = 0; i < _allBoxes.Count && idx < maxUn; i++)
                if (!_allBoxes[i].Mitigated) buffer[idx++] = _allBoxes[i];
            for (int i = 0; i < _allBoxes.Count && idx < buffer.Length; i++)
                if (_allBoxes[i].Mitigated) buffer[idx++] = _allBoxes[i];

            // insertion‐sort by date ascending
            for (int i = 1; i < idx; i++)
            {
                var tmp = buffer[i];
                int j = i - 1;
                while (j >= 0 && buffer[j].Date > tmp.Date)
                {
                    buffer[j + 1] = buffer[j];
                    j--;
                }
                buffer[j + 1] = tmp;
            }

            // draw each
            for (int k = 0; k < idx; k++)
            {
                var sb = buffer[k];
                DateTime boxEnd = sb.Mitigated ? sb.MitigationUtc : rightUtc;
                float x1 = (float)conv.GetChartX(sb.StartUtc),
                      x2 = (float)conv.GetChartX(boxEnd),
                      y1 = (float)conv.GetChartY(sb.High),
                      y2 = (float)conv.GetChartY(sb.Low);

                Color col = sb.BrokeAbove ? BullBoxColor
                          : sb.BrokeBelow ? BearBoxColor
                          : sb.DefaultColor;

                using var fill = new SolidBrush(Color.FromArgb(60, col));
                using var pen = new Pen(col, 1);
                gfx.FillRectangle(fill, x1, y1, x2 - x1, y2 - y1);
                gfx.DrawRectangle(pen, x1, y1, x2 - x1, y2 - y1);

                Color lblColor = sb.Label == MorningLabel ? Color.Yellow : Color.CornflowerBlue;
                using var lblBrush = new SolidBrush(lblColor);
                gfx.DrawString(sb.Label, _labelFont, lblBrush, x1 + 5, y1 - 20, _stringFormat);
                gfx.DrawString(sb.Date.ToString("MM/dd"), _dateFont, Brushes.White,
                               x1 + 5, y1 - 20 + _labelFont.Height + 2, _stringFormat);

                if (ShowFibs)
                {
                    double range = sb.High - sb.Low;
                    for (int f = 0; f < _fibPcts.Length; f++)
                    {
                        bool show = (f == 0 && ShowThirty)
                                 || (f == 1 && ShowFifty)
                                 || (f == 2 && ShowSeventy);
                        if (!show) continue;

                        float yF = (float)conv.GetChartY(sb.High - _fibPcts[f] * range);
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
                        DrawFibLabel(gfx, $"{(int)(_fibPcts[f] * 100)}%", x1 + 2, yF);
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

        private class HourBar
        {
            public DateTime Utc, EstTime;  // both stored up front
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
