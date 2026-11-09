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

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Chummer.Desktop.ViewModels;
using Chummer.Desktop.Views;

namespace Chummer.Desktop
{
    public sealed class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Avalonia's SynchronizationContext is installed on this (UI) thread now: the engine captures it so its
            // RunOnMainThread marshals to Avalonia's dispatcher.
            Utils.CreateSynchronizationContext();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime objDesktop)
            {
                MainWindowViewModel objViewModel = new MainWindowViewModel();
                objDesktop.MainWindow = new MainWindow { DataContext = objViewModel };
                objViewModel.Start();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
