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

using System.Threading;
using System.Threading.Tasks;

namespace Chummer
{
    /// <summary>
    /// Connects the engine's light/dark mode state (<see cref="ColorManager"/>) to the host's theming.
    /// </summary>
    public interface IThemeService
    {
        /// <summary>
        /// Whether the operating system currently prefers a dark theme. Polled while the color mode is Automatic.
        /// </summary>
        bool SystemPrefersDarkMode { get; }

        /// <summary>
        /// Called after the effective light/dark mode changed, so the UI can restyle itself.
        /// </summary>
        void OnLightDarkModeChanged(bool blnIsLightMode);

        /// <summary>
        /// Called after the effective light/dark mode changed, so the UI can restyle itself.
        /// </summary>
        Task OnLightDarkModeChangedAsync(bool blnIsLightMode, CancellationToken token = default);
    }

    /// <summary>
    /// Used when no UI has registered itself: light mode, nothing to restyle.
    /// </summary>
    public sealed class HeadlessThemeService : IThemeService
    {
        public bool SystemPrefersDarkMode => false;

        public void OnLightDarkModeChanged(bool blnIsLightMode)
        {
            // Nothing to restyle
        }

        public Task OnLightDarkModeChangedAsync(bool blnIsLightMode, CancellationToken token = default)
        {
            return token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask;
        }
    }
}
