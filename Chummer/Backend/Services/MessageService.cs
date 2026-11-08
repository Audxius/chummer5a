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

namespace Chummer
{
    // UI-toolkit-agnostic message box types. Numeric values deliberately match the WinForms
    // MessageBoxButtons / MessageBoxIcon / MessageBoxDefaultButton / DialogResult enums so the
    // WinForms implementation can cast directly.

    public enum MessageButtons
    {
        OK = 0,
        OKCancel = 1,
        AbortRetryIgnore = 2,
        YesNoCancel = 3,
        YesNo = 4,
        RetryCancel = 5
    }

    public enum MessageIcon
    {
        None = 0,
        Hand = 16,
        Stop = 16,
        Error = 16,
        Question = 32,
        Exclamation = 48,
        Warning = 48,
        Asterisk = 64,
        Information = 64
    }

    public enum MessageDefaultButton
    {
        Button1 = 0,
        Button2 = 256,
        Button3 = 512
    }

    public enum MessageResult
    {
        None = 0,
        OK = 1,
        Cancel = 2,
        Abort = 3,
        Retry = 4,
        Ignore = 5,
        Yes = 6,
        No = 7
    }

    /// <summary>
    /// Shows message boxes on behalf of the engine, which must not know which UI toolkit is in use.
    /// </summary>
    public interface IMessageService
    {
        /// <param name="owner">Opaque handle to the UI element the box belongs to (a WinForms Control, an Avalonia Visual...), or null.</param>
        MessageResult Show(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
            MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1);

        /// <param name="owner">Opaque handle to the UI element the box belongs to (a WinForms Control, an Avalonia Visual...), or null.</param>
        Task<MessageResult> ShowAsync(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
            MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1,
            CancellationToken token = default);
    }

    /// <summary>
    /// Used when no UI has registered itself (unit tests, headless tools). Mirrors the long-standing unit test
    /// behaviour of Program.ShowScrollableMessageBox: informational boxes are acknowledged, anything that asks
    /// a question or reports an error is treated as a test failure.
    /// </summary>
    public sealed class HeadlessMessageService : IMessageService
    {
        public MessageResult Show(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
            MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1)
        {
            if (icon == MessageIcon.Error || buttons != MessageButtons.OK)
            {
                Utils.BreakIfDebug();
                throw new InvalidOperationException("We don't want to see MessageBoxes in Unit Tests!" + Environment.NewLine +
                                                    "Caption: " + caption + Environment.NewLine + "Message: " + message);
            }
            return MessageResult.OK;
        }

        public Task<MessageResult> ShowAsync(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
            MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1,
            CancellationToken token = default)
        {
            if (token.IsCancellationRequested)
                return Task.FromCanceled<MessageResult>(token);
            try
            {
                return Task.FromResult(Show(owner, message, caption, buttons, icon, defaultButton));
            }
            catch (Exception e)
            {
                return Task.FromException<MessageResult>(e);
            }
        }
    }

    /// <summary>
    /// Convenience entry points with the same shape as the old Program.ShowScrollableMessageBox overloads.
    /// </summary>
    public static class MessageBoxes
    {
        public static MessageResult Show(string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
            MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1)
        {
            return HostServices.Messages.Show(null, message, caption, buttons, icon, defaultButton);
        }

        public static Task<MessageResult> ShowAsync(string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
            MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1,
            CancellationToken token = default)
        {
            return HostServices.Messages.ShowAsync(null, message, caption, buttons, icon, defaultButton, token);
        }
    }
}
