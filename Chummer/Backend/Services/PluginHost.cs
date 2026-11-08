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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Channel;

namespace Chummer
{
    /// <summary>
    /// The part of a plugin the engine talks to: data the plugin stores inside character files, and telemetry.
    /// </summary>
    public interface ICharacterDataPlugin
    {
        Assembly GetPluginAssembly();

        /// <summary>Contents of the plugin's element inside the character's &lt;plugins&gt; section.</summary>
        string GetSaveToFileElement(Character input);

        void LoadFileElement(Character input, string fileElement);

        [CLSCompliant(false)]
#pragma warning disable CS3010 // CLS-compliant interfaces must have only CLS-compliant members
        ITelemetry SetTelemetryInitialize(ITelemetry telemetry);
#pragma warning restore CS3010 // CLS-compliant interfaces must have only CLS-compliant members
    }

    /// <summary>
    /// Supplies the active plugins to the engine.
    /// </summary>
    public interface IPluginHost
    {
        IReadOnlyList<ICharacterDataPlugin> ActivePlugins { get; }

        Task<IReadOnlyList<ICharacterDataPlugin>> GetActivePluginsAsync(CancellationToken token = default);
    }

    /// <summary>
    /// Used when no UI has registered itself: no plugins.
    /// </summary>
    public sealed class HeadlessPluginHost : IPluginHost
    {
        public IReadOnlyList<ICharacterDataPlugin> ActivePlugins => Array.Empty<ICharacterDataPlugin>();

        public Task<IReadOnlyList<ICharacterDataPlugin>> GetActivePluginsAsync(CancellationToken token = default)
        {
            return token.IsCancellationRequested
                ? Task.FromCanceled<IReadOnlyList<ICharacterDataPlugin>>(token)
                : Task.FromResult<IReadOnlyList<ICharacterDataPlugin>>(Array.Empty<ICharacterDataPlugin>());
        }
    }
}
