/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Chummer.Desktop.Converters
{
    /// <summary>Filled / empty condition monitor box.</summary>
    public sealed class BoolBrushConverter : IValueConverter
    {
        private readonly IBrush _objOn;

        private BoolBrushConverter(Color objOn)
        {
            _objOn = new SolidColorBrush(objOn);
        }

        public static BoolBrushConverter Red { get; } = new BoolBrushConverter(Color.Parse("#FF3B5C"));
        public static BoolBrushConverter Cyan { get; } = new BoolBrushConverter(Color.Parse("#00E5FF"));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool blnOn && blnOn ? _objOn : Brushes.Transparent;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
