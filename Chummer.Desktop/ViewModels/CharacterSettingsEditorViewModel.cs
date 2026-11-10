using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chummer.Backend.Enums;
using System.Threading.Tasks;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>Edits an isolated copy, so closing the editor never changes a runner's rules.</summary>
    public sealed class CharacterSettingsEditorViewModel : ISettingOptionHost, IDisposable
    {
        public static async Task<CharacterSettingsEditorViewModel> CreateAsync(CharacterSettings source)
        {
            var copy = new CharacterSettings();
            try
            {
                await copy.CopyValuesAsync(source).ConfigureAwait(false);
                return new CharacterSettingsEditorViewModel(copy);
            }
            catch { await copy.DisposeAsync().ConfigureAwait(false); throw; }
        }

        private CharacterSettingsEditorViewModel(CharacterSettings copy)
        {
            Settings = copy;
            BuildMethodOption = Choice("BuildMethod", "Label_SelectBP_BuildMethod", Enum.GetNames(typeof(CharacterBuildMethod)));
            PriorityTableOption = Choice("PriorityTable", "\"Priority table\"",
                Settings.LoadDataXPath("priorities.xml").Select("/chummer/prioritytables/prioritytable").Cast<System.Xml.XPath.XPathNavigator>().Select(x => x.Value).Append(Settings.PriorityTable).Distinct());
            LimbCountOption = new NumberSettingOption(this, "LimbCount", "Label_CharacterOptions_LimbCount", null, 0, 100);
            AllowedGradesOption = new FlagsSettingOption(this, "\"Allowed cyberware and bioware grades\"");
            var grades = new[] { "cyberware.xml", "bioware.xml" }.SelectMany(file => Settings.LoadDataXPath(file)
                .Select("/chummer/grades/grade/name").Cast<System.Xml.XPath.XPathNavigator>().Select(x => x.Value)).Distinct().OrderBy(x => x);
            foreach (string grade in grades)
                AllowedGradesOption.Flags.Add(new FlagItem(grade, () => !Settings.BannedWareGrades.Contains(grade), enabled =>
                {
                    if (enabled) Settings.BannedWareGrades.Remove(grade); else Settings.BannedWareGrades.Add(grade);
                }));
            RedlinerOption = new FlagsSettingOption(this, "\"Limbs included by Redliner\"");
            foreach (string limb in new[] { "Skull", "Torso", "Arms", "Legs" })
            {
                string property = "RedlinerExcludes" + limb;
                RedlinerOption.Flags.Add(new FlagItem(limb, () => !(bool)GetValue(property), value => SetValue(property, !value)));
            }
            SourceBooksOption = new FlagsSettingOption(this, "\"Enabled source books\"");
            RebuildSourceBooks();
            CustomDirectoriesOption = new CustomDirectorySettingOption(this, Settings, () => { RebuildSourceBooks(); Refresh(); });
            Groups = CharacterSettingsSchema.BasicOptions(this).Concat(CharacterSettingsSchema.KarmaCosts(this)).Concat(CharacterSettingsSchema.HouseRules(this))
                .Concat(new[] { new SettingGroup("\"SOURCE BOOKS\"", new SettingOption[] { SourceBooksOption }, true),
                    new SettingGroup("\"CUSTOM DATA\"", new SettingOption[] { CustomDirectoriesOption }, true) }).ToList();
            Refresh();
        }

        public CharacterSettings Settings { get; }
        public bool IsEditable => true;
        public ChoiceSettingOption BuildMethodOption { get; }
        public ChoiceSettingOption PriorityTableOption { get; }
        public NumberSettingOption LimbCountOption { get; }
        public FlagsSettingOption AllowedGradesOption { get; }
        public FlagsSettingOption RedlinerOption { get; }
        public FlagsSettingOption SourceBooksOption { get; }
        public CustomDirectorySettingOption CustomDirectoriesOption { get; }
        public IReadOnlyList<SettingGroup> Groups { get; }

        private void RebuildSourceBooks()
        {
            SourceBooksOption.Flags.Clear();
            var books = Settings.LoadDataXPath("books.xml").Select("/chummer/books/book").Cast<System.Xml.XPath.XPathNavigator>()
                .Select(node => new { Code = node.SelectSingleNode("code")?.Value,
                    Name = node.SelectSingleNode("translate")?.Value ?? node.SelectSingleNode("name")?.Value,
                    Permanent = node.SelectSingleNode("permanent") != null }).OrderBy(x => x.Name);
            foreach (var book in books.Where(x => !string.IsNullOrEmpty(x.Code)))
            {
                string code = book.Code;
                if (book.Permanent) Settings.BooksWritable.Add(code);
                SourceBooksOption.Flags.Add(new FlagItem(book.Name + " (" + code + ")", () => Settings.Books.Contains(code), enabled =>
                {
                    if (enabled) Settings.BooksWritable.Add(code); else Settings.BooksWritable.Remove(code);
                    Settings.OnPropertyChanged(nameof(CharacterSettings.Books));
                }, !book.Permanent));
            }
            SourceBooksOption.Refresh();
        }

        private ChoiceSettingOption Choice(string property, string label, IEnumerable<string> values)
        {
            var option = new ChoiceSettingOption(this, label, () => GetValue(property)?.ToString(), value => SetValue(property, value));
            option.SetItems(values.Select(value => new ListItem(value, value)));
            return option;
        }

        public object GetValue(string property) => SettingReflection.Find(typeof(CharacterSettings), property)?.GetValue(Settings);

        public void SetValue(string property, object value)
        {
            var member = SettingReflection.Find(typeof(CharacterSettings), property);
            if (member?.CanWrite != true) return;
            member.SetValue(Settings, SettingReflection.Coerce(value, member.PropertyType));
            Refresh();
        }

        private void Refresh()
        {
            if (Groups == null) return;
            foreach (var option in Groups.SelectMany(x => x.Options)) option.Refresh();
        }

        public bool Validate() => Groups.SelectMany(x => x.Options).OfType<TextSettingOption>().All(x => x.Validate());
        public void Dispose() => Settings.Dispose();
    }
}
