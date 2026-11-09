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
using Avalonia;
using Chummer.Desktop.Services;

namespace Chummer.Desktop
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // 1. Host services first: GlobalSettings' static constructor (touched by almost everything) reads settings.
            DesktopServices.Register();
            // 2. Touch the engine's Utils on this thread so it becomes the engine's "main thread" (Avalonia's UI thread).
            _ = Utils.IsMainThread;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        /// <summary>
        /// Native Wayland when running in a Wayland session (niri, KDE Plasma Wayland, ...), X11 otherwise.
        /// CHUMMER_BACKEND=x11 forces X11 (via XWayland) if a compositor misbehaves.
        /// </summary>
        public static AppBuilder BuildAvaloniaApp()
        {
            AppBuilder objBuilder = AppBuilder.Configure<App>()
                                              .WithInterFont()
                                              .LogToTrace();
            if (OperatingSystem.IsLinux() && UseNativeWayland())
                return objBuilder.UseWayland().UseSkia();
            return objBuilder.UsePlatformDetect();
        }

        internal static bool UseNativeWayland()
        {
            string strForced = Environment.GetEnvironmentVariable("CHUMMER_BACKEND");
            if (!string.IsNullOrEmpty(strForced))
                return string.Equals(strForced, "wayland", StringComparison.OrdinalIgnoreCase);
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        }
    }
}
