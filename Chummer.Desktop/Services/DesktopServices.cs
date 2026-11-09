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

namespace Chummer.Desktop.Services
{
    /// <summary>
    /// Registers the native app's implementations of the engine's host services. Call first thing in Main.
    /// </summary>
    public static class DesktopServices
    {
        public static void Register()
        {
            HostServices.Settings = JsonFileSettingsStore.CreateDefault();
            HostServices.Messages = new AvaloniaMessageService();
            HostServices.Busy = new AvaloniaBusyIndicator();
            HostServices.Theme = new DesktopThemeService();
            HostServices.Dialogs = new AvaloniaDialogService();
            HostServices.Shell = new DesktopAppShell();
            HostServices.Dispatcher = new AvaloniaUiDispatcher();
            HostServices.ImageCodec = new SkiaImageCodec();
            HostServices.AppVersion = typeof(DesktopServices).Assembly.GetName().Version;
            // Plugins, display change notifications and text display keep their headless defaults: the native UI binds to
            // model properties directly instead of having the engine push into controls.
        }
    }

    /// <summary>
    /// The native UI is always the dark "neon" theme; report dark so engine-computed colors (notes highlights etc.)
    /// use their dark-mode variants.
    /// </summary>
    public sealed class DesktopThemeService : IThemeService
    {
        public bool SystemPrefersDarkMode => true;

        public void OnLightDarkModeChanged(bool blnIsLightMode)
        {
        }

        public Task OnLightDarkModeChangedAsync(bool blnIsLightMode, CancellationToken token = default)
            => token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask;
    }

    /// <summary>
    /// Avalonia installs its SynchronizationContext on the UI thread itself; pending work is processed by its loop.
    /// </summary>
    public sealed class AvaloniaUiDispatcher : IUiDispatcher
    {
        public IDisposable InstallSynchronizationContext() => null;

        public void PumpPendingMessages()
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }
}
