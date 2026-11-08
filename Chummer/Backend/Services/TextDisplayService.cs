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
    /// Puts engine-computed text (and a tooltip) onto an opaque UI element passed in by the UI, e.g. the source/page
    /// label of a selection dialog.
    /// </summary>
    public interface ITextDisplayService
    {
        void SetTextAndToolTip(object objTarget, string strText, string strToolTip);

        void SetTextAndToolTip(object objTarget, string strText, string strToolTip, object objParentWindow);

        Task SetTextAndToolTipAsync(object objTarget, string strText, string strToolTip, CancellationToken token = default);

        Task SetTextAndToolTipAsync(object objTarget, string strText, string strToolTip, object objParentWindow, CancellationToken token = default);
    }

    /// <summary>
    /// Used when no UI has registered itself: there is nothing to display on.
    /// </summary>
    public sealed class HeadlessTextDisplayService : ITextDisplayService
    {
        public void SetTextAndToolTip(object objTarget, string strText, string strToolTip)
        {
        }

        public void SetTextAndToolTip(object objTarget, string strText, string strToolTip, object objParentWindow)
        {
        }

        public Task SetTextAndToolTipAsync(object objTarget, string strText, string strToolTip, CancellationToken token = default)
            => token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask;

        public Task SetTextAndToolTipAsync(object objTarget, string strText, string strToolTip, object objParentWindow, CancellationToken token = default)
            => token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask;
    }
}
