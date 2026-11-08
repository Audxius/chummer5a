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

namespace Chummer
{
    /// <summary>
    /// The host UI thread's message loop, as far as the engine needs it. The engine marshals work to the main thread
    /// with Microsoft.VisualStudio.Threading over whatever SynchronizationContext the host installs; this service
    /// installs that context and pumps pending messages while the engine waits on the main thread.
    /// </summary>
    public interface IUiDispatcher
    {
        /// <summary>
        /// Called on the main thread; SynchronizationContext.Current must be the UI context while the returned scope
        /// is alive (the engine captures it inside the scope).
        /// </summary>
        System.IDisposable InstallSynchronizationContext();

        /// <summary>
        /// Process pending UI messages (so the UI stays responsive while the main thread waits on engine locks).
        /// </summary>
        void PumpPendingMessages();
    }

    /// <summary>
    /// Used when no UI has registered itself: whatever context is current stays, and there is nothing to pump.
    /// </summary>
    public sealed class HeadlessUiDispatcher : IUiDispatcher
    {
        public System.IDisposable InstallSynchronizationContext()
        {
            // Keep the current context (possibly none; the creating thread then counts as the main thread).
            return null;
        }

        public void PumpPendingMessages()
        {
            // No message loop
        }
    }
}
