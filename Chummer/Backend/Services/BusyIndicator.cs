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
    /// <summary>
    /// Shows that the application is busy (e.g. a wait cursor) for as long as the returned handle is alive.
    /// </summary>
    public interface IBusyIndicator
    {
        /// <param name="owner">Opaque UI element to show the indicator on, <see cref="UiServices.MainWindow"/>, or null for application-wide.</param>
        IDisposable Begin(object owner = null);

        /// <param name="owner">Opaque UI element to show the indicator on, <see cref="UiServices.MainWindow"/>, or null for application-wide.</param>
        Task<IAsyncDisposable> BeginAsync(object owner = null, CancellationToken token = default);
    }

    /// <summary>
    /// Used when no UI has registered itself: nothing to show.
    /// </summary>
    public sealed class HeadlessBusyIndicator : IBusyIndicator
    {
        private sealed class NoOpHandle : IDisposable, IAsyncDisposable
        {
            public static readonly NoOpHandle Instance = new NoOpHandle();

            public void Dispose()
            {
                // Nothing to undo
            }

            public ValueTask DisposeAsync() => default;
        }

        public IDisposable Begin(object owner = null) => NoOpHandle.Instance;

        public Task<IAsyncDisposable> BeginAsync(object owner = null, CancellationToken token = default)
        {
            return token.IsCancellationRequested
                ? Task.FromCanceled<IAsyncDisposable>(token)
                : Task.FromResult<IAsyncDisposable>(NoOpHandle.Instance);
        }
    }
}
