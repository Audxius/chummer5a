using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Chummer.DataEditing;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class XmlDataWindow : Window
    {
        private readonly ComboBox _file = new ComboBox { Width = 180 };
        private readonly TextBox _amendment = Editor(false);
        private readonly TextBox _original = Editor(true);
        private readonly TextBox _result = Editor(true);
        private readonly TextBox _changes = Editor(true);
        private readonly TextBlock _status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly StackPanel _commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        private string _baseText;
        private string _sourceFile = "gear.xml";
        private string _appliedText;
        private string _savedText;
        private bool IsDirty => !string.Equals(_savedText, _amendment.Text, StringComparison.Ordinal);
        private bool _busy;
        private bool _allowClose;
        private bool _confirmingClose;
        private int _version;
        public string Result => _result.Text ?? "";
        public string Changes => _changes.Text ?? "";

        public XmlDataWindow()
        {
            Title = "XML amendment editor"; Width = 1350; Height = 900; MinWidth = 1050; MinHeight = 650;
            var root = new DockPanel { Margin = new Thickness(20) };
            _file.ItemsSource = Utils.BasicDataFileNames.OrderBy(x => x).ToList(); _file.SelectedItem = _sourceFile;
            _commands.Children.Add(_file);
            AddButton("LOAD BASE", LoadBaseAsync); AddButton("OPEN AMENDMENT…", OpenAmendmentAsync);
            AddButton("APPLY", ApplyAsync); AddButton("FORMAT", FormatAsync); AddButton("SAVE AMENDMENT…", async () => { await SaveAsync(); });
            DockPanel.SetDock(_commands, Dock.Top); _commands.Margin = new Thickness(0, 0, 0, 14); root.Children.Add(_commands);
            var footer = new StackPanel { Spacing = 6, Margin = new Thickness(0, 12, 0, 0) };
            footer.Children.Add(_status);
            footer.Children.Add(new TextBlock { Text = "Save as amend_<file>.xml in an enabled custom data directory.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
            var inputs = new TabControl { ItemsSource = new[] { new TabItem { Header = "AMENDMENT", Content = _amendment }, new TabItem { Header = "BASE DATA", Content = _original } }, SelectedIndex = 0 };
            var output = new TabControl { ItemsSource = new[] { new TabItem { Header = "CHANGES", Content = _changes }, new TabItem { Header = "RESULT XML", Content = _result } }, SelectedIndex = 0 };
            body.Children.Add(inputs); Grid.SetColumn(output, 1); body.Children.Add(output); root.Children.Add(body); Content = root;
            _amendment.Text = LanguageManager.GetString("XmlEditor_AmendmentTemplate").Replace("\\n", "\n");
            _savedText = _amendment.Text;
            _amendment.PropertyChanged += (_, args) =>
            {
                if (args.Property != TextBox.TextProperty) return;
                ++_version; _appliedText = null; _result.Text = ""; _changes.Text = "Apply the amendment to preview its changes.";
            };
            Opened += async (_, _) => { if (_baseText == null) await GuardAsync(LoadBaseAsync); };
            Closing += (_, args) =>
            {
                if (_allowClose || !IsDirty) return;
                args.Cancel = true;
                if (!_confirmingClose) _ = ConfirmCloseAsync();
            };
        }

        private static TextBox Editor(bool readOnly) => new TextBox { IsReadOnly = readOnly, AcceptsReturn = true, AcceptsTab = true,
            FontFamily = new Avalonia.Media.FontFamily("monospace"), FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        private void AddButton(string text, Func<Task> action)
        {
            var button = new Button { Content = text }; button.Click += async (_, _) => await GuardAsync(action); _commands.Children.Add(button);
        }
        private async Task GuardAsync(Func<Task> action)
        {
            if (_busy) return;
            _busy = true; _commands.IsEnabled = false; _amendment.IsReadOnly = true;
            try { await action(); }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _busy = false; _commands.IsEnabled = true; _amendment.IsReadOnly = false; }
        }
        private async Task LoadBaseAsync()
        {
            if (!(_file.SelectedItem is string file)) return;
            _status.Text = "Loading base data…";
            string content = await Task.Run(() => File.ReadAllText(Path.Combine(Utils.GetDataFolderPath, file)));
            string formatted = await Task.Run(() => XmlAmendmentRules.FormatAsync(content));
            _baseText = content; _sourceFile = file; _original.Text = formatted; _appliedText = null; ++_version;
            _result.Text = ""; _changes.Text = "Apply the amendment to preview its changes."; _status.Text = "Base: " + file;
        }
        public async Task LoadContentAsync(string sourceName, string original, string amendment)
        {
            _baseText = original; _sourceFile = sourceName;
            _original.Text = await Task.Run(() => XmlAmendmentRules.FormatAsync(original));
            _amendment.Text = amendment; _savedText = amendment;
            await ApplyAsync();
        }
        private async Task OpenAmendmentAsync()
        {
            if (!await MayDiscardAsync()) return;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open XML amendment", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("XML amendment") { Patterns = new[] { "*.xml" } } } });
            if (files.Count == 0) return;
            using (var stream = await files[0].OpenReadAsync()) using (var reader = new StreamReader(stream, Encoding.UTF8, true)) _amendment.Text = await reader.ReadToEndAsync();
            _savedText = _amendment.Text; _status.Text = "Opened " + files[0].Name;
        }
        public async Task ApplyAsync()
        {
            if (_baseText == null) { _status.Text = "Load base data first."; return; }
            string amendment = _amendment.Text ?? ""; int version = _version;
            _status.Text = "Applying amendment…";
            var preview = await XmlAmendmentRules.PreviewAsync(_baseText, amendment);
            if (version != _version) return;
            _result.Text = preview.Result; _changes.Text = preview.Changes; _appliedText = amendment; _status.Text = "Preview updated.";
        }
        private async Task FormatAsync()
        {
            string content = _amendment.Text ?? "";
            string formatted = await Task.Run(() => XmlAmendmentRules.FormatAsync(content));
            _amendment.Text = formatted; _status.Text = "Amendment formatted.";
        }
        private async Task<bool> SaveAsync()
        {
            if (_appliedText != _amendment.Text) await ApplyAsync();
            if (_appliedText != _amendment.Text || string.IsNullOrEmpty(_appliedText)) return false;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save XML amendment", DefaultExtension = "xml",
                SuggestedFileName = "amend_" + Path.GetFileName(_sourceFile), FileTypeChoices = new[] { new FilePickerFileType("XML amendment") { Patterns = new[] { "*.xml" } } } });
            if (file == null) return false;
            string path = file.TryGetLocalPath();
            if (path != null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(Utils.GetDataFolderPath, _sourceFile)), Utils.PathStringComparison))
                throw new InvalidOperationException("Save the amendment separately from its base data file.");
            using (var stream = await file.OpenWriteAsync())
            {
                stream.SetLength(0);
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) await writer.WriteAsync(_appliedText);
            }
            _savedText = _appliedText; _status.Text = "Saved " + file.Name; return true;
        }
        private async Task<bool> MayDiscardAsync()
        {
            if (!IsDirty) return true;
            var answer = await HostServices.Messages.ShowAsync(this, "Save your amendment first?", "XML amendment editor", MessageButtons.YesNoCancel, MessageIcon.Question);
            if (answer == MessageResult.Cancel) return false;
            return answer != MessageResult.Yes || await SaveAsync();
        }
        private async Task ConfirmCloseAsync()
        {
            _confirmingClose = true;
            try { if (await MayDiscardAsync()) { _allowClose = true; Close(); } }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _confirmingClose = false; }
        }
    }
}
