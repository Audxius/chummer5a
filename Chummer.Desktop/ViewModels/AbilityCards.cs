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

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chummer.Backend.Enums;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>
    /// Builds card groups for everything a runner can *do* (magic, resonance, adept powers, martial arts, critter powers,
    /// AI programs, initiation) and who they know (contacts, spirits/sprites).
    /// </summary>
    internal static class AbilityCards
    {
        private static string L => GlobalSettings.Language;
        private static CultureInfo C => GlobalSettings.CultureInfo;

        private static string Src(string strSource, string strPage) => string.IsNullOrEmpty(strSource) ? string.Empty : (strSource + " " + strPage).Trim();

        public static List<InventoryGroupViewModel> Abilities(Character objCharacter)
        {
            List<InventoryGroupViewModel> lstReturn = new List<InventoryGroupViewModel>();

            List<InventoryItemViewModel> lstSpells = objCharacter.Spells.ToList().Select(x => InventoryItemViewModel.Card(
                x.CurrentDisplayName, x.DisplayCategory(L), x.Alchemical ? "Icon.Drug" : "Icon.Spell",
                x.DisplayType(L) + "   " + x.DisplayRange(L) + "   DV " + x.DisplayDv(L), x.Extended ? "EXT" : string.Empty,
                new[]
                {
                    new ItemStat("Type", x.DisplayType(L)), new ItemStat("Range", x.DisplayRange(L)), new ItemStat("Damage", x.DisplayDamage(L, C)),
                    new ItemStat("Duration", x.DisplayDuration(L)), new ItemStat("Drain", x.DisplayDv(L)), new ItemStat("Descriptors", x.DisplayDescriptors(L)),
                }, x.Notes, Src(x.Source, x.Page))).OrderBy(x => x.Category, System.StringComparer.CurrentCulture).ThenBy(x => x.Name, System.StringComparer.CurrentCulture).ToList();
            if (lstSpells.Count > 0)
                lstReturn.Add(new InventoryGroupViewModel("SPELLS", lstSpells));

            List<InventoryItemViewModel> lstPowers = objCharacter.Powers.ToList().Select(x => InventoryItemViewModel.Card(
                x.CurrentDisplayName, "Adept power", "Icon.AdeptPower", x.DisplayPoints + " PP", x.TotalRating > 0 ? "R" + x.TotalRating.ToString(C) : string.Empty,
                new[] { new ItemStat("Rating", x.TotalRating > 0 ? x.TotalRating.ToString(C) : string.Empty), new ItemStat("Power points", x.DisplayPoints) },
                x.Notes, Src(x.Source, x.Page))).ToList();
            if (lstPowers.Count > 0)
                lstReturn.Add(new InventoryGroupViewModel("ADEPT POWERS", lstPowers));

            List<InventoryItemViewModel> lstForms = objCharacter.ComplexForms.ToList().Select(x => InventoryItemViewModel.Card(
                x.CurrentDisplayName, "Complex form", "Icon.Sprite", "Target " + x.DisplayTarget(L) + "   FV " + x.DisplayFv(L), string.Empty,
                new[] { new ItemStat("Target", x.DisplayTarget(L)), new ItemStat("Duration", x.DisplayDuration(L)), new ItemStat("Fade", x.DisplayFv(L)) },
                x.Notes, Src(x.Source, x.Page))).ToList();
            if (lstForms.Count > 0)
                lstReturn.Add(new InventoryGroupViewModel("COMPLEX FORMS", lstForms));

            List<InventoryItemViewModel> lstPrograms = objCharacter.AIPrograms.ToList().Select(x => InventoryItemViewModel.Card(
                x.CurrentDisplayName, "AI program", "Icon.Cyberdeck", string.Empty, string.Empty, null)).ToList();
            if (lstPrograms.Count > 0)
                lstReturn.Add(new InventoryGroupViewModel("AI PROGRAMS", lstPrograms));

            List<InventoryItemViewModel> lstArts = objCharacter.MartialArts.ToList().Select(x => InventoryItemViewModel.Card(
                x.CurrentDisplayName, "Martial art", "Icon.MartialArt", x.Techniques.Count.ToString(C) + " techniques", string.Empty, null,
                string.Empty, string.Empty, x.Techniques.ToList().Select(y => "◆ " + y.CurrentDisplayName))).ToList();
            if (lstArts.Count > 0)
                lstReturn.Add(new InventoryGroupViewModel("MARTIAL ARTS", lstArts));

            List<InventoryItemViewModel> lstCritter = objCharacter.CritterPowers.ToList().Select(x => InventoryItemViewModel.Card(
                x.CurrentDisplayName, x.DisplayCategory(L), "Icon.Spirit", x.DisplayType(L) + "   " + x.DisplayAction(L), x.Rating > 0 ? "R" + x.Rating.ToString(C) : string.Empty,
                new[] { new ItemStat("Type", x.DisplayType(L)), new ItemStat("Action", x.DisplayAction(L)), new ItemStat("Range", x.DisplayRange(L)), new ItemStat("Duration", x.DisplayDuration(L)) })).ToList();
            if (lstCritter.Count > 0)
                lstReturn.Add(new InventoryGroupViewModel("CRITTER POWERS", lstCritter));

            List<InventoryItemViewModel> lstInitiation = objCharacter.InitiationGrades.ToList().OrderBy(x => x.Grade).Select(x => InventoryItemViewModel.Card(
                (x.Technomancer ? "Submersion " : "Initiation ") + x.Grade.ToString(C), x.Technomancer ? "Submersion grade" : "Initiate grade", "Icon.Initiation",
                string.Join("  ", new[] { x.Group ? "Group" : null, x.Ordeal ? "Ordeal" : null, x.Schooling ? "Schooling" : null }.Where(y => y != null)),
                x.Grade.ToString(C), null)).ToList();
            List<InventoryItemViewModel> lstMetamagic = objCharacter.Metamagics.ToList().Select(x => InventoryItemViewModel.Card(
                x.CurrentDisplayName, "Metamagic / echo", "Icon.Initiation", string.Empty, string.Empty, null)).ToList();
            lstMetamagic.AddRange(objCharacter.Arts.ToList().Select(x => InventoryItemViewModel.Card(x.CurrentDisplayName, "Art", "Icon.Initiation", string.Empty, string.Empty, null)));
            lstMetamagic.AddRange(objCharacter.Enhancements.ToList().Select(x => InventoryItemViewModel.Card(x.CurrentDisplayName, "Enhancement", "Icon.Initiation", string.Empty, string.Empty, null)));
            if (lstInitiation.Count + lstMetamagic.Count > 0)
                lstReturn.Add(new InventoryGroupViewModel("INITIATION & METAMAGIC", lstInitiation.Concat(lstMetamagic)));
            return lstReturn;
        }

        public static List<InventoryGroupViewModel> Network(Character objCharacter)
        {
            List<InventoryGroupViewModel> lstReturn = new List<InventoryGroupViewModel>();
            List<Contact> lstAll = objCharacter.Contacts.ToList();
            foreach (IGrouping<string, Contact> objGroup in lstAll.GroupBy(x => x.EntityType == ContactType.Enemy ? "ENEMIES" : x.EntityType == ContactType.Pet ? "PETS" : "CONTACTS")
                                                                   .OrderBy(x => x.Key == "CONTACTS" ? 0 : x.Key == "PETS" ? 1 : 2))
            {
                lstReturn.Add(new InventoryGroupViewModel(objGroup.Key, objGroup.Select(x => InventoryItemViewModel.Card(
                    string.IsNullOrWhiteSpace(x.Name) ? "(unnamed)" : x.Name, x.DisplayRole, "Icon.Contact",
                    "C" + x.Connection.ToString(C) + "  L" + x.Loyalty.ToString(C) + (x.IsGroup ? "  group" : string.Empty),
                    x.Connection.ToString(C) + "/" + x.Loyalty.ToString(C),
                    new[]
                    {
                        new ItemStat("Connection", x.Connection.ToString(C)), new ItemStat("Loyalty", x.Loyalty.ToString(C)),
                        new ItemStat("Role", x.DisplayRole), new ItemStat("Location", x.Location), new ItemStat("Metatype", x.DisplayMetatype),
                    }, x.Notes)).OrderBy(x => x.Name, System.StringComparer.CurrentCulture)));
            }
            List<InventoryItemViewModel> lstSpirits = objCharacter.Spirits.ToList().Select(x => InventoryItemViewModel.Card(
                string.IsNullOrWhiteSpace(x.CritterName) ? x.CurrentDisplayName : x.CritterName + " (" + x.CurrentDisplayName + ")",
                x.EntityType == SpiritType.Sprite ? "Sprite" : "Spirit", x.EntityType == SpiritType.Sprite ? "Icon.Sprite" : "Icon.Spirit",
                (x.EntityType == SpiritType.Sprite ? "Level " : "Force ") + x.Force.ToString(C) + "   " + x.ServicesOwed.ToString(C) + (x.EntityType == SpiritType.Sprite ? " tasks" : " services")
                + (x.Bound ? "   bound" : string.Empty),
                x.Force.ToString(C),
                new[] { new ItemStat(x.EntityType == SpiritType.Sprite ? "Level" : "Force", x.Force.ToString(C)), new ItemStat(x.EntityType == SpiritType.Sprite ? "Tasks" : "Services", x.ServicesOwed.ToString(C)), new ItemStat("Bound", x.Bound ? "yes" : "no") },
                x.Notes)).ToList();
            if (lstSpirits.Count > 0)
                lstReturn.Add(new InventoryGroupViewModel("SPIRITS & SPRITES", lstSpirits));
            return lstReturn;
        }
    }
}
