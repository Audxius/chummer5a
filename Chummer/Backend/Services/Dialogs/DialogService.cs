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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Chummer
{
    /// <summary>
    /// How a dialog was closed. Values match WinForms' DialogResult.
    /// </summary>
    public enum DialogOutcome
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
    /// A prompt the engine needs answered by the user in the middle of a rules operation (pick an item, a number,
    /// an attribute...). Requests are plain data: settings and setup calls are recorded under the member names of
    /// the dialog that historically implemented them, the host shows whatever UI it likes, then fills in the results.
    /// </summary>
    public abstract class DialogRequest
    {
        private readonly Dictionary<string, object> _dicValues = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly List<DialogOperation> _lstOperations = new List<DialogOperation>();

        protected DialogRequest(params object[] aobjConstructorArguments)
        {
            ConstructorArguments = aobjConstructorArguments ?? Array.Empty<object>();
        }

        /// <summary>Name of the dialog this request corresponds to (the legacy WinForms form's class name).</summary>
        public abstract string DialogName { get; }

        /// <summary>Arguments the legacy form's constructor took.</summary>
        public IReadOnlyList<object> ConstructorArguments { get; }

        /// <summary>Members whose values the host must copy back after the dialog closes.</summary>
        public abstract IReadOnlyCollection<string> ResultNames { get; }

        /// <summary>
        /// Settings assigned and setup methods called by the engine, in the order it did them (anything not assigned
        /// keeps the dialog's own default). Hosts replay these before showing the dialog.
        /// </summary>
        public IReadOnlyList<DialogOperation> Operations => _lstOperations;

        /// <summary>
        /// 0 means the engine already knows the answer (forced value) and the dialog should accept it without being seen.
        /// </summary>
        public double Opacity
        {
            get => Get(1.0);
            set => Set(value);
        }

        protected T Get<T>(T objDefault = default, [CallerMemberName] string strName = null)
        {
            return _dicValues.TryGetValue(strName, out object objValue) && objValue is T objTyped ? objTyped : objDefault;
        }

        protected void Set<T>(T objValue, [CallerMemberName] string strName = null)
        {
            _dicValues[strName] = objValue;
            _lstOperations.Add(new DialogOperation(strName, false, new object[] { objValue }));
        }

        protected void RecordCall(string strMethod, params object[] aobjArguments)
        {
            _lstOperations.Add(new DialogOperation(strMethod, true, aobjArguments ?? Array.Empty<object>()));
        }

        /// <summary>Called by the host after the dialog closed.</summary>
        public void SetResult(string strName, object objValue)
        {
            _dicValues[strName] = objValue;
        }
    }

    /// <summary>
    /// One recorded step of dialog setup: a property assignment or a method call.
    /// </summary>
    public readonly struct DialogOperation
    {
        public DialogOperation(string strName, bool blnIsCall, object[] aobjArguments)
        {
            Name = strName;
            IsCall = blnIsCall;
            Arguments = aobjArguments;
        }

        public string Name { get; }

        /// <summary>True for a method call, false for a property assignment (whose value is Arguments[0]).</summary>
        public bool IsCall { get; }

        public object[] Arguments { get; }
    }

    /// <summary>
    /// Shows <see cref="DialogRequest"/>s to the user.
    /// </summary>
    public interface IDialogService
    {
        /// <param name="owner">Character whose window should own the dialog, an opaque UI element, or null.</param>
        DialogOutcome Show(DialogRequest objRequest, object owner = null, CancellationToken token = default);

        /// <param name="owner">Character whose window should own the dialog, an opaque UI element, or null.</param>
        Task<DialogOutcome> ShowAsync(DialogRequest objRequest, object owner = null, CancellationToken token = default);
    }

    /// <summary>
    /// Used when no UI has registered itself. A request whose answer is already forced (Opacity 0) is accepted;
    /// anything that genuinely needs a user is a failure, like message boxes in unit tests.
    /// </summary>
    public sealed class HeadlessDialogService : IDialogService
    {
        public DialogOutcome Show(DialogRequest objRequest, object owner = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (objRequest == null)
                throw new ArgumentNullException(nameof(objRequest));
            if (objRequest.Opacity == 0)
                return DialogOutcome.OK;
            throw new InvalidOperationException("No UI is registered to answer the " + objRequest.DialogName + " dialog.");
        }

        public Task<DialogOutcome> ShowAsync(DialogRequest objRequest, object owner = null, CancellationToken token = default)
        {
            if (token.IsCancellationRequested)
                return Task.FromCanceled<DialogOutcome>(token);
            try
            {
                return Task.FromResult(Show(objRequest, owner, token));
            }
            catch (Exception e)
            {
                return Task.FromException<DialogOutcome>(e);
            }
        }
    }

    /// <summary>
    /// Drop-in replacement for the WinForms-only ThreadSafeForm wrapper, for engine code. Keeps the same shape
    /// (Get/GetAsync, MyForm, ShowDialogSafe[Async]) so call sites only change type names.
    /// </summary>
    public readonly struct ThreadSafeDialog<T> : IDisposable where T : DialogRequest
    {
        public T MyForm { get; }

        private ThreadSafeDialog(T objRequest)
        {
            MyForm = objRequest;
        }

        public static ThreadSafeDialog<T> Get(Func<T> funcConstructor)
        {
            return new ThreadSafeDialog<T>(funcConstructor());
        }

        public static Task<ThreadSafeDialog<T>> GetAsync(Func<T> funcConstructor, CancellationToken token = default)
        {
            return token.IsCancellationRequested
                ? Task.FromCanceled<ThreadSafeDialog<T>>(token)
                : Task.FromResult(new ThreadSafeDialog<T>(funcConstructor()));
        }

        public static Task<ThreadSafeDialog<T>> GetAsync(Func<CancellationToken, T> funcConstructor, CancellationToken token = default)
        {
            return token.IsCancellationRequested
                ? Task.FromCanceled<ThreadSafeDialog<T>>(token)
                : Task.FromResult(new ThreadSafeDialog<T>(funcConstructor(token)));
        }

        public DialogOutcome ShowDialogSafe(object owner = null, CancellationToken token = default)
        {
            return HostServices.Dialogs.Show(MyForm, owner, token);
        }

        public Task<DialogOutcome> ShowDialogSafeAsync(object owner = null, CancellationToken token = default)
        {
            return HostServices.Dialogs.ShowAsync(MyForm, owner, token);
        }

        public void Dispose()
        {
            // Requests hold no UI resources.
        }
    }

    /// <summary>
    /// Compatibility shims so engine code written against forms (frm.MyForm.DoThreadSafeFuncAsync(x => x.Prop))
    /// keeps compiling against requests, which need no thread marshalling.
    /// </summary>
    public static class DialogRequestExtensions
    {
        public static TResult DoThreadSafeFunc<TRequest, TResult>(this TRequest objRequest, Func<TRequest, TResult> funcToRun, CancellationToken token = default) where TRequest : DialogRequest
        {
            token.ThrowIfCancellationRequested();
            return funcToRun(objRequest);
        }

        public static void DoThreadSafe<TRequest>(this TRequest objRequest, Action<TRequest> funcToRun, CancellationToken token = default) where TRequest : DialogRequest
        {
            token.ThrowIfCancellationRequested();
            funcToRun(objRequest);
        }

        public static Task<TResult> DoThreadSafeFuncAsync<TRequest, TResult>(this TRequest objRequest, Func<TRequest, TResult> funcToRun, CancellationToken token = default) where TRequest : DialogRequest
        {
            return token.IsCancellationRequested ? Task.FromCanceled<TResult>(token) : Task.FromResult(funcToRun(objRequest));
        }

        public static Task DoThreadSafeAsync<TRequest>(this TRequest objRequest, Action<TRequest> funcToRun, CancellationToken token = default) where TRequest : DialogRequest
        {
            if (token.IsCancellationRequested)
                return Task.FromCanceled(token);
            funcToRun(objRequest);
            return Task.CompletedTask;
        }
    }
}
