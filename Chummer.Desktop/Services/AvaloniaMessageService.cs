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
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Chummer.Desktop.Views;

namespace Chummer.Desktop.Services
{
    /// <summary>
    /// Shared helpers for showing modal windows from engine code, which may call from any thread and may need a
    /// synchronous answer while running on the UI thread.
    /// </summary>
    internal static class ModalHelper
    {
        private static WeakReference<Window> s_shellWindow;
        internal static void RegisterShell(Window window) => s_shellWindow = new WeakReference<Window>(window);
        public static Window MainWindow =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
            ?? (s_shellWindow != null && s_shellWindow.TryGetTarget(out var shell) ? shell : null);

        public static Window ResolveOwner(object owner)
        {
            if (owner is Window objWindow)
                return objWindow;
            if (owner is Control objControl && TopLevel.GetTopLevel(objControl) is Window objTop)
                return objTop;
            return MainWindow;
        }

        /// <summary>
        /// Runs a modal window to completion. On the UI thread this spins a nested dispatcher loop (like a WinForms
        /// modal dialog) so synchronous engine code can get its answer; elsewhere it marshals to the UI thread.
        /// </summary>
        public static T RunModal<T>(Func<Task<T>> funcShow)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Task<T> tskShow = funcShow();
                using (CancellationTokenSource objDone = new CancellationTokenSource())
                {
                    tskShow.ContinueWith(_ => objDone.Cancel(), TaskScheduler.FromCurrentSynchronizationContext());
                    if (!tskShow.IsCompleted)
                        Dispatcher.UIThread.MainLoop(objDone.Token);
                }
                return tskShow.GetAwaiter().GetResult();
            }
            return Dispatcher.UIThread.InvokeAsync(funcShow).GetAwaiter().GetResult();
        }

        public static Task<T> RunModalAsync<T>(Func<Task<T>> funcShow, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return Task.FromCanceled<T>(token);
            return Dispatcher.UIThread.CheckAccess() ? funcShow() : Dispatcher.UIThread.InvokeAsync(funcShow);
        }
    }

    /// <summary>
    /// Message boxes in the app's own style.
    /// </summary>
    public sealed class AvaloniaMessageService : IMessageService
    {
        public MessageResult Show(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
            MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1)
        {
            return ModalHelper.RunModal(() => ShowCore(owner, message, caption, buttons, icon, defaultButton));
        }

        public Task<MessageResult> ShowAsync(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
            MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1,
            CancellationToken token = default)
        {
            return ModalHelper.RunModalAsync(() => ShowCore(owner, message, caption, buttons, icon, defaultButton), token);
        }

        private static async Task<MessageResult> ShowCore(object owner, string message, string caption, MessageButtons buttons,
            MessageIcon icon, MessageDefaultButton defaultButton)
        {
            MessageWindow objWindow = new MessageWindow(message, caption, buttons, icon, defaultButton);
            Window objOwner = ModalHelper.ResolveOwner(owner);
            if (objOwner == null || !objOwner.IsVisible)
            {
                TaskCompletionSource<MessageResult> objClosed = new TaskCompletionSource<MessageResult>();
                objWindow.Closed += (sender, args) => objClosed.TrySetResult(objWindow.Result);
                objWindow.Show();
                return await objClosed.Task.ConfigureAwait(true);
            }
            return await objWindow.ShowDialog<MessageResult>(objOwner).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Wait cursor on the main window while the engine is busy.
    /// </summary>
    public sealed class AvaloniaBusyIndicator : IBusyIndicator
    {
        private int _intDepth;

        private sealed class Handle : IDisposable, IAsyncDisposable
        {
            private AvaloniaBusyIndicator _objParent;

            public Handle(AvaloniaBusyIndicator objParent)
            {
                _objParent = objParent;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _objParent, null)?.Decrement();
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return default;
            }
        }

        public IDisposable Begin(object owner = null)
        {
            Increment();
            return new Handle(this);
        }

        public Task<IAsyncDisposable> BeginAsync(object owner = null, CancellationToken token = default)
        {
            if (token.IsCancellationRequested)
                return Task.FromCanceled<IAsyncDisposable>(token);
            Increment();
            return Task.FromResult<IAsyncDisposable>(new Handle(this));
        }

        private void Increment()
        {
            if (Interlocked.Increment(ref _intDepth) == 1)
                Apply(true);
        }

        private void Decrement()
        {
            if (Interlocked.Decrement(ref _intDepth) == 0)
                Apply(false);
        }

        private static void Apply(bool blnBusy)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Window objMain = ModalHelper.MainWindow;
                if (objMain != null)
                    objMain.Cursor = blnBusy ? new Cursor(StandardCursorType.Wait) : Cursor.Default;
            });
        }
    }
}
