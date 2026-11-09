/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Chummer.Desktop.ViewModels;

namespace Chummer.Desktop.Views.Sections
{
    /// <summary>
    /// Karma/nuyen over time (legacy ExpenseChart): a step line of the running total by date, with a soft fill,
    /// the lowest/highest total on the left and the first/last date underneath.
    /// </summary>
    public sealed class LedgerChart : Control
    {
        public static readonly StyledProperty<IReadOnlyList<LedgerPoint>> PointsProperty =
            AvaloniaProperty.Register<LedgerChart, IReadOnlyList<LedgerPoint>>(nameof(Points));

        public static readonly StyledProperty<IBrush> StrokeProperty =
            AvaloniaProperty.Register<LedgerChart, IBrush>(nameof(Stroke), Brushes.Cyan);

        public static readonly StyledProperty<string> ValueFormatProperty =
            AvaloniaProperty.Register<LedgerChart, string>(nameof(ValueFormat), "#,0");

        public static readonly StyledProperty<string> ValueSuffixProperty =
            AvaloniaProperty.Register<LedgerChart, string>(nameof(ValueSuffix), string.Empty);

        static LedgerChart()
        {
            AffectsRender<LedgerChart>(PointsProperty, StrokeProperty, ValueFormatProperty, ValueSuffixProperty);
        }

        public IReadOnlyList<LedgerPoint> Points
        {
            get => GetValue(PointsProperty);
            set => SetValue(PointsProperty, value);
        }

        public IBrush Stroke
        {
            get => GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public string ValueFormat
        {
            get => GetValue(ValueFormatProperty);
            set => SetValue(ValueFormatProperty, value);
        }

        public string ValueSuffix
        {
            get => GetValue(ValueSuffixProperty);
            set => SetValue(ValueSuffixProperty, value);
        }

        private static readonly IBrush s_objLabelBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x8C, 0xA6));
        private static readonly IPen s_objGridPen = new Pen(new SolidColorBrush(Color.FromRgb(0x1F, 0x2C, 0x3D)), 1);
        private static readonly Typeface s_objTypeface = new Typeface(FontFamily.Default);

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            IReadOnlyList<LedgerPoint> lstPoints = Points;
            Rect objBounds = new Rect(Bounds.Size);
            if (lstPoints == null || lstPoints.Count == 0 || objBounds.Width < 40 || objBounds.Height < 30)
                return;
            CultureInfo objCulture = GlobalSettings.CultureInfo;
            double dblMin = Math.Min(0, lstPoints.Min(x => x.Value));
            double dblMax = lstPoints.Max(x => x.Value);
            if (dblMax <= dblMin)
                dblMax = dblMin + 1;
            string strMax = dblMax.ToString(ValueFormat, objCulture) + ValueSuffix;
            string strMin = dblMin.ToString(ValueFormat, objCulture) + ValueSuffix;
            FormattedText txtMax = Label(strMax);
            FormattedText txtMin = Label(strMin);
            double dblLeft = Math.Max(txtMax.Width, txtMin.Width) + 8;
            const double dblBottomLabels = 16;
            Rect objPlot = new Rect(dblLeft, 4, Math.Max(10, objBounds.Width - dblLeft - 4), Math.Max(10, objBounds.Height - dblBottomLabels - 6));

            long lngStart = lstPoints[0].When.Ticks;
            long lngEnd = lstPoints[lstPoints.Count - 1].When.Ticks;
            if (lngEnd <= lngStart)
                lngEnd = lngStart + TimeSpan.TicksPerDay;
            double X(DateTime datWhen) => objPlot.Left + objPlot.Width * (datWhen.Ticks - lngStart) / (double)(lngEnd - lngStart);
            double Y(double dblValue) => objPlot.Bottom - objPlot.Height * (dblValue - dblMin) / (dblMax - dblMin);

            // Grid: top, middle, bottom.
            for (int i = 0; i <= 2; ++i)
            {
                double dblY = Math.Round(objPlot.Top + objPlot.Height * i / 2) + 0.5;
                context.DrawLine(s_objGridPen, new Point(objPlot.Left, dblY), new Point(objPlot.Right, dblY));
            }
            context.DrawText(txtMax, new Point(dblLeft - 6 - txtMax.Width, objPlot.Top - 2));
            context.DrawText(txtMin, new Point(dblLeft - 6 - txtMin.Width, objPlot.Bottom - txtMin.Height + 2));

            // Step line: the total stays flat until the next entry.
            List<Point> lstLine = new List<Point>(lstPoints.Count * 2);
            for (int i = 0; i < lstPoints.Count; ++i)
            {
                Point objPoint = new Point(X(lstPoints[i].When), Y(lstPoints[i].Value));
                if (i > 0)
                    lstLine.Add(new Point(objPoint.X, lstLine[lstLine.Count - 1].Y));
                lstLine.Add(objPoint);
            }
            IBrush objStroke = Stroke ?? Brushes.Cyan;
            Color objColor = objStroke is ISolidColorBrush objSolid ? objSolid.Color : Colors.Cyan;

            StreamGeometry objArea = new StreamGeometry();
            using (StreamGeometryContext objContext = objArea.Open())
            {
                objContext.BeginFigure(new Point(lstLine[0].X, objPlot.Bottom), true);
                foreach (Point objPoint in lstLine)
                    objContext.LineTo(objPoint);
                objContext.LineTo(new Point(lstLine[lstLine.Count - 1].X, objPlot.Bottom));
                objContext.EndFigure(true);
            }
            context.DrawGeometry(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x55, objColor.R, objColor.G, objColor.B), 0),
                    new GradientStop(Color.FromArgb(0x05, objColor.R, objColor.G, objColor.B), 1)
                }
            }, null, objArea);

            StreamGeometry objLine = new StreamGeometry();
            using (StreamGeometryContext objContext = objLine.Open())
            {
                objContext.BeginFigure(lstLine[0], false);
                for (int i = 1; i < lstLine.Count; ++i)
                    objContext.LineTo(lstLine[i]);
                objContext.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(objStroke, 2, lineJoin: PenLineJoin.Round), objLine);
            Point objLast = lstLine[lstLine.Count - 1];
            context.DrawEllipse(objStroke, null, objLast, 3.5, 3.5);

            string strDateFormat = objCulture.DateTimeFormat.ShortDatePattern;
            FormattedText txtStart = Label(lstPoints[0].When.ToString(strDateFormat, objCulture));
            FormattedText txtEnd = Label(lstPoints[lstPoints.Count - 1].When.ToString(strDateFormat, objCulture));
            context.DrawText(txtStart, new Point(objPlot.Left, objPlot.Bottom + 3));
            if (objPlot.Width > txtStart.Width + txtEnd.Width + 20)
                context.DrawText(txtEnd, new Point(objPlot.Right - txtEnd.Width, objPlot.Bottom + 3));
        }

        private static FormattedText Label(string strText)
            => new FormattedText(strText ?? string.Empty, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, s_objTypeface, 10, s_objLabelBrush);
    }
}
