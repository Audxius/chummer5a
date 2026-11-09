/*  This file is part of Chummer5a.
 *
 *  Chummer5a is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Chummer5a is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with Chummer5a.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  You can obtain the full source code for Chummer5a at
 *  https://github.com/chummer5a/chummer5a
 */

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Chummer.Desktop.Converters
{
    /// <summary>
    /// "Icon.Foo" resource key -> geometry, for icons chosen by view models (attribute, skill category, item type).
    /// </summary>
    public sealed class IconKeyConverter : IValueConverter
    {
        public static IconKeyConverter Instance { get; } = new IconKeyConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string strKey && Application.Current != null
                && (Application.Current.TryGetResource(strKey, Application.Current.ActualThemeVariant, out object objResource) || Application.Current.TryGetResource("Icon.Gear", Application.Current.ActualThemeVariant, out objResource)))
                return objResource as Geometry;
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
