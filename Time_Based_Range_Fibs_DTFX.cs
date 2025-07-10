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
        //── Drawing helpers & cache fields ────────────────────────────────────────
        private Font emojiFont;
        private Font fibLabelFont;
        private StringFormat stringFormat;
        private Font DateFont = new Font("Segoe UI", 8, FontStyle.Bold);
        private Color DateFontColor = Color.White;

        private HistoricalData hoursHistory;
        private readonly TimeZoneInfo estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        // cache the last hour we rebuilt sessions, and the sessions themselves
        private int _lastHistoryHour = -1;
        private List<SessionBox> _cachedSessions = new List<SessionBox>();

        //── Session times (now fixed — no longer user inputs) ────────────────────
        private TimeSpan MorningStart = new TimeSpan(9, 0, 0);
        private TimeSpan MorningEnd = new TimeSpan(10, 0, 0);
        private TimeSpan AfternoonStart = new TimeSpan(15, 0, 0);
        private TimeSpan AfternoonEnd = new TimeSpan(16, 0, 0);

        //── Other inputs ─────────────────────────────────────────────────────────
        [InputParameter("History Lookback (days)", 4)]
        public int HistoryLookbackDays { get; set; } = 5;

        [InputParameter("Show Morning Box", 5)]
        public bool ShowMorningBox { get; set; } = true;
        [InputParameter("Show Afternoon Box", 6)]
        public bool ShowAfternoonBox { get; set; } = true;

        [InputParameter("Show Profitable Loser Box", 7)]
        public bool ShowProfitableLoserBox { get; set; } = true;

        [InputParameter("Fill Session Boxes", 8)]
        public bool FillSessionBoxes = true;

        [InputParameter("Turn Off All Fibs", 9)]
        public bool TurnOffAllFibs { get; set; } = false;
        [InputParameter("Show 30% Retracement", 10)]
        public bool ShowThirty { get; set; } = true;
        [InputParameter("Show 50% Retracement", 11)]
        public bool ShowFifty { get; set; } = true;
        [InputParameter("Show 70% Retracement", 12)]
        public bool ShowSeventy { get; set; } = true;

        [InputParameter("Morning Label Emoji", 13)]
        public string MorningLabelEmoji { get; set; } = "9";
        [InputParameter("Afternoon Label Emoji", 14)]
        public string AfternoonLabelEmoji { get; set; } = "3";

        [InputParameter("Show Date Label", 15)]
        public bool ShowDateLabel { get; set; } = true;

        [InputParameter("Bullish Box Color", 16)]
        public Color BullBoxColor { get; set; } = Color.FromArgb(51, 0x4C, 0xAF, 0x50);
        [InputParameter("Bearish Box Color", 17)]
        public Color BearBoxColor { get; set; } = Color.FromArgb(51, 0xF2, 0x36, 0x45);

        [InputParameter("Fib Line Color", 18)]
        public LineOptions FibLineStyle { get; set; } = new LineOptions()
        {
            Color = Color.FromArgb(253, 216, 53),
            LineStyle = LineStyle.Dash,
            Width = 1,
            WithCheckBox = false
        };

        [InputParameter("Max Unmitigated Boxes", 19)]
        public int MaxUnmitigatedBoxes { get; set; } = 5;
        [InputParameter("Max Mitigated Boxes", 20)]
        public int MaxMitigatedBoxes { get; set; } = 0;

        
        public Time_Based_Range_Fibs_DTFX()
        {
            Name = "Time_Based_Range_Fibs_DTFX";
            Description = "Overlays session‐range boxes + fib levels (EST). Added: Turn Off All Fibs flag, custom emojis, toggle short date label.";
            SeparateWindow = false;
            OnBackGround = true;
        }

        protected override void OnInit()
        {
            fibLabelFont = new Font("Segoe UI", 8, FontStyle.Regular);
            emojiFont = new Font("Segoe UI Emoji", 8, FontStyle.Bold);
            stringFormat = new StringFormat()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            // initial load
            hoursHistory = Symbol.GetHistory(
                Period.HOUR1,
                Symbol.HistoryType,
                DateTime.UtcNow.AddDays(-HistoryLookbackDays)
            );
        }

        protected override void OnSettingsUpdated()
        {
            base.OnSettingsUpdated();
            // force a rebuild on the very next OnPaintChart
            _lastHistoryHour = -1;
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            base.OnPaintChart(args);
            if (CurrentChart == null)
                return;

            var gfx = args.Graphics;
            var wnd = CurrentChart.MainWindow;
            var conv = wnd.CoordinatesConverter;
            gfx.SmoothingMode = SmoothingMode.None;
            gfx.PixelOffsetMode = PixelOffsetMode.HighSpeed;

            // compute now in EST
            DateTime nowEst = TimeZoneInfo.ConvertTime(DateTime.UtcNow, estZone);
            DateTime rightEdgeUtc = conv.GetTime(wnd.ClientRectangle.Right);

            // ── Throttle history reload & session building to once per EST hour ──
            if (_lastHistoryHour != nowEst.Hour)
            {
                // reload history
                hoursHistory = Symbol.GetHistory(
                    Period.HOUR1,
                    Symbol.HistoryType,
                    DateTime.UtcNow.AddDays(-HistoryLookbackDays)
                );
                _lastHistoryHour = nowEst.Hour;

                // rebuild session list
                _cachedSessions.Clear();

                // find each session’s high/low, breakout, mitigation
                for (int i = 0; i < hoursHistory.Count; i++)
                {
                    if (hoursHistory[i, SeekOriginHistory.Begin] is not HistoryItemBar bar)
                        continue;

                    var bEst = TimeZoneInfo.ConvertTime(bar.TimeLeft, TimeZoneInfo.Utc, estZone);
                    var date = bEst.Date;

                    // two sessions: morning & afternoon
                    for (int si = 0; si < 2; si++)
                    {
                        var key = (si == 0) ? "Morning" : "Afternoon";
                        var start = (si == 0) ? MorningStart : AfternoonStart;
                        var end = (si == 0) ? MorningEnd : AfternoonEnd;
                        var show = (si == 0) ? ShowMorningBox : ShowAfternoonBox;
                        var emoji = (si == 0) ? MorningLabelEmoji : AfternoonLabelEmoji;

                        if (!show || bEst.TimeOfDay < start || bEst.TimeOfDay >= end)
                            continue;

                        // avoid duplicates
                        if (_cachedSessions.Any(s => s.Date == date && s.Key == key))
                            continue;

                        // session bounds in UTC
                        DateTime sUtc = TimeZoneInfo.ConvertTimeToUtc(date.Add(start), estZone);
                        DateTime eUtc = TimeZoneInfo.ConvertTimeToUtc(date.Add(end), estZone);

                        // compute high/low
                        double high = double.MinValue, low = double.MaxValue;
                        for (int j = 0; j < hoursHistory.Count; j++)
                        {
                            if (hoursHistory[j, SeekOriginHistory.Begin] is not HistoryItemBar b2)
                                continue;
                            var tEst2 = TimeZoneInfo.ConvertTime(b2.TimeLeft, TimeZoneInfo.Utc, estZone);
                            if (tEst2 < date.Add(start) || tEst2 >= date.Add(end))
                                continue;
                            high = Math.Max(high, b2.High);
                            low = Math.Min(low, b2.Low);
                        }

                        // breakout detection (include bar ending exactly at session end)
                        bool up = false, down = false;
                        int fbIdx = -1;
                        for (int j = 0; j < hoursHistory.Count; j++)
                        {
                            if (hoursHistory[j, SeekOriginHistory.Begin] is not HistoryItemBar b2)
                                continue;
                            var tEst2 = TimeZoneInfo.ConvertTime(b2.TimeLeft, TimeZoneInfo.Utc, estZone);
                            if (tEst2 < date.Add(end))
                                continue;
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
                                if (hoursHistory[k, SeekOriginHistory.Begin] is not HistoryItemBar mb)
                                    continue;
                                if (up && mb.Close < low) { mitigated = true; mitUtc = mb.TimeLeft; break; }
                                if (down && mb.Close > high) { mitigated = true; mitUtc = mb.TimeLeft; break; }
                            }
                        }

                        // for today’s still-open session, peek at last bar close
                        if (date == nowEst.Date && !up && !down)
                        {
                            for (int k = hoursHistory.Count - 1; k >= 0; k--)
                            {
                                if (hoursHistory[k, SeekOriginHistory.Begin] is HistoryItemBar hb)
                                {
                                    if (hb.Close > high) up = true;
                                    else if (hb.Close < low) down = true;
                                    break;
                                }
                            }
                        }

                        // stash it
                        _cachedSessions.Add(new SessionBox
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
            }

            // ── now DRAW from the cached list ────────────────────────────────────────
            var unmitList = _cachedSessions
                .Where(s => !s.Mitigated)
                .OrderByDescending(s => s.Date)
                .Take(MaxUnmitigatedBoxes)
                .ToList();

            var mitList = _cachedSessions
                .Where(s => s.Mitigated)
                .OrderByDescending(s => s.Date)
                .Take(MaxMitigatedBoxes)
                .ToList();

            var toDraw = unmitList
                .Concat(mitList)
                .OrderBy(s => s.Date)
                .ThenBy(s => s.Key)
                .ToList();

            foreach (var b in toDraw)
            {
                float x1 = (float)conv.GetChartX(b.StartUtc);
                DateTime limitUtc = b.Mitigated ? b.MitigationUtc : rightEdgeUtc;
                float x2 = (float)conv.GetChartX(limitUtc);
                float y1 = (float)conv.GetChartY(b.High);
                float y2 = (float)conv.GetChartY(b.Low);

                // box fill
                var col = b.BrokeAbove ? BullBoxColor
                       : b.BrokeBelow ? BearBoxColor
                       : Color.Gray;
                if (FillSessionBoxes)
                {
                    using (var fbBrush = new SolidBrush(Color.FromArgb(30, col)))
                        gfx.FillRectangle(fbBrush, x1, y1, x2 - x1, y2 - y1);
                }
                // border
                int brighten = 1;
                var bright = Color.FromArgb(
                    255,
                    Math.Min(col.R + brighten, 255),
                    Math.Min(col.G + brighten, 255),
                    Math.Min(col.B + brighten, 255)
                );
                using (var pen = new Pen(bright, 2))
                    gfx.DrawRectangle(pen, x1, y1, x2 - x1, y2 - y1);

                // fib lines
                double range = b.High - b.Low;
                for (int pi = 0; pi < 3; pi++)
                {
                    double p = (pi == 0) ? 0.3 : (pi == 1) ? 0.5 : 0.7;
                    bool show = !TurnOffAllFibs &&
                                ((pi == 0 && ShowThirty) ||
                                 (pi == 1 && ShowFifty) ||
                                 (pi == 2 && ShowSeventy));
                    if (!show) continue;

                    float yF = (float)conv.GetChartY(
                        b.BrokeBelow
                        ? b.Low + range * p
                        : b.High - range * p
                    );
                    float dashLen = 5f;
                    float gapLen = 7f;

                    using var lp = new Pen(FibLineStyle.Color, FibLineStyle.Width)
                    {
                        DashStyle = DashStyle.Custom,
                        DashPattern = new float[] { dashLen, gapLen },
                        DashCap = DashCap.Flat
                    };
                    gfx.DrawLine(lp, x1, yF, x2, yF);
                    DrawFibLabel(gfx, $"{(int)(p * 100)}%", x1 + 2, yF);
                }

                // emoji & optional date
                float ex = x1 + 5, ey = y1 - 20;
                using (var eb = new SolidBrush(b.Key == "Morning" ? Color.Yellow : Color.CornflowerBlue))
                    gfx.DrawString(b.Label, emojiFont, eb, ex, ey, stringFormat);

                if (ShowDateLabel)
                {
                    string dt = b.Date.ToString("M/d");
                    using (var db = new SolidBrush(DateFontColor))
                        gfx.DrawString(dt, DateFont, db, ex, ey + emojiFont.Height + 2, stringFormat);
                }
            }
            if (ShowProfitableLoserBox)
            {
                // Get the most recent bar from 6PM–7PM EST
                DateTime? latest6pmSessionDate = null;
                double? open = null, close = null;
                for (int i = hoursHistory.Count - 1; i >= 0; i--)
                {
                    if (hoursHistory[i, SeekOriginHistory.Begin] is not HistoryItemBar bar)
                        continue;

                    var estTime = TimeZoneInfo.ConvertTime(bar.TimeLeft, TimeZoneInfo.Utc, estZone);
                    if (estTime.TimeOfDay == new TimeSpan(18, 0, 0)) // ✅ bar starting at 6:00 PM EST
                    {
                        latest6pmSessionDate = estTime.Date;
                        open = bar.Open;
                        close = bar.Close;
                        break;
                    }
                }

                if (latest6pmSessionDate.HasValue && open.HasValue && close.HasValue)
                {
                    DateTime sessionStartUtc = TimeZoneInfo.ConvertTimeToUtc(latest6pmSessionDate.Value.AddHours(18), estZone);
                    float x1 = (float)conv.GetChartX(sessionStartUtc);
                    float x2 = wnd.ClientRectangle.Right;
                    float y1 = (float)conv.GetChartY(open.Value);
                    float y2 = (float)conv.GetChartY(close.Value);

                    float topY = Math.Min(y1, y2);
                    float height = Math.Abs(y2 - y1);

                    if (FillSessionBoxes)
                    {
                        using (var fillBrush = new SolidBrush(Color.FromArgb(15, 0x00, 0x00, 0xFF))) // 16% transparent
                            gfx.FillRectangle(fillBrush, x1, topY, x2 - x1, height);
                    }
                    using (var borderPen = new Pen(Color.FromArgb(255, 0x29, 0x62, 0xFF), 2))
                        gfx.DrawRectangle(borderPen, x1, topY, x2 - x1, height);
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
            using (var bg = new SolidBrush(Color.FromArgb(220, 255, 215, 0))) g.FillPath(bg, path);
            using (var p = new Pen(Color.FromArgb(220, 255, 215, 0))) g.DrawPath(p, path);
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
