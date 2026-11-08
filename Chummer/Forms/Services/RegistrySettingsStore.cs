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

using Microsoft.Win32;

namespace Chummer
{
    /// <summary>
    /// Windows implementation of <see cref="ISettingsStore"/>: HKEY_CURRENT_USER\Software\Chummer5, exactly where
    /// Chummer has always kept its global settings.
    /// </summary>
    public sealed class RegistrySettingsStore : ISettingsStore
    {
        private const string RootPath = "Software\\Chummer5";

        public ISettingsKey OpenRoot() => Wrap(Registry.CurrentUser.OpenSubKey(RootPath, true));

        public ISettingsKey CreateRoot() => Wrap(Registry.CurrentUser.CreateSubKey(RootPath, true));

        private static ISettingsKey Wrap(RegistryKey objKey) => objKey == null ? null : new Key(objKey);

        private sealed class Key : ISettingsKey
        {
            private readonly RegistryKey _objKey;

            public Key(RegistryKey objKey)
            {
                _objKey = objKey;
            }

            public object GetValue(string strName) => _objKey.GetValue(strName);

            public void SetValue(string strName, object objValue) => _objKey.SetValue(strName, objValue);

            public void DeleteValue(string strName) => _objKey.DeleteValue(strName, false);

            public string[] GetValueNames() => _objKey.GetValueNames();

            public string[] GetSubKeyNames() => _objKey.GetSubKeyNames();

            public ISettingsKey OpenSubKey(string strName) => Wrap(_objKey.OpenSubKey(strName, true));

            public ISettingsKey CreateSubKey(string strName) => Wrap(_objKey.CreateSubKey(strName, true));

            public void DeleteSubKeyTree(string strName) => _objKey.DeleteSubKeyTree(strName, false);

            public void Dispose() => _objKey.Dispose();
        }
    }
}
