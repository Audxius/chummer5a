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
    /// WinForms implementation of <see cref="IMessageService"/>: the existing scrollable message box.
    /// </summary>
    public sealed class WinFormsMessageService : IMessageService
    {
        public MessageResult Show(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
            MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1)
        {
            return (MessageResult)Program.ShowScrollableMessageBox(owner as Control, message, caption, (MessageBoxButtons)buttons,
                (MessageBoxIcon)icon, (MessageBoxDefaultButton)defaultButton);
        }

        public async Task<MessageResult> ShowAsync(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
            MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1,
            CancellationToken token = default)
        {
            return (MessageResult)await Program.ShowScrollableMessageBoxAsync(owner as Control, message, caption, (MessageBoxButtons)buttons,
                (MessageBoxIcon)icon, (MessageBoxDefaultButton)defaultButton, token).ConfigureAwait(false);
        }
    }
}
