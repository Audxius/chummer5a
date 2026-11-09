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

        public ContactCardViewModel(CharacterViewModel objOwner, Contact objContact)
        {
            _objOwner = objOwner;
            _objContact = objContact;
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
            Notes = (objContact.Notes ?? string.Empty).Trim();

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

            List<string> lstTags = new List<string>();
            if (IsEnemy)
                lstTags.Add("ENEMY");
            if (IsPet)
                lstTags.Add("PET");
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

        /// <summary>Text matched by the contacts search box.</summary>
        public string SearchText => string.Join(" ", new[] { Name, Role, Location, Notes }.Concat(Facts.Select(x => x.Value)).Concat(Tags));

        [RelayCommand]
        private Task ConnectionUp() => ChangeAsync(Connection + 1, true);

        [RelayCommand]
        private Task ConnectionDown() => ChangeAsync(Connection - 1, true);

        [RelayCommand]
        private Task LoyaltyUp() => ChangeAsync(Loyalty + 1, false);

        [RelayCommand]
        private Task LoyaltyDown() => ChangeAsync(Loyalty - 1, false);

        // Same bounds as the legacy ContactControl: connection 1..MaxContactConnection, loyalty 1..6 when enabled.
        private async Task ChangeAsync(int intNew, bool blnConnection)
        {
            if (IsReadOnly || intNew < 1)
                return;
            if (blnConnection)
            {
                if (intNew > MaxConnection)
                    return;
                await Task.Run(() => _objContact.SetConnectionAsync(intNew)).ConfigureAwait(true);
            }
            else
            {
                if (!LoyaltyEnabled || intNew > 6)
                    return;
                await Task.Run(() => _objContact.SetLoyaltyAsync(intNew)).ConfigureAwait(true);
            }
            _objOwner.MarkDirty();
            _objOwner.Reload();
        }
    }
}
