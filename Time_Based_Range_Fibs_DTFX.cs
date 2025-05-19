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
        //–– Fonts & drawing helpers
        private Font emojiFont;
        private Font fibLabelFont;
        private StringFormat stringFormat;

        //–– Cached history
        private HistoricalData hoursHistory;  // 1h bars for session box logic

        //–– Caches to avoid recomputation in OnPaintChart
        private int lastHoursCount = -1;
        private TimeSpan lastMorningStart, lastMorningEnd, lastAfternoonStart, lastAfternoonEnd;
        private bool lastShowMorning, lastShowAfternoon;
        private List<SessionBox> sessionBoxesCache = new List<SessionBox>();

        private int lastChartCount = -1;
        private List<InsideBarInfo> insideBarsCache = new List<InsideBarInfo>();

        //–– Input Parameters
        [InputParameter("Morning Session Start Time")] private TimeSpan MorningStart = new TimeSpan(9, 0, 0);
        [InputParameter("Morning Session End Time")] private TimeSpan MorningEnd = new TimeSpan(10, 0, 0);
        [InputParameter("Afternoon Session Start Time")] private TimeSpan AfternoonStart = new TimeSpan(15, 0, 0);
        [InputParameter("Afternoon Session End Time")] private TimeSpan AfternoonEnd = new TimeSpan(16, 0, 0);

        [InputParameter("Show Morning Box")] private bool ShowMorningBox = true;
        [InputParameter("Show Afternoon Box")] private bool ShowAfternoonBox = true;

        [InputParameter("Show 30% Retracement")] private bool ShowThirty = true;
        [InputParameter("Show 50% Retracement")] private bool ShowFifty = true;
        [InputParameter("Show 70% Retracement")] private bool ShowSeventy = true;

        [InputParameter("Bullish Box Color", 1)] public Color BullBoxColor = Color.LimeGreen;
        [InputParameter("Bearish Box Color", 2)] public Color BearBoxColor = Color.Red;
        [InputParameter("Fib Line Color", 3)]
        private LineOptions FibLineStyle = new LineOptions
        {
            Color = Color.Yellow,
            LineStyle = LineStyle.Dash,
            Width = 1,
            WithCheckBox = false
        };

        [InputParameter("Bullish Inside Bar Color", 6)] public Color InsideBullColor = Color.LimeGreen;
        [InputParameter("Bearish Inside Bar Color", 7)] public Color InsideBearColor = Color.Red;

        private Font DateFont = new Font("Segoe UI", 8, FontStyle.Bold);
        private Color DateFontColor = Color.White;

        [InputParameter("Max Unmitigated Boxes", 4)] private int MaxUnmitigatedBoxes = 5;
        [InputParameter("Max Mitigated Boxes", 5)] private int MaxMitigatedBoxes = 5;

        public Time_Based_Range_Fibs_DTFX() : base()
        {
            Name = "Time_Based_Range_Fibs_DTFX_Opt";
            Description = "Optimized: session boxes + fibs + customizable inside-bar coloring.";
            SeparateWindow = false;
        }

        protected override void OnInit()
        {
            // Fonts & formatting
            fibLabelFont = new Font("Segoe UI", 8, FontStyle.Bold);
            emojiFont = new Font("Segoe UI Emoji", 12, FontStyle.Bold);
            stringFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            // Fetch 1-hour history once
            hoursHistory = Symbol.GetHistory(Period.HOUR1, Symbol.HistoryType, DateTime.UtcNow.AddDays(-45));
        }

        public override IList<SettingItem> Settings
        {
            get
            {
                var settings = base.Settings;
                var sep = settings.FirstOrDefault()?.SeparatorGroup;

                // Session times
                settings.Add(new SettingItemDateTime("Morning Session Start Time", DateTime.Today.Add(MorningStart)) { SeparatorGroup = sep, Format = DatePickerFormat.Time });
                settings.Add(new SettingItemDateTime("Morning Session End Time", DateTime.Today.Add(MorningEnd)) { SeparatorGroup = sep, Format = DatePickerFormat.Time });
                settings.Add(new SettingItemDateTime("Afternoon Session Start Time", DateTime.Today.Add(AfternoonStart)) { SeparatorGroup = sep, Format = DatePickerFormat.Time });
                settings.Add(new SettingItemDateTime("Afternoon Session End Time", DateTime.Today.Add(AfternoonEnd)) { SeparatorGroup = sep, Format = DatePickerFormat.Time });

                // Date label styling
                settings.Add(new SettingItemFont("Date Font", DateFont) { SeparatorGroup = sep });
                settings.Add(new SettingItemColor("Date Font Color", DateFontColor) { SeparatorGroup = sep });

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
            var conv = CurrentChart.MainWindow.CoordinatesConverter;
            var chartHist = this.HistoricalData;
            DateTime rightTime = conv.GetTime(CurrentChart.MainWindow.ClientRectangle.Right);
            var estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

            // --- Update sessionBoxesCache if hoursHistory changed or settings changed ---
            bool sessDirty = hoursHistory.Count != lastHoursCount
                              || MorningStart != lastMorningStart
                              || MorningEnd != lastMorningEnd
                              || AfternoonStart != lastAfternoonStart
                              || AfternoonEnd != lastAfternoonEnd
                              || ShowMorningBox != lastShowMorning
                              || ShowAfternoonBox != lastShowAfternoon;
            if (sessDirty)
            {
                sessionBoxesCache.Clear();
                lastHoursCount = hoursHistory.Count;
                lastMorningStart = MorningStart;
                lastMorningEnd = MorningEnd;
                lastAfternoonStart = AfternoonStart;
                lastAfternoonEnd = AfternoonEnd;
                lastShowMorning = ShowMorningBox;
                lastShowAfternoon = ShowAfternoonBox;

                // build raw SessionBox list
                for (int i = 0; i < hoursHistory.Count; i++)
                {
                    if (hoursHistory[i, SeekOriginHistory.Begin] is not HistoryItemBar bar)
                        continue;
                    DateTime estBar = TimeZoneInfo.ConvertTime(bar.TimeLeft, TimeZoneInfo.Utc, estZone);
                    DateTime date = estBar.Date;

                    foreach (var sess in new[] {
                        new { Key="Morning", Start=MorningStart, End=MorningEnd, Show=ShowMorningBox, Label="☀️" },
                        new { Key="Afternoon", Start=AfternoonStart, End=AfternoonEnd, Show=ShowAfternoonBox, Label="🏧" }
                    })
                    {
                        if (!sess.Show || estBar.TimeOfDay != sess.Start)
                            continue;
                        if (sessionBoxesCache.Any(b => b.Date == date && b.Key == sess.Key))
                            continue;

                        DateTime sEst = date.Add(sess.Start);
                        DateTime eEst = date.Add(sess.End);
                        DateTime sUtc = TimeZoneInfo.ConvertTimeToUtc(sEst, estZone);

                        // find high/low
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
                        if (high == double.MinValue || low == double.MaxValue) continue;

                        // breakout & mitigation
                        bool up = false, down = false;
                        int firstIdx = -1;
                        for (int j = 0; j < hoursHistory.Count; j++)
                        {
                            if (hoursHistory[j, SeekOriginHistory.Begin] is not HistoryItemBar b3) continue;
                            var t3 = TimeZoneInfo.ConvertTime(b3.TimeLeft, TimeZoneInfo.Utc, estZone);
                            if (t3 <= eEst) continue;
                            if (b3.Close > high) { up = true; firstIdx = j; break; }
                            if (b3.Close < low) { down = true; firstIdx = j; break; }
                        }
                        bool mitigated = false; DateTime mitUtc = DateTime.MinValue;
                        if (firstIdx >= 0)
                        {
                            for (int k = firstIdx + 1; k < hoursHistory.Count; k++)
                            {
                                if (hoursHistory[k, SeekOriginHistory.Begin] is not HistoryItemBar mb) continue;
                                if ((up && mb.Close < low) || (down && mb.Close > high))
                                {
                                    mitigated = true; mitUtc = mb.TimeLeft; break;
                                }
                            }
                        }

                        sessionBoxesCache.Add(new SessionBox
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
            }

            // --- Update insideBarsCache if chartHist changed or settings changed ---
            bool insideDirty = chartHist.Count != lastChartCount
                                  || MorningStart != lastMorningStart
                                  || MorningEnd != lastMorningEnd
                                  || AfternoonStart != lastAfternoonStart
                                  || AfternoonEnd != lastAfternoonEnd
                                  || ShowMorningBox != lastShowMorning
                                  || ShowAfternoonBox != lastShowAfternoon;
            if (insideDirty)
            {
                insideBarsCache.Clear();
                lastChartCount = chartHist.Count;

                // detect inside-bars for enabled sessions
                for (int j = 1; j < chartHist.Count - 1; j++)
                {
                    if (chartHist[j, SeekOriginHistory.Begin] is not HistoryItemBar cur ||
                       chartHist[j - 1, SeekOriginHistory.Begin] is not HistoryItemBar prev ||
                       chartHist[j + 1, SeekOriginHistory.Begin] is not HistoryItemBar next)
                        continue;
                    DateTime curEst = TimeZoneInfo.ConvertTime(cur.TimeLeft, TimeZoneInfo.Utc, estZone);

                    // check session filter
                    bool inMorning = ShowMorningBox && curEst.TimeOfDay >= MorningStart && curEst.TimeOfDay < MorningEnd;
                    bool inAfter = ShowAfternoonBox && curEst.TimeOfDay >= AfternoonStart && curEst.TimeOfDay < AfternoonEnd;
                    if (!inMorning && !inAfter) continue;

                    // inside-bar?
                    if (cur.High < prev.High && cur.Low > prev.Low)
                    {
                        insideBarsCache.Add(new InsideBarInfo
                        {
                            CurTime = cur.TimeLeft,
                            NextTime = next.TimeLeft,
                            Open = cur.Open,
                            Close = cur.Close,
                            High = cur.High,
                            Low = cur.Low
                        });
                    }
                }
            }

            // --- DRAWING ---
            // 1) session boxes
            foreach (var b in sessionBoxesCache.OrderBy(d => d.Date).ThenBy(d => d.Key))
            {
                float x1 = (float)conv.GetChartX(b.StartUtc);
                DateTime endUtc = b.Mitigated ? b.MitigationUtc : rightTime;
                float x2 = (float)conv.GetChartX(endUtc);
                float y1 = (float)conv.GetChartY(b.High);
                float y2 = (float)conv.GetChartY(b.Low);

                Color boxCol = b.BrokeAbove ? BullBoxColor : b.BrokeBelow ? BearBoxColor : Color.Gray;
                using (var fill = new SolidBrush(Color.FromArgb(60, boxCol))) gfx.FillRectangle(fill, x1, y1, x2 - x1, y2 - y1);
                using (var pen = new Pen(boxCol, 1)) gfx.DrawRectangle(pen, x1, y1, x2 - x1, y2 - y1);

                // fib lines
                double range = b.High - b.Low;
                foreach (var p in new float[] { 0.3f, 0.5f, 0.7f })
                {
                    bool show = (p == 0.3f && ShowThirty) || (p == 0.5f && ShowFifty) || (p == 0.7f && ShowSeventy);
                    if (!show) continue;
                    double price = b.High - range * p;
                    float yF = (float)conv.GetChartY(price);
                    using (var l = new Pen(FibLineStyle.Color, FibLineStyle.Width) { DashStyle = ConvertLineStyleToDashStyle(FibLineStyle.LineStyle) })
                        gfx.DrawLine(l, x1, yF, x2, yF);
                    DrawFibLabel(gfx, $"{(int)(p * 100)}%", x1 + 2, yF);
                }

                // emoji + date
                using (var eb = new SolidBrush(b.Key == "Morning" ? Color.Yellow : Color.CornflowerBlue))
                    gfx.DrawString(b.Label, emojiFont, eb, x1 + 5, y1 - 20, stringFormat);
                using (var dfb = new SolidBrush(DateFontColor))
                {
                    gfx.DrawString(b.Date.ToString("MM/dd"), DateFont, dfb, x1 + 5, y1 - 20 + emojiFont.Height + 2, stringFormat);
                }
            }

            // 2) inside-bar fills
            float shiftFactor = 0.40f;
            foreach (var ib in insideBarsCache)
            {
                // compute x bounds
                TimeSpan halfW = TimeSpan.FromTicks((ib.NextTime - ib.CurTime).Ticks / 2);
                var center = ib.CurTime;
                float bx1 = (float)conv.GetChartX(center - halfW);
                float bx2 = (float)conv.GetChartX(center + halfW);
                float barPx = bx2 - bx1, shiftPx = barPx * shiftFactor;
                bx1 += shiftPx; bx2 += shiftPx;

                float yHigh = (float)conv.GetChartY(ib.High);
                float yLow = (float)conv.GetChartY(ib.Low);
                float h = yLow - yHigh;

                Color col = ib.Close > ib.Open ? Color.FromArgb(100, InsideBullColor) : Color.FromArgb(100, InsideBearColor);
                using (var bsh = new SolidBrush(col))
                    gfx.FillRectangle(bsh, bx1, yHigh, bx2 - bx1, h);
            }
        }

        private DashStyle ConvertLineStyleToDashStyle(LineStyle ls) => ls switch
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
            using (var path = new GraphicsPath())
            {
                path.AddArc(rect.Left, rect.Top, rad, rad, 180, 90);
                path.AddArc(rect.Right - rad, rect.Top, rad, rad, 270, 90);
                path.AddArc(rect.Right - rad, rect.Bottom - rad, rad, rad, 0, 90);
                path.AddArc(rect.Left, rect.Bottom - rad, rad, rad, 90, 90);
                path.CloseFigure();
                using (var bg = new SolidBrush(Color.Gold)) g.FillPath(bg, path);
                using (var p = new Pen(Color.Gold)) g.DrawPath(p, path);
            }
            g.DrawString(text, fibLabelFont, Brushes.Black, x + pad, y - sz.Height / 2);
        }

        //–– Data containers
        private class SessionBox
        {
            public DateTime Date;
            public string Key;
            public string Label;
            public double High, Low;
            public bool BrokeAbove, BrokeBelow, Mitigated;
            public DateTime StartUtc, MitigationUtc;
        }
        private struct InsideBarInfo
        {
            public DateTime CurTime, NextTime;
            public double Open, High, Low, Close;
        }
    }
}
