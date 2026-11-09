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

namespace Chummer.Desktop.Localization
{
    /// <summary>
    /// XAML markup extension for UI strings: {loc:Tr String_Karma}. Uses the engine's LanguageManager, so the native UI
    /// shares Chummer's existing translations (en-us, de-de, fr-fr, ja-jp, pt-br, zh-cn).
    /// </summary>
    public sealed class TrExtension
    {
        public TrExtension()
        {
        }

        public TrExtension(string strKey)
        {
            Key = strKey;
        }

        public string Key { get; set; }

        /// <summary>Optional transform: "upper" for HUD-style headings.</summary>
        public string Case { get; set; }

        public object ProvideValue(IServiceProvider serviceProvider)
        {
            string strReturn = Tr.Get(Key);
            return string.Equals(Case, "upper", StringComparison.OrdinalIgnoreCase)
                ? strReturn.ToUpper(GlobalSettings.CultureInfo)
                : strReturn;
        }
    }

    public static class Tr
    {
        /// <summary>Translated UI string, or the key itself if it is unknown (visible during development).</summary>
        public static string Get(string strKey)
        {
            if (string.IsNullOrEmpty(strKey))
                return string.Empty;
            string strReturn = LanguageManager.GetString(strKey, false);
            if (string.IsNullOrEmpty(strReturn))
                return strKey;
            // WinForms-era strings carry &-mnemonics and label colons; neither belongs in this UI.
            return strReturn.Replace("&&", "\u0001").Replace("&", string.Empty).Replace("\u0001", "&").TrimEnd().TrimEnd(':', '\uFF1A').TrimEnd();
        }
    }
}
