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
using Chummer.Backend.Equipment;

namespace Chummer.Desktop.ViewModels
{
    public enum InventoryKind
    {
        Weapon,
        Armor,
        Cyberware,
        Gear,
        Vehicle,
        Drug,
        Lifestyle
    }

    /// <summary>One stat line on an item's hover card.</summary>
    public sealed class ItemStat
    {
        public ItemStat(string strLabel, string strValue)
        {
            Label = strLabel;
            Value = strValue;
        }

        public string Label { get; }
        public string Value { get; }
    }

    /// <summary>
    /// A read-only snapshot of one owned item (weapon, armor, cyberware...), shaped for the inventory grid and hover card.
    /// Built on a background thread; holds no engine references the UI touches later.
    /// </summary>
    public sealed class InventoryItemViewModel
    {
        private InventoryItemViewModel(InventoryKind eKind, string strName, string strCategory, string strIconKey)
        {
            Kind = eKind;
            Name = strName ?? string.Empty;
            Category = strCategory ?? string.Empty;
            IconKey = strIconKey;
        }

        public InventoryKind Kind { get; }
        public string Name { get; }
        public string Category { get; }
        public string IconKey { get; }
        public string Badge { get; private set; } = string.Empty;
        public string Headline { get; private set; } = string.Empty;
        public bool IsEquipped { get; private set; } = true;
        public string Cost { get; private set; } = string.Empty;
        public string Avail { get; private set; } = string.Empty;
        public string Source { get; private set; } = string.Empty;
        public string Notes { get; private set; } = string.Empty;
        public List<ItemStat> Stats { get; } = new List<ItemStat>();
        public List<string> Contents { get; } = new List<string>();

        public bool HasBadge => Badge.Length > 0;
        public bool HasNotes => Notes.Length > 0;
        public bool HasContents => Contents.Count > 0;
        public string ContentsText => string.Join("\n", Contents.Take(16)) + (Contents.Count > 16 ? "\n+" + (Contents.Count - 16).ToString(CultureInfo.CurrentCulture) + " more" : string.Empty);
        public string KindLabel => Kind.ToString().ToUpperInvariant();
        public double Opacity => IsEquipped ? 1.0 : 0.55;

        /// <summary>Everything searchable about the item, lower-cased.</summary>
        public string SearchText { get; private set; } = string.Empty;

        private void Finish()
        {
            SearchText = (Name + " " + Category + " " + KindLabel + " " + Headline + " " + string.Join(" ", Contents) + " " + Notes).ToLowerInvariant();
        }

        private static string Money(Character objCharacter, decimal decValue)
            => decValue.ToString(objCharacter.Settings.NuyenFormat, GlobalSettings.CultureInfo) + LanguageManager.GetString("String_NuyenSymbol");

        private static string Src(string strSource, string strPage)
            => string.IsNullOrEmpty(strSource) ? string.Empty : (strSource + " " + strPage).Trim();

        /// <summary>A generic card (spell, power, contact...) using the same tile and hover card as items.</summary>
        public static InventoryItemViewModel Card(string strName, string strCategory, string strIconKey, string strHeadline, string strBadge,
                                                 IEnumerable<ItemStat> lstStats, string strNotes = "", string strSource = "", IEnumerable<string> lstContents = null)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Gear, strName, strCategory, strIconKey)
            {
                Headline = strHeadline ?? string.Empty,
                Badge = strBadge ?? string.Empty,
                Notes = strNotes ?? string.Empty,
                Source = strSource ?? string.Empty,
            };
            if (lstStats != null)
                objReturn.Stats.AddRange(lstStats.Where(x => !string.IsNullOrEmpty(x.Value)));
            if (lstContents != null)
                objReturn.Contents.AddRange(lstContents);
            objReturn.Finish();
            return objReturn;
        }

        // ---------- factories ----------

        public static IEnumerable<InventoryItemViewModel> FromCharacter(Character objCharacter)
        {
            foreach (Weapon objWeapon in objCharacter.Weapons.ToList())
                yield return FromWeapon(objCharacter, objWeapon);
            foreach (Armor objArmor in objCharacter.Armor.ToList())
                yield return FromArmor(objCharacter, objArmor);
            foreach (Cyberware objCyberware in objCharacter.Cyberware.ToList())
                yield return FromCyberware(objCharacter, objCyberware);
            foreach (Gear objGear in objCharacter.Gear.ToList())
                yield return FromGear(objCharacter, objGear);
            foreach (Vehicle objVehicle in objCharacter.Vehicles.ToList())
                yield return FromVehicle(objCharacter, objVehicle);
            foreach (Drug objDrug in objCharacter.Drugs.ToList())
                yield return FromDrug(objCharacter, objDrug);
            foreach (Lifestyle objLifestyle in objCharacter.Lifestyles.ToList())
                yield return FromLifestyle(objCharacter, objLifestyle);
        }

        private static InventoryItemViewModel FromWeapon(Character objCharacter, Weapon objWeapon)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Weapon, objWeapon.CurrentDisplayName, objWeapon.DisplayCategory(GlobalSettings.Language), WeaponIcon(objWeapon.Category));
            string strDamage = objWeapon.DisplayDamage;
            string strAp = objWeapon.DisplayTotalAP;
            objReturn.Headline = "DV " + strDamage + "   AP " + strAp;
            objReturn.Stats.Add(new ItemStat("Damage", strDamage));
            objReturn.Stats.Add(new ItemStat("AP", strAp));
            objReturn.Stats.Add(new ItemStat("Accuracy", objWeapon.GetTotalAccuracy().ToString(CultureInfo.CurrentCulture)));
            objReturn.Stats.Add(new ItemStat("Dice pool", objWeapon.DicePool.ToString(CultureInfo.CurrentCulture)));
            if (objWeapon.RangeType == "Melee")
                objReturn.Stats.Add(new ItemStat("Reach", objWeapon.TotalReach.ToString(CultureInfo.CurrentCulture)));
            else
            {
                objReturn.Stats.Add(new ItemStat("Mode", objWeapon.DisplayMode));
                objReturn.Stats.Add(new ItemStat("RC", objWeapon.DisplayTotalRC.Item1));
                objReturn.Stats.Add(new ItemStat("Ammo", objWeapon.DisplayAmmo));
            }
            objReturn.Stats.Add(new ItemStat("Conceal", objWeapon.DisplayConcealability));
            objReturn.IsEquipped = objWeapon.Equipped;
            objReturn.Cost = Money(objCharacter, objWeapon.TotalCost);
            objReturn.Avail = objWeapon.DisplayTotalAvail;
            objReturn.Source = Src(objWeapon.Source, objWeapon.Page);
            objReturn.Notes = objWeapon.Notes ?? string.Empty;
            foreach (WeaponAccessory objAccessory in objWeapon.WeaponAccessories.ToList())
                objReturn.Contents.Add("◆ " + objAccessory.CurrentDisplayName);
            foreach (Weapon objUnder in objWeapon.UnderbarrelWeapons.ToList())
                objReturn.Contents.Add("◆ " + objUnder.CurrentDisplayName);
            objReturn.Finish();
            return objReturn;
        }

        private static InventoryItemViewModel FromArmor(Character objCharacter, Armor objArmor)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Armor, objArmor.CurrentDisplayName, objArmor.DisplayCategory(GlobalSettings.Language), "Icon.Armor");
            objReturn.Badge = objArmor.DisplayArmorValue;
            objReturn.Headline = "Armor " + objArmor.DisplayArmorValue;
            objReturn.Stats.Add(new ItemStat("Armor", objArmor.DisplayArmorValue));
            objReturn.Stats.Add(new ItemStat("Capacity", objArmor.TotalArmorCapacity(GlobalSettings.CultureInfo)));
            if (objArmor.Rating > 0)
                objReturn.Stats.Add(new ItemStat("Rating", objArmor.Rating.ToString(CultureInfo.CurrentCulture)));
            objReturn.IsEquipped = objArmor.Equipped;
            objReturn.Cost = Money(objCharacter, objArmor.TotalCost);
            objReturn.Avail = objArmor.DisplayTotalAvail;
            objReturn.Source = Src(objArmor.Source, objArmor.Page);
            objReturn.Notes = objArmor.Notes ?? string.Empty;
            foreach (ArmorMod objMod in objArmor.ArmorMods.ToList())
                objReturn.Contents.Add("◆ " + objMod.CurrentDisplayName);
            foreach (Gear objGear in objArmor.GearChildren.ToList())
                objReturn.Contents.Add("◆ " + objGear.CurrentDisplayName);
            objReturn.Finish();
            return objReturn;
        }

        private static InventoryItemViewModel FromCyberware(Character objCharacter, Cyberware objCyberware)
        {
            bool blnBio = objCyberware.SourceType == Improvement.ImprovementSource.Bioware;
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Cyberware, objCyberware.CurrentDisplayNameShort, objCyberware.DisplayCategory(GlobalSettings.Language),
                                                                          blnBio ? "Icon.Bioware" : "Icon.Cyberware");
            string strEssence = objCyberware.CalculatedESS.ToString("0.####", GlobalSettings.CultureInfo);
            objReturn.Badge = objCyberware.Rating > 0 ? "R" + objCyberware.Rating.ToString(CultureInfo.CurrentCulture) : string.Empty;
            objReturn.Headline = "ESS " + strEssence + "   " + objCyberware.Grade?.CurrentDisplayName;
            objReturn.Stats.Add(new ItemStat("Essence", strEssence));
            objReturn.Stats.Add(new ItemStat("Grade", objCyberware.Grade?.CurrentDisplayName ?? string.Empty));
            if (objCyberware.Rating > 0)
                objReturn.Stats.Add(new ItemStat("Rating", objCyberware.Rating.ToString(CultureInfo.CurrentCulture)));
            if (!string.IsNullOrEmpty(objCyberware.Location))
                objReturn.Stats.Add(new ItemStat("Location", objCyberware.Location));
            objReturn.Cost = Money(objCharacter, objCyberware.TotalCost);
            objReturn.Avail = objCyberware.DisplayTotalAvail;
            objReturn.Source = Src(objCyberware.Source, objCyberware.Page);
            objReturn.Notes = objCyberware.Notes ?? string.Empty;
            foreach (Cyberware objChild in objCyberware.Children.ToList())
                objReturn.Contents.Add("◆ " + objChild.CurrentDisplayName);
            foreach (Gear objGear in objCyberware.GearChildren.ToList())
                objReturn.Contents.Add("◆ " + objGear.CurrentDisplayName);
            objReturn.Finish();
            return objReturn;
        }

        private static InventoryItemViewModel FromGear(Character objCharacter, Gear objGear)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Gear, objGear.CurrentDisplayNameShort, objGear.DisplayCategory(GlobalSettings.Language), GearIcon(objGear.Category));
            if (objGear.Quantity != 1)
                objReturn.Badge = "×" + objGear.Quantity.ToString("0.##", GlobalSettings.CultureInfo);
            else if (objGear.Rating > 0)
                objReturn.Badge = "R" + objGear.Rating.ToString(CultureInfo.CurrentCulture);
            objReturn.Headline = objGear.DisplayCategory(GlobalSettings.Language);
            if (objGear.Rating > 0)
                objReturn.Stats.Add(new ItemStat("Rating", objGear.Rating.ToString(CultureInfo.CurrentCulture)));
            objReturn.Stats.Add(new ItemStat("Quantity", objGear.Quantity.ToString("0.##", GlobalSettings.CultureInfo)));
            if (objGear.IsCommlink)
            {
                objReturn.Stats.Add(new ItemStat("Device rating", objGear.GetTotalMatrixAttribute("Device Rating").ToString(CultureInfo.CurrentCulture)));
                foreach (string strAttribute in new[] { "Attack", "Sleaze", "Data Processing", "Firewall" })
                {
                    int intValue = objGear.GetTotalMatrixAttribute(strAttribute);
                    if (intValue > 0)
                        objReturn.Stats.Add(new ItemStat(strAttribute, intValue.ToString(CultureInfo.CurrentCulture)));
                }
            }
            objReturn.IsEquipped = objGear.Equipped;
            objReturn.Cost = Money(objCharacter, objGear.TotalCost);
            objReturn.Avail = objGear.DisplayTotalAvail;
            objReturn.Source = Src(objGear.Source, objGear.Page);
            objReturn.Notes = objGear.Notes ?? string.Empty;
            foreach (Gear objChild in objGear.Children.ToList())
                objReturn.Contents.Add("◆ " + objChild.CurrentDisplayName);
            objReturn.Finish();
            return objReturn;
        }

        private static InventoryItemViewModel FromVehicle(Character objCharacter, Vehicle objVehicle)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Vehicle, objVehicle.CurrentDisplayName, objVehicle.DisplayCategory(GlobalSettings.Language), VehicleIcon(objVehicle.Category));
            objReturn.Headline = "HND " + objVehicle.TotalHandling + "   SPD " + objVehicle.TotalSpeed + "   BOD " + objVehicle.TotalBody.ToString(CultureInfo.CurrentCulture);
            objReturn.Stats.Add(new ItemStat("Handling", objVehicle.TotalHandling));
            objReturn.Stats.Add(new ItemStat("Speed", objVehicle.TotalSpeed));
            objReturn.Stats.Add(new ItemStat("Acceleration", objVehicle.TotalAccel));
            objReturn.Stats.Add(new ItemStat("Body", objVehicle.TotalBody.ToString(CultureInfo.CurrentCulture)));
            objReturn.Stats.Add(new ItemStat("Armor", objVehicle.TotalArmor.ToString(CultureInfo.CurrentCulture)));
            objReturn.Stats.Add(new ItemStat("Pilot", objVehicle.Pilot.ToString(CultureInfo.CurrentCulture)));
            objReturn.Stats.Add(new ItemStat("Sensor", objVehicle.CalculatedSensor.ToString(CultureInfo.CurrentCulture)));
            objReturn.Cost = Money(objCharacter, objVehicle.TotalCost);
            objReturn.Avail = objVehicle.DisplayTotalAvail;
            objReturn.Source = Src(objVehicle.Source, objVehicle.Page);
            objReturn.Notes = objVehicle.Notes ?? string.Empty;
            foreach (VehicleMod objMod in objVehicle.Mods.ToList())
                objReturn.Contents.Add("◆ " + objMod.CurrentDisplayName);
            foreach (Weapon objWeapon in objVehicle.Weapons.ToList())
                objReturn.Contents.Add("⌖ " + objWeapon.CurrentDisplayName);
            foreach (Gear objGear in objVehicle.GearChildren.ToList())
                objReturn.Contents.Add("◇ " + objGear.CurrentDisplayName);
            objReturn.Finish();
            return objReturn;
        }

        private static InventoryItemViewModel FromDrug(Character objCharacter, Drug objDrug)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Drug, objDrug.CurrentDisplayName, objDrug.Category, "Icon.Drug");
            objReturn.Badge = "×" + objDrug.Quantity.ToString("0.##", GlobalSettings.CultureInfo);
            objReturn.Headline = objDrug.Category;
            objReturn.Stats.Add(new ItemStat("Doses", objDrug.Quantity.ToString("0.##", GlobalSettings.CultureInfo)));
            objReturn.Cost = Money(objCharacter, objDrug.TotalCost);
            objReturn.Finish();
            return objReturn;
        }

        private static InventoryItemViewModel FromLifestyle(Character objCharacter, Lifestyle objLifestyle)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Lifestyle, objLifestyle.CurrentDisplayName, objLifestyle.BaseLifestyle, "Icon.Lifestyle");
            objReturn.Badge = objLifestyle.Increments.ToString(CultureInfo.CurrentCulture) + "×";
            objReturn.Headline = objLifestyle.BaseLifestyle + "   " + objLifestyle.DisplayTotalMonthlyCost;
            objReturn.Stats.Add(new ItemStat("Lifestyle", objLifestyle.BaseLifestyle));
            objReturn.Stats.Add(new ItemStat("Per period", objLifestyle.DisplayTotalMonthlyCost));
            objReturn.Stats.Add(new ItemStat("Periods paid", objLifestyle.Increments.ToString(CultureInfo.CurrentCulture)));
            objReturn.Cost = Money(objCharacter, objLifestyle.TotalCost);
            objReturn.Source = Src(objLifestyle.Source, objLifestyle.Page);
            objReturn.Notes = objLifestyle.Notes ?? string.Empty;
            objReturn.Finish();
            return objReturn;
        }

        // ---------- icon mapping by data category ----------

        internal static string WeaponIcon(string strCategory)
        {
            switch (strCategory ?? string.Empty)
            {
                case "Blades":
                case "Clubs":
                case "Exotic Melee Weapons":
                case "Unarmed":
                case "Improvised Weapons":
                    return "Icon.Blade";
                case "Assault Rifles":
                case "Sniper Rifles":
                case "Sporting Rifles":
                case "Shotguns":
                case "Assault Cannons":
                case "Light Machineguns":
                case "Medium Machineguns":
                case "Heavy Machineguns":
                    return "Icon.Rifle";
                case "Grenades":
                case "Missile Launchers":
                case "Grenade Launchers":
                case "Rocket Launchers":
                    return "Icon.Explosive";
                case "Gear":
                    return "Icon.Ammo";
                default:
                    return "Icon.Weapon";
            }
        }

        internal static string GearIcon(string strCategory)
        {
            string strLoop = strCategory ?? string.Empty;
            if (strLoop == "Commlinks" || strLoop == "Commlink Accessories")
                return "Icon.Commlink";
            if (strLoop.StartsWith("Cyberdeck", StringComparison.Ordinal) || strLoop == "Rigger Command Consoles" || strLoop == "Cyberterminals")
                return "Icon.Cyberdeck";
            if (strLoop.StartsWith("Ammunition", StringComparison.Ordinal))
                return "Icon.Ammo";
            if (strLoop == "Explosives")
                return "Icon.Explosive";
            if (strLoop == "Biotech" || strLoop == "Medical" || strLoop == "Slap Patches")
                return "Icon.Medkit";
            if (strLoop == "Breaking and Entering Gear" || strLoop == "B&E Gear" || strLoop == "Lockpicks")
                return "Icon.Lockpick";
            if (strLoop == "Sensors" || strLoop == "Sensor Functions" || strLoop == "Audio Devices" || strLoop == "Optical Devices"
                || strLoop == "Vision Devices" || strLoop == "Vision Enhancements" || strLoop == "Audio Enhancements" || strLoop == "Security Devices")
                return "Icon.Sensor";
            if (strLoop == "Foci" || strLoop == "Magical Supplies" || strLoop == "Metamagic Foci" || strLoop.IndexOf("Focus", StringComparison.Ordinal) >= 0)
                return "Icon.Focus";
            if (strLoop == "Drugs" || strLoop == "Toxins")
                return "Icon.Drug";
            if (strLoop == "Contracts/Upkeep" || strLoop == "Licenses" || strLoop == "ID/Credsticks")
                return "Icon.Contract";
            if (strLoop == "Common Programs" || strLoop == "Hacking Programs" || strLoop == "Software" || strLoop == "Autosofts" || strLoop == "Skillsofts")
                return "Icon.Cyberdeck";
            return "Icon.Gear";
        }

        internal static string VehicleIcon(string strCategory)
        {
            string strLoop = strCategory ?? string.Empty;
            if (strLoop.IndexOf("Drone", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Icon.Drone";
            if (strLoop.IndexOf("Boat", StringComparison.OrdinalIgnoreCase) >= 0 || strLoop.IndexOf("Submarine", StringComparison.OrdinalIgnoreCase) >= 0
                || strLoop.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Icon.Boat";
            if (strLoop.IndexOf("craft", StringComparison.OrdinalIgnoreCase) >= 0 || strLoop.IndexOf("VTOL", StringComparison.OrdinalIgnoreCase) >= 0
                || strLoop.IndexOf("Wing", StringComparison.OrdinalIgnoreCase) >= 0 || strLoop.IndexOf("LTA", StringComparison.OrdinalIgnoreCase) >= 0
                || strLoop.IndexOf("Plane", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Icon.Aircraft";
            return "Icon.Vehicle";
        }
    }
}

namespace Chummer.Desktop.ViewModels
{
    public sealed class InventoryGroupViewModel
    {
        public InventoryGroupViewModel(InventoryKind eKind, IEnumerable<InventoryItemViewModel> lstItems)
        {
            Kind = eKind;
            Items = lstItems.ToList();
        }

        public InventoryGroupViewModel(string strTitle, IEnumerable<InventoryItemViewModel> lstItems)
        {
            _strTitle = strTitle;
            Items = lstItems.ToList();
        }

        private readonly string _strTitle;

        public InventoryKind Kind { get; }
        public List<InventoryItemViewModel> Items { get; }
        public string Name => _strTitle ?? Title(Kind);
        public string CountText => Items.Count.ToString(CultureInfo.CurrentCulture);

        public static string Title(InventoryKind eKind)
        {
            switch (eKind)
            {
                case InventoryKind.Weapon: return "WEAPONS";
                case InventoryKind.Armor: return "ARMOR";
                case InventoryKind.Cyberware: return "CYBER / BIOWARE";
                case InventoryKind.Gear: return "GEAR";
                case InventoryKind.Vehicle: return "VEHICLES & DRONES";
                case InventoryKind.Drug: return "DRUGS";
                default: return "LIFESTYLES";
            }
        }
    }

    /// <summary>A category chip above the inventory grid.</summary>
    public sealed class InventoryFilterOption : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        private readonly CharacterViewModel _objOwner;

        public InventoryFilterOption(InventoryKind? eKind, string strName, string strIconKey, int intCount, CharacterViewModel objOwner)
        {
            Kind = eKind;
            Name = strName;
            IconKey = strIconKey;
            Count = intCount;
            _objOwner = objOwner;
            SelectCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => _objOwner.InventoryKind = Kind);
        }

        public InventoryKind? Kind { get; }
        public string Name { get; }
        public string IconKey { get; }
        public int Count { get; }
        public string Label => Name + "  " + Count.ToString(CultureInfo.CurrentCulture);
        public bool IsSelected => _objOwner.InventoryKind == Kind;
        public CommunityToolkit.Mvvm.Input.IRelayCommand SelectCommand { get; }

        public void Refresh() => OnPropertyChanged(nameof(IsSelected));
    }
}
