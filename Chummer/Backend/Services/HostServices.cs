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
    /// Registry of the host services the engine (Backend) calls into: UI (message boxes, busy indicators...) and
    /// platform (settings storage...). The engine never references a UI toolkit or OS API directly; the host
    /// application (WinForms today, Avalonia in the native port) registers implementations at startup.
    /// Headless defaults are used until then, which is what unit tests run against.
    /// </summary>
    public static class HostServices
    {
        /// <summary>
        /// Placeholder owner meaning "the application's main window", for engine code that has no UI element of its own.
        /// </summary>
        public static readonly object MainWindow = new object();

        private static IMessageService s_objMessages = new HeadlessMessageService();

        private static IBusyIndicator s_objBusy = new HeadlessBusyIndicator();

        private static ISettingsStore s_objSettings = new MemorySettingsStore();

        private static IThemeService s_objTheme = new HeadlessThemeService();

        private static IDialogService s_objDialogs = new HeadlessDialogService();

        private static IAppShell s_objShell = new HeadlessAppShell();

        private static IPluginHost s_objPlugins = new HeadlessPluginHost();

        private static IDisplayChangeNotifier s_objDisplayChanges = new HeadlessDisplayChangeNotifier();

        private static IUiDispatcher s_objDispatcher = new HeadlessUiDispatcher();

        private static ITextDisplayService s_objTextDisplay = new HeadlessTextDisplayService();

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

        /// <summary>
        /// Global settings storage. Must be registered before GlobalSettings is first touched, as its static
        /// constructor loads from here.
        /// </summary>
        public static ISettingsStore Settings
        {
            get => s_objSettings;
            set => s_objSettings = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// OS light/dark preference and UI restyling. Must be registered before ColorManager is first touched.
        /// </summary>
        public static IThemeService Theme
        {
            get => s_objTheme;
            set => s_objTheme = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Prompts the engine needs answered mid-operation (pick an item, a number...).
        /// </summary>
        public static IDialogService Dialogs
        {
            get => s_objDialogs;
            set => s_objDialogs = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// The application shell: windows showing characters, opening characters, reacting to global setting changes.
        /// </summary>
        public static IAppShell Shell
        {
            get => s_objShell;
            set => s_objShell = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Active plugins (character file data, telemetry).
        /// </summary>
        public static IPluginHost Plugins
        {
            get => s_objPlugins;
            set => s_objPlugins = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Displayed aspects of engine objects changed (lets UI elements showing them refresh).
        /// </summary>
        public static IDisplayChangeNotifier DisplayChanges
        {
            get => s_objDisplayChanges;
            set => s_objDisplayChanges = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// The host UI thread message loop. Must be registered before Utils.CreateSynchronizationContext is called.
        /// </summary>
        public static IUiDispatcher Dispatcher
        {
            get => s_objDispatcher;
            set => s_objDispatcher = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Puts engine-computed text and tooltips onto UI elements passed in by the UI.
        /// </summary>
        public static ITextDisplayService TextDisplay
        {
            get => s_objTextDisplay;
            set => s_objTextDisplay = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
}
