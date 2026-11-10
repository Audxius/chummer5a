using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chummer.Desktop.ViewModels
{
    public sealed class CustomDirectorySettingOption : SettingOption
    {
        private readonly CharacterSettings _settings;
        private readonly Action _changed;
        private bool _refreshing;
        public ObservableCollection<CustomDirectoryRow> Directories { get; } = new ObservableCollection<CustomDirectoryRow>();

        public CustomDirectorySettingOption(ISettingOptionHost host, CharacterSettings settings, Action changed)
            : base(host, "\"Custom data directories (in loading order)\"")
        {
            _settings = settings; _changed = changed;
            // Different directories can carry the same manifest id/version; settings serialize that identity once.
            var installed = GlobalSettings.CustomDataDirectoryInfos.GroupBy(x => x.CharacterSettingsSaveKey).Select(x => x.First()).ToList();
            foreach (var entry in settings.CustomDataDirectoryKeys)
            {
                string id = CustomDataDirectoryInfo.GetIdFromCharacterSettingsSaveKey(entry.Key, out _);
                var match = installed.FirstOrDefault(x => x.CharacterSettingsSaveKey == entry.Key || x.Name == entry.Key || !string.IsNullOrEmpty(id) && x.InternalId == id);
                Directories.Add(new CustomDirectoryRow(this, entry.Key, match?.CurrentDisplayName ?? entry.Key + " (missing)", match?.DirectoryPath ?? "", entry.Value));
                if (match != null) installed.Remove(match);
            }
            foreach (var info in installed.OrderBy(x => x.CurrentDisplayName))
                Directories.Add(new CustomDirectoryRow(this, info.CharacterSettingsSaveKey, info.CurrentDisplayName, info.DirectoryPath, false));
        }

        internal void Apply()
        {
            if (_refreshing) return;
            using (_settings.LockObject.EnterWriteLock())
            {
                _settings.CustomDataDirectoryKeys.Clear();
                foreach (var row in Directories) _settings.CustomDataDirectoryKeys.Add(row.Key, row.Value);
            }
            _settings.OnPropertyChanged(nameof(CharacterSettings.CustomDataDirectoryKeys));
            _changed();
        }

        internal void Move(CustomDirectoryRow row, int delta)
        {
            int index = Directories.IndexOf(row);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= Directories.Count) return;
            Directories.Move(index, target); Apply();
        }

        protected override void RefreshValue()
        {
            _refreshing = true;
            try
            {
                foreach (var row in Directories)
                    row.Value = _settings.CustomDataDirectoryKeys.TryGetValue(row.Key, out bool enabled) && enabled;
            }
            finally { _refreshing = false; }
        }
    }

    public sealed partial class CustomDirectoryRow : ObservableObject
    {
        private readonly CustomDirectorySettingOption _owner;
        public CustomDirectoryRow(CustomDirectorySettingOption owner, string key, string name, string path, bool enabled)
        { _owner = owner; Key = key; Name = name; Path = path; _value = enabled; }
        public string Key { get; }
        public string Name { get; }
        public string Path { get; }
        [ObservableProperty] private bool _value;
        partial void OnValueChanged(bool value) => _owner.Apply();
        [RelayCommand] private void MoveUp() => _owner.Move(this, -1);
        [RelayCommand] private void MoveDown() => _owner.Move(this, 1);
    }
}
