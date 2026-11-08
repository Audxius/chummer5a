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
using System.Diagnostics;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using NLog;

namespace Chummer
{
    public class CustomTelemetryInitializer : ITelemetryInitializer
    {
        private static readonly Lazy<Logger> s_ObjLogger = new Lazy<Logger>(LogManager.GetCurrentClassLogger);
        private static Logger Log => s_ObjLogger.Value;

        // Set session data:
        //private static string Hostname =  Dns.GetHostName();

        [CLSCompliant(false)]
        public void Initialize(ITelemetry telemetry)
        {
            if (telemetry == null)
                throw new ArgumentNullException(nameof(telemetry));
            if (telemetry.Context.GlobalProperties.ContainsKey("Milestone"))
                telemetry.Context.GlobalProperties["Milestone"] = Utils.IsMilestoneVersion.ToString(GlobalSettings.InvariantCultureInfo);
            else
                telemetry.Context.GlobalProperties.Add("Milestone", Utils.IsMilestoneVersion.ToString(GlobalSettings.InvariantCultureInfo));

            telemetry.Context.Device.OperatingSystem = Environment.OSVersion.ToString();
            string strUploadClientId = GlobalSettings.UploadClientId.ToString();
            telemetry.Context.Cloud.RoleInstance = strUploadClientId;
            telemetry.Context.Cloud.RoleName = strUploadClientId;
            telemetry.Context.Device.Id = Environment.MachineName;
            telemetry.Context.Session.Id = strUploadClientId;
            telemetry.Context.User.Id = strUploadClientId;

            telemetry.Context.Component.Version = Utils.CurrentChummerVersion.ToString();

            if (Debugger.IsAttached)
            {
                //don't fill the "productive" log with garbage from debug sessions
                telemetry.Context.InstrumentationKey = "f4b2ea1b-afe4-4bd6-9175-f5bb167a4d8b";
            }
            foreach (ICharacterDataPlugin plugin in HostServices.Plugins.ActivePlugins)
            {
                try
                {
                    telemetry = plugin.SetTelemetryInitialize(telemetry);
                }
                catch (Exception e)
                {
                    e = e.Demystify();
                    Log.Error(e);
#if DEBUG
                    throw;
#endif
                }
            }
        }
    }
}
