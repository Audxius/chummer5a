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
    /// Which part of an object's display changed.
    /// </summary>
    public enum DisplayAspect
    {
        Text,
        Color,
        ToolTipText,
        TypeData,
        Location,
        ServicesOwed
    }

    /// <summary>
    /// Lets UI elements that display an engine object (e.g. tree nodes for contacts and spirits) refresh when a
    /// displayed aspect of it changes. The engine reports changes; the host decides what to refresh.
    /// </summary>
    public interface IDisplayChangeNotifier
    {
        void OnDisplayChanged(object objSource, DisplayAspect eAspect, CancellationToken token = default);

        Task OnDisplayChangedAsync(object objSource, DisplayAspect eAspect, CancellationToken token = default);
    }

    /// <summary>
    /// Used when no UI has registered itself: nothing displays anything.
    /// </summary>
    public sealed class HeadlessDisplayChangeNotifier : IDisplayChangeNotifier
    {
        public void OnDisplayChanged(object objSource, DisplayAspect eAspect, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
        }

        public Task OnDisplayChangedAsync(object objSource, DisplayAspect eAspect, CancellationToken token = default)
        {
            return token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask;
        }
    }
}
