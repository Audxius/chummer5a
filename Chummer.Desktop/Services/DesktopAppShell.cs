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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Chummer.Desktop.ViewModels;

namespace Chummer.Desktop.Services
{
    /// <summary>
    /// The native app shell: open characters are tabs in the main window.
    /// </summary>
    public sealed class DesktopAppShell : IAppShell
    {
        private readonly HeadlessAppShell _objLoader = new HeadlessAppShell();

        private static T OnUiThread<T>(Func<T> funcToRun)
        {
            return Dispatcher.UIThread.CheckAccess() ? funcToRun() : Dispatcher.UIThread.Invoke(funcToRun);
        }

        public bool IsCharacterOpenInAnyWindow(Character objCharacter)
            => OnUiThread(() => MainWindowViewModel.Current?.OpenCharacters.Any(x => ReferenceEquals(x.Character, objCharacter)) == true);

        public Task<bool> IsCharacterOpenInAnyWindowAsync(Character objCharacter, CancellationToken token = default)
            => token.IsCancellationRequested ? Task.FromCanceled<bool>(token) : Task.FromResult(IsCharacterOpenInAnyWindow(objCharacter));

        public CharacterWindowKinds GetOpenWindowKinds(string strFilePath)
            => OnUiThread(() => MainWindowViewModel.Current?.OpenCharacters.Any(x => string.Equals(x.FilePath, strFilePath, Utils.PathStringComparison)) == true
                   ? CharacterWindowKinds.Editor
                   : CharacterWindowKinds.None);

        public Task<CharacterWindowKinds> GetOpenWindowKindsAsync(string strFilePath, CancellationToken token = default)
            => token.IsCancellationRequested ? Task.FromCanceled<CharacterWindowKinds>(token) : Task.FromResult(GetOpenWindowKinds(strFilePath));

        public Character LoadCharacter(string strFileName, bool blnShowErrors = true, CancellationToken token = default)
        {
            using (HostServices.Busy.Begin())
                return _objLoader.LoadCharacter(strFileName, blnShowErrors, token);
        }

        public async Task<Character> LoadCharacterAsync(string strFileName, bool blnShowErrors = true, CancellationToken token = default)
        {
            IAsyncDisposable objBusy = await HostServices.Busy.BeginAsync(token: token).ConfigureAwait(false);
            try
            {
                // Loading is heavy; keep it off the UI thread.
                return await Task.Run(() => _objLoader.LoadCharacterAsync(strFileName, blnShowErrors, token), token).ConfigureAwait(false);
            }
            finally
            {
                await objBusy.DisposeAsync().ConfigureAwait(false);
            }
        }

        public Task<bool> SwitchToOpenCharacterAsync(Character objCharacter, CancellationToken token = default)
        {
            return Dispatcher.UIThread.InvokeAsync(() =>
            {
                MainWindowViewModel objMain = MainWindowViewModel.Current;
                CharacterViewModel objOpen = objMain?.OpenCharacters.FirstOrDefault(x => ReferenceEquals(x.Character, objCharacter));
                if (objOpen == null)
                    return false;
                objMain.CurrentPage = objOpen;
                return true;
            }).GetTask();
        }

        public Task OpenCharacterAsync(Character objCharacter, CancellationToken token = default)
        {
            return Dispatcher.UIThread.InvokeAsync(() => MainWindowViewModel.Current?.OpenCharacterAsync(objCharacter) ?? Task.CompletedTask);
        }

        public void OnLanguageChanged()
        {
            // Translations are applied when windows are (re)created; changing language takes effect on restart.
        }

        public Task OnLanguageChangedAsync(CancellationToken token = default)
            => token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask;

        public void OnTelemetryDisabledChanged(bool blnDisabled)
        {
            // The native app sends no telemetry.
        }

        public void OnEmulatedBrowserVersionChanged(int intVersion)
        {
            // No embedded Internet Explorer.
        }
    }
}
