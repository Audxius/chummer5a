using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Chummer.Rules;

namespace Chummer.Desktop.Views.Dialogs
{
    public sealed class PacksExportWindow : Window
    {
        private readonly Character _character;
        private readonly TextBox _name = new TextBox { PlaceholderText = "Kit name" };
        private readonly TextBox _preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, FontFamily = new Avalonia.Media.FontFamily("monospace") };
        private readonly TextBlock _status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        private readonly Dictionary<string, CheckBox> _sections = new();
        private readonly Button _save = new Button { Content = "SAVE KIT…", Classes = { "primary" } };
        private readonly Button _generate = new Button { Content = "PREVIEW" };
        public string Output => _preview.Text ?? "";

        public PacksExportWindow(Character character)
        {
            _character = character; Title = "Create PACKS kit"; Width = 960; Height = 740;
            var root = new DockPanel { Margin = new Thickness(24) };
            var footer = new StackPanel { Spacing = 10 }; footer.Children.Add(_status);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            actions.Children.Add(_generate); actions.Children.Add(_save);
            var close = new Button { Content = "CLOSE" }; close.Click += (_, _) => Close(); actions.Children.Add(close);
            footer.Children.Add(actions); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var choices = new StackPanel { Width = 230, Spacing = 10, Margin = new Thickness(0, 0, 20, 16) };
            choices.Children.Add(new TextBlock { Text = "KIT NAME", Classes = { "label" } }); choices.Children.Add(_name);
            foreach (var section in new[] { ("Attributes", "Attributes"), ("Qualities", "Qualities"), ("StartingNuyen", "Starting nuyen"),
                ("MartialArts", "Martial arts"), ("Spells", "Spells"), ("ComplexForms", "Complex forms"), ("Cyberware", "Cyberware and bioware"),
                ("Lifestyle", "Lifestyles"), ("Armor", "Armor"), ("Weapons", "Weapons"), ("Gear", "Gear and plugins"), ("Vehicles", "Vehicles") })
            {
                var check = new CheckBox { Content = section.Item2, IsChecked = true };
                _sections.Add(section.Item1, check); choices.Children.Add(check);
            }
            DockPanel.SetDock(choices, Dock.Left); root.Children.Add(choices); root.Children.Add(_preview); Content = root;
            _generate.Click += async (_, _) => await GenerateAsync();
            _save.Click += async (_, _) => await SaveAsync();
        }

        public async Task GenerateAsync(string name = null)
        {
            if (name != null) _name.Text = name;
            var options = new PacksExportOptions();
            foreach (var entry in _sections) typeof(PacksExportOptions).GetProperty(entry.Key)?.SetValue(options, entry.Value.IsChecked == true);
            string kitName = _name.Text;
            _generate.IsEnabled = _save.IsEnabled = false;
            try
            {
                var document = await Task.Run(() => PacksExporter.ExportAsync(_character, kitName, options));
                using var buffer = new StringWriter();
                using (var writer = System.Xml.XmlWriter.Create(buffer, new System.Xml.XmlWriterSettings { Indent = true, OmitXmlDeclaration = true })) document.Save(writer);
                _preview.Text = buffer.ToString(); _status.Text = "Kit ready. Save in a custom data directory to make it available in the PACKS picker.";
            }
            catch (Exception ex) { _preview.Text = ""; _status.Text = ex.Message; }
            finally { _generate.IsEnabled = _save.IsEnabled = true; }
        }

        private async Task SaveAsync()
        {
            await GenerateAsync(); if (string.IsNullOrEmpty(Output)) return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save PACKS kit", DefaultExtension = "xml",
                SuggestedFileName = "custom_runner_packs.xml", FileTypeChoices = new[] { new FilePickerFileType("PACKS XML") { Patterns = new[] { "*_packs.xml" } } } });
            if (file == null) return;
            try
            {
                string output = Output;
                string path = file.TryGetLocalPath();
                if (path != null && File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    var existing = new System.Xml.XmlDocument { XmlResolver = null };
                    using (var input = await file.OpenReadAsync())
                    using (var reader = System.Xml.XmlReader.Create(input, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null })) existing.Load(reader);
                    var generated = new System.Xml.XmlDocument { XmlResolver = null }; generated.LoadXml(output);
                    output = PacksExporter.Append(existing, generated).OuterXml;
                }
                using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
                using var writer = new StreamWriter(stream); await writer.WriteAsync(output); _status.Text = "PACKS kit saved.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
    }
}
