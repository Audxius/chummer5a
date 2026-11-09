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
using System.Text;
using System.Threading.Tasks;
using System.Xml.XPath;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Chummer.Desktop.Localization;
using Chummer.Selection;

namespace Chummer.Desktop.Views.Dialogs
{
    /// <summary>What the spell picker returns (SelectSpell's SelectedSpell / Limited / Extended / Alchemical / FreeBonus / AddAgain).</summary>
    public sealed class SpellPick
    {
        public string Id { get; set; }
        public bool Limited { get; set; }
        public bool Extended { get; set; }
        public bool Alchemical { get; set; }
        public bool Free { get; set; }
        public bool AddAgain { get; set; }
    }

    /// <summary>
    /// Native SelectSpell: category and search filters, the spell's stats, and the Limited / Extended / Alchemical / Free options
    /// with the karma it costs in career mode.
    /// </summary>
    public sealed class SpellPickerWindow : HudWindow
    {
        private SpellPick _objResult;

        public SpellPickerWindow(Character objCharacter, bool blnFreeOnly) : base("Learn spell", 900)
        {
            DataBrowser objBrowser = DataBrowser.Spell(objCharacter, string.Empty, false);
            XPathNavigator xmlRoot = objCharacter.LoadDataXPath("spells.xml");
            bool blnCareer = objCharacter.Created;
            List<ListItem> lstCategories = new List<ListItem> { new ListItem(string.Empty, LanguageManager.GetString("String_ShowAll")) };
            foreach (XPathNavigator xmlCategory in xmlRoot.Select("/chummer/categories/category"))
                lstCategories.Add(new ListItem(xmlCategory.Value, xmlCategory.SelectSingleNode("@translate")?.Value ?? xmlCategory.Value));
            ComboBox cboCategory = new ComboBox { ItemsSource = lstCategories, SelectedIndex = 0, MinWidth = 170 };
            cboCategory.ItemTemplate = new FuncDataTemplate<ListItem>((x, _) => new TextBlock { Text = x.Name });
            TextBox txtSearch = new TextBox { PlaceholderText = Tr.Get("String_Search"), Margin = new Thickness(8, 0, 0, 0) };
            CheckBox chkAvailable = new CheckBox { Content = "Only what this runner can learn", IsChecked = true };
            ListBox lstChoices = new ListBox { Height = 430 };
            lstChoices.ItemTemplate = new FuncDataTemplate<ListItem>((x, _) => new TextBlock { Text = x.Name });

            TextBlock txtName = new TextBlock { FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
            TextBlock txtStats = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
            CheckBox chkLimited = new CheckBox { Content = "Limited (−2 drain, needs a fetish)" };
            CheckBox chkExtended = new CheckBox { Content = "Extended area" };
            CheckBox chkAlchemical = new CheckBox { Content = "Alchemical preparation" };
            CheckBox chkFree = new CheckBox { Content = "Free spell (no karma)" };
            TextBlock txtCost = new TextBlock { FontSize = 26, FontWeight = FontWeight.Black };
            TextBlock txtCostLabel = new TextBlock { Classes = { "label" }, Text = blnCareer ? "KARMA COST" : "COST" };
            StackPanel pnlDetails = new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(18, 0, 0, 0),
                Children = { txtName, txtStats, chkLimited, chkExtended, chkAlchemical, chkFree, new StackPanel { Children = { txtCostLabel, txtCost } } }
            };

            Button cmdOk = new Button { Content = "LEARN", IsDefault = true, Classes = { "primary" }, MinWidth = 90 };
            Button cmdAgain = new Button { Content = "LEARN & PICK ANOTHER" };
            Button cmdCancel = new Button { Content = Tr.Get("String_Cancel"), IsCancel = true };

            bool blnUpdating = false;
            void Apply(CheckBox chkBox, SpellChoice objChoice)
            {
                chkBox.IsVisible = objChoice.Visible;
                chkBox.IsEnabled = objChoice.Enabled;
                if (objChoice.Forced)
                    chkBox.IsChecked = true;
                else if (!objChoice.Visible || !objChoice.Enabled)
                    chkBox.IsChecked = false;
            }

            void UpdateCost()
            {
                if (!(lstChoices.SelectedItem is ListItem objItem))
                    return;
                if (!blnCareer)
                {
                    txtCost.Text = chkFree.IsChecked == true ? "free" : "1 spell";
                    return;
                }
                if (chkFree.IsChecked == true)
                {
                    txtCost.Text = "0";
                    return;
                }
                bool blnRitual = objBrowser.Node(objItem.Value?.ToString())?.SelectSingleNode("category")?.Value == "Rituals";
                txtCost.Text = objCharacter.SpellKarmaCost(chkAlchemical.IsChecked == true ? "Preparations" : blnRitual ? "Rituals" : "Spells").ToString(GlobalSettings.CultureInfo);
            }

            void UpdateDetails()
            {
                if (!(lstChoices.SelectedItem is ListItem objItem))
                {
                    pnlDetails.IsVisible = false;
                    cmdOk.IsEnabled = cmdAgain.IsEnabled = false;
                    return;
                }
                pnlDetails.IsVisible = true;
                cmdOk.IsEnabled = cmdAgain.IsEnabled = true;
                string strId = objItem.Value?.ToString();
                XPathNavigator xmlSpell = objBrowser.Node(strId);
                txtName.Text = objItem.Name;
                StringBuilder sbdStats = new StringBuilder();
                foreach ((string strLabel, string strElement) in new[] { ("Category", "category"), ("Type", "type"), ("Range", "range"), ("Damage", "damage"), ("Duration", "duration"), ("Drain", "dv"), ("Descriptors", "descriptor") })
                {
                    string strValue = xmlSpell?.SelectSingleNode(strElement)?.Value;
                    if (!string.IsNullOrEmpty(strValue) && strValue != "0")
                        sbdStats.Append(strLabel).Append(":  ").AppendLine(strValue);
                }
                string strSource = xmlSpell?.SelectSingleNode("source")?.Value;
                if (!string.IsNullOrEmpty(strSource))
                    sbdStats.Append("Source:  ").Append(strSource).Append(' ').AppendLine(xmlSpell.SelectSingleNode("altpage")?.Value ?? xmlSpell.SelectSingleNode("page")?.Value);
                txtStats.Text = sbdStats.ToString().TrimEnd();
                SpellChoices objChoices = MagicSelectors.SpellOptions(objCharacter, strId, blnFreeOnly);
                blnUpdating = true;
                Apply(chkLimited, objChoices.Limited);
                Apply(chkExtended, objChoices.Extended);
                Apply(chkAlchemical, objChoices.Alchemical);
                Apply(chkFree, objChoices.Free);
                blnUpdating = false;
                UpdateCost();
            }

            void Rebuild(bool blnOnlyAvailable)
            {
                string strCategory = (cboCategory.SelectedItem as ListItem?)?.Value?.ToString() ?? string.Empty;
                string strFilter = txtSearch.Text ?? string.Empty;
                string strOld = (lstChoices.SelectedItem as ListItem?)?.Value?.ToString();
                List<ListItem> lstItems = objBrowser.Options(blnOnlyAvailable)
                    .Where(x => strCategory.Length == 0 || objBrowser.Node(x.Value?.ToString())?.SelectSingleNode("category")?.Value == strCategory)
                    .Where(x => strFilter.Length == 0 || (x.Name ?? string.Empty).IndexOf(strFilter, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    .ToList();
                lstChoices.ItemsSource = lstItems;
                ListItem objOld = lstItems.FirstOrDefault(x => x.Value?.ToString() == strOld);
                if (objOld.Value != null)
                    lstChoices.SelectedItem = objOld;
                UpdateDetails();
            }

            cboCategory.SelectionChanged += (sender, args) => Rebuild(chkAvailable.IsChecked == true);
            txtSearch.TextChanged += (sender, args) => Rebuild(chkAvailable.IsChecked == true);
            chkAvailable.IsCheckedChanged += (sender, args) => Rebuild(chkAvailable.IsChecked == true);
            lstChoices.SelectionChanged += (sender, args) => UpdateDetails();
            chkAlchemical.IsCheckedChanged += (sender, args) =>
            {
                if (!blnUpdating)
                    UpdateCost();
            };
            chkFree.IsCheckedChanged += (sender, args) =>
            {
                if (!blnUpdating)
                    UpdateCost();
            };

            void Accept(bool blnAgain)
            {
                if (!(lstChoices.SelectedItem is ListItem objItem) || !objBrowser.Validate(objItem.Value?.ToString()))
                    return;
                _objResult = new SpellPick
                {
                    Id = objItem.Value?.ToString(),
                    Limited = chkLimited.IsChecked == true,
                    Extended = chkExtended.IsChecked == true,
                    Alchemical = chkAlchemical.IsChecked == true,
                    Free = chkFree.IsChecked == true,
                    AddAgain = blnAgain,
                };
                Close();
            }
            cmdOk.Click += (sender, args) => Accept(false);
            cmdAgain.Click += (sender, args) => Accept(true);
            lstChoices.DoubleTapped += (sender, args) => Accept(false);
            cmdCancel.Click += (sender, args) => Close();

            Grid grdFilters = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            grdFilters.Children.Add(cboCategory);
            Grid.SetColumn(txtSearch, 1);
            grdFilters.Children.Add(txtSearch);
            Grid grdBody = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            grdBody.Children.Add(lstChoices);
            ScrollViewer scrDetails = new ScrollViewer { Content = pnlDetails, MaxHeight = 440 };
            Grid.SetColumn(scrDetails, 1);
            grdBody.Children.Add(scrDetails);
            Content = Frame(new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Heading(blnFreeOnly ? "Learn spell  //  free spells only" : "Learn spell"), grdFilters, chkAvailable, grdBody, ButtonRow(cmdOk, cmdAgain, cmdCancel)
                }
            });
            Opened += (sender, args) => txtSearch.Focus();
            Rebuild(true);
        }

        public static async Task<SpellPick> ShowAsync(object owner, Character objCharacter, bool blnFreeOnly)
        {
            SpellPickerWindow objWindow = new SpellPickerWindow(objCharacter, blnFreeOnly);
            await DialogHost.ShowModalAsync(objWindow, owner).ConfigureAwait(true);
            return objWindow._objResult;
        }
    }
}
