// Copyright QUANTOWER LLC. © 2017-2025. All rights reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Utils;

namespace Time_Based_Range_Fibs_DTFX
{
    public class Time_Based_Range_Fibs_DTFX : Indicator
    {
        //–– Drawing helpers
        private Font emojiFont;
        private Font fibLabelFont;
        private StringFormat stringFormat;

        //–– Histories
        private HistoricalData hoursHistory;     // 1-hour bars for session high/low

        //–– Session times
        [InputParameter("Morning Session Start Time")]
        private TimeSpan MorningStart = new TimeSpan(9, 0, 0);
        [InputParameter("Morning Session End Time")]
        private TimeSpan MorningEnd = new TimeSpan(10, 0, 0);
        [InputParameter("Afternoon Session Start Time")]
        private TimeSpan AfternoonStart = new TimeSpan(15, 0, 0);
        [InputParameter("Afternoon Session End Time")]
        private TimeSpan AfternoonEnd = new TimeSpan(16, 0, 0);

        //–– Show toggles
        [InputParameter("Show Morning Box")]
        private bool ShowMorningBox = true;
        [InputParameter("Show Afternoon Box")]
        private bool ShowAfternoonBox = true;

        //–– Fibonacci toggles
        [InputParameter("Show 30% Retracement")]
        private bool ShowThirty = true;
        [InputParameter("Show 50% Retracement")]
        private bool ShowFifty = true;
        [InputParameter("Show 70% Retracement")]
        private bool ShowSeventy = true;

        //–– Box colors & line style
        [InputParameter("Bullish Box Color", 1)]
        public Color BullBoxColor = Color.LimeGreen;
        [InputParameter("Bearish Box Color", 2)]
        public Color BearBoxColor = Color.Red;
        [InputParameter("Fib Line Color", 3)]
        private LineOptions FibLineStyle = new LineOptions()
        {
            Color = Color.Yellow,
            LineStyle = LineStyle.Dash,
            Width = 1,
            WithCheckBox = false
        };

        //–– ** new inside-bar colors **
        [InputParameter("Bullish Inside Bar Color", 6)]
        public Color InsideBullColor = Color.LimeGreen;
        [InputParameter("Bearish Inside Bar Color", 7)]
        public Color InsideBearColor = Color.Red;

        //–– Date label style
        private Font DateFont = new Font("Segoe UI", 8, FontStyle.Bold);
        private Color DateFontColor = Color.White;

        //–– Limits
        [InputParameter("Max Unmitigated Boxes", 4)]
        private int MaxUnmitigatedBoxes = 5;
        [InputParameter("Max Mitigated Boxes", 5)]
        private int MaxMitigatedBoxes = 5;

        public Time_Based_Range_Fibs_DTFX() : base()
        {
            Name = "Time_Based_Range_Fibs_DTFX3";
            Description = "Session-range boxes + Fibs + inside-bar coloring (customizable).";
            SeparateWindow = false;
        }

        protected override void OnInit()
        {
            // fonts & formatting
            fibLabelFont = new Font("Segoe UI", 8, FontStyle.Bold);
            emojiFont = new Font("Segoe UI Emoji", 12, FontStyle.Bold);
            stringFormat = new StringFormat()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            // fetch 1-hour bars for session ranges (last 45 days)
            hoursHistory = Symbol.GetHistory(
                Period.HOUR1,
                Symbol.HistoryType,
                DateTime.UtcNow.AddDays(-45)
            );
        }

        public override IList<SettingItem> Settings
        {
            get
            {
                var settings = base.Settings;
                var sep = settings.FirstOrDefault()?.SeparatorGroup;

                // date/time inputs
                settings.Add(new SettingItemDateTime("Morning Session Start Time", DateTime.Today.Add(MorningStart))
                {
                    SeparatorGroup = sep,
                    Format = DatePickerFormat.Time
                });
                settings.Add(new SettingItemDateTime("Morning Session End Time", DateTime.Today.Add(MorningEnd))
                {
                    SeparatorGroup = sep,
                    Format = DatePickerFormat.Time
                });
                settings.Add(new SettingItemDateTime("Afternoon Session Start Time", DateTime.Today.Add(AfternoonStart))
                {
                    SeparatorGroup = sep,
                    Format = DatePickerFormat.Time
                });
                settings.Add(new SettingItemDateTime("Afternoon Session End Time", DateTime.Today.Add(AfternoonEnd))
                {
                    SeparatorGroup = sep,
                    Format = DatePickerFormat.Time
                });

                // date label styling
                settings.Add(new SettingItemFont("Date Font", DateFont)
                {
                    SeparatorGroup = sep
                });
                settings.Add(new SettingItemColor("Date Font Color", DateFontColor)
                {
                    SeparatorGroup = sep
                });

                return settings;
            }
            set
            {
                base.Settings = value;
                if (value.TryGetValue("Morning Session Start Time", out DateTime ms)) MorningStart = ms.TimeOfDay;
                if (value.TryGetValue("Morning Session End Time", out DateTime me)) MorningEnd = me.TimeOfDay;
                if (value.TryGetValue("Afternoon Session Start Time", out DateTime as_)) AfternoonStart = as_.TimeOfDay;
                if (value.TryGetValue("Afternoon Session End Time", out DateTime ae)) AfternoonEnd = ae.TimeOfDay;
                if (value.TryGetValue("Date Font", out Font df)) DateFont = df;
                if (value.TryGetValue("Date Font Color", out Color dc)) DateFontColor = dc;

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
            var chartHist = this.HistoricalData;  // current-chart bars
            DateTime rightTime = conv.GetTime(wnd.ClientRectangle.Right);
            var estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

            // 1) build session boxes …
            var all = new List<SessionBox>();
            for (int i = 0; i < hoursHistory.Count; i++)
            {
                if (hoursHistory[i, SeekOriginHistory.Begin] is not HistoryItemBar bar)
                    continue;
                DateTime estBar = TimeZoneInfo.ConvertTime(bar.TimeLeft, TimeZoneInfo.Utc, estZone);
                DateTime date = estBar.Date;

                foreach (var sess in new[]
                         {
                             new { Label = "☀️", Start = MorningStart,   End = MorningEnd,   Show = ShowMorningBox,   Key = "Morning"   },
                             new { Label = "🏧", Start = AfternoonStart, End = AfternoonEnd, Show = ShowAfternoonBox, Key = "Afternoon" }
                         })
                {
                    if (!sess.Show || estBar.TimeOfDay != sess.Start)
                        continue;
                    if (all.Any(b => b.Date == date && b.Key == sess.Key))
                        continue;

                    DateTime sEst = date.Add(sess.Start);
                    DateTime eEst = date.Add(sess.End);
                    DateTime sUtc = TimeZoneInfo.ConvertTimeToUtc(sEst, estZone);
                    DateTime eUtc = TimeZoneInfo.ConvertTimeToUtc(eEst, estZone);

                    double high = double.MinValue, low = double.MaxValue;
                    for (int j = 0; j < hoursHistory.Count; j++)
                    {
                        if (hoursHistory[j, SeekOriginHistory.Begin] is not HistoryItemBar b2)
                            continue;
                        var t2 = TimeZoneInfo.ConvertTime(b2.TimeLeft, TimeZoneInfo.Utc, estZone);
                        if (t2 < sEst || t2 >= eEst) continue;
                        high = Math.Max(high, b2.High);
                        low = Math.Min(low, b2.Low);
                    }
                    if (high == double.MinValue || low == double.MaxValue)
                        continue;

                    bool up = false, down = false;
                    int firstIdx = -1;
                    for (int j = 0; j < hoursHistory.Count; j++)
                    {
                        if (hoursHistory[j, SeekOriginHistory.Begin] is not HistoryItemBar b3)
                            continue;
                        var t3 = TimeZoneInfo.ConvertTime(b3.TimeLeft, TimeZoneInfo.Utc, estZone);
                        if (t3 <= eEst) continue;
                        if (b3.Close > high) { up = true; firstIdx = j; break; }
                        if (b3.Close < low) { down = true; firstIdx = j; break; }
                    }

                    bool mitigated = false;
                    DateTime mitUtc = DateTime.MinValue;
                    if (firstIdx >= 0)
                    {
                        for (int k = firstIdx + 1; k < hoursHistory.Count; k++)
                        {
                            if (hoursHistory[k, SeekOriginHistory.Begin] is not HistoryItemBar mb)
                                continue;
                            if ((up && mb.Close < low) ||
                                (down && mb.Close > high))
                            {
                                mitigated = true;
                                mitUtc = mb.TimeLeft;
                                break;
                            }
                        }
                    }

                    all.Add(new SessionBox
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
                        MitigationUtc = mitUtc
                    });
                }
            }

            // 2) limit & order …
            var unmit = all.Where(b => !b.Mitigated)
                           .OrderByDescending(b => b.Date)
                           .Take(MaxUnmitigatedBoxes);
            var mit = all.Where(b => b.Mitigated)
                           .OrderByDescending(b => b.Date)
                           .Take(MaxMitigatedBoxes);
            var toDraw = unmit.Concat(mit)
                              .OrderBy(b => b.Date)
                              .ThenBy(b => b.Key);

            // 3) draw each session box + inside bars + fibs + emoji + date …
            foreach (var b in toDraw)
            {
                // session window
                TimeSpan sessStart = b.Key == "Morning" ? MorningStart : AfternoonStart;
                TimeSpan sessEnd = b.Key == "Morning" ? MorningEnd : AfternoonEnd;
                DateTime sEst = b.Date.Add(sessStart);
                DateTime eEst = b.Date.Add(sessEnd);

                // box coords
                float x1 = (float)conv.GetChartX(b.StartUtc);
                DateTime endUtc = b.Mitigated ? b.MitigationUtc : rightTime;
                float x2 = (float)conv.GetChartX(endUtc);
                float y1 = (float)conv.GetChartY(b.High);
                float y2 = (float)conv.GetChartY(b.Low);

                Color col = b.BrokeAbove ? BullBoxColor
                          : b.BrokeBelow ? BearBoxColor
                          : Color.Gray;

                using (var fill = new SolidBrush(Color.FromArgb(60, col)))
                    gfx.FillRectangle(fill, x1, y1, x2 - x1, y2 - y1);
                using (var pen = new Pen(col, 1))
                    gfx.DrawRectangle(pen, x1, y1, x2 - x1, y2 - y1);

                // — inside-bar coloring on chartHist —
                for (int j = 1; j < chartHist.Count - 1; j++)
                {
                    if (chartHist[j, SeekOriginHistory.Begin] is not HistoryItemBar curBar ||
                        chartHist[j - 1, SeekOriginHistory.Begin] is not HistoryItemBar prevBar ||
                        chartHist[j + 1, SeekOriginHistory.Begin] is not HistoryItemBar nextBar)
                        continue;

                    DateTime curEst = TimeZoneInfo.ConvertTime(curBar.TimeLeft, TimeZoneInfo.Utc, estZone);
                    if (curEst < sEst || curEst >= eEst)
                        continue;

                    if (curBar.High < prevBar.High && curBar.Low > prevBar.Low)
                    {
                        // center & shift
                        TimeSpan fullWidth = nextBar.TimeLeft - curBar.TimeLeft;
                        TimeSpan halfWidth = TimeSpan.FromTicks(fullWidth.Ticks / 2);
                        DateTime center = curBar.TimeLeft;

                        float bx1 = (float)conv.GetChartX(center - halfWidth);
                        float bx2 = (float)conv.GetChartX(center + halfWidth);
                        float barPx = bx2 - bx1;
                        float shiftPx = barPx * 0.40f;
                        bx1 += shiftPx;
                        bx2 += shiftPx;

                        // wick-to-wick
                        float yHigh = (float)conv.GetChartY(curBar.High);
                        float yLow = (float)conv.GetChartY(curBar.Low);
                        float height = yLow - yHigh;

                        // use your input-param colors here
                        Color ibColor = curBar.Close > curBar.Open
                                         ? Color.FromArgb(100, InsideBullColor)
                                         : Color.FromArgb(100, InsideBearColor);

                        using var ibBrush = new SolidBrush(ibColor);
                        gfx.FillRectangle(ibBrush, bx1, yHigh, bx2 - bx1, height);
                    }
                }

                // fib lines
                double range = b.High - b.Low;
                foreach (var p in new float[] { 0.3f, 0.5f, 0.7f })
                {
                    bool show = (p == 0.3f && ShowThirty)
                             || (p == 0.5f && ShowFifty)
                             || (p == 0.7f && ShowSeventy);
                    if (!show) continue;

                    double price = b.High - range * p;
                    float yF = (float)conv.GetChartY(price);
                    using var l = new Pen(FibLineStyle.Color, FibLineStyle.Width)
                    {
                        DashStyle = ConvertLineStyleToDashStyle(FibLineStyle.LineStyle)
                    };
                    gfx.DrawLine(l, x1, yF, x2, yF);
                    DrawFibLabel(gfx, $"{(int)(p * 100)}%", x1 + 2, yF);
                }

                // emoji + date
                float ex = x1 + 5, ey = y1 - 20;
                using (var eb = new SolidBrush(b.Key == "Morning" ? Color.Yellow : Color.CornflowerBlue))
                    gfx.DrawString(b.Label, emojiFont, eb, ex, ey, stringFormat);

                string dateText = b.Date.ToString("MM/dd");
                using (var dfb = new SolidBrush(DateFontColor))
                {
                    float dateY = ey + emojiFont.Height + 2;
                    gfx.DrawString(dateText, DateFont, dfb, ex, dateY, stringFormat);
                }
            }
        }

        private DashStyle ConvertLineStyleToDashStyle(LineStyle ls) =>
            ls switch
            {
                LineStyle.Solid => DashStyle.Solid,
                LineStyle.Dash => DashStyle.Dash,
                LineStyle.Dot => DashStyle.Dot,
                LineStyle.DashDot => DashStyle.DashDot,
                _ => DashStyle.Solid,
            };

        private void DrawFibLabel(Graphics g, string text, float x, float y)
        {
            const int pad = 4, rad = 6;
            SizeF sz = g.MeasureString(text, fibLabelFont);
            var rect = new RectangleF(x, y - sz.Height / 2, sz.Width + pad * 2, sz.Height);

            using var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, rad, rad, 180, 90);
            path.AddArc(rect.Right - rad, rect.Top, rad, rad, 270, 90);
            path.AddArc(rect.Right - rad, rect.Bottom - rad, rad, rad, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - rad, rad, rad, 90, 90);
            path.CloseFigure();

            using var bg = new SolidBrush(Color.Gold);
            g.FillPath(bg, path);

            using var p = new Pen(Color.Gold);
            g.DrawPath(p, path);

            g.DrawString(text, fibLabelFont, Brushes.Black, x + pad, y - sz.Height / 2);
        }

        private class SessionBox
        {
            public DateTime Date;
            public string Key;
            public string Label;
            public double High, Low;
            public bool BrokeAbove, BrokeBelow;
            public bool Mitigated;
            public DateTime StartUtc, MitigationUtc;
        }
    }
}
