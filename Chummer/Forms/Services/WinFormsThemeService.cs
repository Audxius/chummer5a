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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Chummer
{
    /// <summary>
    /// Windows implementation of <see cref="IThemeService"/>: reads the "apps use light theme" personalization
    /// setting and restyles the WinForms main window (moved out of ColorManager).
    /// </summary>
    public sealed class WinFormsThemeService : IThemeService
    {
        // The setting for whether stuff uses dark mode or light mode is accessible purely through a specific registry key
        // So we save that key for accessing both at startup and should the setting be changed while Chummer is running
        private readonly RegistryKey _objPersonalizeKey;

        public WinFormsThemeService()
        {
            try
            {
                _objPersonalizeKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
            }
            catch (ObjectDisposedException)
            {
                //swallow this
            }
            catch (System.Security.SecurityException)
            {
                //swallow this
            }
        }

        public bool SystemPrefersDarkMode
        {
            get
            {
                object objLightModeResult = _objPersonalizeKey?.GetValue("AppsUseLightTheme");
                if (objLightModeResult != null && int.TryParse(objLightModeResult.ToString(), out int intTemp))
                    return intTemp == 0;
                return false;
            }
        }

        public void OnLightDarkModeChanged(bool blnIsLightMode)
        {
            ChummerMainForm frmMain = Program.MainForm;
            if (frmMain == null)
                return;
            using (CursorWait.New(frmMain))
                frmMain.UpdateLightDarkMode(CancellationToken.None);
        }

        public async Task OnLightDarkModeChangedAsync(bool blnIsLightMode, CancellationToken token = default)
        {
            ChummerMainForm frmMain = Program.MainForm;
            if (frmMain == null)
                return;
            CursorWait objCursorWait = await CursorWait.NewAsync(frmMain, token: token).ConfigureAwait(false);
            try
            {
                await frmMain.UpdateLightDarkModeAsync(token).ConfigureAwait(false);
            }
            finally
            {
                await objCursorWait.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
