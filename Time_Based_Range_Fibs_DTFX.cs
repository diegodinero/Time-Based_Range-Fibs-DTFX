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

        [InputParameter("Turn Off All Fibs", 7)]
        public bool TurnOffAllFibs { get; set; } = false;

        [InputParameter("Show 30% Retracement", 8)]
        public bool ShowThirty { get; set; } = true;

        [InputParameter("Show 50% Retracement", 9)]
        public bool ShowFifty { get; set; } = true;

        [InputParameter("Show 70% Retracement", 10)]
        public bool ShowSeventy { get; set; } = true;

        [InputParameter("Morning Label Emoji", 11)]
        public string MorningLabelEmoji { get; set; } = "9";

        [InputParameter("Afternoon Label Emoji", 12)]
        public string AfternoonLabelEmoji { get; set; } = "3";

        [InputParameter("Show Date Label", 13)]
        public bool ShowDateLabel { get; set; } = true;

        [InputParameter("Bullish Box Color", 14)]
        public Color BullBoxColor { get; set; } = Color.FromArgb(51, 0x4C, 0xAF, 0x50);

        [InputParameter("Bearish Box Color", 15)]
        public Color BearBoxColor { get; set; } = Color.FromArgb(51, 0xF2, 0x36, 0x45);

        [InputParameter("Fib Line Color", 16)]
        public LineOptions FibLineStyle { get; set; } = new LineOptions()
        {
            Color = Color.Yellow,
            LineStyle = LineStyle.Dash,
            Width = 1,
            WithCheckBox = false
        };

        [InputParameter("Max Unmitigated Boxes", 17)]
        public int MaxUnmitigatedBoxes { get; set; } = 5;

        [InputParameter("Max Mitigated Boxes", 18)]
        public int MaxMitigatedBoxes { get; set; } = 0;

        //–– Date‐label style (not exposed)
        private Font DateFont = new Font("Segoe UI", 8, FontStyle.Bold);
        private Color DateFontColor = Color.White;

        public Time_Based_Range_Fibs_DTFX()
        {
            Name = "Time_Based_Range_Fibs_DTFX";
            Description = "Overlays session‐range boxes + fib levels (EST). Added: Turn Off All Fibs flag, custom emojis, toggle short date label.";
            SeparateWindow = false;
            OnBackGround = true;
        }

        protected override void OnInit()
        {
            fibLabelFont = new Font("Segoe UI", 8, FontStyle.Bold);
            emojiFont = new Font("Segoe UI Emoji", 8, FontStyle.Bold);
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
            if (CurrentChart == null)
                return;

            var gfx = args.Graphics;
            var wnd = CurrentChart.MainWindow;
            var conv = wnd.CoordinatesConverter;
            DateTime rightEdge = conv.GetTime(wnd.ClientRectangle.Right);

            // 1) Build the 'sessions' list without LINQ
            List<SessionBox> sessions = new List<SessionBox>();
            for (int i = 0; i < hoursHistory.Count; i++)
            {
                if (hoursHistory[i, SeekOriginHistory.Begin] is not HistoryItemBar bar)
                    continue;

                var estTime = TimeZoneInfo.ConvertTime(bar.TimeLeft, TimeZoneInfo.Utc, estZone);
                var date = estTime.Date;

                // Two session definitions: "Morning" and "Afternoon"
                for (int si = 0; si < 2; si++)
                {
                    string key = (si == 0) ? "Morning" : "Afternoon";
                    TimeSpan start = (si == 0) ? MorningStart : AfternoonStart;
                    TimeSpan end = (si == 0) ? MorningEnd : AfternoonEnd;
                    bool show = (si == 0) ? ShowMorningBox : ShowAfternoonBox;
                    string emoji = (si == 0) ? MorningLabelEmoji : AfternoonLabelEmoji;

                    if (!show || estTime.TimeOfDay < start || estTime.TimeOfDay >= end)
                        continue;

                    // Check if a SessionBox with same date/key is already in 'sessions'
                    bool alreadyExists = false;
                    for (int k = 0; k < sessions.Count; k++)
                    {
                        if (sessions[k].Date == date && sessions[k].Key == key)
                        {
                            alreadyExists = true;
                            break;
                        }
                    }
                    if (alreadyExists)
                        continue;

                    DateTime sUtc = TimeZoneInfo.ConvertTimeToUtc(date.Add(start), estZone);
                    DateTime eUtc = TimeZoneInfo.ConvertTimeToUtc(date.Add(end), estZone);

                    // determine high/low of session
                    double high = double.MinValue, low = double.MaxValue;
                    for (int j = 0; j < hoursHistory.Count; j++)
                    {
                        if (hoursHistory[j, SeekOriginHistory.Begin] is not HistoryItemBar b2)
                            continue;

                        var bEst = TimeZoneInfo.ConvertTime(b2.TimeLeft, TimeZoneInfo.Utc, estZone);
                        if (bEst < date.Add(start) || bEst >= date.Add(end))
                            continue;

                        if (b2.High > high) high = b2.High;
                        if (b2.Low < low) low = b2.Low;
                    }

                    // breakout detection
                    bool up = false, down = false;
                    int fbIdx = -1;
                    for (int j = 0; j < hoursHistory.Count; j++)
                    {
                        if (hoursHistory[j, SeekOriginHistory.Begin] is not HistoryItemBar b2)
                            continue;

                        var bEst = TimeZoneInfo.ConvertTime(b2.TimeLeft, TimeZoneInfo.Utc, estZone);
                        if (bEst <= date.Add(end))
                            continue;

                        if (b2.Close > high)
                        {
                            up = true;
                            fbIdx = j;
                            break;
                        }
                        if (b2.Close < low)
                        {
                            down = true;
                            fbIdx = j;
                            break;
                        }
                    }

                    // mitigation detection
                    bool mitigated = false;
                    DateTime mitUtc = DateTime.MinValue;
                    if (fbIdx >= 0)
                    {
                        for (int k = fbIdx + 1; k < hoursHistory.Count; k++)
                        {
                            if (hoursHistory[k, SeekOriginHistory.Begin] is not HistoryItemBar mb)
                                continue;

                            if (up && mb.Close < low)
                            {
                                mitigated = true;
                                mitUtc = mb.TimeLeft;
                                break;
                            }
                            if (down && mb.Close > high)
                            {
                                mitigated = true;
                                mitUtc = mb.TimeLeft;
                                break;
                            }
                        }
                    }

                    // Add the session box
                    sessions.Add(new SessionBox
                    {
                        Date = date,
                        Key = key,
                        Label = emoji,
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

            // 2) Separate into unmitigated and mitigated lists (no LINQ)
            List<SessionBox> unmitList = new List<SessionBox>();
            List<SessionBox> mitList = new List<SessionBox>();
            for (int i = 0; i < sessions.Count; i++)
            {
                if (!sessions[i].Mitigated)
                    unmitList.Add(sessions[i]);
                else
                    mitList.Add(sessions[i]);
            }

            // 3) Sort each list by Date descending
            unmitList.Sort((a, b) => b.Date.CompareTo(a.Date));
            mitList.Sort((a, b) => b.Date.CompareTo(a.Date));

            // 4) Take only up to the max counts
            List<SessionBox> chosenUnmit = new List<SessionBox>();
            for (int i = 0; i < unmitList.Count && i < MaxUnmitigatedBoxes; i++)
                chosenUnmit.Add(unmitList[i]);

            List<SessionBox> chosenMit = new List<SessionBox>();
            for (int i = 0; i < mitList.Count && i < MaxMitigatedBoxes; i++)
                chosenMit.Add(mitList[i]);

            // 5) Merge them into toDraw
            List<SessionBox> toDraw = new List<SessionBox>();
            for (int i = 0; i < chosenUnmit.Count; i++)
                toDraw.Add(chosenUnmit[i]);
            for (int i = 0; i < chosenMit.Count; i++)
                toDraw.Add(chosenMit[i]);

            // 6) Sort toDraw by Date ascending, then by Key
            toDraw.Sort((a, b) =>
            {
                int cmp = a.Date.CompareTo(b.Date);
                if (cmp != 0)
                    return cmp;
                return string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            });

            // 7) Draw each session box exactly as before
            foreach (var b in toDraw)
            {
                float x1 = (float)conv.GetChartX(b.StartUtc);
                DateTime limitUtc = b.Mitigated ? b.MitigationUtc : rightEdge;
                float x2 = (float)conv.GetChartX(limitUtc);
                float y1 = (float)conv.GetChartY(b.High);
                float y2 = (float)conv.GetChartY(b.Low);

                var col = b.BrokeAbove ? BullBoxColor
                       : b.BrokeBelow ? BearBoxColor
                       : Color.Gray;

                // Fill the box semi-transparently
                using (var fbBrush = new SolidBrush(Color.FromArgb(60, col)))
                    gfx.FillRectangle(fbBrush, x1, y1, x2 - x1, y2 - y1);

                // Compute a brighter, fully opaque outline color
                int brightenAmount = 40;
                int r = Math.Min(col.R + brightenAmount, 255);
                int g = Math.Min(col.G + brightenAmount, 255);
                int bC = Math.Min(col.B + brightenAmount, 255);
                var brightColor = Color.FromArgb(255, r, g, bC);

                // Draw a thicker (2px) border using the brightened color
                using (var borderPen = new Pen(brightColor, 2))
                    gfx.DrawRectangle(borderPen, x1, y1, x2 - x1, y2 - y1);

                // Draw Fibonacci levels
                double range = b.High - b.Low;
                for (int pi = 0; pi < 3; pi++)
                {
                    double p = (pi == 0) ? 0.3 : (pi == 1) ? 0.5 : 0.7;
                    bool show = !TurnOffAllFibs
                             && ((pi == 0 && ShowThirty)
                              || (pi == 1 && ShowFifty)
                              || (pi == 2 && ShowSeventy));
                    if (!show)
                        continue;

                    // ← modified here
                    float yF;
                    if (b.BrokeBelow)
                        yF = (float)conv.GetChartY(b.Low + range * p);
                    else
                        yF = (float)conv.GetChartY(b.High - range * p);

                    using var l = new Pen(FibLineStyle.Color, FibLineStyle.Width)
                    {
                        DashStyle = ConvertLineStyleToDashStyle(FibLineStyle.LineStyle)
                    };
                    gfx.DrawLine(l, x1, yF, x2, yF);
                    DrawFibLabel(gfx, $"{(int)(p * 100)}%", x1 + 2, yF);
                }


                // Draw emoji + optional date label
                float ex = x1 + 5;
                float ey = y1 - 20;
                using (var eb = new SolidBrush(b.Key == "Morning" ? Color.Yellow : Color.CornflowerBlue))
                    gfx.DrawString(b.Label, emojiFont, eb, ex, ey, stringFormat);

                if (ShowDateLabel)
                {
                    string dt = b.Date.ToString("M/d");
                    using (var db = new SolidBrush(DateFontColor))
                        gfx.DrawString(dt, DateFont, db, ex, ey + emojiFont.Height + 2, stringFormat);
                }
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
