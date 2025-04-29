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
        private HistoricalData hoursHistory;

        //–– Session times
        [InputParameter("Morning Session Start Time")]
        private TimeSpan MorningStart = new TimeSpan(9, 0, 0);
        [InputParameter("Morning Session End Time")]
        private TimeSpan MorningEnd = new TimeSpan(10, 0, 0);
        [InputParameter("Afternoon Session Start Time")]
        private TimeSpan AfternoonStart = new TimeSpan(15, 0, 0);
        [InputParameter("Afternoon Session End Time")]
        private TimeSpan AfternoonEnd = new TimeSpan(16, 0, 0);
        // –– How many days of hourly bars to scan (so you can show older boxes)
        [InputParameter("History Lookback (days)", 6)]
        private int HistoryLookbackDays = 30;
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

        //–– Colors & line style
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

        //–– New configurable date style
        private Font DateFont = new Font("Segoe UI", 8, FontStyle.Bold);
        private Color DateFontColor = Color.White;

        //–– New limits
        [InputParameter("Max Unmitigated Boxes", 4)]
        private int MaxUnmitigatedBoxes = 5;
        [InputParameter("Max Mitigated Boxes", 5)]
        private int MaxMitigatedBoxes = 5;

        public Time_Based_Range_Fibs_DTFX() : base()
        {
            Name = "Time_Based_Range_Fibs_DTFX3";
            Description = "Overlays session-range boxes and Fibonacci levels, extending until 100% mitigation.";
            SeparateWindow = false;
        }

        protected override void OnInit()
        {
            fibLabelFont = new Font("Segoe UI", 8, FontStyle.Bold);
            // now pulls HistoryLookbackDays worth of 1-hour bars
            hoursHistory = Symbol.GetHistory(
                Period.HOUR1,
                Symbol.HistoryType,
                DateTime.UtcNow.AddDays(-HistoryLookbackDays)
            );
            emojiFont = new Font("Segoe UI Emoji", 12, FontStyle.Bold);
            stringFormat = new StringFormat()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
        }

        public override IList<SettingItem> Settings
        {
            get
            {
                // allow editing of session times
                var settings = base.Settings;
                var sep = settings.FirstOrDefault()?.SeparatorGroup;

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

                // ... after your DateTime settings:
                settings.Add(new SettingItemFont("Date Font", DateFont)
                {
                    SeparatorGroup = sep
                });
                settings.Add(new SettingItemColor("Date Font Color", DateFontColor)
                {
                    SeparatorGroup = sep
                });

                // History look-back, default = 5
                settings.Add(new SettingItemInteger(
                    "History Lookback (days)",
                    HistoryLookbackDays
                )
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
                if (value.TryGetValue("History Lookback (days)", out int d)) HistoryLookbackDays = d;

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
            DateTime rightTime = conv.GetTime(wnd.ClientRectangle.Right);
            var estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

            // 1) Build list of session‐boxes from your 1H data
            var sessions = new List<SessionBox>();
            for (int i = 0; i < hoursHistory.Count; i++)
            {
                if (hoursHistory[i, SeekOriginHistory.Begin] is not HistoryItemBar bar1h)
                    continue;

                // convert that 1H bar’s timestamp into EST
                DateTime estTime = TimeZoneInfo.ConvertTime(bar1h.TimeLeft, TimeZoneInfo.Utc, estZone);
                DateTime date = estTime.Date;

                foreach (var sess in new[]
                {
            new { Label="☀️", Start=MorningStart,   End=MorningEnd,   Show=ShowMorningBox,   Key="Morning"   },
            new { Label="🏧", Start=AfternoonStart, End=AfternoonEnd, Show=ShowAfternoonBox, Key="Afternoon" }
        })
                {
                    if (!sess.Show || estTime.TimeOfDay != sess.Start)
                        continue;

                    // avoid duplicates
                    if (sessions.Any(s => s.Date == date && s.Key == sess.Key))
                        continue;

                    // session start/end in EST and UTC
                    DateTime sEst = date.Add(sess.Start), eEst = date.Add(sess.End);
                    DateTime sUtc = TimeZoneInfo.ConvertTimeToUtc(sEst, estZone),
                             eUtc = TimeZoneInfo.ConvertTimeToUtc(eEst, estZone);

                    // 1a) high/low from that exact 1H bar
                    double high = bar1h.High, low = bar1h.Low;

                    // 1b) first breakout in the 1H series
                    bool up = false, down = false;
                    int firstIdx = -1;
                    for (int j = i + 1; j < hoursHistory.Count; j++)
                    {
                        if (hoursHistory[j, SeekOriginHistory.Begin] is not HistoryItemBar b) continue;
                        if (b.Close > high) { up = true; firstIdx = j; break; }
                        if (b.Close < low) { down = true; firstIdx = j; break; }
                    }

                    // 1c) 100% mitigation scan
                    bool mitigated = false;
                    DateTime mitUtc = DateTime.MinValue;
                    if (firstIdx >= 0)
                    {
                        for (int k = firstIdx + 1; k < hoursHistory.Count; k++)
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
                        MitigationUtc = mitUtc
                    });
                }
            }

            // 2) apply your user-limits
            var unmit = sessions.Where(b => !b.Mitigated)
                               .OrderByDescending(b => b.Date)
                               .Take(MaxUnmitigatedBoxes);
            var mit = sessions.Where(b => b.Mitigated)
                               .OrderByDescending(b => b.Date)
                               .Take(MaxMitigatedBoxes);
            var toDraw = unmit.Concat(mit)
                              .OrderBy(b => b.Date)
                              .ThenBy(b => b.Key);

            // 3) finally—draw all boxes & fibs & date/emoji on whatever timeframe
            foreach (var b in toDraw)
            {
                float x1 = (float)conv.GetChartX(b.StartUtc);
                DateTime endUtc = b.Mitigated ? b.MitigationUtc : rightTime;
                float x2 = (float)conv.GetChartX(endUtc);
                float y1 = (float)conv.GetChartY(b.High);
                float y2 = (float)conv.GetChartY(b.Low);

                Color col = b.BrokeAbove ? BullBoxColor
                          : b.BrokeBelow ? BearBoxColor
                          : Color.Gray;
                using var fill = new SolidBrush(Color.FromArgb(60, col));
                gfx.FillRectangle(fill, x1, y1, x2 - x1, y2 - y1);
                using var pen = new Pen(col, 1);
                gfx.DrawRectangle(pen, x1, y1, x2 - x1, y2 - y1);

                double range = b.High - b.Low;
                foreach (var p in new float[] { 0.3f, 0.5f, 0.7f })
                {
                    bool show = (p == 0.3f && ShowThirty)
                             || (p == 0.5f && ShowFifty)
                             || (p == 0.7f && ShowSeventy);
                    if (!show) continue;

                    double price = b.High - range * p;
                    float yf = (float)conv.GetChartY(price);
                    using var l = new Pen(FibLineStyle.Color, FibLineStyle.Width)
                    { DashStyle = ConvertLineStyleToDashStyle(FibLineStyle.LineStyle) };
                    gfx.DrawLine(l, x1, yf, x2, yf);
                    DrawFibLabel(gfx, $"{(int)(p * 100)}%", x1 + 2, yf);
                }

                // draw the emoji & date beneath
                float ex = x1 + 5, ey = y1 - 20;
                using var eb = new SolidBrush(b.Key == "Morning" ? Color.Yellow : Color.CornflowerBlue);
                gfx.DrawString(b.Label, emojiFont, eb, ex, ey, stringFormat);

                string dt = b.Date.ToString("MM/dd");
                using var db = new SolidBrush(DateFontColor);
                float dy = ey + emojiFont.Height + 2;
                gfx.DrawString(dt, DateFont, db, ex, dy, stringFormat);
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

        // helper class at bottom of file
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
