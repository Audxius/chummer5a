/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.XPath;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Chummer.Backend.Enums;
using Chummer.Desktop.Services;

namespace Chummer.Desktop.Views.Dialogs
{
    /// <summary>Snapshot of every editable contact field (legacy ContactControl, expanded view).</summary>
    public sealed class ContactFields
    {
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public int Connection { get; set; } = 1;
        public int Loyalty { get; set; } = 1;
        public string Metatype { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Age { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string PreferredPayment { get; set; } = string.Empty;
        public string HobbiesVice { get; set; } = string.Empty;
        public string PersonalLife { get; set; } = string.Empty;
        public bool IsGroup { get; set; }
        public bool Family { get; set; }
        public bool Blackmail { get; set; }
        public bool Free { get; set; }
        public string Notes { get; set; } = string.Empty;

        public static ContactFields From(Contact objContact) => new ContactFields
        {
            Name = objContact.Name ?? string.Empty,
            Role = objContact.Role ?? string.Empty,
            Location = objContact.Location ?? string.Empty,
            Connection = objContact.Connection,
            Loyalty = objContact.Loyalty,
            Metatype = objContact.Metatype ?? string.Empty,
            Gender = objContact.Gender ?? string.Empty,
            Age = objContact.Age ?? string.Empty,
            Type = objContact.Type ?? string.Empty,
            PreferredPayment = objContact.PreferredPayment ?? string.Empty,
            HobbiesVice = objContact.HobbiesVice ?? string.Empty,
            PersonalLife = objContact.PersonalLife ?? string.Empty,
            IsGroup = objContact.IsGroup,
            Family = objContact.Family,
            Blackmail = objContact.Blackmail,
            Free = objContact.Free,
            Notes = objContact.Notes ?? string.Empty,
        };

        /// <summary>Writes the fields back (engine setters enforce their own rules, as the legacy bindings did).</summary>
        public void ApplyTo(Contact objContact)
        {
            objContact.Name = Name;
            objContact.Role = Role;
            objContact.Location = Location;
            objContact.Metatype = Metatype;
            objContact.Gender = Gender;
            objContact.Age = Age;
            objContact.Type = Type;
            objContact.PreferredPayment = PreferredPayment;
            objContact.HobbiesVice = HobbiesVice;
            objContact.PersonalLife = PersonalLife;
            if (objContact.GroupEnabled)
                objContact.IsGroup = IsGroup;
            objContact.Family = Family;
            objContact.Blackmail = Blackmail;
            if (objContact.FreeEnabled)
                objContact.Free = Free;
            objContact.Notes = Notes;
            if (!objContact.ReadOnly)
            {
                int intMaxConnection = Math.Max(1, objContact.CharacterObject?.MaxContactConnection ?? 12);
                objContact.Connection = Math.Max(1, Math.Min(intMaxConnection, Connection));
                if (objContact.LoyaltyEnabled)
                    objContact.Loyalty = Math.Max(1, Math.Min(6, Loyalty));
            }
        }
    }

    /// <summary>Editor for one contact / enemy / pet, with the suggestion lists legacy Chummer offers (data/contacts.xml).</summary>
    public sealed class ContactEditorWindow : HudWindow
    {
        private bool _blnSaved;

        private ContactEditorWindow(string strTitle) : base(strTitle, 640)
        {
            MaxHeight = 900;
        }

        public static async Task<bool> EditAsync(object owner, Character objCharacter, ContactFields objFields, string strTitle, bool blnGroupEnabled, bool blnFreeEnabled, bool blnLoyaltyEnabled, int intMaxConnection, bool includeCritters = false)
        {
            Dictionary<string, List<string>> dicLists = await Task.Run(() => LoadLists(objCharacter, includeCritters)).ConfigureAwait(true);
            ContactEditorWindow objWindow = new ContactEditorWindow(strTitle);

            Grid grdFields = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("120,*,24,120,*"),
                RowSpacing = 8,
            };
            int intRow = 0;
            void AddRow(string strLabel, Control objControl, int intColumn = 0, bool blnNewRow = true)
            {
                if (blnNewRow && intColumn == 0)
                    grdFields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                TextBlock lblLabel = new TextBlock { Text = strLabel.ToUpperInvariant(), Classes = { "label" }, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(lblLabel, intRow);
                Grid.SetColumn(lblLabel, intColumn);
                Grid.SetRow(objControl, intRow);
                Grid.SetColumn(objControl, intColumn + 1);
                grdFields.Children.Add(lblLabel);
                grdFields.Children.Add(objControl);
                if (intColumn != 0)
                    ++intRow;
            }
            AutoCompleteBox Suggest(string strValue, string strList) => new AutoCompleteBox
            {
                Text = strValue,
                ItemsSource = dicLists.TryGetValue(strList, out List<string> lstValues) ? lstValues : new List<string>(),
                FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0,
                IsTextCompletionEnabled = false,
            };

            TextBox txtName = new TextBox { Text = objFields.Name, PlaceholderText = "Name" };
            AutoCompleteBox txtRole = Suggest(objFields.Role, "role");
            TextBox txtLocation = new TextBox { Text = objFields.Location };
            NumericUpDown nudConnection = new NumericUpDown { Minimum = 1, Maximum = Math.Max(1, intMaxConnection), Value = objFields.Connection, FormatString = "0" };
            NumericUpDown nudLoyalty = new NumericUpDown { Minimum = 1, Maximum = 6, Value = objFields.Loyalty, FormatString = "0", IsEnabled = blnLoyaltyEnabled };
            AutoCompleteBox txtMetatype = Suggest(objFields.Metatype, "metatype");
            AutoCompleteBox txtGender = Suggest(objFields.Gender, "gender");
            AutoCompleteBox txtAge = Suggest(objFields.Age, "age");
            AutoCompleteBox txtType = Suggest(objFields.Type, "type");
            AutoCompleteBox txtPayment = Suggest(objFields.PreferredPayment, "payment");
            AutoCompleteBox txtVice = Suggest(objFields.HobbiesVice, "vice");
            AutoCompleteBox txtPersonal = Suggest(objFields.PersonalLife, "personal");
            CheckBox chkGroup = new CheckBox { Content = "Group contact", IsChecked = objFields.IsGroup, IsEnabled = blnGroupEnabled };
            CheckBox chkFamily = new CheckBox { Content = "Family", IsChecked = objFields.Family };
            CheckBox chkBlackmail = new CheckBox { Content = "Blackmail", IsChecked = objFields.Blackmail };
            CheckBox chkFree = new CheckBox { Content = "Free (no karma cost)", IsChecked = objFields.Free, IsEnabled = blnFreeEnabled };
            TextBox txtNotes = new TextBox { Text = objFields.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110 };

            grdFields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumnSpan(txtName, 4);
            AddRow("Name", txtName);
            ++intRow;
            AddRow("Role", txtRole);
            AddRow("Location", txtLocation, 3);
            AddRow("Connection", nudConnection);
            AddRow("Loyalty", nudLoyalty, 3);
            AddRow("Type", txtType);
            AddRow("Metatype", txtMetatype, 3);
            AddRow("Gender", txtGender);
            AddRow("Age", txtAge, 3);
            AddRow("Payment", txtPayment);
            AddRow("Hobby / vice", txtVice, 3);
            AddRow("Personal life", txtPersonal);
            ++intRow;

            WrapPanel pnlFlags = new WrapPanel { Margin = new Thickness(0, 6, 0, 0), Children = { chkGroup, chkFamily, chkBlackmail, chkFree } };
            foreach (Control objFlag in pnlFlags.Children)
                objFlag.Margin = new Thickness(0, 0, 18, 0);

            Button cmdSave = new Button { Content = "SAVE", IsDefault = true, Classes = { "primary" } };
            Button cmdCancel = new Button { Content = "CANCEL", IsCancel = true };
            cmdSave.Click += (sender, args) =>
            {
                objFields.Name = txtName.Text ?? string.Empty;
                objFields.Role = txtRole.Text ?? string.Empty;
                objFields.Location = txtLocation.Text ?? string.Empty;
                objFields.Connection = (int)(nudConnection.Value ?? 1);
                objFields.Loyalty = (int)(nudLoyalty.Value ?? 1);
                objFields.Metatype = txtMetatype.Text ?? string.Empty;
                objFields.Gender = txtGender.Text ?? string.Empty;
                objFields.Age = txtAge.Text ?? string.Empty;
                objFields.Type = txtType.Text ?? string.Empty;
                objFields.PreferredPayment = txtPayment.Text ?? string.Empty;
                objFields.HobbiesVice = txtVice.Text ?? string.Empty;
                objFields.PersonalLife = txtPersonal.Text ?? string.Empty;
                objFields.IsGroup = chkGroup.IsChecked == true;
                objFields.Family = chkFamily.IsChecked == true;
                objFields.Blackmail = chkBlackmail.IsChecked == true;
                objFields.Free = chkFree.IsChecked == true;
                objFields.Notes = txtNotes.Text ?? string.Empty;
                objWindow._blnSaved = true;
                objWindow.Close();
            };
            cmdCancel.Click += (sender, args) => objWindow.Close();

            objWindow.Content = Frame(new ScrollViewer
            {
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        Heading(strTitle),
                        grdFields,
                        pnlFlags,
                        new TextBlock { Text = "NOTES", Classes = { "label" }, Margin = new Thickness(0, 8, 0, 0) },
                        txtNotes,
                        ButtonRow(cmdSave, cmdCancel),
                    }
                }
            });
            objWindow.Opened += (sender, args) => txtName.Focus();
            Window objOwner = ModalHelper.ResolveOwner(owner);
            if (objOwner != null && objOwner.IsVisible)
                await objWindow.ShowDialog(objOwner).ConfigureAwait(true);
            else
            {
                TaskCompletionSource<bool> objClosed = new TaskCompletionSource<bool>();
                objWindow.Closed += (sender, args) => objClosed.TrySetResult(true);
                objWindow.Show();
                await objClosed.Task.ConfigureAwait(true);
            }
            return objWindow._blnSaved;
        }

        // Same sources as legacy ContactControl.LoadContactList: data/contacts.xml lists and metatypes.xml (with metavariants).
        private static Dictionary<string, List<string>> LoadLists(Character objCharacter, bool includeCritters)
        {
            Dictionary<string, List<string>> dicReturn = new Dictionary<string, List<string>>();
            XPathNavigator xmlContacts = objCharacter.LoadDataXPath("contacts.xml").SelectSingleNode("/chummer");
            void Fill(string strKey, string strXPath)
            {
                List<string> lstValues = new List<string>();
                if (xmlContacts != null)
                {
                    foreach (XPathNavigator xmlNode in xmlContacts.Select(strXPath))
                    {
                        string strValue = xmlNode.Value;
                        if (!string.IsNullOrWhiteSpace(strValue) && !lstValues.Contains(strValue))
                            lstValues.Add(strValue);
                    }
                }
                lstValues.Sort(StringComparer.CurrentCulture);
                dicReturn[strKey] = lstValues;
            }
            Fill("role", "contacts/contact");
            Fill("gender", "genders/gender");
            Fill("age", "ages/age");
            Fill("type", "types/type");
            Fill("payment", "preferredpayments/preferredpayment");
            Fill("vice", "hobbiesvices/hobbyvice");
            Fill("personal", "personallives/personallife");
            List<string> lstMetatypes = new List<string>();
            foreach (string file in includeCritters ? new[] { "metatypes.xml", "critters.xml" } : new[] { "metatypes.xml" })
            foreach (XPathNavigator xmlMetatype in objCharacter.LoadDataXPath(file).Select("/chummer/metatypes/metatype"))
            {
                string strName = xmlMetatype.SelectSingleNode("name")?.Value;
                if (string.IsNullOrEmpty(strName))
                    continue;
                if (!lstMetatypes.Contains(strName)) lstMetatypes.Add(strName);
                foreach (XPathNavigator xmlVariant in xmlMetatype.Select("metavariants/metavariant/name"))
                {
                    if (!lstMetatypes.Contains(xmlVariant.Value))
                        lstMetatypes.Add(xmlVariant.Value); // legacy stores the metavariant name itself
                }
            }
            dicReturn["metatype"] = lstMetatypes;
            lstMetatypes.Sort(StringComparer.CurrentCulture);
            return dicReturn;
        }
    }
}
