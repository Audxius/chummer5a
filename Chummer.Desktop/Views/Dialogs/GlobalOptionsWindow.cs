using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Chummer.Desktop.ViewModels;

namespace Chummer.Desktop.Views.Dialogs
{
    /// <summary>Native global preferences; changes are staged until Save.</summary>
    public sealed class GlobalOptionsWindow : Window, ISettingOptionHost
    {
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>();
        public bool Accepted { get; private set; }
        public bool IsEditable => true;
        public object GetValue(string property) => _values[property];
        public void SetValue(string property, object value) => _values[property] = value;

        public GlobalOptionsWindow(string requestedBook = null, bool linkReader = false)
        {
            Title = "Global options"; Width = 900; Height = 680; MinWidth = 700; MinHeight = 480;
            var root = new DockPanel { Margin = new Thickness(24) };
            var footer = new StackPanel { Spacing = 10, Margin = new Thickness(0, 16, 0, 0) };
            var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var save = new Button { Content = "SAVE OPTIONS", Classes = { "primary" } };
            var pdfPath = new TextBox { PlaceholderText = "Path to the sourcebook PDF" };
            var pdfOffset = new NumericUpDown { Minimum = -10000, Maximum = 10000, Value = 0, FormatString = "0", Width = 130 };
            if (!string.IsNullOrEmpty(requestedBook) && GlobalSettings.SourcebookInfos.TryGetValue(requestedBook, out var originalBook))
            { pdfPath.Text = originalBook.Path; pdfOffset.Value = originalBook.Offset; }
            var cancel = new Button { Content = "CANCEL" };
            cancel.Click += (_, _) => Close();
            save.Click += async (_, _) =>
            {
                save.IsEnabled = false;
                try
                {
                    if (linkReader && (!File.Exists((string)_values["PdfAppPath"]) || string.IsNullOrWhiteSpace((string)_values["PdfParameters"])))
                        throw new InvalidOperationException("Choose an existing PDF reader and enter its launch arguments.");
                    string bookPath = pdfPath.Text;
                    if (!string.IsNullOrEmpty(requestedBook))
                    {
                        string local = Uri.TryCreate(bookPath, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : bookPath;
                        if (!File.Exists(local)) throw new InvalidOperationException("Choose an existing sourcebook PDF.");
                        bookPath = new Uri(Path.GetFullPath(local)).AbsoluteUri;
                    }
                    int offset = (int)(pdfOffset.Value ?? 0);
                    await Task.Run(async () =>
                    {
                        foreach (var entry in _values)
                            SettingReflection.Find(typeof(GlobalSettings), entry.Key).SetValue(null, entry.Value);
                        if (!string.IsNullOrEmpty(requestedBook))
                        {
                            var books = (await GlobalSettings.GetSourcebookInfosAsync()).ToDictionary(x => x.Key, x => x.Value);
                            if (!books.TryGetValue(requestedBook, out var book)) books[requestedBook] = book = new SourcebookInfo { Code = requestedBook };
                            book.Path = bookPath; book.Offset = offset;
                            await GlobalSettings.SetSourcebookInfosAsync(books, false);
                        }
                        await GlobalSettings.SaveOptionsToRegistry();
                    });
                    Accepted = true;
                    Close();
                }
                catch (Exception ex) { error.Text = ex.Message; }
                finally { save.IsEnabled = true; }
            };
            buttons.Children.Add(save); buttons.Children.Add(cancel);
            footer.Children.Add(error); footer.Children.Add(buttons);
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var options = new StackPanel { Spacing = 14 };
            void AddGroup(string title, params (string Property, string Label)[] fields)
            {
                options.Children.Add(new TextBlock { Text = title, Classes = { "h2" }, Margin = new Thickness(0, 12, 0, 0) });
                foreach (var field in fields)
                {
                    _values[field.Property] = SettingReflection.Find(typeof(GlobalSettings), field.Property).GetValue(null);
                    var row = new BoolSettingOption(this, field.Property, "\"" + field.Label + "\"");
                    row.Refresh(); options.Children.Add(CharacterSettingsWindow.Row(row));
                }
            }
            AddGroup("SESSION", ("ConfirmDelete", "Confirm removal of character items"), ("ConfirmKarmaExpense", "Confirm karma spending"),
                ("DatesIncludeTime", "Include time in expense dates"), ("CreateBackupOnCareer", "Back up a runner before career conversion"));
            AddGroup("STORE", ("HideItemsOverAvailLimit", "Hide items over the creation availability limit"), ("SearchInCategoryOnly", "Search only within the selected category"));
            AddGroup("CHARACTER SHEETS", ("PrintSkillsWithZeroRating", "Include untrained skills"), ("PrintExpenses", "Include expenses"),
                ("PrintFreeExpenses", "Include zero-value expenses"), ("PrintNotes", "Include notes"));
            AddGroup("DATA", ("LiveCustomData", "Reload custom data when it changes"), ("ShowCharacterCustomDataWarning", "Warn about missing character custom data"));
            options.Children.Add(new TextBlock { Text = "PDF READER", Classes = { "h2" }, Margin = new Thickness(0, 12, 0, 0) });
            TextBox AddText(string property, string label)
            {
                _values[property] = SettingReflection.Find(typeof(GlobalSettings), property).GetValue(null) ?? "";
                options.Children.Add(new TextBlock { Text = label });
                var input = new TextBox { Text = (string)_values[property], Name = property };
                input.TextChanged += (_, _) => _values[property] = input.Text ?? "";
                options.Children.Add(input); return input;
            }
            var reader = AddText("PdfAppPath", "Reader executable");
            AddText("PdfParameters", "Arguments · {page}, {localpath}, {absolutepath}");
            var browseReader = new Button { Content = "Choose PDF reader…" };
            async Task BrowseReader()
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose PDF reader executable", AllowMultiple = false });
                string path = files.FirstOrDefault()?.TryGetLocalPath(); if (!string.IsNullOrEmpty(path)) reader.Text = path;
            }
            browseReader.Click += async (_, _) => await BrowseReader(); options.Children.Add(browseReader);
            if (!string.IsNullOrEmpty(requestedBook))
            {
                options.Children.Add(new TextBlock { Text = "SOURCEBOOK · " + requestedBook, Classes = { "h2" }, Margin = new Thickness(0, 12, 0, 0) });
                options.Children.Add(pdfPath);
                var browse = new Button { Content = "Choose sourcebook PDF…" };
                browse.Click += async (_, _) =>
                {
                    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Link " + requestedBook + " PDF", AllowMultiple = false,
                        FileTypeFilter = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } } });
                    string path = files.FirstOrDefault()?.TryGetLocalPath(); if (!string.IsNullOrEmpty(path)) pdfPath.Text = path;
                };
                options.Children.Add(browse); options.Children.Add(new TextBlock { Text = "Page offset" }); options.Children.Add(pdfOffset);
            }
            root.Children.Add(new ScrollViewer { Content = options }); Content = root;
        }
    }
}
