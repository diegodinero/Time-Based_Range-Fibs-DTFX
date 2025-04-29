// Copyright QUANTOWER LLC. © 2017-2025. All rights reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Chart;    // for SettingItemDateTime
using TradingPlatform.BusinessLayer.Utils;

namespace Time_Based_Range_Fibs_DTFX
{
    public class Time_Based_Range_Fibs_DTFX : Indicator
    {
        //–– Drawing helpers
        private Font emojiFont;
        private Font fibLabelFont;
        private StringFormat stringFormat;
        private HistoricalData hoursHistory;
        private readonly TimeZoneInfo estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        //–– Session times (backed by the DateTime pickers below)
        private TimeSpan MorningStart = new TimeSpan(9, 0, 0);
        private TimeSpan MorningEnd = new TimeSpan(10, 0, 0);
        private TimeSpan AfternoonStart = new TimeSpan(15, 0, 0);
        private TimeSpan AfternoonEnd = new TimeSpan(16, 0, 0);

        //–– Other inputs
        [InputParameter("History Lookback (days)", 4)]
        public int HistoryLookbackDays { get; set; } = 5;
        [InputParameter("Show Morning Box", 5)]
        public bool ShowMorningBox { get; set; } = true;
        [InputParameter("Show Afternoon Box", 6)]
        public bool ShowAfternoonBox { get; set; } = true;
        [InputParameter("Show 30% Retracement", 7)]
        public bool ShowThirty { get; set; } = true;
        [InputParameter("Show 50% Retracement", 8)]
        public bool ShowFifty { get; set; } = true;
        [InputParameter("Show 70% Retracement", 9)]
        public bool ShowSeventy { get; set; } = true;
        [InputParameter("Bullish Box Color", 10)]
        public Color BullBoxColor { get; set; } = Color.LimeGreen;
        [InputParameter("Bearish Box Color", 11)]
        public Color BearBoxColor { get; set; } = Color.Red;
        [InputParameter("Fib Line Color", 12)]
        public LineOptions FibLineStyle { get; set; } = new LineOptions()
        {
            Color = Color.Yellow,
            LineStyle = LineStyle.Dash,
            Width = 1,
            WithCheckBox = false
        };
        [InputParameter("Max Unmitigated Boxes", 13)]
        public int MaxUnmitigatedBoxes { get; set; } = 5;
        [InputParameter("Max Mitigated Boxes", 14)]
        public int MaxMitigatedBoxes { get; set; } = 5;

        //–– Date‐label style (not exposed)
        private Font DateFont = new Font("Segoe UI", 8, FontStyle.Bold);
        private Color DateFontColor = Color.White;

        public Time_Based_Range_Fibs_DTFX()
        {
            Name = "Time_Based_Range_Fibs_DTFX3";
            Description = "Overlays session‐range boxes and Fibonacci levels (all in EST), unmitigated boxes extend until mitigated.";
            SeparateWindow = false;
        }

        protected override void OnInit()
        {
            fibLabelFont = new Font("Segoe UI", 8, FontStyle.Bold);
            emojiFont = new Font("Segoe UI Emoji", 12, FontStyle.Bold);
            stringFormat = new StringFormat()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            hoursHistory = Symbol.GetHistory(
                Period.HOUR1,
                Symbol.HistoryType,
                DateTime.UtcNow.AddDays(-HistoryLookbackDays)
            );
        }

        public override IList<SettingItem> Settings
        {
            get
            {
                var settings = base.Settings;
                var sep = settings.FirstOrDefault()?.SeparatorGroup;

                // compute "today" in EST
                DateTime estToday = TimeZoneInfo.ConvertTime(DateTime.UtcNow, estZone).Date;

                // Insert EST time pickers at top
                settings.Insert(0, new SettingItemDateTime(
                    "Afternoon Session End Time",
                    estToday.Add(AfternoonEnd))
                { SeparatorGroup = sep, Format = DatePickerFormat.Time });

                settings.Insert(0, new SettingItemDateTime(
                    "Afternoon Session Start Time",
                    estToday.Add(AfternoonStart))
                { SeparatorGroup = sep, Format = DatePickerFormat.Time });

                settings.Insert(0, new SettingItemDateTime(
                    "Morning Session End Time",
                    estToday.Add(MorningEnd))
                { SeparatorGroup = sep, Format = DatePickerFormat.Time });

                settings.Insert(0, new SettingItemDateTime(
                    "Morning Session Start Time",
                    estToday.Add(MorningStart))
                { SeparatorGroup = sep, Format = DatePickerFormat.Time });

                return settings;
            }
            set
            {
                base.Settings = value;

                // map each DateTime picker back into its TimeSpan (interpreting as EST)
                foreach (var si in value)
                {
                    if (si is SettingItemDateTime dt)
                    {
                        var dateVal = TimeZoneInfo.ConvertTime((DateTime)dt.Value, estZone);
                        switch (dt.Name)
                        {
                            case "Morning Session Start Time":
                                MorningStart = dateVal.TimeOfDay; break;
                            case "Morning Session End Time":
                                MorningEnd = dateVal.TimeOfDay; break;
                            case "Afternoon Session Start Time":
                                AfternoonStart = dateVal.TimeOfDay; break;
                            case "Afternoon Session End Time":
                                AfternoonEnd = dateVal.TimeOfDay; break;
                        }
                    }
                }

                // reload history if lookback changed
                hoursHistory = Symbol.GetHistory(
                    Period.HOUR1,
                    Symbol.HistoryType,
                    DateTime.UtcNow.AddDays(-HistoryLookbackDays)
                );

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
            DateTime rightEdge = conv.GetTime(wnd.ClientRectangle.Right);

            var sessions = new List<SessionBox>();
            for (int i = 0; i < hoursHistory.Count; i++)
            {
                if (hoursHistory[i, SeekOriginHistory.Begin] is not HistoryItemBar bar)
                    continue;

                var estTime = TimeZoneInfo.ConvertTime(bar.TimeLeft, TimeZoneInfo.Utc, estZone);
                var date = estTime.Date;

                foreach (var sess in new[]
                {
                    new { Label="☀️", Start=MorningStart,   End=MorningEnd,   Show=ShowMorningBox,   Key="Morning"   },
                    new { Label="🏧", Start=AfternoonStart, End=AfternoonEnd, Show=ShowAfternoonBox, Key="Afternoon" }
                })
                {
                    if (!sess.Show || estTime.TimeOfDay < sess.Start || estTime.TimeOfDay >= sess.End)
                        continue;
                    if (sessions.Any(s => s.Date == date && s.Key == sess.Key))
                        continue;

                    DateTime sUtc = TimeZoneInfo.ConvertTimeToUtc(date.Add(sess.Start), estZone);
                    DateTime eUtc = TimeZoneInfo.ConvertTimeToUtc(date.Add(sess.End), estZone);

                    // determine high/low of session
                    double high = double.MinValue, low = double.MaxValue;
                    for (int j = 0; j < hoursHistory.Count; j++)
                    {
                        if (hoursHistory[j, SeekOriginHistory.Begin] is not HistoryItemBar b2) continue;
                        var bEst = TimeZoneInfo.ConvertTime(b2.TimeLeft, TimeZoneInfo.Utc, estZone);
                        if (bEst < date.Add(sess.Start) || bEst >= date.Add(sess.End)) continue;
                        high = Math.Max(high, b2.High);
                        low = Math.Min(low, b2.Low);
                    }

                    // breakout detection
                    bool up = false, down = false;
                    int fbIdx = -1;
                    for (int j = 0; j < hoursHistory.Count; j++)
                    {
                        if (hoursHistory[j, SeekOriginHistory.Begin] is not HistoryItemBar b2) continue;
                        var bEst = TimeZoneInfo.ConvertTime(b2.TimeLeft, TimeZoneInfo.Utc, estZone);
                        if (bEst <= date.Add(sess.End)) continue;
                        if (b2.Close > high) { up = true; fbIdx = j; break; }
                        if (b2.Close < low) { down = true; fbIdx = j; break; }
                    }

                    // mitigation detection
                    bool mitigated = false;
                    DateTime mitUtc = DateTime.MinValue;
                    if (fbIdx >= 0)
                    {
                        for (int k = fbIdx + 1; k < hoursHistory.Count; k++)
                        {
                            if (hoursHistory[k, SeekOriginHistory.Begin] is not HistoryItemBar mb) continue;
                            if (up && mb.Close < low) { mitigated = true; mitUtc = mb.TimeLeft; break; }
                            if (down && mb.Close > high) { mitigated = true; mitUtc = mb.TimeLeft; break; }
                        }
                    }

                    sessions.Add(new SessionBox
                    {
                        Date = date,
                        Key = sess.Key,
                        Label = sess.Label,
                        High = high,
                        Low = low,
                        BrokeAbove = up,
                        BrokeBelow = down,
                        Mitigated = mitigated,
                        StartUtc = sUtc,
                        MitigationUtc = mitigated ? mitUtc : DateTime.MinValue
                    });
                }
            }

            // apply limits & draw
            var unmit = sessions.Where(s => !s.Mitigated)
                                .OrderByDescending(s => s.Date)
                                .Take(MaxUnmitigatedBoxes);
            var mit = sessions.Where(s => s.Mitigated)
                                .OrderByDescending(s => s.Date)
                                .Take(MaxMitigatedBoxes);
            var toDraw = unmit.Concat(mit)
                              .OrderBy(s => s.Date)
                              .ThenBy(s => s.Key);

            foreach (var b in toDraw)
            {
                float x1 = (float)conv.GetChartX(b.StartUtc);
                DateTime endUtc = b.Mitigated
                    ? b.MitigationUtc
                    : rightEdge;
                float x2 = (float)conv.GetChartX(endUtc);
                float y1 = (float)conv.GetChartY(b.High);
                float y2 = (float)conv.GetChartY(b.Low);

                var col = b.BrokeAbove ? BullBoxColor
                       : b.BrokeBelow ? BearBoxColor
                       : Color.Gray;
                using (var fb = new SolidBrush(Color.FromArgb(60, col)))
                    gfx.FillRectangle(fb, x1, y1, x2 - x1, y2 - y1);
                using (var pen = new Pen(col, 1))
                    gfx.DrawRectangle(pen, x1, y1, x2 - x1, y2 - y1);

                // Fibonacci levels
                double range = b.High - b.Low;
                foreach (var p in new[] { 0.3, 0.5, 0.7 })
                {
                    bool show = (p == 0.3 && ShowThirty)
                             || (p == 0.5 && ShowFifty)
                             || (p == 0.7 && ShowSeventy);
                    if (!show) continue;
                    float yF = (float)conv.GetChartY(b.High - range * p);
                    using var l = new Pen(FibLineStyle.Color, FibLineStyle.Width)
                    { DashStyle = ConvertLineStyleToDashStyle(FibLineStyle.LineStyle) };
                    gfx.DrawLine(l, x1, yF, x2, yF);
                    DrawFibLabel(gfx, $"{(int)(p * 100)}%", x1 + 2, yF);
                }

                // emoji + date label
                float ex = x1 + 5, ey = y1 - 20;
                using (var eb = new SolidBrush(b.Key == "Morning" ? Color.Yellow : Color.CornflowerBlue))
                    gfx.DrawString(b.Label, emojiFont, eb, ex, ey, stringFormat);

                string dt = b.Date.ToString("MM/dd");
                using (var db = new SolidBrush(DateFontColor))
                    gfx.DrawString(dt, DateFont, db, ex, ey + emojiFont.Height + 2, stringFormat);
            }
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
            var sz = g.MeasureString(text, fibLabelFont);
            var rect = new RectangleF(x, y - sz.Height / 2, sz.Width + pad * 2, sz.Height);
            using var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, rad, rad, 180, 90);
            path.AddArc(rect.Right - rad, rect.Top, rad, rad, 270, 90);
            path.AddArc(rect.Right - rad, rect.Bottom - rad, rad, rad, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - rad, rad, rad, 90, 90);
            path.CloseFigure();
            using (var bg = new SolidBrush(Color.Gold)) g.FillPath(bg, path);
            using (var p = new Pen(Color.Gold)) g.DrawPath(p, path);
            g.DrawString(text, fibLabelFont, Brushes.Black, x + pad, y - sz.Height / 2);
        }

        private class SessionBox
        {
            public DateTime Date;
            public string Key, Label;
            public double High, Low;
            public bool BrokeAbove, BrokeBelow, Mitigated;
            public DateTime StartUtc, MitigationUtc;
        }
    }
}
