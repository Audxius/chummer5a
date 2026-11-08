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

namespace Chummer
{
    /// <summary>
    /// Registry of the UI services the engine (Backend) calls into. The engine never references a UI toolkit
    /// directly; the host application (WinForms today, Avalonia in the native port) registers implementations
    /// at startup. Headless defaults are used until then, which is what unit tests run against.
    /// </summary>
    public static class UiServices
    {
        /// <summary>
        /// Placeholder owner meaning "the application's main window", for engine code that has no UI element of its own.
        /// </summary>
        public static readonly object MainWindow = new object();

        private static IMessageService s_objMessages = new HeadlessMessageService();

        private static IBusyIndicator s_objBusy = new HeadlessBusyIndicator();

        public static IMessageService Messages
        {
            get => s_objMessages;
            set => s_objMessages = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static IBusyIndicator Busy
        {
            get => s_objBusy;
            set => s_objBusy = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
}
