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
using System.Linq;

namespace Chummer
{
    /// <summary>
    /// One node of the application's persistent settings tree. Modelled on the subset of the Windows registry API
    /// that Chummer has always used, so the registry remains a drop-in backend on Windows while other platforms
    /// can store the same tree in a file. Names are case-insensitive. Missing values or keys are never an error.
    /// </summary>
    public interface ISettingsKey : IDisposable
    {
        /// <returns>The stored value, or null if there is none.</returns>
        object GetValue(string strName);

        void SetValue(string strName, object objValue);

        void DeleteValue(string strName);

        string[] GetValueNames();

        string[] GetSubKeyNames();

        /// <returns>The sub key, or null if it does not exist.</returns>
        ISettingsKey OpenSubKey(string strName);

        /// <returns>The sub key, created if it did not exist.</returns>
        ISettingsKey CreateSubKey(string strName);

        void DeleteSubKeyTree(string strName);
    }

    /// <summary>
    /// Persistent storage for global (per-user, not per-character) settings.
    /// </summary>
    public interface ISettingsStore
    {
        /// <returns>The application's root settings key, or null if nothing has ever been saved.</returns>
        ISettingsKey OpenRoot();

        /// <returns>The application's root settings key, created if necessary.</returns>
        ISettingsKey CreateRoot();
    }

    /// <summary>
    /// Settings tree held in memory. Used as the headless default (unit tests always start from a clean slate)
    /// and as the in-memory model behind file-based stores.
    /// </summary>
    public sealed class MemorySettingsStore : ISettingsStore
    {
        private readonly object _objLock = new object();
        private Node _objRoot;

        /// <summary>
        /// Raised after any change, so a file-backed store can persist the tree.
        /// </summary>
        public event EventHandler Changed;

        public ISettingsKey OpenRoot()
        {
            lock (_objLock)
                return _objRoot == null ? null : new Key(this, _objRoot);
        }

        public ISettingsKey CreateRoot()
        {
            lock (_objLock)
                return new Key(this, _objRoot ?? (_objRoot = new Node()));
        }

        /// <summary>
        /// Snapshot of the whole tree for serialization: values are converted to strings (or ints).
        /// </summary>
        public SettingsSnapshot Export()
        {
            lock (_objLock)
                return _objRoot?.ToSnapshot();
        }

        /// <summary>
        /// Replaces the whole tree, e.g. after reading it from a file.
        /// </summary>
        public void Import(SettingsSnapshot objSnapshot)
        {
            lock (_objLock)
                _objRoot = objSnapshot == null ? null : Node.FromSnapshot(objSnapshot);
        }

        private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

        private sealed class Node
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, Node> Children = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

            public SettingsSnapshot ToSnapshot()
            {
                SettingsSnapshot objReturn = new SettingsSnapshot();
                foreach (KeyValuePair<string, object> kvp in Values)
                    objReturn.Values[kvp.Key] = kvp.Value;
                foreach (KeyValuePair<string, Node> kvp in Children)
                    objReturn.Children[kvp.Key] = kvp.Value.ToSnapshot();
                return objReturn;
            }

            public static Node FromSnapshot(SettingsSnapshot objSnapshot)
            {
                Node objReturn = new Node();
                foreach (KeyValuePair<string, object> kvp in objSnapshot.Values)
                    objReturn.Values[kvp.Key] = kvp.Value;
                foreach (KeyValuePair<string, SettingsSnapshot> kvp in objSnapshot.Children)
                    objReturn.Children[kvp.Key] = FromSnapshot(kvp.Value);
                return objReturn;
            }
        }

        private sealed class Key : ISettingsKey
        {
            private readonly MemorySettingsStore _objStore;
            private readonly Node _objNode;

            public Key(MemorySettingsStore objStore, Node objNode)
            {
                _objStore = objStore;
                _objNode = objNode;
            }

            public object GetValue(string strName)
            {
                lock (_objStore._objLock)
                    return _objNode.Values.TryGetValue(strName, out object objValue) ? objValue : null;
            }

            public void SetValue(string strName, object objValue)
            {
                if (strName == null)
                    throw new ArgumentNullException(nameof(strName));
                lock (_objStore._objLock)
                    _objNode.Values[strName] = objValue ?? throw new ArgumentNullException(nameof(objValue));
                _objStore.OnChanged();
            }

            public void DeleteValue(string strName)
            {
                bool blnRemoved;
                lock (_objStore._objLock)
                    blnRemoved = _objNode.Values.Remove(strName);
                if (blnRemoved)
                    _objStore.OnChanged();
            }

            public string[] GetValueNames()
            {
                lock (_objStore._objLock)
                    return _objNode.Values.Keys.ToArray();
            }

            public string[] GetSubKeyNames()
            {
                lock (_objStore._objLock)
                    return _objNode.Children.Keys.ToArray();
            }

            public ISettingsKey OpenSubKey(string strName)
            {
                lock (_objStore._objLock)
                {
                    Node objCurrent = _objNode;
                    foreach (string strPart in SplitPath(strName))
                    {
                        if (!objCurrent.Children.TryGetValue(strPart, out objCurrent))
                            return null;
                    }
                    return new Key(_objStore, objCurrent);
                }
            }

            public ISettingsKey CreateSubKey(string strName)
            {
                lock (_objStore._objLock)
                {
                    Node objCurrent = _objNode;
                    foreach (string strPart in SplitPath(strName))
                    {
                        if (!objCurrent.Children.TryGetValue(strPart, out Node objChild))
                        {
                            objChild = new Node();
                            objCurrent.Children.Add(strPart, objChild);
                        }
                        objCurrent = objChild;
                    }
                    return new Key(_objStore, objCurrent);
                }
            }

            public void DeleteSubKeyTree(string strName)
            {
                bool blnRemoved;
                lock (_objStore._objLock)
                    blnRemoved = _objNode.Children.Remove(strName);
                if (blnRemoved)
                    _objStore.OnChanged();
            }

            public void Dispose()
            {
                // Nothing to release: nodes live as long as the store.
            }

            private static string[] SplitPath(string strName)
            {
                return (strName ?? string.Empty).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            }
        }
    }

    /// <summary>
    /// Plain data form of a settings tree, for serialization.
    /// </summary>
    public sealed class SettingsSnapshot
    {
        public Dictionary<string, object> Values { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, SettingsSnapshot> Children { get; } = new Dictionary<string, SettingsSnapshot>(StringComparer.OrdinalIgnoreCase);
    }
}
