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
        private Font emojiFont;
        private StringFormat stringFormat;
        private Font fibLabelFont;

        [InputParameter("Morning Session Start Time")]
        private TimeSpan MorningStart = new TimeSpan(9, 0, 0);

        [InputParameter("Morning Session End Time")]
        private TimeSpan MorningEnd = new TimeSpan(10, 0, 0);

        [InputParameter("Afternoon Session Start Time")]
        private TimeSpan AfternoonStart = new TimeSpan(15, 0, 0);

        [InputParameter("Afternoon Session End Time")]
        private TimeSpan AfternoonEnd = new TimeSpan(16, 0, 0);

        [InputParameter("Show Morning Box")]
        private bool ShowMorningBox = true;

        [InputParameter("Show Afternoon Box")]
        private bool ShowAfternoonBox = true;

        [InputParameter("Show 30% Retracement")]
        private bool Show_Thirty_Retracements = true;

        [InputParameter("Show 50% Retracement")]
        private bool Show_Fifty_Retracements = true;

        [InputParameter("Show 70% Retracement")]
        private bool Show_Seventy_Retracements = true;

        [InputParameter("Bullish Box Color", 1)]
        public Color BullBoxColor = Color.LimeGreen;

        [InputParameter("Bearish Box Color", 2)]
        public Color BearBoxColor = Color.Red;

        [InputParameter("Fib Line Color", 3)]
        private LineOptions LineOptions_Fib = new LineOptions()
        {
            Color = Color.Yellow,
            LineStyle = LineStyle.Dash,
            Width = 1,
            WithCheckBox = false
        };

        [InputParameter("Extension Length (in pixels)", 10)]
        private int ExtensionLength = 150;

        private HistoricalData hoursHistory;

        public Time_Based_Range_Fibs_DTFX()
            : base()
        {
            Name = "Time_Based_Range_Fibs_DTFX3";
            Description = "This indicator overlays time-specific price range boxes and Fibonacci retracement levels on your chart.";
            SeparateWindow = false;
        }

        protected override void OnInit()
        {
            this.fibLabelFont = new Font("Segoe UI", 8, FontStyle.Bold);
            this.hoursHistory = this.Symbol.GetHistory(Period.HOUR1, this.Symbol.HistoryType, DateTime.UtcNow.AddDays(-5));
            this.emojiFont = new Font("Segoe UI Emoji", 12, FontStyle.Bold);
            this.stringFormat = new StringFormat()
            {
                LineAlignment = StringAlignment.Center,
                Alignment = StringAlignment.Center
            };

        }


        public override IList<SettingItem> Settings
        {
            get
            {
                var settings = base.Settings;
                var defaultSeparator = settings.FirstOrDefault()?.SeparatorGroup;

                settings.Add(new SettingItemDateTime("Morning Session Start Time", DateTime.Today.Add(MorningStart))
                {
                    SeparatorGroup = defaultSeparator,
                    Format = DatePickerFormat.Time
                });

                settings.Add(new SettingItemDateTime("Morning Session End Time", DateTime.Today.Add(MorningEnd))
                {
                    SeparatorGroup = defaultSeparator,
                    Format = DatePickerFormat.Time
                });

                settings.Add(new SettingItemDateTime("Afternoon Session Start Time", DateTime.Today.Add(AfternoonStart))
                {
                    SeparatorGroup = defaultSeparator,
                    Format = DatePickerFormat.Time
                });

                settings.Add(new SettingItemDateTime("Afternoon Session End Time", DateTime.Today.Add(AfternoonEnd))
                {
                    SeparatorGroup = defaultSeparator,
                    Format = DatePickerFormat.Time
                });

                return settings;
            }

            set
            {
                base.Settings = value;

                if (value.TryGetValue("Morning Session Start Time", out DateTime morningStart))
                    MorningStart = morningStart.TimeOfDay;

                if (value.TryGetValue("Morning Session End Time", out DateTime morningEnd))
                    MorningEnd = morningEnd.TimeOfDay;

                if (value.TryGetValue("Afternoon Session Start Time", out DateTime afternoonStart))
                    AfternoonStart = afternoonStart.TimeOfDay;

                if (value.TryGetValue("Afternoon Session End Time", out DateTime afternoonEnd))
                    AfternoonEnd = afternoonEnd.TimeOfDay;

            }
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {

            base.OnPaintChart(args);

            if (this.CurrentChart == null)
                return;

            Graphics graphics = args.Graphics;
            var mainWindow = this.CurrentChart.MainWindow;

            DateTime leftTime = mainWindow.CoordinatesConverter.GetTime(mainWindow.ClientRectangle.Left);
            DateTime rightTime = mainWindow.CoordinatesConverter.GetTime(mainWindow.ClientRectangle.Right);
            TimeZoneInfo estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

            HashSet<(DateTime Date, string SessionType)> drawnSessions = new();

            for (int i = 0; i < this.HistoricalData.Count; i++)
            {
                if (this.HistoricalData[i, SeekOriginHistory.Begin] is not HistoryItemBar bar)
                    continue;

                DateTime estTime = TimeZoneInfo.ConvertTime(bar.TimeLeft, TimeZoneInfo.Utc, estZone);
                DateTime date = estTime.Date;

                foreach (var session in new[]
                {
            new { Label = "☀️", Start = MorningStart, End = MorningEnd, Show = ShowMorningBox, Key = "Morning" },
            new { Label = "🏧", Start = AfternoonStart, End = AfternoonEnd, Show = ShowAfternoonBox, Key = "Afternoon" }
        })
                {
                    if (!session.Show || estTime.TimeOfDay != session.Start)
                        continue;

                    var sessionKey = (date, session.Key);
                    if (drawnSessions.Contains(sessionKey))
                        continue;

                    drawnSessions.Add(sessionKey);

                    DateTime sessionStartEst = date.Add(session.Start);
                    DateTime sessionEndEst = date.Add(session.End);
                    DateTime sessionStartUtc = TimeZoneInfo.ConvertTimeToUtc(sessionStartEst, estZone);
                    DateTime sessionEndUtc = TimeZoneInfo.ConvertTimeToUtc(sessionEndEst, estZone);

                    // Get high and low within the session and the first candle for direction
                    double high = double.MinValue;
                    double low = double.MaxValue;
                    HistoryItemBar? firstBarInSession = null;

                    for (int j = 0; j < this.HistoricalData.Count; j++)
                    {
                        if (this.HistoricalData[j, SeekOriginHistory.Begin] is not HistoryItemBar b)
                            continue;

                        DateTime bEstTime = TimeZoneInfo.ConvertTime(b.TimeLeft, TimeZoneInfo.Utc, estZone);

                        if (bEstTime >= sessionStartEst && bEstTime < sessionEndEst)
                        {
                            high = Math.Max(high, b.High);
                            low = Math.Min(low, b.Low);

                            if (firstBarInSession == null)
                                firstBarInSession = b;
                        }
                    }

                    if (high == double.MinValue || low == double.MaxValue || firstBarInSession == null)
                        continue;

                    // Determine breakout direction after session ends
                    bool brokeAbove = false;
                    bool brokeBelow = false;

                    for (int j = 0; j < this.HistoricalData.Count; j++)
                    {
                        if (this.HistoricalData[j, SeekOriginHistory.Begin] is not HistoryItemBar b)
                            continue;

                        DateTime bEstTime = TimeZoneInfo.ConvertTime(b.TimeLeft, TimeZoneInfo.Utc, estZone);
                        if (bEstTime <= sessionEndEst)
                            continue;

                        if (b.Close > high)
                        {
                            brokeAbove = true;
                            break;
                        }
                        else if (b.Close < low)
                        {
                            brokeBelow = true;
                            break;
                        }
                    }

                    Color sessionColor = Color.Gray; // fallback if no breakout
                    if (brokeAbove)
                        sessionColor = BullBoxColor;
                    else if (brokeBelow)
                        sessionColor = BearBoxColor;

                    Color fillColor = Color.FromArgb(60, sessionColor);

                    float xStart = (float)mainWindow.CoordinatesConverter.GetChartX(sessionStartUtc);
                    float xEnd = xStart + ExtensionLength;

                    float yHigh = (float)mainWindow.CoordinatesConverter.GetChartY(high);
                    float yLow = (float)mainWindow.CoordinatesConverter.GetChartY(low);
                    float rectHeight = yLow - yHigh;

                    using (SolidBrush fill = new SolidBrush(fillColor))
                        graphics.FillRectangle(fill, xStart, yHigh, xEnd - xStart, rectHeight);

                    using (Pen pen = new Pen(sessionColor, 1))
                    {
                        graphics.DrawLine(pen, xStart, yHigh, xEnd, yHigh);
                        graphics.DrawLine(pen, xStart, yLow, xEnd, yLow);
                    }

                    // Draw Fib levels
                    double range = high - low;
                    float[] levels = new float[] { 0.3f, 0.5f, 0.7f };

                    foreach (var p in levels)
                    {
                        if ((p == 0.3f && Show_Thirty_Retracements) ||
                            (p == 0.5f && Show_Fifty_Retracements) ||
                            (p == 0.7f && Show_Seventy_Retracements))
                        {
                            double fib = high - (range * p);
                            float yFib = (float)mainWindow.CoordinatesConverter.GetChartY(fib);

                            graphics.DrawLine(new Pen(LineOptions_Fib.Color, LineOptions_Fib.Width)
                            {
                                DashStyle = ConvertLineStyleToDashStyle(LineOptions_Fib.LineStyle)
                            }, xStart, yFib, xEnd, yFib);

                            DrawFibLabel(graphics, $"{(int)(p * 100)}%", xStart - 40, yFib);
                        }
                    }

                    // Emoji
                    float emojiY = yHigh - 20;
                    float xCenter = xStart + 5; // align emoji directly above session start candle
                    graphics.DrawString(session.Label, emojiFont, Brushes.CornflowerBlue, xCenter, emojiY, stringFormat);

                }
            }
        }





        private DashStyle ConvertLineStyleToDashStyle(LineStyle lineStyle)
        {
            switch (lineStyle)
            {
                case LineStyle.Solid:
                    return DashStyle.Solid;
                case LineStyle.Dash:
                    return DashStyle.Dash;
                case LineStyle.Dot:
                    return DashStyle.Dot;
                case LineStyle.DashDot:
                    return DashStyle.DashDot;
                default:
                    return DashStyle.Solid;
            }
        }

        private void DrawFibLabel(Graphics g, string text, float x, float y)
        {
            var padding = 4;
            var bgColor = Color.Gold;
            var textColor = Color.Black;

            SizeF textSize = g.MeasureString(text, fibLabelFont);
            RectangleF rect = new RectangleF(x, y - textSize.Height / 2, textSize.Width + padding * 2, textSize.Height);

            using (GraphicsPath path = new GraphicsPath())
            {
                int radius = 6;
                path.AddArc(rect.Left, rect.Top, radius, radius, 180, 90);
                path.AddArc(rect.Right - radius, rect.Top, radius, radius, 270, 90);
                path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
                path.AddArc(rect.Left, rect.Bottom - radius, radius, radius, 90, 90);
                path.CloseFigure();

                using (SolidBrush brush = new SolidBrush(bgColor))
                    g.FillPath(brush, path);

                using (Pen pen = new Pen(bgColor))
                    g.DrawPath(pen, path);
            }

            g.DrawString(text, fibLabelFont, new SolidBrush(textColor), x + padding, y - textSize.Height / 2);
        }
    }
}
