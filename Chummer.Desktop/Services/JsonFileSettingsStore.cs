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
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chummer.Desktop.Services
{
    /// <summary>
    /// Global settings persisted as JSON in the platform config directory
    /// ($XDG_CONFIG_HOME/chummer5/settings.json on Linux, %APPDATA%\chummer5 on Windows, ~/Library/... on macOS).
    /// </summary>
    public sealed class JsonFileSettingsStore : ISettingsStore
    {
        private readonly MemorySettingsStore _objMemory = new MemorySettingsStore();
        private readonly string _strPath;
        private readonly Timer _tmrSave;
        private int _intDirty;

        public JsonFileSettingsStore(string strPath)
        {
            _strPath = strPath ?? throw new ArgumentNullException(nameof(strPath));
            Load();
            _tmrSave = new Timer(_ => SaveIfDirty(), null, Timeout.Infinite, Timeout.Infinite);
            _objMemory.Changed += (sender, args) =>
            {
                Interlocked.Exchange(ref _intDirty, 1);
                _tmrSave.Change(500, Timeout.Infinite); // debounce bursts of changes (saving all settings writes ~60 values)
            };
            AppDomain.CurrentDomain.ProcessExit += (sender, args) => SaveIfDirty();
        }

        public static JsonFileSettingsStore CreateDefault()
        {
            string strBase = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(strBase))
                strBase = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return new JsonFileSettingsStore(Path.Combine(strBase, "chummer5", "settings.json"));
        }

        public string FilePath => _strPath;

        public ISettingsKey OpenRoot() => _objMemory.OpenRoot();

        public ISettingsKey CreateRoot() => _objMemory.CreateRoot();

        private void Load()
        {
            if (!File.Exists(_strPath))
                return;
            try
            {
                JObject objRoot = JObject.Parse(File.ReadAllText(_strPath));
                _objMemory.Import(ToSnapshot(objRoot));
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
            {
                // A corrupt settings file must not stop the app; keep a copy and start fresh.
                try
                {
                    File.Copy(_strPath, _strPath + ".corrupt", true);
                }
                catch (IOException)
                {
                    // ignore
                }
            }
        }

        private void SaveIfDirty()
        {
            if (Interlocked.Exchange(ref _intDirty, 0) == 0)
                return;
            try
            {
                SettingsSnapshot objSnapshot = _objMemory.Export();
                if (objSnapshot == null)
                    return;
                Directory.CreateDirectory(Path.GetDirectoryName(_strPath) ?? ".");
                string strTemp = _strPath + ".tmp";
                File.WriteAllText(strTemp, ToJson(objSnapshot).ToString(Formatting.Indented));
                File.Move(strTemp, _strPath, true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Interlocked.Exchange(ref _intDirty, 1);
            }
        }

        // Values are stored as JSON primitives; sub keys as nested objects under "keys".
        private static JObject ToJson(SettingsSnapshot objSnapshot)
        {
            JObject objValues = new JObject();
            foreach (KeyValuePair<string, object> kvp in objSnapshot.Values)
                objValues[kvp.Key] = JToken.FromObject(kvp.Value);
            JObject objKeys = new JObject();
            foreach (KeyValuePair<string, SettingsSnapshot> kvp in objSnapshot.Children)
                objKeys[kvp.Key] = ToJson(kvp.Value);
            return new JObject { ["values"] = objValues, ["keys"] = objKeys };
        }

        private static SettingsSnapshot ToSnapshot(JObject objJson)
        {
            SettingsSnapshot objReturn = new SettingsSnapshot();
            if (objJson["values"] is JObject objValues)
            {
                foreach (JProperty objProperty in objValues.Properties())
                {
                    if (objProperty.Value is JValue objValue && objValue.Value != null)
                        objReturn.Values[objProperty.Name] = objValue.Type == JTokenType.Integer ? (object)Convert.ToInt32(objValue.Value) : objValue.Value.ToString();
                }
            }
            if (objJson["keys"] is JObject objKeys)
            {
                foreach (JProperty objProperty in objKeys.Properties())
                {
                    if (objProperty.Value is JObject objChild)
                        objReturn.Children[objProperty.Name] = ToSnapshot(objChild);
                }
            }
            return objReturn;
        }
    }
}
