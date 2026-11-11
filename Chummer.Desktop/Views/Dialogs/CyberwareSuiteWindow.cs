using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Chummer.Desktop.ViewModels;
using Chummer.Rules;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class CyberwareSuiteWindow : Window
    {
        private readonly CharacterViewModel _owner;
        private readonly ComboBox _type = new ComboBox { ItemsSource = new[] { "Cyberware", "Bioware" }, SelectedIndex = 0, MinWidth = 140 };
        private readonly ComboBox _suites = new ComboBox { MinWidth = 300 };
        private readonly TextBox _name = new TextBox { PlaceholderText = "New suite name" };
        private readonly TextBox _preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly TextBlock _status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly Button _buy = new Button { Content = "INSTALL SUITE", Classes = { "primary" }, IsEnabled = false };
        private readonly StackPanel _toolbar = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 0, 16) };
        private XmlDocument _document;
        private Task _load;
        private int _version;
        private bool _changing;
        public string PreviewText => _preview.Text ?? "";
        private Improvement.ImprovementSource Source => _type.SelectedIndex == 1 ? Improvement.ImprovementSource.Bioware : Improvement.ImprovementSource.Cyberware;
        public CyberwareSuiteWindow(CharacterViewModel owner)
        {
            _owner = owner; Title = "Cyberware and bioware suites"; Width = 950; Height = 640;
            var root = new DockPanel { Margin = new Thickness(24) };
            var top = _toolbar;
            var choose = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; choose.Children.Add(_type); choose.Children.Add(_suites);
            var open = new Button { Content = "OPEN SUITES XML…" }; choose.Children.Add(open); top.Children.Add(choose);
            _suites.ItemTemplate = new FuncDataTemplate<XmlNode>((node, _) => new TextBlock { Text = node?["translate"]?.InnerText ?? node?["name"]?.InnerText ?? "" });
            var author = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12 }; author.Children.Add(_name);
            var create = new Button { Content = "CREATE FROM IMPLANTS" }; Grid.SetColumn(create, 1); author.Children.Add(create);
            var save = new Button { Content = "SAVE XML…" }; Grid.SetColumn(save, 2); author.Children.Add(save); top.Children.Add(author);
            DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
            var footer = new StackPanel { Spacing = 12, Margin = new Thickness(0, 16, 0, 0) }; footer.Children.Add(_status);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; actions.Children.Add(_buy);
            var close = new Button { Content = "CLOSE" }; close.Click += (_, _) => Close(); actions.Children.Add(close); footer.Children.Add(actions);
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(_preview); Content = root;
            _type.SelectionChanged += async (_, _) => { if (!_changing) await LoadInstalledAsync(); };
            _suites.SelectionChanged += async (_, _) => { if (!_changing) await PreviewAsync(); };
            open.Click += async (_, _) => await OpenAsync(); create.Click += async (_, _) => await CreateAsync();
            save.Click += async (_, _) => await SaveAsync(); _buy.Click += async (_, _) => await InstallAsync();
            Opened += async (_, _) => await LoadAsync(); Closed += (_, _) => ++_version;
        }
        public Task LoadAsync() => _load ??= LoadInstalledAsync();
        private async Task LoadInstalledAsync()
        {
            var source = Source;
            _toolbar.IsEnabled = false;
            try { SetDocument(await _owner.ReadModelAsync(() => _owner.Character.LoadDataAsync(source == Improvement.ImprovementSource.Cyberware ? "cyberware.xml" : "bioware.xml"))); await PreviewAsync(); }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _toolbar.IsEnabled = true; }
        }
        private void SetDocument(XmlDocument document)
        {
            _document = document; _changing = true;
            var suites = document.SelectNodes("/chummer/suites/suite").Cast<XmlNode>().ToArray(); _suites.ItemsSource = suites;
            _suites.SelectedIndex = suites.Length == 0 ? -1 : 0; _changing = false;
            _preview.Text = ""; _buy.IsEnabled = false;
            _status.Text = suites.Length == 0 ? "No suites available. Open a suite file or create one from your implants." : "";
        }
        public async Task CreateAsync(string name = null)
        {
            if (name != null) _name.Text = name; string suiteName = _name.Text; var source = Source;
            _toolbar.IsEnabled = false; _buy.IsEnabled = false; ++_version;
            try { SetDocument(await _owner.ReadModelAsync(() => CyberwareSuiteRules.ExportAsync(_owner.Character, suiteName, source))); await PreviewAsync(); }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _toolbar.IsEnabled = true; }
        }
        private async Task PreviewAsync()
        {
            int version = ++_version; _buy.IsEnabled = false;
            var suite = _suites.SelectedItem as XmlNode; var source = Source;
            if (suite == null) return;
            _status.Text = "Calculating suite…";
            try
            {
                var quote = await _owner.ReadModelAsync(() => CyberwareSuiteRules.QuoteAsync(_owner.Character, suite, source));
                if (version != _version) return;
                if (quote == null) { _status.Text = "This suite is unavailable for this character or grade."; return; }
                _preview.Text = quote.Name + "\nGrade: " + quote.Grade + "\nCost: " + quote.Cost.ToString("N2") + "¥\nEssence: " + quote.Essence.ToString("0.##") + "\n\n" + string.Join("\n", quote.Items);
                _buy.IsEnabled = true; _status.Text = "Suite discount included. Installation may ask for implant-specific choices.";
            }
            catch (Exception ex) { if (version == _version) _status.Text = ex.Message; }
        }
        private async Task OpenAsync()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open suites XML", FileTypeFilter = new[] { new FilePickerFileType("XML") { Patterns = new[] { "*.xml" } } } });
            if (files.Count == 0) return;
            try
            {
                var document = new XmlDocument { XmlResolver = null };
                using (var stream = await files[0].OpenReadAsync())
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
                if (document.SelectSingleNode("/chummer/suites/suite") == null) throw new ArgumentException("No suites found in this file.");
                _changing = true; _type.SelectedIndex = document.SelectSingleNode("/chummer/suites/suite/biowares") == null ? 0 : 1; _changing = false;
                SetDocument(document); await PreviewAsync();
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
        private async Task SaveAsync()
        {
            if (_document?.SelectSingleNode("/chummer/suites/suite") == null) return;
            var document = _document;
            string type = Source == Improvement.ImprovementSource.Cyberware ? "cyberware" : "bioware";
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save suites", SuggestedFileName = "custom_suites_" + type + ".xml", DefaultExtension = "xml" });
            if (file == null) return;
            try
            {
                var output = document;
                string path = file.TryGetLocalPath();
                if (path != null && File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    var existing = new XmlDocument { XmlResolver = null };
                    using (var input = await file.OpenReadAsync())
                    using (var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) existing.Load(reader);
                    output = CyberwareSuiteRules.Append(existing, output);
                }
                using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
                using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Async = true, Indent = true });
                output.Save(writer); await writer.FlushAsync(); _status.Text = "Suites saved. Place this file in an enabled custom data directory to list it automatically.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
        public async Task InstallAsync()
        {
            var suite = _suites.SelectedItem as XmlNode; var source = Source; if (suite == null) return;
            _buy.IsEnabled = false; _toolbar.IsEnabled = false; ++_version;
            try
            {
                RuleResult result = RuleResult.NotAllowed;
                await _owner.RunRuleAsync(async t => result = await CyberwareSuiteRules.PurchaseAsync(_owner.Character, suite, source, t));
                _status.Text = result == RuleResult.Changed ? "Suite installed." : result == RuleResult.Cancelled ? "Installation cancelled." : "Suite could not be installed.";
            }
            catch (OperationCanceledException) { _status.Text = "Installation cancelled."; }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _toolbar.IsEnabled = true; _buy.IsEnabled = true; }
        }
    }
}
