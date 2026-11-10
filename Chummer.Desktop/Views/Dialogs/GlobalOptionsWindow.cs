using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Chummer.Desktop.ViewModels;

namespace Chummer.Desktop.Views.Dialogs
{
    /// <summary>Native global preferences; changes are staged until Save.</summary>
    public sealed class GlobalOptionsWindow : Window, ISettingOptionHost
    {
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>();
        public bool IsEditable => true;
        public object GetValue(string property) => _values[property];
        public void SetValue(string property, object value) => _values[property] = value;

        public GlobalOptionsWindow()
        {
            Title = "Global options"; Width = 900; Height = 680; MinWidth = 700; MinHeight = 480;
            var root = new DockPanel { Margin = new Thickness(24) };
            var footer = new StackPanel { Spacing = 10, Margin = new Thickness(0, 16, 0, 0) };
            var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var save = new Button { Content = "SAVE OPTIONS", Classes = { "primary" } };
            var cancel = new Button { Content = "CANCEL" };
            cancel.Click += (_, _) => Close();
            save.Click += async (_, _) =>
            {
                save.IsEnabled = false;
                try
                {
                    await Task.Run(async () =>
                    {
                        foreach (var entry in _values)
                            SettingReflection.Find(typeof(GlobalSettings), entry.Key).SetValue(null, entry.Value);
                        await GlobalSettings.SaveOptionsToRegistry();
                    });
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
            root.Children.Add(new ScrollViewer { Content = options }); Content = root;
        }
    }
}
