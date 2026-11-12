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
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Chummer.Backend.Equipment;
using Chummer.Rules;
using CommunityToolkit.Mvvm.ComponentModel;

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
        public ItemStat(string strLabel, string strValue, string strRollName = null)
        {
            Label = strLabel;
            Value = strValue;
            RollName = strRollName ?? string.Empty;
        }

        public string Label { get; }
        public string Value { get; }

        /// <summary>Dice Terminal roll opened when the stat is clicked; empty when the stat is not rollable.</summary>
        public string RollName { get; }

        public bool IsRollable => RollName.Length > 0;
        public bool IsPlain => RollName.Length == 0;
    }

    /// <summary>One entry of a weapon's fire menu.</summary>
    public sealed class FireModeOption
    {
        public FireModeOption(FiringModeInfo objInfo)
        {
            Mode = objInfo.Mode;
            Label = objInfo.Label;
            Allowed = objInfo.Allowed;
            Rounds = objInfo.Rounds;
        }

        public FiringMode Mode { get; }
        public string Label { get; }
        public bool Allowed { get; }
        public int Rounds { get; }
        public string ShortLabel
        {
            get
            {
                switch (Mode)
                {
                    case FiringMode.SingleShot: return "SS";
                    case FiringMode.ShortBurst: return "SB";
                    case FiringMode.LongBurst: return "LB";
                    case FiringMode.FullBurst: return "FA";
                    default: return "SUP";
                }
            }
        }
    }

    /// <summary>
    /// A snapshot of one owned item (weapon, armor, cyberware...), shaped for the inventory slot grid and detail panel.
    /// Built on a background thread; <see cref="Item"/> is the engine object the session actions work on.
    /// </summary>
    public sealed class InventoryItemViewModel : ObservableObject
    {
        private InventoryItemViewModel(InventoryKind eKind, string strName, string strCategory, string strIconKey)
        {
            Kind = eKind;
            Name = strName ?? string.Empty;
            Category = strCategory ?? string.Empty;
            IconKey = strIconKey;
            Accent = AccentFor(eKind);
        }

        public InventoryKind Kind { get; }
        public string Name { get; }
        public string Category { get; }
        public string IconKey { get; }

        /// <summary>Engine object behind the slot (Weapon, Gear, Armor...); null for ability cards.</summary>
        internal object Item { get; private set; }
        internal int PhysicalConditionMax { get; private set; }
        internal int PhysicalConditionFilled { get; private set; }
        internal int MatrixConditionMax { get; private set; }
        internal int MatrixConditionFilled { get; private set; }
        public bool CanSetLocation { get; private set; }
        public string LocationName { get; private set; } = "";
        public bool HasLocation => LocationName.Length > 0;

        public bool CanAddMod => CharacterViewModel.ModSectionFor(Item) != null;
        public bool CanAddWeaponMount => Item is Vehicle;
        public bool CanEditWeaponMount => Item is WeaponMount mount && !mount.IncludedInVehicle;
        public bool CanAddMountedWeapon { get; private set; }
        public bool CanEditImplant { get; private set; }
        public int SortOrder { get; private set; }
        public bool CanReorder { get; private set; }
        public bool CanModifyEquipment => CanAddMod || CanEditImplant || CanAddWeaponMount || CanAddMountedWeapon || CanEditWeaponMount || IsLifestyle;
        public bool CanOrganizeEquipment => CanRename || CanReorder || CanSetLocation || HasLocation;
        public bool IsLifestyle => Item is Lifestyle;

        /// <summary>Engine InternalId, used to keep the selection across reloads.</summary>
        public string Id { get; private set; } = string.Empty;

        public string Badge { get; private set; } = string.Empty;
        public string Headline { get; private set; } = string.Empty;
        public bool IsEquipped { get; private set; } = true;
        public string Cost { get; private set; } = string.Empty;
        public string Avail { get; private set; } = string.Empty;
        public string Source { get; private set; } = string.Empty;
        public string Notes { get; private set; } = string.Empty;
        public string Description { get; private set; } = string.Empty;
        public bool HasDescription => Description.Length > 0;
        public List<ItemStat> Stats { get; } = new List<ItemStat>();
        public List<string> Contents { get; } = new List<string>();

        /// <summary>Mods, accessories, plugins and contents as nested mini-slots.</summary>
        public List<InventoryItemViewModel> Children { get; } = new List<InventoryItemViewModel>();

        public bool HasBadge => Badge.Length > 0;
        public bool HasNotes => Notes.Length > 0;
        public bool HasContents => Contents.Count > 0;
        public bool HasChildren => Children.Count > 0;
        public bool HasSource => Source.Length > 0;
        public bool HasAvail => Avail.Length > 0;
        public string ContentsText => string.Join("\n", Contents.Take(16)) + (Contents.Count > 16 ? "\n+" + (Contents.Count - 16).ToString(CultureInfo.CurrentCulture) + " more" : string.Empty);
        public string KindLabel => Kind.ToString().ToUpperInvariant();
        public double Opacity => IsEquipped ? 1.0 : 0.55;

        // ---------- look ----------

        public Color Accent { get; }
        public IBrush AccentBrush => new ImmutableSolidColorBrush(Accent);
        public IBrush AccentFill => new ImmutableLinearGradientBrush(
            new[] { new ImmutableGradientStop(0, Color.FromArgb(0x30, Accent.R, Accent.G, Accent.B)), new ImmutableGradientStop(1, Color.FromArgb(0x06, Accent.R, Accent.G, Accent.B)) },
            startPoint: new Avalonia.RelativePoint(0.5, 0, Avalonia.RelativeUnit.Relative), endPoint: new Avalonia.RelativePoint(0.5, 1, Avalonia.RelativeUnit.Relative));
        public BoxShadows AccentGlow => new BoxShadows(new BoxShadow { Blur = 16, Color = Color.FromArgb(0x55, Accent.R, Accent.G, Accent.B) });

        /// <summary>"Rarity" colour per kind of item, like a game inventory.</summary>
        public static Color AccentFor(InventoryKind eKind)
        {
            switch (eKind)
            {
                case InventoryKind.Weapon: return Color.FromRgb(0xFF, 0x4D, 0x5E);
                case InventoryKind.Armor: return Color.FromRgb(0x00, 0xE5, 0xFF);
                case InventoryKind.Cyberware: return Color.FromRgb(0xB6, 0x6D, 0xFF);
                case InventoryKind.Gear: return Color.FromRgb(0x39, 0xFF, 0x88);
                case InventoryKind.Vehicle: return Color.FromRgb(0xF3, 0xE6, 0x00);
                case InventoryKind.Drug: return Color.FromRgb(0xFF, 0x2E, 0x88);
                default: return Color.FromRgb(0xFF, 0xA0, 0x2E);
            }
        }

        // ---------- selection ----------

        private bool _blnIsSelected;

        public bool IsSelected
        {
            get => _blnIsSelected;
            set => SetProperty(ref _blnIsSelected, value);
        }

        // ---------- corner badges ----------

        public string RatingBadge { get; private set; } = string.Empty;
        public string QuantityBadge { get; private set; } = string.Empty;
        public bool HasRatingBadge => RatingBadge.Length > 0;
        public bool HasQuantityBadge => QuantityBadge.Length > 0;

        // ---------- session state & what can be done ----------

        public bool IsCareer { get; private set; }
        public bool CanEquip { get; private set; }
        public bool ShowEquippedMarker => CanEquip && IsEquipped;
        public string EquipLabel => IsEquipped ? "UNEQUIP" : "EQUIP";
        public bool HasWireless { get; private set; }
        public bool IsWirelessOn { get; private set; }
        public bool ShowWirelessBadge => HasWireless && IsWirelessOn;
        public string WirelessLabel => IsWirelessOn ? "WIRELESS ON" : "WIRELESS OFF";
        public bool IsCommlink { get; private set; }
        public bool IsActiveCommlink { get; private set; }
        public string CommlinkLabel => IsActiveCommlink ? "ACTIVE COMMLINK" : "MAKE ACTIVE";

        public bool UsesAmmo { get; private set; }
        public bool CanFire => UsesAmmo && IsCareer;
        public int AmmoRemaining { get; private set; }
        public int AmmoCapacity { get; private set; }
        public string AmmoLoadedName { get; private set; } = string.Empty;
        public string AmmoText => AmmoRemaining.ToString(CultureInfo.CurrentCulture) + (AmmoCapacity > 0 ? " / " + AmmoCapacity.ToString(CultureInfo.CurrentCulture) : string.Empty);
        public double AmmoFraction => AmmoCapacity > 0 ? Math.Max(0, Math.Min(1, AmmoRemaining / (double)AmmoCapacity)) : AmmoRemaining > 0 ? 1 : 0;
        public double AmmoBarWidth => 200 * AmmoFraction;
        public bool IsAmmoLow => AmmoCapacity > 0 && AmmoRemaining * 4 <= AmmoCapacity;
        public bool CanUnload { get; private set; }
        public List<FireModeOption> FireModes { get; } = new List<FireModeOption>();
        public List<FireModeOption> AllowedFireModes => FireModes.Where(x => x.Allowed).ToList();

        public bool IsArmorPiece => Item is Armor;
        public bool CanDamageArmor => IsArmorPiece && IsCareer;
        public int ArmorDamage { get; private set; }
        public string ArmorDamageText => ArmorDamage.ToString(CultureInfo.CurrentCulture);

        public bool HasQuantity { get; private set; }
        public decimal Quantity { get; private set; }
        public string QuantityText => Quantity.ToString("0.##", GlobalSettings.CultureInfo);
        public bool CanAddQuantity => Item is Gear || Item is Drug;
        public bool IsConsumable { get; private set; }

        public bool CanRename { get; private set; }
        public bool CanSell { get; private set; }
        public bool CanSellNow => CanSell && IsCareer;
        public bool CanRemove { get; private set; }
        public bool CanEditNotes => Item is IHasNotes;
        public bool CanReapplyBonuses => Item is Gear || Item is Armor || Item is ArmorMod || Item is Cyberware || Item is Weapon;
        public bool HasActions => Item != null;

        /// <summary>Everything searchable about the item, lower-cased.</summary>
        public string SearchText { get; private set; } = string.Empty;

        private void Finish()
        {
            var ordered = Children.GroupBy(x => x.Item?.GetType()).SelectMany(g => g.OrderBy(x => x.SortOrder)).ToList();
            Children.Clear(); Children.AddRange(ordered);
            SearchText = (Name + " " + Category + " " + KindLabel + " " + Headline + " " + string.Join(" ", Contents) + " " + Notes).ToLowerInvariant();
        }

        private void Bind(Character objCharacter, object objItem, string strId)
        {
            Item = objItem;
            SortOrder = (objItem as ICanSort)?.SortOrder ?? 0;
            CanReorder = objItem is ICanSort;
            Id = strId ?? string.Empty;
            IsCareer = objCharacter.Created;
            CanEditImplant = objItem is Cyberware implant && implant.ParentVehicle == null && implant.SourceID != Cyberware.EssenceHoleGUID && implant.SourceID != Cyberware.EssenceAntiHoleGUID
                && !(implant.Capacity == "[*]" && implant.Parent != null);
            CanAddMountedWeapon = objItem is WeaponMount mount && !mount.IsWeaponsFull;
            CanSetLocation = EquipmentLocationRules.LocationsFor(objCharacter, objItem) != null;
            LocationName = (objItem as IHasLocation)?.Location?.Name ?? "";
            if (LocationName.Length > 0) Stats.Add(new ItemStat("Location", LocationName));
            if (objItem is Vehicle vehicle)
            {
                PhysicalConditionMax = vehicle.PhysicalCM;
                PhysicalConditionFilled = vehicle.PhysicalCMFilled;
            }
            if (objItem is IHasMatrixAttributes device)
            {
                MatrixConditionMax = device.MatrixCM;
                MatrixConditionFilled = device.MatrixCMFilled;
            }
            CanEquip = InventoryRules.CanChangeEquipped(objItem);
            if (objItem is IHasWirelessBonus objWireless)
            {
                HasWireless = true;
                IsWirelessOn = objWireless.WirelessOn;
            }
            if (objItem is IHasMatrixAttributes objMatrix && objMatrix.IsCommlink)
            {
                IsCommlink = true;
                IsActiveCommlink = objMatrix.IsActiveCommlink(objCharacter);
            }
            CanRename = InventoryRules.RenameDescriptionKey(objItem) != null;
            CanSell = InventoryRules.CanSell(objItem);
            CanRemove = InventoryRules.CanRemove(objItem);
        }

        private static string Money(Character objCharacter, decimal decValue)
            => decValue.ToString(objCharacter.Settings.NuyenFormat, GlobalSettings.CultureInfo) + LanguageManager.GetString("String_NuyenSymbol");

        private static string Src(string strSource, string strPage)
            => string.IsNullOrEmpty(strSource) ? string.Empty : (strSource + " " + strPage).Trim();

        private static string Rating(int intRating) => intRating > 0 ? "R" + intRating.ToString(CultureInfo.CurrentCulture) : string.Empty;

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
                yield return FromGear(objCharacter, objGear, InventoryKind.Gear);
            foreach (Vehicle objVehicle in objCharacter.Vehicles.ToList())
                yield return FromVehicle(objCharacter, objVehicle);
            foreach (Drug objDrug in objCharacter.Drugs.ToList())
                yield return FromDrug(objCharacter, objDrug);
            foreach (Lifestyle objLifestyle in objCharacter.Lifestyles.ToList())
                yield return FromLifestyle(objCharacter, objLifestyle);
        }

        private static InventoryItemViewModel FromWeapon(Character objCharacter, Weapon objWeapon)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Weapon, objWeapon.CurrentDisplayName, objWeapon.DisplayCategory(GlobalSettings.Language),
                                                                          InventoryIcons.ForWeapon(objWeapon.Category, objWeapon.Name));
            objReturn.Bind(objCharacter, objWeapon, objWeapon.InternalId);
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
            if (objWeapon.Rating > 0)
                objReturn.Stats.Add(new ItemStat("Rating", objWeapon.Rating.ToString(CultureInfo.CurrentCulture)));
            objReturn.RatingBadge = Rating(objWeapon.Rating);
            objReturn.IsEquipped = objWeapon.Equipped;
            if (InventoryRules.UsesAmmo(objWeapon))
            {
                objReturn.UsesAmmo = true;
                objReturn.AmmoRemaining = objWeapon.AmmoRemaining;
                try
                {
                    List<string> lstCounts = InventoryRules.GetReloadCountsAsync(objWeapon).GetAwaiter().GetResult();
                    objReturn.AmmoCapacity = (int)lstCounts.Select(InventoryRules.ParseCount).DefaultIfEmpty(0).Max();
                    objReturn.FireModes.AddRange(InventoryRules.GetFiringModesAsync(objWeapon).GetAwaiter().GetResult().Select(x => new FireModeOption(x)));
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    Utils.BreakIfDebug();
                }
                Gear objLoaded = objWeapon.AmmoLoaded;
                objReturn.AmmoLoadedName = objLoaded != null
                    ? objLoaded.CurrentDisplayNameShort
                    : LanguageManager.GetString(objWeapon.RequireAmmo ? (objReturn.AmmoRemaining > 0 ? "String_ExternalSource" : "String_Empty") : "String_MountInternal");
                objReturn.CanUnload = InventoryRules.CanUnload(objWeapon);
                objReturn.QuantityBadge = objReturn.AmmoRemaining.ToString(CultureInfo.CurrentCulture) + "rd";
            }
            objReturn.Cost = Money(objCharacter, objWeapon.TotalCost);
            objReturn.Avail = objWeapon.DisplayTotalAvail;
            objReturn.Source = Src(objWeapon.Source, objWeapon.Page);
            objReturn.Notes = objWeapon.Notes ?? string.Empty;
            foreach (WeaponAccessory objAccessory in objWeapon.WeaponAccessories.ToList())
            {
                objReturn.Contents.Add("◆ " + objAccessory.CurrentDisplayName);
                objReturn.Children.Add(FromAccessory(objCharacter, objAccessory));
            }
            foreach (Weapon objUnder in objWeapon.UnderbarrelWeapons.ToList())
            {
                objReturn.Contents.Add("◆ " + objUnder.CurrentDisplayName);
                objReturn.Children.Add(FromWeapon(objCharacter, objUnder));
            }
            objReturn.Finish();
            return objReturn;
        }

        private static InventoryItemViewModel FromAccessory(Character objCharacter, WeaponAccessory objAccessory)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Weapon, objAccessory.CurrentDisplayName, "Accessory", "Icon.Settings");
            objReturn.Bind(objCharacter, objAccessory, objAccessory.InternalId);
            objReturn.IsEquipped = objAccessory.Equipped;
            objReturn.RatingBadge = Rating(objAccessory.Rating);
            if (objAccessory.Rating > 0)
                objReturn.Stats.Add(new ItemStat("Rating", objAccessory.Rating.ToString(CultureInfo.CurrentCulture)));
            if (objAccessory.IncludedInWeapon)
                objReturn.Stats.Add(new ItemStat("Included", "with weapon"));
            objReturn.Cost = Money(objCharacter, objAccessory.TotalCost);
            objReturn.Avail = objAccessory.DisplayTotalAvail;
            objReturn.Source = objAccessory.SourceDetail.ToString();
            objReturn.Notes = objAccessory.Notes ?? string.Empty;
            foreach (Gear objGear in objAccessory.GearChildren.ToList())
                objReturn.Children.Add(FromGear(objCharacter, objGear, InventoryKind.Gear));
            objReturn.Finish();
            return objReturn;
        }

        private static InventoryItemViewModel FromArmor(Character objCharacter, Armor objArmor)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Armor, objArmor.CurrentDisplayName, objArmor.DisplayCategory(GlobalSettings.Language),
                                                                          InventoryIcons.ForArmor(objArmor.Category, objArmor.Name));
            objReturn.Bind(objCharacter, objArmor, objArmor.InternalId);
            objReturn.Badge = objArmor.DisplayArmorValue;
            objReturn.Headline = "Armor " + objArmor.DisplayArmorValue;
            objReturn.RatingBadge = objArmor.DisplayArmorValue;
            objReturn.Stats.Add(new ItemStat("Armor", objArmor.DisplayArmorValue));
            objReturn.Stats.Add(new ItemStat("Capacity", objArmor.TotalArmorCapacity(GlobalSettings.CultureInfo)));
            if (objArmor.Rating > 0)
                objReturn.Stats.Add(new ItemStat("Rating", objArmor.Rating.ToString(CultureInfo.CurrentCulture)));
            objReturn.ArmorDamage = objArmor.ArmorDamage;
            if (objArmor.ArmorDamage > 0)
                objReturn.Stats.Add(new ItemStat("Damage", "−" + objArmor.ArmorDamage.ToString(CultureInfo.CurrentCulture)));
            objReturn.IsEquipped = objArmor.Equipped;
            objReturn.Cost = Money(objCharacter, objArmor.TotalCost);
            objReturn.Avail = objArmor.DisplayTotalAvail;
            objReturn.Source = Src(objArmor.Source, objArmor.Page);
            objReturn.Notes = objArmor.Notes ?? string.Empty;
            foreach (ArmorMod objMod in objArmor.ArmorMods.ToList())
            {
                objReturn.Contents.Add("◆ " + objMod.CurrentDisplayName);
                objReturn.Children.Add(FromArmorMod(objCharacter, objMod));
            }
            foreach (Gear objGear in objArmor.GearChildren.ToList())
            {
                objReturn.Contents.Add("◆ " + objGear.CurrentDisplayName);
                objReturn.Children.Add(FromGear(objCharacter, objGear, InventoryKind.Gear));
            }
            objReturn.Finish();
            return objReturn;
        }

        private static InventoryItemViewModel FromArmorMod(Character objCharacter, ArmorMod objMod)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Armor, objMod.CurrentDisplayName, objMod.DisplayCategory(GlobalSettings.Language), "Icon.Settings");
            objReturn.Bind(objCharacter, objMod, objMod.InternalId);
            objReturn.IsEquipped = objMod.Equipped;
            objReturn.RatingBadge = Rating(objMod.Rating);
            if (objMod.Armor != 0)
                objReturn.Stats.Add(new ItemStat("Armor", "+" + objMod.Armor.ToString(CultureInfo.CurrentCulture)));
            if (objMod.Rating > 0)
                objReturn.Stats.Add(new ItemStat("Rating", objMod.Rating.ToString(CultureInfo.CurrentCulture)));
            objReturn.Cost = Money(objCharacter, objMod.TotalCost);
            objReturn.Avail = objMod.DisplayTotalAvail;
            objReturn.Source = objMod.SourceDetail.ToString();
            objReturn.Notes = objMod.Notes ?? string.Empty;
            foreach (Gear objGear in objMod.GearChildren.ToList())
                objReturn.Children.Add(FromGear(objCharacter, objGear, InventoryKind.Gear));
            objReturn.Finish();
            return objReturn;
        }

        private static InventoryItemViewModel FromCyberware(Character objCharacter, Cyberware objCyberware)
        {
            bool blnBio = objCyberware.SourceType == Improvement.ImprovementSource.Bioware;
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Cyberware, objCyberware.CurrentDisplayNameShort, objCyberware.DisplayCategory(GlobalSettings.Language),
                                                                          InventoryIcons.ForCyberware(objCyberware.Category, objCyberware.Name, blnBio));
            objReturn.Bind(objCharacter, objCyberware, objCyberware.InternalId);
            string strEssence = objCyberware.CalculatedESS.ToString("0.####", GlobalSettings.CultureInfo);
            objReturn.Badge = Rating(objCyberware.Rating);
            objReturn.RatingBadge = objReturn.Badge;
            objReturn.Headline = "ESS " + strEssence + "   " + objCyberware.Grade?.CurrentDisplayName;
            objReturn.Stats.Add(new ItemStat("Essence", strEssence));
            objReturn.Stats.Add(new ItemStat("Grade", objCyberware.Grade?.CurrentDisplayName ?? string.Empty));
            if (objCyberware.Rating > 0)
                objReturn.Stats.Add(new ItemStat("Rating", objCyberware.Rating.ToString(CultureInfo.CurrentCulture)));
            if (!string.IsNullOrEmpty(objCyberware.Location))
                objReturn.Stats.Add(new ItemStat("Location", objCyberware.Location));
            if (objCyberware.IsCommlink)
            {
                objReturn.Stats.Add(new ItemStat("Device rating", objCyberware.GetTotalMatrixAttribute("Device Rating").ToString(CultureInfo.CurrentCulture)));
                AddMatrixStats(objReturn, objCyberware);
            }
            objReturn.Cost = Money(objCharacter, objCyberware.TotalCost);
            objReturn.Avail = objCyberware.DisplayTotalAvail;
            objReturn.Source = Src(objCyberware.Source, objCyberware.Page);
            objReturn.Notes = objCyberware.Notes ?? string.Empty;
            foreach (Cyberware objChild in objCyberware.Children.ToList())
            {
                objReturn.Contents.Add("◆ " + objChild.CurrentDisplayName);
                objReturn.Children.Add(FromCyberware(objCharacter, objChild));
            }
            foreach (Gear objGear in objCyberware.GearChildren.ToList())
            {
                objReturn.Contents.Add("◆ " + objGear.CurrentDisplayName);
                objReturn.Children.Add(FromGear(objCharacter, objGear, InventoryKind.Gear));
            }
            objReturn.Finish();
            return objReturn;
        }

        private static void AddMatrixStats(InventoryItemViewModel objReturn, IHasMatrixAttributes objMatrix)
        {
            foreach (string strAttribute in new[] { "Attack", "Sleaze", "Data Processing", "Firewall" })
            {
                int intValue = objMatrix.GetTotalMatrixAttribute(strAttribute);
                if (intValue > 0)
                    objReturn.Stats.Add(new ItemStat(strAttribute, intValue.ToString(CultureInfo.CurrentCulture)));
            }
        }

        private static readonly HashSet<string> s_setConsumableCategories = new HashSet<string>
        {
            "Ammunition", "Biotech", "Drugs", "Custom Drugs", "Toxins", "Chemicals", "Explosives", "Food", "Tir Tairngire Beverages",
            "Alchemical Preperations", "Magical Compounds", "Magical Supplies", "BTLs", "Currency", "Nanogear"
        };

        private static InventoryItemViewModel FromGear(Character objCharacter, Gear objGear, InventoryKind eKind)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(eKind, objGear.CurrentDisplayNameShort, objGear.DisplayCategory(GlobalSettings.Language),
                                                                          InventoryIcons.ForGear(objGear.Category, objGear.Name));
            objReturn.Bind(objCharacter, objGear, objGear.InternalId);
            objReturn.HasQuantity = true;
            objReturn.Quantity = objGear.Quantity;
            objReturn.IsConsumable = s_setConsumableCategories.Contains(objGear.Category) || objGear.Name.IndexOf("Medkit", StringComparison.OrdinalIgnoreCase) >= 0
                                     || objGear.Name.IndexOf("Patch", StringComparison.OrdinalIgnoreCase) >= 0;
            if (objGear.Quantity != 1)
            {
                objReturn.Badge = "×" + objGear.Quantity.ToString("0.##", GlobalSettings.CultureInfo);
                objReturn.QuantityBadge = objReturn.Badge;
            }
            else if (objGear.Rating > 0)
                objReturn.Badge = Rating(objGear.Rating);
            objReturn.RatingBadge = Rating(objGear.Rating);
            objReturn.Headline = objGear.DisplayCategory(GlobalSettings.Language);
            if (objGear.Rating > 0)
                objReturn.Stats.Add(new ItemStat("Rating", objGear.Rating.ToString(CultureInfo.CurrentCulture)));
            objReturn.Stats.Add(new ItemStat("Quantity", objGear.Quantity.ToString("0.##", GlobalSettings.CultureInfo)));
            if (objGear.IsCommlink)
            {
                objReturn.Stats.Add(new ItemStat("Device rating", objGear.GetTotalMatrixAttribute("Device Rating").ToString(CultureInfo.CurrentCulture)));
                AddMatrixStats(objReturn, objGear);
            }
            if (!string.IsNullOrEmpty(objGear.Capacity) && objGear.Capacity != "0")
                objReturn.Stats.Add(new ItemStat("Capacity", objGear.Capacity));
            objReturn.IsEquipped = objGear.Equipped;
            objReturn.Cost = Money(objCharacter, objGear.TotalCost);
            objReturn.Avail = objGear.DisplayTotalAvail;
            objReturn.Source = Src(objGear.Source, objGear.Page);
            objReturn.Notes = objGear.Notes ?? string.Empty;
            foreach (Gear objChild in objGear.Children.ToList())
            {
                objReturn.Contents.Add("◆ " + objChild.CurrentDisplayName);
                objReturn.Children.Add(FromGear(objCharacter, objChild, InventoryKind.Gear));
            }
            objReturn.Finish();
            return objReturn;
        }

        private static InventoryItemViewModel FromVehicle(Character objCharacter, Vehicle objVehicle)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Vehicle, objVehicle.CurrentDisplayName, objVehicle.DisplayCategory(GlobalSettings.Language),
                                                                          InventoryIcons.ForVehicle(objVehicle.Category, objVehicle.Name));
            objReturn.Bind(objCharacter, objVehicle, objVehicle.InternalId);
            objReturn.Headline = "HND " + objVehicle.TotalHandling + "   SPD " + objVehicle.TotalSpeed + "   BOD " + objVehicle.TotalBody.ToString(CultureInfo.CurrentCulture);
            objReturn.Stats.Add(new ItemStat("Handling", objVehicle.TotalHandling));
            objReturn.Stats.Add(new ItemStat("Speed", objVehicle.TotalSpeed));
            objReturn.Stats.Add(new ItemStat("Acceleration", objVehicle.TotalAccel));
            objReturn.Stats.Add(new ItemStat("Body", objVehicle.TotalBody.ToString(CultureInfo.CurrentCulture)));
            objReturn.Stats.Add(new ItemStat("Armor", objVehicle.TotalArmor.ToString(CultureInfo.CurrentCulture)));
            objReturn.Stats.Add(new ItemStat("Pilot", objVehicle.Pilot.ToString(CultureInfo.CurrentCulture)));
            objReturn.Stats.Add(new ItemStat("Sensor", objVehicle.CalculatedSensor.ToString(CultureInfo.CurrentCulture)));
            objReturn.Stats.Add(new ItemStat("Seats", objVehicle.TotalSeats.ToString(CultureInfo.CurrentCulture)));
            objReturn.Cost = Money(objCharacter, objVehicle.TotalCost);
            objReturn.Avail = objVehicle.DisplayTotalAvail;
            objReturn.Source = Src(objVehicle.Source, objVehicle.Page);
            objReturn.Notes = objVehicle.Notes ?? string.Empty;
            foreach (VehicleMod objMod in objVehicle.Mods.ToList())
            {
                objReturn.Contents.Add("◆ " + objMod.CurrentDisplayName);
                objReturn.Children.Add(FromVehicleMod(objCharacter, objMod));
            }
            foreach (Weapon objWeapon in objVehicle.Weapons.ToList())
            {
                objReturn.Contents.Add("⌖ " + objWeapon.CurrentDisplayName);
                objReturn.Children.Add(FromWeapon(objCharacter, objWeapon));
            }
            foreach (WeaponMount mount in objVehicle.WeaponMounts.ToList())
            {
                objReturn.Contents.Add("⌖ " + mount.CurrentDisplayName);
                objReturn.Children.Add(FromWeaponMount(objCharacter, mount));
            }
            foreach (Gear objGear in objVehicle.GearChildren.ToList())
            {
                objReturn.Contents.Add("◇ " + objGear.CurrentDisplayName);
                objReturn.Children.Add(FromGear(objCharacter, objGear, InventoryKind.Gear));
            }
            objReturn.Finish();
            return objReturn;
        }

        private static InventoryItemViewModel FromWeaponMount(Character character, WeaponMount mount)
        {
            var item = new InventoryItemViewModel(InventoryKind.Vehicle, mount.CurrentDisplayName, "Weapon mount", "Icon.Item.WeaponMount");
            item.Bind(character, mount, mount.InternalId);
            item.IsEquipped = mount.Equipped;
            item.Cost = Money(character, mount.TotalCost); item.Avail = mount.DisplayTotalAvail;
            item.Source = mount.SourceDetail.ToString(); item.Notes = mount.Notes ?? "";
            item.Stats.Add(new ItemStat("Slots", mount.CalculatedSlots.ToString(GlobalSettings.CultureInfo)));
            item.Stats.Add(new ItemStat("Weapon capacity", mount.WeaponCapacity.ToString(GlobalSettings.CultureInfo)));
            item.Stats.Add(new ItemStat("Allowed weapons", mount.AllowedWeaponCategories));
            foreach (var option in mount.WeaponMountOptions) item.Stats.Add(new ItemStat(option.Category, option.CurrentDisplayName));
            foreach (Weapon weapon in mount.Weapons) { item.Contents.Add("⌖ " + weapon.CurrentDisplayName); item.Children.Add(FromWeapon(character, weapon)); }
            foreach (VehicleMod mod in mount.Mods) { item.Contents.Add("◆ " + mod.CurrentDisplayName); item.Children.Add(FromVehicleMod(character, mod)); }
            item.Finish(); return item;
        }

        private static InventoryItemViewModel FromVehicleMod(Character objCharacter, VehicleMod objMod)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Vehicle, objMod.CurrentDisplayName, objMod.DisplayCategory(GlobalSettings.Language), "Icon.Settings");
            objReturn.Bind(objCharacter, objMod, objMod.InternalId);
            objReturn.CanEquip = false;
            objReturn.RatingBadge = Rating(objMod.Rating);
            if (objMod.Rating > 0)
                objReturn.Stats.Add(new ItemStat("Rating", objMod.Rating.ToString(CultureInfo.CurrentCulture)));
            if (objMod.IncludedInVehicle)
                objReturn.Stats.Add(new ItemStat("Included", "with vehicle"));
            objReturn.Cost = Money(objCharacter, objMod.TotalCost);
            objReturn.Avail = objMod.DisplayTotalAvail;
            objReturn.Source = objMod.SourceDetail.ToString();
            objReturn.Notes = objMod.Notes ?? string.Empty;
            foreach (Weapon objWeapon in objMod.Weapons.ToList())
                objReturn.Children.Add(FromWeapon(objCharacter, objWeapon));
            objReturn.Finish();
            return objReturn;
        }

        private static InventoryItemViewModel FromDrug(Character objCharacter, Drug objDrug)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Drug, objDrug.CurrentDisplayName, objDrug.Category, InventoryIcons.ForDrug(objDrug.Name));
            objReturn.Bind(objCharacter, objDrug, objDrug.InternalId);
            objReturn.Badge = "×" + objDrug.Quantity.ToString("0.##", GlobalSettings.CultureInfo);
            objReturn.QuantityBadge = objReturn.Badge;
            objReturn.HasQuantity = true;
            objReturn.IsConsumable = true;
            objReturn.Quantity = objDrug.Quantity;
            objReturn.Headline = objDrug.Category;
            objReturn.Description = objDrug.Description;
            objReturn.Stats.Add(new ItemStat("Doses", objDrug.Quantity.ToString("0.##", GlobalSettings.CultureInfo)));
            objReturn.Cost = Money(objCharacter, objDrug.TotalCost);
            objReturn.Notes = objDrug.Notes ?? string.Empty;
            objReturn.Finish();
            return objReturn;
        }

        private static InventoryItemViewModel FromLifestyle(Character objCharacter, Lifestyle objLifestyle)
        {
            InventoryItemViewModel objReturn = new InventoryItemViewModel(InventoryKind.Lifestyle, objLifestyle.CurrentDisplayName, objLifestyle.BaseLifestyle,
                                                                          InventoryIcons.ForLifestyle(objLifestyle.BaseLifestyle, objLifestyle.Name));
            objReturn.Bind(objCharacter, objLifestyle, objLifestyle.InternalId);
            objReturn.Badge = objLifestyle.Increments.ToString(CultureInfo.CurrentCulture) + "×";
            objReturn.QuantityBadge = objReturn.Badge;
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

        /// <summary>This item and every nested child, depth first.</summary>
        public IEnumerable<InventoryItemViewModel> SelfAndDescendants()
        {
            yield return this;
            foreach (InventoryItemViewModel objChild in Children)
            {
                foreach (InventoryItemViewModel objLoop in objChild.SelfAndDescendants())
                    yield return objLoop;
            }
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
        public IBrush AccentBrush => new Avalonia.Media.Immutable.ImmutableSolidColorBrush(InventoryItemViewModel.AccentFor(Kind));
        public string IconKey => KindIcon(Kind);

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

        public static string KindIcon(InventoryKind eKind)
        {
            switch (eKind)
            {
                case InventoryKind.Weapon: return "Icon.Item.HeavyPistol";
                case InventoryKind.Armor: return "Icon.Item.Vest";
                case InventoryKind.Cyberware: return "Icon.Item.Cyberarm";
                case InventoryKind.Gear: return "Icon.Item.Backpack";
                case InventoryKind.Vehicle: return "Icon.Item.Car";
                case InventoryKind.Drug: return "Icon.Item.Pills";
                default: return "Icon.Item.House";
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
            IconKey = eKind.HasValue ? InventoryGroupViewModel.KindIcon(eKind.Value) : strIconKey;
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
