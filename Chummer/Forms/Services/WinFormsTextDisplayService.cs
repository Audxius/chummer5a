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
using System.Windows.Forms;

namespace Chummer
{
    /// <summary>
    /// WinForms implementation of <see cref="ITextDisplayService"/> (moved from SourceString.SetControl).
    /// </summary>
    public sealed class WinFormsTextDisplayService : ITextDisplayService
    {
        public void SetTextAndToolTip(object objTarget, string strText, string strToolTip)
        {
            if (!(objTarget is Control objControl))
                return;
            objControl.DoThreadSafe(x => x.Text = strText);
            objControl.SetToolTip(strToolTip);
        }

        public void SetTextAndToolTip(object objTarget, string strText, string strToolTip, object objParentWindow)
        {
            if (!(objTarget is Control objControl))
                return;
            objControl.DoThreadSafe(x => x.Text = strText);
            if (objControl is IControlWithToolTip objWithToolTip)
                objWithToolTip.ToolTipText = strToolTip;
            else
                objControl.SetToolTip(objParentWindow as Form, strToolTip);
        }

        public async Task SetTextAndToolTipAsync(object objTarget, string strText, string strToolTip, CancellationToken token = default)
        {
            if (!(objTarget is Control objControl))
                return;
            await objControl.DoThreadSafeAsync(x => x.Text = strText, token).ConfigureAwait(false);
            await objControl.SetToolTipAsync(strToolTip, token).ConfigureAwait(false);
        }

        public async Task SetTextAndToolTipAsync(object objTarget, string strText, string strToolTip, object objParentWindow, CancellationToken token = default)
        {
            if (!(objTarget is Control objControl))
                return;
            await objControl.DoThreadSafeAsync(x => x.Text = strText, token).ConfigureAwait(false);
            if (objControl is IControlWithToolTip objWithToolTip)
                await objWithToolTip.SetToolTipTextAsync(strToolTip, token).ConfigureAwait(false);
            else
                await objControl.SetToolTipAsync(objParentWindow as Form, strToolTip, token).ConfigureAwait(false);
        }
    }
}
