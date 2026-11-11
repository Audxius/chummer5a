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
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Media.Imaging;
using Chummer.Backend.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>One contact, enemy or pet as a dossier card: who they are, how useful (connection) and how loyal, plus editing.</summary>
    public sealed partial class ContactCardViewModel : ObservableObject
    {
        private readonly CharacterViewModel _objOwner;
        private readonly Contact _objContact;
        private readonly byte[] _portraitData;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ExpansionLabel))]
        [NotifyPropertyChangedFor(nameof(ExpansionTip))]
        private bool _isExpanded;
        public string ExpansionLabel => IsExpanded ? "⌃" : "⌄";
        public string ExpansionTip => IsExpanded ? "Collapse contact" : "Show full contact";
        public Bitmap Portrait { get; }
        public bool HasPortrait => Portrait != null;
        public string SummaryDescription => HasNotes ? Notes : Subtitle;

        public ContactCardViewModel(CharacterViewModel objOwner, Contact objContact)
        {
            _objOwner = objOwner;
            _objContact = objContact;
            _isExpanded = objOwner.ExpandedContactIds.ContainsKey(objContact.InternalId);
            var mugshot = objContact.MainMugshot;
            if (mugshot != null)
            {
                _portraitData = mugshot.EncodedBytes;
                try
                {
                    Portrait = mugshot.HostImage as Bitmap;
                    if (Portrait == null)
                    {
                        using var stream = new MemoryStream(mugshot.EncodedBytes);
                        Portrait = Bitmap.DecodeToWidth(stream, 256);
                        mugshot.HostImage = Portrait;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is IOException) { }
            }
            CultureInfo objCulture = GlobalSettings.CultureInfo;
            Name = string.IsNullOrWhiteSpace(objContact.Name) ? "(unnamed)" : objContact.Name;
            Role = objContact.DisplayRole ?? string.Empty;
            Location = objContact.Location ?? string.Empty;
            Connection = objContact.Connection;
            Loyalty = objContact.Loyalty;
            MaxConnection = Math.Max(1, objContact.CharacterObject?.MaxContactConnection ?? 12);
            IsEnemy = objContact.IsEnemy;
            IsPet = objContact.EntityType == ContactType.Pet;
            IsReadOnly = objContact.ReadOnly;
            LoyaltyEnabled = objContact.LoyaltyEnabled && !IsReadOnly;
            Notes = (objContact.Notes ?? string.Empty).RtfToPlainText().Trim();

            Initials = string.Concat(Name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Where(x => char.IsLetterOrDigit(x[0])).Take(2).Select(x => char.ToUpper(x[0], objCulture)));
            if (Initials.Length == 0)
                Initials = "?";

            List<ItemStat> lstFacts = new List<ItemStat>();
            void AddFact(string strLabel, string strValue)
            {
                if (!string.IsNullOrWhiteSpace(strValue) && strValue != "Unknown")
                    lstFacts.Add(new ItemStat(strLabel, strValue.Trim()));
            }
            AddFact("Type", objContact.DisplayType);
            AddFact("Metatype", objContact.DisplayMetatype);
            AddFact("Gender", objContact.DisplayGender);
            AddFact("Age", objContact.DisplayAge);
            AddFact("Payment", objContact.DisplayPreferredPayment);
            AddFact("Hobby / vice", objContact.DisplayHobbiesVice);
            AddFact("Personal life", objContact.DisplayPersonalLife);
            Facts = lstFacts;
            LinkedFile = !string.IsNullOrEmpty(objContact.FileName) ? objContact.FileName : objContact.RelativeFileName;
            var linked = objContact.LinkedCharacter;
            LinkedStats = linked == null ? Array.Empty<ItemStat>() : linked.AttributeSection.AttributeList
                .Select(x => new ItemStat(x.DisplayNameLong(GlobalSettings.Language), x.DisplayValue)).ToArray();
            LinkedSkills = linked == null ? Array.Empty<ItemStat>() : linked.SkillsSection.Skills.Where(x => x.Rating > 0)
                .Select(x => new ItemStat(x.CurrentDisplayName, x.Rating.ToString(GlobalSettings.CultureInfo))).ToArray();

            List<string> lstTags = new List<string>();
            if (IsEnemy)
                lstTags.Add("ENEMY");
            if (IsPet)
                lstTags.Add("PET / COHORT");
            if (objContact.IsGroup)
                lstTags.Add("GROUP");
            if (objContact.Family)
                lstTags.Add("FAMILY");
            if (objContact.Blackmail)
                lstTags.Add("BLACKMAIL");
            if (objContact.Free)
                lstTags.Add("FREE");
            Tags = lstTags;

            ConnectionPips = Enumerable.Range(1, Math.Max(6, MaxConnection)).Select(x => x <= Connection).ToList();
            LoyaltyPips = Enumerable.Range(1, 6).Select(x => x <= Loyalty).ToList();
        }

        public string Name { get; }
        public string LinkedFile { get; }
        public IReadOnlyList<ItemStat> LinkedStats { get; }
        public IReadOnlyList<ItemStat> LinkedSkills { get; }
        internal Task<ContactCardViewModel> RefreshProfileAsync() => _objOwner.ReadModelAsync(() => Task.FromResult(new ContactCardViewModel(_objOwner, _objContact)));
        internal Task<bool> ExistsAsync() => _objOwner.ReadModelAsync(() => Task.FromResult(_objOwner.Character.Contacts.Contains(_objContact)));
        internal Task<Bitmap> LoadFullPortraitAsync() => Task.Run(() =>
        {
            if (_portraitData == null) return null;
            using var stream = new MemoryStream(_portraitData);
            return new Bitmap(stream);
        });
        internal Task LinkProfileAsync(string file) => _objOwner.LinkContactAsync(_objContact, file);
        [RelayCommand]
        private void OpenProfile() => _objOwner.SelectedContactProfile = this;
        public string Id => _objContact.InternalId;
        public string Role { get; }
        public string Location { get; }
        public string Initials { get; }
        public int Connection { get; }
        public int Loyalty { get; }
        public int MaxConnection { get; }
        public bool IsEnemy { get; }
        public bool IsPet { get; }
        public bool IsReadOnly { get; }
        public bool IsEditable => !IsReadOnly;
        public bool LoyaltyEnabled { get; }
        public string Notes { get; }
        public bool HasNotes => Notes.Length > 0;
        public IReadOnlyList<ItemStat> Facts { get; }
        public IReadOnlyList<string> Tags { get; }
        public bool HasTags => Tags.Count > 0;
        public IReadOnlyList<bool> ConnectionPips { get; }
        public IReadOnlyList<bool> LoyaltyPips { get; }

        public string Subtitle => string.Join("  //  ", new[] { Role, Location }.Where(x => !string.IsNullOrWhiteSpace(x)));
        public bool HasSubtitle => Subtitle.Length > 0;
        public string ConnectionText => Connection.ToString(GlobalSettings.CultureInfo);
        public string LoyaltyText => Loyalty.ToString(GlobalSettings.CultureInfo);
        public bool CanIncreaseConnection => IsEditable && Connection < MaxConnection;
        public bool CanDecreaseConnection => IsEditable && Connection > 1;
        public bool CanIncreaseLoyalty => LoyaltyEnabled && Loyalty < 6;
        public bool CanDecreaseLoyalty => LoyaltyEnabled && Loyalty > 1;
        partial void OnIsExpandedChanged(bool value)
        {
            if (value) _objOwner.ExpandedContactIds.TryAdd(_objContact.InternalId, true);
            else _objOwner.ExpandedContactIds.TryRemove(_objContact.InternalId, out _);
        }

        /// <summary>Text matched by the contacts search box.</summary>
        public string SearchText => string.Join(" ", new[] { Name, Role, Location, Notes }.Concat(Facts.Select(x => x.Value)).Concat(Tags));

        [RelayCommand]
        private Task Edit(object owner) => _objOwner.EditContactAsync(_objContact, owner);

        [RelayCommand]
        private Task Delete() => _objOwner.DeleteContactAsync(_objContact);

        [RelayCommand(CanExecute = nameof(CanIncreaseConnection))]
        private Task ConnectionUp() => ChangeAsync(Connection + 1, true);

        [RelayCommand(CanExecute = nameof(CanDecreaseConnection))]
        private Task ConnectionDown() => ChangeAsync(Connection - 1, true);

        [RelayCommand(CanExecute = nameof(CanIncreaseLoyalty))]
        private Task LoyaltyUp() => ChangeAsync(Loyalty + 1, false);

        [RelayCommand(CanExecute = nameof(CanDecreaseLoyalty))]
        private Task LoyaltyDown() => ChangeAsync(Loyalty - 1, false);

        // Same bounds as the legacy ContactControl: connection 1..MaxContactConnection, loyalty 1..6 when enabled.
        private async Task ChangeAsync(int intNew, bool blnConnection)
        {
            await _objOwner.RunRuleAsync(t => Chummer.Rules.ContactRules.SetRatingAsync(_objOwner.Character, _objContact, intNew, blnConnection, t));
        }
    }
}
