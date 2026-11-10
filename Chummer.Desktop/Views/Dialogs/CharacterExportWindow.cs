using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;
using Chummer.Printing;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class CharacterExportWindow : Window
    {
        private readonly Character _character;
        private readonly ComboBox _format = new ComboBox { Width = 200 };
        private readonly ComboBox _language = new ComboBox { Width = 180 };
        private readonly TextBox _output = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
            FontFamily = new Avalonia.Media.FontFamily("monospace") };
        private readonly TextBlock _status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private int _generation;
        private Task _pending;
        private string _pendingFormat;
        private string _pendingLanguage;
        public string Output => _output.Text ?? "";

        public CharacterExportWindow(Character character)
        {
            _character = character; Title = "Export character"; Width = 1000; Height = 760; MinWidth = 700; MinHeight = 500;
            var root = new DockPanel { Margin = new Thickness(24) };
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 14) };
            _format.ItemsSource = CharacterExporter.Formats(); _format.SelectedIndex = 0;
            var languages = CharacterExporter.Languages();
            _language.ItemsSource = languages; _language.SelectedItem = languages.First(x => x.Value?.ToString() == GlobalSettings.Language);
            _language.ItemTemplate = new FuncDataTemplate<ListItem>((item, _) => new TextBlock { Text = item.Name });
            var generate = new Button { Content = "GENERATE" }; generate.Click += async (_, _) => await GenerateAsync();
            var copy = new Button { Content = "COPY" }; copy.Click += async (_, _) => { if (Clipboard != null && !string.IsNullOrEmpty(Output)) await Clipboard.SetTextAsync(Output); };
            var save = new Button { Content = "SAVE EXPORT…", Classes = { "primary" } }; save.Click += async (_, _) => await SaveAsync();
            var close = new Button { Content = "CLOSE" }; close.Click += (_, _) => Close();
            toolbar.Children.Add(_format); toolbar.Children.Add(_language); toolbar.Children.Add(generate); toolbar.Children.Add(copy); toolbar.Children.Add(save); toolbar.Children.Add(close);
            DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
            DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
            root.Children.Add(_output); Content = root;
            _format.SelectionChanged += async (_, _) => await GenerateAsync();
            _language.SelectionChanged += async (_, _) => await GenerateAsync();
            Opened += async (_, _) => await GenerateAsync();
            Closed += (_, _) => ++_generation;
        }

        public Task GenerateAsync()
        {
            string format = _format.SelectedItem as string;
            string language = (_language.SelectedItem as ListItem?)?.Value?.ToString() ?? GlobalSettings.Language;
            if (format == null) return Task.CompletedTask;
            if (_pending != null && !_pending.IsCompleted && format == _pendingFormat && language == _pendingLanguage)
                return _pending;
            _pendingFormat = format; _pendingLanguage = language;
            return _pending = GenerateCoreAsync(format, language, ++_generation);
        }

        private async Task GenerateCoreAsync(string format, string language, int generation)
        {
            _output.Text = ""; _status.Text = "Generating export…";
            try
            {
                string result = await Task.Run(() => CharacterExporter.RenderAsync(_character, format, language));
                if (generation != _generation) return;
                _output.Text = result; _status.Text = result.Length + " characters";
            }
            catch (Exception ex) { if (generation == _generation) _status.Text = ex.Message; }
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrEmpty(Output)) return;
            string format = _format.SelectedItem as string;
            string extension = format == "JSON" ? "json" : "xml";
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save character export", DefaultExtension = extension,
                SuggestedFileName = (_character.Alias.Length > 0 ? _character.Alias : "Runner") + "." + extension,
                FileTypeChoices = new[] { new FilePickerFileType(format ?? "Export") { Patterns = new[] { "*." + extension } } } });
            if (file == null) return;
            try
            {
                using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
                using var writer = new StreamWriter(stream); await writer.WriteAsync(Output); _status.Text = "Export saved.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
    }
}
