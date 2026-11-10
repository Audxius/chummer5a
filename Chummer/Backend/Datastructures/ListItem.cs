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
using System.Collections;
using System.Diagnostics;
using System.Globalization;

namespace Chummer
{
    /// <summary>
    /// ListItem class to make populating a DropDownList from a DataSource easier.
    /// </summary>
    [DebuggerDisplay("{Name} {Value?.ToString() ?? \"\"}")]
    public readonly struct ListItem : IEquatable<ListItem>, IComparable, IComparable<ListItem>
    {
        public static readonly ListItem Blank = new ListItem(string.Empty, string.Empty);

        public ListItem(object objValue, string strName)
        {
            Value = objValue;
            Name = strName;
        }

        /// <summary>
        /// Value.
        /// </summary>
        public object Value { get; }

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; }

        public bool Equals(ListItem other)
        {
            return Name == other.Name && object.Equals(Value, other.Value);
        }

        public override bool Equals(object obj)
        {
            return obj is ListItem item ? Equals(item) : Value?.Equals(obj) == true;
        }

        public override int GetHashCode()
        {
            return Value?.GetHashCode() ?? 0;
        }

        public int CompareTo(object obj)
        {
            if (obj is ListItem objItem)
                return CompareTo(objItem);
            return string.CompareOrdinal(ToString(), obj?.ToString() ?? string.Empty);
        }

        public int CompareTo(ListItem other)
        {
            return CompareListItems.CompareNames(this, other);
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(ListItem x, ListItem y)
        {
            return x.Equals(y);
        }

        public static bool operator !=(ListItem x, ListItem y)
        {
            return !x.Equals(y);
        }

        public static bool operator ==(ListItem x, object y)
        {
            return x.Equals(y);
        }

        public static bool operator !=(ListItem x, object y)
        {
            return !x.Equals(y);
        }

        public static bool operator ==(object x, ListItem y)
        {
            return x?.Equals(y) ?? false;
        }

        public static bool operator !=(object x, ListItem y)
        {
            return !(x?.Equals(y) ?? false);
        }

        public static bool operator <(ListItem left, ListItem right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(ListItem left, ListItem right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >(ListItem left, ListItem right)
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator >=(ListItem left, ListItem right)
        {
            return left.CompareTo(right) >= 0;
        }
    }

    public static class CompareListItems
    {
        /// <summary>
        /// Sort ListItems by Name in alphabetical order.
        /// </summary>
        public static int CompareNames(ListItem objX, ListItem objY)
        {
            return string.Compare(objX.Name, objY.Name, GlobalSettings.CultureInfo, CompareOptions.StringSort);
        }
    }

}
