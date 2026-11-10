using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Chummer.Selection;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class MasterIndexWindow : Window
    {
        private readonly CharacterSettings _settings;
        private IReadOnlyList<MasterIndexEntry> _entries = Array.Empty<MasterIndexEntry>();
        private readonly TextBox _search = new TextBox { PlaceholderText = "Search names, source books and notes…" };
        private readonly ComboBox _book = new ComboBox { Width = 150 };
        private readonly ComboBox _file = new ComboBox { Width = 180 };
        private readonly ListBox _list = new ListBox();
        private readonly TextBlock _status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly TextBlock _details = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly TextBlock _link = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } };
        private readonly NumericUpDown _offset = new NumericUpDown { Minimum = -10000, Maximum = 10000, Value = 0, Width = 120 };
        public MasterIndexEntry Selected => _list.SelectedItem as MasterIndexEntry;

        public MasterIndexWindow(CharacterSettings settings)
        {
            _settings = settings; Title = "Master Index"; Width = 1180; Height = 800; MinWidth = 900; MinHeight = 600;
            var root = new DockPanel { Margin = new Thickness(24) };
            var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 16) };
            toolbar.Children.Add(_search); Grid.SetColumn(_book, 1); toolbar.Children.Add(_book); Grid.SetColumn(_file, 2); toolbar.Children.Add(_file);
            var refresh = new Button { Content = "REFRESH" }; refresh.Click += async (_, _) => await LoadAsync(); Grid.SetColumn(refresh, 3); toolbar.Children.Add(refresh);
            DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
            DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
            var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,340"), ColumnSpacing = 20 };
            _list.ItemTemplate = new FuncDataTemplate<MasterIndexEntry>((entry, _) =>
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,100"), Margin = new Thickness(4, 6) };
                row.Children.Add(new TextBlock { Text = entry.Name, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
                var source = new TextBlock { Text = entry.DisplaySource, Classes = { "label" } }; Grid.SetColumn(source, 1); row.Children.Add(source);
                return row;
            });
            layout.Children.Add(_list);
            var detail = new StackPanel { Spacing = 14 };
            detail.Children.Add(new TextBlock { Text = "SOURCE LOOKUP", Classes = { "h2" } }); detail.Children.Add(_details); detail.Children.Add(_link);
            var open = new Button { Content = "OPEN PDF AT PAGE", Classes = { "primary" } }; open.Click += async (_, _) => await OpenPdfAsync();
            var link = new Button { Content = "LINK SOURCE PDF" }; link.Click += async (_, _) => await LinkPdfAsync();
            var offset = new Button { Content = "SAVE PAGE OFFSET" }; offset.Click += async (_, _) => await SaveOffsetAsync();
            detail.Children.Add(open); detail.Children.Add(link); detail.Children.Add(new TextBlock { Text = "PDF page offset", Classes = { "label" } });
            detail.Children.Add(_offset); detail.Children.Add(offset);
            var close = new Button { Content = "CLOSE" }; close.Click += (_, _) => Close(); detail.Children.Add(close);
            Grid.SetColumn(detail, 1); layout.Children.Add(detail); root.Children.Add(layout); Content = root;
            _search.TextChanged += (_, _) => Filter(); _book.SelectionChanged += (_, _) => Filter(); _file.SelectionChanged += (_, _) => Filter();
            _list.SelectionChanged += (_, _) => RefreshDetails();
        }

        public async Task LoadAsync()
        {
            _status.Text = "Loading source index…";
            try
            {
                _entries = await Task.Run(() => MasterIndexCatalog.LoadAsync(_settings));
                _book.ItemsSource = new[] { "ALL BOOKS" }.Concat(_entries.Select(x => x.Book).Distinct().OrderBy(x => x)).ToList(); _book.SelectedIndex = 0;
                _file.ItemsSource = new[] { "ALL TYPES" }.Concat(_entries.Select(x => x.File).Distinct().OrderBy(x => x)).ToList(); _file.SelectedIndex = 0;
                Filter();
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        public void Search(string query) { _search.Text = query; }

        private void Filter()
        {
            string[] words = (_search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var shown = _entries.Where(x => (_book.SelectedIndex <= 0 || x.Book == _book.SelectedItem as string)
                && (_file.SelectedIndex <= 0 || x.File == _file.SelectedItem as string)
                && words.All(word => (x.Name + " " + x.Book + " " + x.Notes).Contains(word, StringComparison.CurrentCultureIgnoreCase))).ToList();
            _list.ItemsSource = shown; _list.SelectedIndex = shown.Count > 0 ? 0 : -1;
            _status.Text = shown.Count + " / " + _entries.Count + " entries";
        }

        private void RefreshDetails()
        {
            var entry = Selected;
            _details.Text = entry == null ? "Choose an index entry." : entry.Name + "\n" + entry.DisplaySource + "\n" + entry.File + "\n\n" + entry.Notes;
            if (entry != null && GlobalSettings.SourcebookInfos.TryGetValue(entry.Book, out var book))
            { _link.Text = string.IsNullOrEmpty(book.Path) ? "No PDF linked to this source." : book.Path; _offset.Value = book.Offset; }
            else { _link.Text = "No PDF linked to this source."; _offset.Value = 0; }
        }

        private async Task LinkPdfAsync()
        {
            if (Selected == null) return;
            string code = Selected.Book;
            var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Link " + code + " PDF", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } } });
            string path = picked.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                await Task.Run(async () =>
                {
                    var books = (await GlobalSettings.GetSourcebookInfosAsync()).ToDictionary(x => x.Key, x => x.Value);
                    if (!books.TryGetValue(code, out var book)) books[code] = book = new SourcebookInfo { Code = code };
                    book.Path = new Uri(path).AbsoluteUri;
                    await GlobalSettings.SetSourcebookInfosAsync(books, false);
                    await GlobalSettings.SaveOptionsToRegistry();
                });
                RefreshDetails();
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        private async Task SaveOffsetAsync()
        {
            if (Selected == null || !GlobalSettings.SourcebookInfos.TryGetValue(Selected.Book, out var book)) return;
            book.Offset = (int)(_offset.Value ?? 0);
            await Task.Run(() => GlobalSettings.SaveOptionsToRegistry()); _status.Text = "Page offset saved.";
        }

        private async Task OpenPdfAsync()
        {
            if (Selected == null) return;
            if (!GlobalSettings.SourcebookInfos.TryGetValue(Selected.Book, out var book) || string.IsNullOrEmpty(book.Path))
            { await LinkPdfAsync(); return; }
            try
            {
                var uri = new Uri(book.Path);
                if (!File.Exists(uri.LocalPath)) { _status.Text = "The linked PDF could not be found."; return; }
                if (!string.IsNullOrWhiteSpace(GlobalSettings.PdfAppPath) && File.Exists(GlobalSettings.PdfAppPath) && !string.IsNullOrWhiteSpace(GlobalSettings.PdfParameters))
                    await CommonFunctions.OpenPdf(Selected.Source, _settings);
                else
                {
                    int page = int.TryParse(Selected.Page, out int printed) ? printed : 1;
                    string target = new UriBuilder(uri) { Fragment = "page=" + Math.Max(1, page + book.Offset) }.Uri.AbsoluteUri;
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                }
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
    }
}
