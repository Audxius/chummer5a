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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using Chummer.Backend.Equipment;
using Chummer.Rules;

namespace Chummer.Selection
{
    /// <summary>Everything the store sells (one per legacy Select* form).</summary>
    public enum EquipmentKind
    {
        Gear,
        Weapon,
        Armor,
        Cyberware,
        Bioware,
        Vehicle,
        Drug,
        Lifestyle,
        WeaponAccessory,
        ArmorMod,
        GearPlugin,
        VehicleMod
    }

    /// <summary>A kind-specific list column (weapons: DV/AP/..., vehicles: handling/speed/...).</summary>
    public sealed class CatalogColumn
    {
        public CatalogColumn(string strHeader, double dblWidth = 64)
        {
            Header = strHeader;
            Width = dblWidth;
        }

        public string Header { get; }
        public double Width { get; }
    }

    /// <summary>One row of the store's result list.</summary>
    public sealed class CatalogRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string CategoryDisplay { get; set; } = string.Empty;
        public string Avail { get; set; } = string.Empty;
        public int AvailValue { get; set; }
        public char AvailSuffix { get; set; }

        /// <summary>Base cost at the lowest rating / quantity (before markup); Variable items give their minimum.</summary>
        public decimal Cost { get; set; }
        public string CostText { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;

        /// <summary>Values for <see cref="EquipmentCatalog.Columns"/>, same order.</summary>
        public string[] Stats { get; set; } = Array.Empty<string>();

        /// <summary>True when the price is above what the character can pay (career) / has left (create).</summary>
        public bool Unaffordable { get; set; }

        /// <summary>True when availability is above the campaign's limit.</summary>
        public bool OverAvailLimit { get; set; }
    }

    public sealed class CatalogQuery
    {
        public string Category { get; set; } = string.Empty;
        public string Search { get; set; } = string.Empty;
        public bool OnlyAffordable { get; set; }
        public bool HideOverAvailLimit { get; set; }
        public bool Free { get; set; }
        public decimal Markup { get; set; }
        public bool BlackMarket { get; set; }
        public string GradeId { get; set; } = string.Empty;
    }

    public sealed class CatalogListing
    {
        public List<CatalogRow> Rows { get; } = new List<CatalogRow>();

        /// <summary>Legacy "String_RestrictedItemsHidden": entries left out by the availability / affordability filters.</summary>
        public int HiddenCount { get; set; }
        public string HiddenText { get; set; } = string.Empty;
    }

    /// <summary>What can be chosen for one item: rating range, quantity steps, mounts, black market eligibility.</summary>
    public sealed class ItemOptions
    {
        public int MinRating { get; set; }
        public int MaxRating { get; set; }
        public bool HasRating => MaxRating > 0 && MaxRating != MinRating || MaxRating > 1;
        public string RatingLabel { get; set; } = string.Empty;
        public decimal QuantityMinimum { get; set; } = 1;
        public decimal QuantityStep { get; set; } = 1;
        public int QuantityDecimals { get; set; }
        public bool HasQuantity { get; set; }
        public bool CanBlackMarket { get; set; }
        public bool CanDoItYourself { get; set; }
        public List<string> Mounts { get; } = new List<string>();
        public List<string> ExtraMounts { get; } = new List<string>();
    }

    /// <summary>Price and full details for the highlighted item with the chosen options.</summary>
    public sealed class ItemQuote
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;

        /// <summary>What will be deducted (career) or added to the build total (create).</summary>
        public decimal Price { get; set; }
        public string PriceText { get; set; } = string.Empty;
        public bool IsVariable { get; set; }
        public string Avail { get; set; } = string.Empty;
        public int AvailValue { get; set; }
        public char AvailSuffix { get; set; }
        public string AvailTest { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public List<KeyValuePair<string, string>> Stats { get; } = new List<KeyValuePair<string, string>>();
        public decimal NuyenBefore { get; set; }
        public decimal NuyenAfter => NuyenBefore - Price;
        public bool CanAfford => Price <= NuyenBefore;
    }

    /// <summary>
    /// The store's catalogue: category lists, filtered listings and live price quotes, lifted from the legacy Select* forms
    /// (SelectGear.RefreshList / UpdateGearInfo, SelectWeapon.BuildWeaponList, SelectArmor, SelectCyberware.RefreshList,
    /// SelectVehicle.BuildVehicleList, SelectDrug, SelectLifestyle, SelectWeaponAccessory, SelectArmorMod, SelectVehicleMod).
    /// </summary>
    public sealed class EquipmentCatalog
    {
        private readonly Character _objCharacter;
        private readonly XPathNavigator _xmlRoot;
        private readonly string _strItemXPath;
        private readonly HashSet<string> _setBlackMarketMaps = new HashSet<string>();
        private readonly HashSet<string> _setAllowedCategories = new HashSet<string>();
        private readonly string _strParentCategoryFilter = string.Empty;
        private Dictionary<string, string> _dicCategoryNames;

        public EquipmentCatalog(Character objCharacter, EquipmentKind eKind, object objParent = null)
        {
            _objCharacter = objCharacter ?? throw new ArgumentNullException(nameof(objCharacter));
            Kind = eKind;
            Parent = objParent;
            switch (eKind)
            {
                case EquipmentKind.Gear:
                case EquipmentKind.GearPlugin:
                    DataFile = "gear.xml";
                    _strItemXPath = "gears/gear";
                    break;
                case EquipmentKind.Weapon:
                    DataFile = "weapons.xml";
                    _strItemXPath = "weapons/weapon";
                    break;
                case EquipmentKind.WeaponAccessory:
                    DataFile = "weapons.xml";
                    _strItemXPath = "accessories/accessory";
                    break;
                case EquipmentKind.Armor:
                    DataFile = "armor.xml";
                    _strItemXPath = "armors/armor";
                    break;
                case EquipmentKind.ArmorMod:
                    DataFile = "armor.xml";
                    _strItemXPath = "mods/mod";
                    break;
                case EquipmentKind.Cyberware:
                    DataFile = "cyberware.xml";
                    _strItemXPath = "cyberwares/cyberware";
                    break;
                case EquipmentKind.Bioware:
                    DataFile = "bioware.xml";
                    _strItemXPath = "biowares/bioware";
                    break;
                case EquipmentKind.Vehicle:
                    DataFile = "vehicles.xml";
                    _strItemXPath = "vehicles/vehicle";
                    break;
                case EquipmentKind.VehicleMod:
                    DataFile = "vehicles.xml";
                    _strItemXPath = "mods/mod";
                    break;
                case EquipmentKind.Drug:
                    DataFile = DrugsData.CatalogFileName;
                    _strItemXPath = "drugs/drug";
                    break;
                case EquipmentKind.Lifestyle:
                    DataFile = "lifestyles.xml";
                    _strItemXPath = "lifestyles/lifestyle";
                    break;
            }
            _xmlRoot = objCharacter.LoadDataXPath(DataFile).SelectSingleNode("/chummer");
            XPathNavigator xmlCategories = eKind == EquipmentKind.VehicleMod
                ? _xmlRoot?.SelectSingleNode("modcategories")
                : _xmlRoot;
            if (xmlCategories != null)
            {
                foreach (string strCategory in objCharacter.GenerateBlackMarketMappings(xmlCategories))
                    _setBlackMarketMaps.Add(strCategory);
            }
            CanBlackMarket = objCharacter.BlackMarketDiscount;
            IsCareer = objCharacter.Created;

            // Mods / plugins only list what fits the owner (legacy tsAddArmorMod_Click, PickGear addoncategory).
            if (eKind == EquipmentKind.ArmorMod && objParent is Armor objArmor)
            {
                XPathNavigator xmlArmor = objArmor.GetNodeXPath();
                string strForce = xmlArmor?.SelectSingleNode("forcemodcategory")?.Value;
                if (!string.IsNullOrEmpty(strForce))
                    AddAllowed(strForce);
                else
                {
                    AddAllowed(objArmor.Category + "," + objArmor.Name);
                    AddAllowed(xmlArmor?.SelectSingleNode("addmodcategory")?.Value);
                    _setAllowedCategories.Add("General");
                }
            }
            else if (eKind == EquipmentKind.GearPlugin && objParent is Gear objGear)
            {
                XPathNavigator xmlGear = objGear.GetNodeXPath();
                if (xmlGear != null)
                {
                    foreach (XPathNavigator xmlCategory in xmlGear.Select("addoncategory"))
                        _setAllowedCategories.Add(xmlCategory.Value);
                }
                if (_setAllowedCategories.Count > 0)
                    _strParentCategoryFilter = " and (contains(capacity, \"[\") or category = \"Custom\")";
            }
        }

        private void AddAllowed(string strList)
        {
            if (string.IsNullOrEmpty(strList))
                return;
            foreach (string strItem in strList.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!string.IsNullOrWhiteSpace(strItem))
                    _setAllowedCategories.Add(strItem.Trim());
            }
        }

        public EquipmentKind Kind { get; }
        public object Parent { get; }
        public string DataFile { get; }
        public bool IsCareer { get; }

        /// <summary>Legacy chkBlackMarketDiscount.Visible: only when the character has a Black Market Pipeline style quality.</summary>
        public bool CanBlackMarket { get; }

        public bool IsWare => Kind == EquipmentKind.Cyberware || Kind == EquipmentKind.Bioware;
        public bool HasGrades => IsWare || Kind == EquipmentKind.Drug;

        public IReadOnlyList<CatalogColumn> Columns
        {
            get
            {
                switch (Kind)
                {
                    case EquipmentKind.Weapon:
                        return new[] { new CatalogColumn("ACC", 50), new CatalogColumn("DV", 70), new CatalogColumn("AP", 44), new CatalogColumn("MODE", 70), new CatalogColumn("RC", 40), new CatalogColumn("AMMO", 64) };
                    case EquipmentKind.Armor:
                        return new[] { new CatalogColumn("ARMOR", 56), new CatalogColumn("CAP", 48), new CatalogColumn("RATING", 56) };
                    case EquipmentKind.ArmorMod:
                        return new[] { new CatalogColumn("ARMOR", 56), new CatalogColumn("CAP", 56), new CatalogColumn("RATING", 56) };
                    case EquipmentKind.Gear:
                    case EquipmentKind.GearPlugin:
                        return new[] { new CatalogColumn("RATING", 56), new CatalogColumn("CAP", 56) };
                    case EquipmentKind.Cyberware:
                    case EquipmentKind.Bioware:
                        return new[] { new CatalogColumn("ESS", 56), new CatalogColumn("CAP", 64), new CatalogColumn("RATING", 56) };
                    case EquipmentKind.Vehicle:
                        return new[] { new CatalogColumn("HAND", 52), new CatalogColumn("SPD", 44), new CatalogColumn("ACC", 44), new CatalogColumn("BOD", 40), new CatalogColumn("ARM", 40), new CatalogColumn("PIL", 36), new CatalogColumn("SENS", 44), new CatalogColumn("SEAT", 40) };
                    case EquipmentKind.VehicleMod:
                        return new[] { new CatalogColumn("SLOTS", 52), new CatalogColumn("RATING", 56) };
                    case EquipmentKind.WeaponAccessory:
                        return new[] { new CatalogColumn("MOUNT", 110), new CatalogColumn("RATING", 56) };
                    case EquipmentKind.Drug:
                        return new[] { new CatalogColumn("SPEED", 56), new CatalogColumn("RATING", 56) };
                    case EquipmentKind.Lifestyle:
                        return new[] { new CatalogColumn("DICE", 56) };
                    default:
                        return Array.Empty<CatalogColumn>();
                }
            }
        }

        public XPathNavigator Node(string strId) => _xmlRoot?.TryGetNodeByNameOrId(_strItemXPath, strId);

        #region Categories

        /// <summary>Category list ("Show All" first), only categories with something to show (legacy AnyItemInList).</summary>
        public List<ListItem> GetCategories()
        {
            List<ListItem> lstReturn = new List<ListItem>();
            _dicCategoryNames = new Dictionary<string, string>();
            if (_xmlRoot == null)
                return lstReturn;
            string strCategoryXPath;
            switch (Kind)
            {
                case EquipmentKind.WeaponAccessory:
                case EquipmentKind.Lifestyle:
                    strCategoryXPath = null;
                    break;
                case EquipmentKind.ArmorMod:
                    strCategoryXPath = "modcategories/category";
                    break;
                case EquipmentKind.VehicleMod:
                    strCategoryXPath = "modcategories/category";
                    break;
                default:
                    strCategoryXPath = "categories/category";
                    break;
            }
            HashSet<string> setUsed = new HashSet<string>();
            foreach (XPathNavigator xmlItem in _xmlRoot.Select(_strItemXPath + "[" + BaseFilter(string.Empty, string.Empty) + "]"))
            {
                string strCategory = xmlItem.SelectSingleNode("category")?.Value;
                if (!string.IsNullOrEmpty(strCategory) && !setUsed.Contains(strCategory) && IsListable(xmlItem))
                    setUsed.Add(strCategory);
            }
            if (strCategoryXPath != null)
            {
                foreach (XPathNavigator xmlCategory in _xmlRoot.Select(strCategoryXPath))
                {
                    string strCategory = xmlCategory.Value;
                    if (string.IsNullOrEmpty(strCategory) || !setUsed.Contains(strCategory) || _dicCategoryNames.ContainsKey(strCategory))
                        continue;
                    // Gear categories marked show="False" are only offered when explicitly allowed.
                    if (!_setAllowedCategories.Contains(strCategory) && xmlCategory.SelectSingleNode("@show")?.Value == bool.FalseString)
                        continue;
                    string strDisplay = xmlCategory.SelectSingleNode("@translate")?.Value ?? strCategory;
                    _dicCategoryNames[strCategory] = strDisplay;
                    lstReturn.Add(new ListItem(strCategory, strDisplay));
                }
            }
            foreach (string strCategory in setUsed)
            {
                if (_dicCategoryNames.ContainsKey(strCategory) || strCategoryXPath != null && Kind != EquipmentKind.ArmorMod && Kind != EquipmentKind.VehicleMod)
                    continue;
                _dicCategoryNames[strCategory] = strCategory;
                lstReturn.Add(new ListItem(strCategory, strCategory));
            }
            lstReturn.Sort(CompareListItems.CompareNames);
            if (lstReturn.Count > 1)
                lstReturn.Insert(0, new ListItem("Show All", LanguageManager.GetString("String_ShowAll")));
            return lstReturn;
        }

        private string CategoryName(string strCategory)
        {
            if (string.IsNullOrEmpty(strCategory))
                return string.Empty;
            if (_dicCategoryNames == null)
                GetCategories();
            return _dicCategoryNames != null && _dicCategoryNames.TryGetValue(strCategory, out string strReturn) ? strReturn : strCategory;
        }

        #endregion

        #region Listing

        /// <summary>XPath predicate: books, category, search and the kind's structural filters.</summary>
        private string BaseFilter(string strCategory, string strSearch)
        {
            StringBuilder sbdFilter = new StringBuilder(_objCharacter.Settings.BookXPath());
            if (sbdFilter.Length == 0)
                sbdFilter.Append("true()");
            if (!string.IsNullOrEmpty(strCategory) && strCategory != "Show All" && (GlobalSettings.SearchInCategoryOnly || string.IsNullOrEmpty(strSearch)))
            {
                sbdFilter.Append(" and (category = ").Append(strCategory.CleanXPath());
                if (IsWare)
                    sbdFilter.Append(" or category = \"None\"");
                sbdFilter.Append(')');
            }
            else if (_setAllowedCategories.Count > 0)
            {
                sbdFilter.Append(" and (").Append(string.Join(" or ", _setAllowedCategories.Select(x => "category = " + x.CleanXPath()))).Append(')');
            }
            if (!string.IsNullOrEmpty(strSearch))
                sbdFilter.Append(" and ").Append(CommonFunctions.GenerateSearchXPath(strSearch));
            switch (Kind)
            {
                case EquipmentKind.Gear:
                    sbdFilter.Append(" and not(requireparent)");
                    break;
                case EquipmentKind.GearPlugin:
                    sbdFilter.Append(_strParentCategoryFilter);
                    break;
                case EquipmentKind.Weapon:
                    // Legacy SelectWeapon: no cyberweapons, nothing that needs a mount (underbarrels) at top level.
                    sbdFilter.Append(" and not(cyberware = \"True\") and not(mount) and not(extramount)");
                    break;
                case EquipmentKind.Cyberware:
                case EquipmentKind.Bioware:
                    if (Parent is Cyberware)
                        sbdFilter.Append(" and (requireparent or contains(capacity, \"[\")) and not(mountsto)");
                    else if (Kind == EquipmentKind.Cyberware && !_objCharacter.AddCyberwareEnabled
                             || Kind == EquipmentKind.Bioware && !_objCharacter.AddBiowareEnabled)
                        sbdFilter.Append(" and (id = ").Append(Cyberware.EssenceHoleGuidString.CleanXPath()).Append(" or id = ")
                                 .Append(Cyberware.EssenceAntiHoleGuidString.CleanXPath()).Append(" or mountsto)");
                    else
                        sbdFilter.Append(" and not(requireparent)");
                    break;
                case EquipmentKind.Lifestyle:
                    // Legacy SelectLifestyle for standard lifestyles.
                    sbdFilter.Append(" and (source = \"SR5\" or category = \"Contracts\") and name != \"ID ERROR. Re-add life style to fix\"");
                    break;
            }
            return sbdFilter.ToString();
        }

        /// <summary>Per-entry checks that cannot be done in XPath (requirements, parent details, mounts).</summary>
        private bool IsListable(XPathNavigator xmlItem)
        {
            switch (Kind)
            {
                case EquipmentKind.WeaponAccessory:
                    return Parent is Weapon objWeapon && objWeapon.CheckAccessoryRequirements(xmlItem)
                           && xmlItem.RequirementsMet(_objCharacter, objWeapon);
                case EquipmentKind.GearPlugin:
                {
                    XPathNavigator xmlParent = (Parent as IHasXmlDataNode)?.GetNodeXPath();
                    XPathNavigator xmlTest = xmlItem.SelectSingleNode("forbidden/parentdetails");
                    if (xmlTest != null && xmlParent.ProcessFilterOperationNode(xmlTest, false))
                        return false;
                    xmlTest = xmlItem.SelectSingleNode("required/parentdetails");
                    if (xmlTest != null && !xmlParent.ProcessFilterOperationNode(xmlTest, false))
                        return false;
                    return xmlItem.RequirementsMet(_objCharacter, Parent);
                }
                case EquipmentKind.Gear:
                    if (xmlItem.SelectSingleNode("required/parentdetails") != null)
                        return false;
                    return xmlItem.RequirementsMet(_objCharacter);
                case EquipmentKind.Cyberware:
                case EquipmentKind.Bioware:
                {
                    if (xmlItem.SelectSingleNode("mountsto") == null)
                    {
                        if (!_objCharacter.AddCyberwareEnabled && xmlItem.SelectSingleNode("subsystems/cyberware") != null)
                            return false;
                        if (!_objCharacter.AddBiowareEnabled && xmlItem.SelectSingleNode("subsystems/bioware") != null)
                            return false;
                    }
                    if (Parent is Cyberware objParent)
                    {
                        XPathNavigator xmlParent = objParent.GetNodeXPath();
                        XPathNavigator xmlTest = xmlItem.SelectSingleNode("forbidden/parentdetails");
                        if (xmlTest != null && xmlParent.ProcessFilterOperationNode(xmlTest, false))
                            return false;
                        xmlTest = xmlItem.SelectSingleNode("required/parentdetails");
                        if (xmlTest != null && !xmlParent.ProcessFilterOperationNode(xmlTest, false))
                            return false;
                    }
                    (int intMin, int intMax) = WareRatingRange(xmlItem);
                    if (intMax < intMin)
                        return false;
                    // Ex-Cons cannot have forbidden or restricted 'ware
                    if (_objCharacter.ExCon && xmlItem.SelectSingleNode("mountsto") == null)
                    {
                        string strAvail = (xmlItem.SelectSingleNode("avail")?.Value ?? string.Empty).ProcessFixedValuesString(intMin);
                        if (strAvail.EndsWith("F", StringComparison.Ordinal) || strAvail.EndsWith("R", StringComparison.Ordinal))
                            return false;
                    }
                    return xmlItem.RequirementsMet(_objCharacter, Parent as Cyberware);
                }
                case EquipmentKind.ArmorMod:
                    return xmlItem.RequirementsMet(_objCharacter, Parent);
                case EquipmentKind.VehicleMod:
                    return xmlItem.SelectSingleNode("hide") == null && xmlItem.RequirementsMet(_objCharacter, Parent);
                default:
                    return xmlItem.RequirementsMet(_objCharacter);
            }
        }

        private (int, int) WareRatingRange(XPathNavigator xmlItem)
        {
            int intMin = 1;
            int intMax = 1;
            string strMin = xmlItem.SelectSingleNode("minrating")?.Value ?? string.Empty;
            if (!string.IsNullOrEmpty(strMin) && !int.TryParse(strMin, out intMin))
                intMin = EvaluateInt(strMin, 1, 1);
            string strMax = xmlItem.SelectSingleNode("rating")?.Value ?? string.Empty;
            if (!string.IsNullOrEmpty(strMax) && !int.TryParse(strMax, out intMax))
                intMax = EvaluateInt(strMax, intMin, 1);
            if (string.IsNullOrEmpty(strMax))
                intMax = 0;
            return (intMin, intMax);
        }

        private int EvaluateInt(string strExpression, int intRating, int intDefault)
        {
            decimal? decValue = Evaluate(strExpression, intRating);
            return decValue.HasValue ? decValue.Value.StandardRound() : intDefault;
        }

        /// <summary>Numbers in data files: FixedValues(), {Rating}, character attributes, simple arithmetic.</summary>
        private decimal? Evaluate(string strExpression, int intRating)
        {
            if (string.IsNullOrEmpty(strExpression))
                return null;
            strExpression = strExpression.ProcessFixedValuesString(intRating);
            if (!strExpression.DoesNeedXPathProcessingToBeConvertedToNumber(out decimal decValue))
                return decValue;
            string strRating = intRating.ToString(GlobalSettings.InvariantCultureInfo);
            strExpression = strExpression.Replace("{Rating}", strRating).Replace("{Parent Rating}", "0").Replace("Parent Rating", "0").Replace("Rating", strRating);
            StringBuilder sbdValue = new StringBuilder(strExpression);
            Vehicle.FillAttributesInXPathWithDummies(sbdValue);
            strExpression = _objCharacter.ProcessAttributesInXPath(sbdValue.ToString());
            (bool blnSuccess, object objResult) = CommonFunctions.EvaluateInvariantXPath(strExpression);
            if (blnSuccess && objResult is double dblResult)
                return Convert.ToDecimal(dblResult);
            return null;
        }

        private static XPathNavigator RatedNode(XPathNavigator xmlItem, string strName, int intRating)
        {
            XPathNavigator xmlNode = xmlItem.SelectSingleNode(strName);
            if (xmlNode != null)
                return xmlNode;
            int intBest = int.MaxValue;
            int intHighest = 0;
            XPathNavigator xmlHighest = null;
            foreach (XPathNavigator xmlLoop in xmlItem.SelectChildren(XPathNodeType.Element))
            {
                if (!xmlLoop.Name.StartsWith(strName, StringComparison.Ordinal)
                    || !int.TryParse(xmlLoop.Name.Substring(strName.Length), out int intLoop))
                    continue;
                if (intLoop >= intRating && intLoop < intBest)
                {
                    intBest = intLoop;
                    xmlNode = xmlLoop;
                }
                if (intLoop >= intHighest)
                {
                    intHighest = intLoop;
                    xmlHighest = xmlLoop;
                }
            }
            return xmlNode ?? xmlHighest;
        }

        /// <summary>Base cost from data (legacy SelectionShared.CheckNuyenRestriction's reading of the cost node).</summary>
        private (decimal Cost, bool Variable, decimal Max) DataCost(XPathNavigator xmlItem, int intRating)
        {
            string strCost = RatedNode(xmlItem, "cost", intRating)?.Value;
            if (string.IsNullOrEmpty(strCost))
                return (0, false, 0);
            strCost = strCost.ProcessFixedValuesString(intRating);
            if (strCost.StartsWith("Variable", StringComparison.Ordinal))
            {
                strCost = strCost.TrimStartOnce("Variable(", true).TrimEndOnce(')');
                int intHyphen = strCost.IndexOf('-');
                decimal decMax = decimal.MaxValue;
                if (intHyphen != -1)
                {
                    decimal.TryParse(strCost.Substring(intHyphen + 1), NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out decMax);
                    strCost = strCost.Substring(0, intHyphen);
                }
                decimal.TryParse(strCost.FastEscape('+'), NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out decimal decMin);
                return (decMin, true, decMax);
            }
            return (Evaluate(strCost, intRating) ?? 0, false, 0);
        }

        private int AvailBonus(XPathNavigator xmlItem)
            => ImprovementManager.ValueOf(_objCharacter, Improvement.ImprovementType.Availability,
                                          strImprovedName: xmlItem.SelectSingleNode("id")?.Value, blnIncludeNonImproved: true).StandardRound();

        private int MinRating(XPathNavigator xmlItem)
        {
            if (IsWare)
                return Math.Max(1, WareRatingRange(xmlItem).Item1);
            string strMin = xmlItem.SelectSingleNode("minrating")?.Value;
            int intMin = string.IsNullOrEmpty(strMin) ? 1 : EvaluateInt(strMin, 1, 1);
            return Math.Max(1, intMin);
        }

        private bool IsBlackMarketCategory(string strCategory)
        {
            if (Kind == EquipmentKind.WeaponAccessory)
                return Parent is Weapon objWeapon && _setBlackMarketMaps.Contains(objWeapon.Category);
            return _setBlackMarketMaps.Contains(strCategory);
        }

        /// <summary>Whether the Black Market Pipeline discount may be ticked for this item (legacy chkBlackMarketDiscount.Enabled).</summary>
        public bool BlackMarketAllowedFor(string strId)
        {
            if (!CanBlackMarket)
                return false;
            XPathNavigator xmlItem = Node(strId);
            return xmlItem != null && IsBlackMarketCategory(xmlItem.SelectSingleNode("category")?.Value);
        }

        public CatalogListing GetListing(CatalogQuery objQuery, CancellationToken token = default)
        {
            CatalogListing objReturn = new CatalogListing();
            if (_xmlRoot == null)
                return objReturn;
            if (objQuery == null)
                objQuery = new CatalogQuery();
            if (_dicCategoryNames == null)
                GetCategories();
            string strFilter = BaseFilter(objQuery.Category, objQuery.Search);
            Grade objGrade = null;
            if (IsWare && !string.IsNullOrEmpty(objQuery.GradeId))
            {
                objGrade = _objCharacter.GetGradesList(PurchaseRules.WareSource(Kind)).Find(x => string.Equals(x.SourceIDString, objQuery.GradeId, StringComparison.OrdinalIgnoreCase));
                if (objGrade != null)
                {
                    string strGrade = objGrade.Name.CleanXPath();
                    strFilter += " and (not(forcegrade) or forcegrade = \"None\" or forcegrade = " + strGrade + ") and (not(bannedgrades[grade = " + strGrade + "]))";
                }
            }
            decimal decNuyen = _objCharacter.GetAvailableNuyen();
            decimal decBaseMultiplier = 1 + (IsCareer ? objQuery.Markup : 0) / 100.0m;
            if (objGrade != null && IsWare)
                decBaseMultiplier *= objGrade.Cost;
            int intMaxAvail = _objCharacter.Settings.MaximumAvailability;
            string strNuyen = LanguageManager.GetString("String_NuyenSymbol");
            string strFormat = _objCharacter.Settings.NuyenFormat;
            XPathNodeIterator objIterator;
            try
            {
                objIterator = _xmlRoot.Select(_strItemXPath + "[" + strFilter + "]");
            }
            catch (XPathException)
            {
                return objReturn;
            }
            foreach (XPathNavigator xmlItem in objIterator)
            {
                token.ThrowIfCancellationRequested();
                string strId = xmlItem.SelectSingleNode("id")?.Value;
                if (string.IsNullOrEmpty(strId) || !IsListable(xmlItem))
                    continue;
                int intRating = MinRating(xmlItem);
                string strCategory = xmlItem.SelectSingleNode("category")?.Value ?? string.Empty;
                decimal decMultiplier = decBaseMultiplier;
                if (IsBlackMarketCategory(strCategory) && (objQuery.BlackMarket || GlobalSettings.AssumeBlackMarket) && CanBlackMarket)
                    decMultiplier *= 0.9m;
                int intAvailBonus = AvailBonus(xmlItem);
                if (objQuery.HideOverAvailLimit && !xmlItem.CheckAvailRestriction(_objCharacter, intRating, intAvailBonus))
                {
                    ++objReturn.HiddenCount;
                    continue;
                }
                (decimal decCost, bool blnVariable, decimal decMax) = DataCost(xmlItem, intRating);
                if (Kind == EquipmentKind.Lifestyle)
                    decCost = DataCost(xmlItem, 0).Cost;
                decCost *= decMultiplier;
                bool blnUnaffordable = !objQuery.Free && decCost > decNuyen;
                if (objQuery.OnlyAffordable && blnUnaffordable)
                {
                    ++objReturn.HiddenCount;
                    continue;
                }
                CatalogRow objRow = new CatalogRow
                {
                    Id = strId,
                    Name = xmlItem.SelectSingleNode("translate")?.Value ?? xmlItem.SelectSingleNode("name")?.Value ?? strId,
                    Category = strCategory,
                    CategoryDisplay = CategoryName(strCategory),
                    Cost = objQuery.Free ? 0 : decCost,
                    Unaffordable = blnUnaffordable,
                    Source = (xmlItem.SelectSingleNode("source")?.Value + " " + (xmlItem.SelectSingleNode("altpage")?.Value ?? xmlItem.SelectSingleNode("page")?.Value)).Trim(),
                    Stats = RowStats(xmlItem, intRating),
                };
                if (objQuery.Free)
                    objRow.CostText = 0.0m.ToString(strFormat, GlobalSettings.CultureInfo) + strNuyen;
                else if (blnVariable)
                    objRow.CostText = decCost.ToString(strFormat, GlobalSettings.CultureInfo) + strNuyen
                                      + (decMax == decimal.MaxValue ? "+" : "–" + (decMax * decMultiplier).ToString(strFormat, GlobalSettings.CultureInfo) + strNuyen);
                else
                    objRow.CostText = decCost.ToString(strFormat, GlobalSettings.CultureInfo) + strNuyen + (Kind == EquipmentKind.Lifestyle ? "/mo" : string.Empty);
                if (Kind != EquipmentKind.Lifestyle)
                {
                    string strAvail = RatedNode(xmlItem, "avail", intRating)?.Value ?? string.Empty;
                    AvailabilityValue objAvail = new AvailabilityValue(intRating, strAvail, intAvailBonus);
                    objRow.Avail = objAvail.ToString();
                    objRow.AvailValue = objAvail.Value;
                    objRow.AvailSuffix = objAvail.Suffix;
                    objRow.OverAvailLimit = objAvail.Value > intMaxAvail;
                }
                objReturn.Rows.Add(objRow);
            }
            // Same names in different categories get the category appended (legacy duplicate-name handling).
            foreach (IGrouping<string, CatalogRow> objGroup in objReturn.Rows.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            {
                if (objGroup.Select(x => x.Category).Distinct().Count() < 2)
                    continue;
                foreach (CatalogRow objRow in objGroup)
                    objRow.Name += " [" + objRow.CategoryDisplay + "]";
            }
            objReturn.Rows.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));
            if (objReturn.HiddenCount > 0)
                objReturn.HiddenText = string.Format(GlobalSettings.CultureInfo, LanguageManager.GetString("String_RestrictedItemsHidden"), objReturn.HiddenCount);
            return objReturn;
        }

        private static string Pretty(string strValue)
        {
            if (string.IsNullOrEmpty(strValue))
                return "–";
            return strValue.Replace("{", string.Empty).Replace("}", string.Empty);
        }

        private string RowStats(XPathNavigator xmlItem, int intRating, string strName)
            => Pretty(xmlItem.SelectSingleNode(strName)?.Value);

        private string RatingRange(XPathNavigator xmlItem, string strMaxElement = "rating")
        {
            if (IsWare)
            {
                (int intMin, int intMax) = WareRatingRange(xmlItem);
                if (intMax <= 0)
                    return "–";
                return intMin >= intMax ? intMax.ToString(GlobalSettings.CultureInfo) : intMin.ToString(GlobalSettings.CultureInfo) + "–" + intMax.ToString(GlobalSettings.CultureInfo);
            }
            string strMax = xmlItem.SelectSingleNode(strMaxElement)?.Value;
            if (string.IsNullOrEmpty(strMax) || strMax == "0")
                return "–";
            int intMaxRating = EvaluateInt(strMax, 1, 0);
            if (intMaxRating <= 0)
                return "–";
            int intMinRating = MinRating(xmlItem);
            return intMinRating >= intMaxRating ? intMaxRating.ToString(GlobalSettings.CultureInfo) : intMinRating.ToString(GlobalSettings.CultureInfo) + "–" + intMaxRating.ToString(GlobalSettings.CultureInfo);
        }

        private string Capacity(XPathNavigator xmlItem, string strElement, int intRating)
        {
            string strCapacity = xmlItem.SelectSingleNode(strElement)?.Value;
            if (string.IsNullOrEmpty(strCapacity))
                return "–";
            if (strCapacity == "[*]")
                return "*";
            int intSlash = strCapacity.IndexOf("/[", StringComparison.Ordinal);
            string strSuffix = string.Empty;
            if (intSlash != -1)
            {
                strSuffix = strCapacity.Substring(intSlash);
                strCapacity = strCapacity.Substring(0, intSlash);
            }
            bool blnBrackets = strCapacity.StartsWith("[", StringComparison.Ordinal) && strCapacity.Length > 2;
            if (blnBrackets)
                strCapacity = strCapacity.Substring(1, strCapacity.Length - 2);
            decimal? decValue = Evaluate(strCapacity, intRating);
            string strValue = decValue.HasValue ? decValue.Value.ToString("#,0.##", GlobalSettings.CultureInfo) : strCapacity;
            return (blnBrackets ? "[" + strValue + "]" : strValue) + strSuffix;
        }

        private string[] RowStats(XPathNavigator xmlItem, int intRating)
        {
            switch (Kind)
            {
                case EquipmentKind.Weapon:
                    return new[]
                    {
                        RowStats(xmlItem, intRating, "accuracy"), RowStats(xmlItem, intRating, "damage"), RowStats(xmlItem, intRating, "ap"),
                        RowStats(xmlItem, intRating, "mode"), RowStats(xmlItem, intRating, "rc"), RowStats(xmlItem, intRating, "ammo")
                    };
                case EquipmentKind.Armor:
                    return new[] { RowStats(xmlItem, intRating, "armor"), Capacity(xmlItem, "armorcapacity", intRating), RatingRange(xmlItem) };
                case EquipmentKind.ArmorMod:
                    return new[] { RowStats(xmlItem, intRating, "armor"), Capacity(xmlItem, "armorcapacity", intRating), RatingRange(xmlItem, "maxrating") };
                case EquipmentKind.Gear:
                case EquipmentKind.GearPlugin:
                    return new[] { RatingRange(xmlItem), Capacity(xmlItem, "capacity", intRating) };
                case EquipmentKind.Cyberware:
                case EquipmentKind.Bioware:
                {
                    string strEss = xmlItem.SelectSingleNode("ess")?.Value;
                    decimal? decEss = Evaluate(strEss, intRating);
                    return new[]
                    {
                        decEss.HasValue ? decEss.Value.ToString("0.###", GlobalSettings.CultureInfo) : Pretty(strEss),
                        Capacity(xmlItem, "capacity", intRating), RatingRange(xmlItem)
                    };
                }
                case EquipmentKind.Vehicle:
                {
                    string strHandling = xmlItem.SelectSingleNode("handling")?.Value ?? string.Empty;
                    string strOffroad = xmlItem.SelectSingleNode("offroadhandling")?.Value;
                    if (!string.IsNullOrEmpty(strOffroad) && strOffroad != strHandling)
                        strHandling += "/" + strOffroad;
                    string strSpeed = xmlItem.SelectSingleNode("speed")?.Value ?? string.Empty;
                    string strOffSpeed = xmlItem.SelectSingleNode("offroadspeed")?.Value;
                    if (!string.IsNullOrEmpty(strOffSpeed) && strOffSpeed != strSpeed)
                        strSpeed += "/" + strOffSpeed;
                    string strAccel = xmlItem.SelectSingleNode("accel")?.Value ?? string.Empty;
                    string strOffAccel = xmlItem.SelectSingleNode("offroadaccel")?.Value;
                    if (!string.IsNullOrEmpty(strOffAccel) && strOffAccel != strAccel)
                        strAccel += "/" + strOffAccel;
                    return new[]
                    {
                        Pretty(strHandling), Pretty(strSpeed), Pretty(strAccel), RowStats(xmlItem, intRating, "body"), RowStats(xmlItem, intRating, "armor"),
                        RowStats(xmlItem, intRating, "pilot"), RowStats(xmlItem, intRating, "sensor"), RowStats(xmlItem, intRating, "seats")
                    };
                }
                case EquipmentKind.VehicleMod:
                    return new[] { RowStats(xmlItem, intRating, "slots"), RatingRange(xmlItem) };
                case EquipmentKind.WeaponAccessory:
                    return new[] { Pretty((xmlItem.SelectSingleNode("mount")?.Value ?? string.Empty).Replace("/", " / ")), RatingRange(xmlItem) };
                case EquipmentKind.Drug:
                    return new[] { RowStats(xmlItem, intRating, "speed"), RatingRange(xmlItem) };
                case EquipmentKind.Lifestyle:
                    return new[] { RowStats(xmlItem, intRating, "dice") };
                default:
                    return Array.Empty<string>();
            }
        }

        #endregion

        #region Options and quotes

        public ItemOptions GetOptions(string strId)
        {
            ItemOptions objReturn = new ItemOptions();
            XPathNavigator xmlItem = Node(strId);
            if (xmlItem == null)
                return objReturn;
            objReturn.CanBlackMarket = BlackMarketAllowedFor(strId);
            objReturn.RatingLabel = LanguageManager.GetString("Label_Rating").TrimEnd(':');
            string strRatingLabel = xmlItem.SelectSingleNode("ratinglabel")?.Value;
            if (!string.IsNullOrEmpty(strRatingLabel))
                objReturn.RatingLabel = LanguageManager.GetString(strRatingLabel, false) is string strLabel && !string.IsNullOrEmpty(strLabel) ? strLabel : strRatingLabel;
            if (IsWare)
            {
                (int intMin, int intMax) = WareRatingRange(xmlItem);
                objReturn.MinRating = Math.Max(intMax > 0 ? 1 : 0, intMin);
                objReturn.MaxRating = Math.Max(objReturn.MinRating, intMax);
            }
            else
            {
                string strMaxElement = Kind == EquipmentKind.ArmorMod || Kind == EquipmentKind.WeaponAccessory && xmlItem.SelectSingleNode("rating") == null ? "maxrating" : "rating";
                string strMax = xmlItem.SelectSingleNode(strMaxElement)?.Value;
                int intMax = string.IsNullOrEmpty(strMax) || strMax == "0" ? 0 : EvaluateInt(strMax, 1, 0);
                if (intMax > 0)
                {
                    objReturn.MinRating = Math.Min(intMax, MinRating(xmlItem));
                    objReturn.MaxRating = intMax;
                }
            }
            if (Kind == EquipmentKind.Gear || Kind == EquipmentKind.GearPlugin)
            {
                objReturn.HasQuantity = true;
                string strCostFor = xmlItem.SelectSingleNode("costfor")?.Value;
                if (!string.IsNullOrEmpty(strCostFor) && decimal.TryParse(strCostFor, NumberStyles.Any, GlobalSettings.InvariantCultureInfo, out decimal decCostFor) && decCostFor > 0)
                {
                    objReturn.QuantityMinimum = decCostFor;
                    objReturn.QuantityStep = decCostFor;
                }
                string strName = xmlItem.SelectSingleNode("name")?.Value ?? string.Empty;
                if (strName.StartsWith("Nuyen", StringComparison.Ordinal))
                {
                    objReturn.QuantityDecimals = Math.Max(0, _objCharacter.Settings.MaxNuyenDecimals);
                    decimal decMinimum = 1.0m;
                    for (int i = 0; i < objReturn.QuantityDecimals; ++i)
                        decMinimum /= 10.0m;
                    objReturn.QuantityMinimum = decMinimum;
                }
                else if (xmlItem.SelectSingleNode("category")?.Value == "Currency")
                {
                    objReturn.QuantityDecimals = 2;
                    objReturn.QuantityMinimum = 0.01m;
                }
                objReturn.CanDoItYourself = xmlItem.SelectSingleNode("category")?.Value == "Commlink Upgrade";
            }
            if (Kind == EquipmentKind.WeaponAccessory && Parent is Weapon objWeapon)
            {
                List<string> lstAllowed = objWeapon.GetAccessoryMounts().ToList();
                void Fill(List<string> lstTarget, string strData)
                {
                    List<string> lstData = (strData ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (!lstData.Contains("None"))
                        lstData.Add("None");
                    foreach (string strMount in lstData)
                    {
                        if (lstAllowed.Contains(strMount))
                            lstTarget.Add(strMount);
                    }
                }
                Fill(objReturn.Mounts, xmlItem.SelectSingleNode("mount")?.Value);
                Fill(objReturn.ExtraMounts, xmlItem.SelectSingleNode("extramount")?.Value);
            }
            return objReturn;
        }

        /// <summary>Grades the store offers (legacy SelectCyberware.PopulateGrades / SelectDrug).</summary>
        public List<ListItem> GetGrades()
        {
            List<ListItem> lstReturn = new List<ListItem>();
            if (!HasGrades)
                return lstReturn;
            Improvement.ImprovementSource eSource = Kind == EquipmentKind.Drug ? Improvement.ImprovementSource.Drug : PurchaseRules.WareSource(Kind);
            if (Parent is Cyberware objParent)
            {
                Grade objForced = objParent.Grade;
                if (objForced != null)
                    lstReturn.Add(new ListItem(objForced.SourceIDString, objForced.CurrentDisplayName));
                return lstReturn;
            }
            List<Grade> lstGrades = _objCharacter.GetGradesList(eSource);
            HashSet<string> setBanned = _objCharacter.Settings.BannedWareGrades;
            List<string> lstDisabled = ImprovementManager.GetCachedImprovementListForValueOf(_objCharacter,
                    Kind == EquipmentKind.Bioware ? Improvement.ImprovementType.DisableBiowareGrade : Improvement.ImprovementType.DisableCyberwareGrade)
                .Select(x => x.ImprovedName).ToList();
            foreach (Grade objGrade in lstGrades)
            {
                if (objGrade.Name == "None")
                    continue;
                if (IsWare)
                {
                    if (objGrade.Name.ContainsAny(lstDisabled))
                        continue;
                    if (_objCharacter.AdapsinEnabled && Kind == EquipmentKind.Cyberware)
                    {
                        if (!objGrade.Adapsin && objGrade.Name.ContainsAny(lstGrades.Where(x => x.Adapsin).Select(x => x.Name)))
                            continue;
                    }
                    else if (objGrade.Adapsin)
                        continue;
                    if (_objCharacter.BurnoutsWayEnabled)
                    {
                        if (!objGrade.Burnout && objGrade.Name.ContainsAny(lstGrades.Where(x => x.Burnout).Select(x => x.Name)))
                            continue;
                    }
                    else if (objGrade.Burnout)
                        continue;
                    if (!IsCareer && !_objCharacter.IgnoreRules && (setBanned.Contains(objGrade.Name) || objGrade.Name.ContainsAny(setBanned)))
                        continue;
                    if (!objGrade.GetNodeXPath().RequirementsMet(_objCharacter))
                        continue;
                }
                lstReturn.Add(new ListItem(objGrade.SourceIDString, objGrade.CurrentDisplayName));
            }
            return lstReturn;
        }

        private static async Task DisposeTempAsync(object objItem)
        {
            if (objItem is IAsyncDisposable objAsync)
                await objAsync.DisposeAsync().ConfigureAwait(false);
            else if (objItem is IDisposable objDisposable)
                objDisposable.Dispose();
        }

        /// <summary>
        /// Price the item with the chosen options the way the purchase will: a throw-away engine object is built like the legacy
        /// pickers do (no improvements, no prompts), then markup and the restricted / forbidden multipliers are applied.
        /// </summary>
        public async Task<ItemQuote> QuoteAsync(PurchaseRequest objRequest, CancellationToken token = default)
        {
            ItemQuote objQuote = new ItemQuote();
            XPathNavigator xmlNav = Node(objRequest.ItemId);
            if (xmlNav == null)
                return objQuote;
            XmlNode xmlItem = _objCharacter.LoadData(DataFile).TryGetNodeByNameOrId("/chummer/" + _strItemXPath, objRequest.ItemId);
            objQuote.NuyenBefore = await _objCharacter.GetAvailableNuyenAsync(token: token).ConfigureAwait(false);
            objQuote.Name = xmlNav.SelectSingleNode("translate")?.Value ?? xmlNav.SelectSingleNode("name")?.Value ?? string.Empty;
            objQuote.Category = CategoryName(xmlNav.SelectSingleNode("category")?.Value);
            string strSource = xmlNav.SelectSingleNode("source")?.Value;
            string strPage = xmlNav.SelectSingleNode("altpage")?.Value ?? xmlNav.SelectSingleNode("page")?.Value;
            if (!string.IsNullOrEmpty(strSource))
                objQuote.Source = (await SourceString.GetSourceStringAsync(strSource, strPage, GlobalSettings.Language, GlobalSettings.CultureInfo, _objCharacter, token).ConfigureAwait(false)).ToString();
            (decimal decDataCost, bool blnVariable, decimal decDataMax) = DataCost(xmlNav, Math.Max(1, objRequest.Rating));
            objQuote.IsVariable = blnVariable;
            int intRating = objRequest.Rating;
            decimal decCost = 0;
            AvailabilityValue objAvail = new AvailabilityValue(Math.Max(1, intRating), RatedNode(xmlNav, "avail", Math.Max(1, intRating))?.Value ?? string.Empty, AvailBonus(xmlNav));
            List<KeyValuePair<string, string>> lstStats = objQuote.Stats;
            void Stat(string strLabel, string strValue)
            {
                if (!string.IsNullOrEmpty(strValue) && strValue != "–")
                    lstStats.Add(new KeyValuePair<string, string>(strLabel, strValue));
            }
            List<Weapon> lstWeapons = new List<Weapon>(1);
            List<Vehicle> lstVehicles = new List<Vehicle>(1);
            object objTemp = null;
            try
            {
                switch (Kind)
                {
                    case EquipmentKind.Weapon:
                    {
                        Weapon objWeapon = new Weapon(_objCharacter);
                        objTemp = objWeapon;
                        await objWeapon.CreateAsync(xmlItem, lstWeapons, true, false, true, blnForSelectForm: true, token: token).ConfigureAwait(false);
                        objWeapon.DiscountCost = objRequest.BlackMarket;
                        decCost = await objWeapon.GetTotalCostAsync(token).ConfigureAwait(false);
                        objAvail = await objWeapon.TotalAvailTupleAsync(token: token).ConfigureAwait(false);
                        Stat("Accuracy", await objWeapon.GetDisplayAccuracyAsync(token).ConfigureAwait(false));
                        Stat("Damage", await objWeapon.GetDisplayDamageAsync(token).ConfigureAwait(false));
                        Stat("AP", await objWeapon.GetDisplayTotalAPAsync(token).ConfigureAwait(false));
                        if (objWeapon.RangeType == "Ranged")
                        {
                            Stat("Mode", await objWeapon.GetDisplayModeAsync(token).ConfigureAwait(false));
                            Stat("RC", (await objWeapon.GetDisplayTotalRCAsync(token).ConfigureAwait(false)).Item1);
                            Stat("Ammo", await objWeapon.GetDisplayAmmoAsync(token).ConfigureAwait(false));
                        }
                        else
                            Stat("Reach", (await objWeapon.GetTotalReachAsync(token).ConfigureAwait(false)).ToString(GlobalSettings.CultureInfo));
                        Stat("Concealability", await objWeapon.GetDisplayConcealabilityAsync(token).ConfigureAwait(false));
                        Stat("Dice pool", (await objWeapon.GetDicePoolAsync(token: token).ConfigureAwait(false)).ToString(GlobalSettings.CultureInfo));
                        List<string> lstAccessories = new List<string>();
                        foreach (WeaponAccessory objAccessory in objWeapon.WeaponAccessories)
                            lstAccessories.Add(await objAccessory.GetCurrentDisplayNameAsync(token).ConfigureAwait(false));
                        if (lstAccessories.Count > 0)
                            Stat("Included", string.Join(", ", lstAccessories));
                        break;
                    }
                    case EquipmentKind.Armor:
                    {
                        Armor objArmor = new Armor(_objCharacter);
                        objTemp = objArmor;
                        await objArmor.CreateAsync(xmlItem, intRating, lstWeapons, true, true, true, true, token).ConfigureAwait(false);
                        objArmor.DiscountCost = objRequest.BlackMarket;
                        decCost = await objArmor.GetTotalCostAsync(token).ConfigureAwait(false);
                        objAvail = await objArmor.TotalAvailTupleAsync(token: token).ConfigureAwait(false);
                        Stat("Armor", (await objArmor.GetTotalArmorAsync(token: token).ConfigureAwait(false)).ToString(GlobalSettings.CultureInfo));
                        Stat("Capacity", await objArmor.GetDisplayCapacityAsync(token).ConfigureAwait(false));
                        List<string> lstMods = new List<string>();
                        foreach (ArmorMod objMod in objArmor.ArmorMods)
                            lstMods.Add(await objMod.GetCurrentDisplayNameAsync(token).ConfigureAwait(false));
                        if (lstMods.Count > 0)
                            Stat("Included", string.Join(", ", lstMods));
                        break;
                    }
                    case EquipmentKind.Vehicle:
                    {
                        Vehicle objVehicle = new Vehicle(_objCharacter);
                        objTemp = objVehicle;
                        await objVehicle.CreateAsync(xmlItem, true, true, false, true, true, token).ConfigureAwait(false);
                        if (objRequest.UsedVehicle)
                            objVehicle.Cost = (decDataCost * (1 - Math.Max(20, Math.Min(50, objRequest.UsedVehicleDiscount)) / 100.0m)).ToString(GlobalSettings.InvariantCultureInfo);
                        objVehicle.DiscountCost = objRequest.BlackMarket;
                        decCost = await objVehicle.GetTotalCostAsync(token).ConfigureAwait(false);
                        objAvail = await objVehicle.TotalAvailTupleAsync(token: token).ConfigureAwait(false);
                        Stat("Handling", await objVehicle.GetTotalHandlingAsync(token).ConfigureAwait(false));
                        Stat("Speed", await objVehicle.GetTotalSpeedAsync(token).ConfigureAwait(false));
                        Stat("Acceleration", await objVehicle.GetTotalAccelAsync(token).ConfigureAwait(false));
                        Stat("Body", (await objVehicle.GetTotalBodyAsync(token).ConfigureAwait(false)).ToString(GlobalSettings.CultureInfo));
                        Stat("Armor", (await objVehicle.GetTotalArmorAsync(token).ConfigureAwait(false)).ToString(GlobalSettings.CultureInfo));
                        Stat("Pilot", (await objVehicle.GetPilotAsync(token).ConfigureAwait(false)).ToString(GlobalSettings.CultureInfo));
                        Stat("Sensor", (await objVehicle.GetCalculatedSensorAsync(token).ConfigureAwait(false)).ToString(GlobalSettings.CultureInfo));
                        Stat("Seats", (await objVehicle.GetTotalSeatsAsync(token).ConfigureAwait(false)).ToString(GlobalSettings.CultureInfo));
                        break;
                    }
                    case EquipmentKind.Gear:
                    case EquipmentKind.GearPlugin:
                    {
                        Gear objGear = new Gear(_objCharacter);
                        objTemp = objGear;
                        await objGear.CreateAsync(xmlItem, intRating, lstWeapons, string.Empty, false, true, true, Parent as IHasName, token).ConfigureAwait(false);
                        await objGear.SetQuantityAsync(objRequest.Quantity, token).ConfigureAwait(false);
                        objGear.DiscountCost = objRequest.BlackMarket;
                        if (objRequest.DoItYourself)
                            objGear.Cost = "(" + objGear.Cost + ") * 0.5";
                        decCost = await objGear.GetTotalCostAsync(token).ConfigureAwait(false);
                        objAvail = await objGear.TotalAvailTupleAsync(token: token).ConfigureAwait(false);
                        Stat("Capacity", await objGear.GetDisplayCapacityAsync(token).ConfigureAwait(false));
                        string strDevice = xmlNav.SelectSingleNode("devicerating")?.Value;
                        if (!string.IsNullOrEmpty(strDevice))
                            Stat("Device rating", (Evaluate(strDevice, intRating) ?? 0).ToString("#,0.##", GlobalSettings.CultureInfo));
                        foreach (string strAttribute in new[] { "attack", "sleaze", "dataprocessing", "firewall" })
                        {
                            string strValue = xmlNav.SelectSingleNode(strAttribute)?.Value;
                            if (!string.IsNullOrEmpty(strValue))
                                Stat(strAttribute == "dataprocessing" ? "Data proc." : char.ToUpperInvariant(strAttribute[0]) + strAttribute.Substring(1), Pretty(strValue));
                        }
                        break;
                    }
                    case EquipmentKind.Cyberware:
                    case EquipmentKind.Bioware:
                    {
                        Grade objGrade = Parent is Cyberware objParentWare
                            ? await objParentWare.GetGradeAsync(token).ConfigureAwait(false)
                            : await PurchaseRules.FindGradeAsync(_objCharacter, PurchaseRules.WareSource(Kind), objRequest.GradeId, token).ConfigureAwait(false);
                        Cyberware objWare = new Cyberware(_objCharacter);
                        objTemp = objWare;
                        if (await (await _objCharacter.GetSettingsAsync(token).ConfigureAwait(false)).GetAllowCyberwareESSDiscountsAsync(token).ConfigureAwait(false))
                            await objWare.SetESSDiscountAsync(objRequest.EssenceDiscount, token).ConfigureAwait(false);
                        await objWare.CreateAsync(xmlItem, objGrade, PurchaseRules.WareSource(Kind), intRating, lstWeapons, lstVehicles, false, true, string.Empty,
                                                  Parent as Cyberware, null, true, token).ConfigureAwait(false);
                        await objWare.SetDiscountCostAsync(objRequest.BlackMarket, token).ConfigureAwait(false);
                        decCost = await objWare.GetTotalCostAsync(token).ConfigureAwait(false);
                        objAvail = await objWare.TotalAvailTupleAsync(token: token).ConfigureAwait(false);
                        Stat("Essence", (await objWare.GetCalculatedESSAsync(token).ConfigureAwait(false)).ToString(_objCharacter.Settings.EssenceFormat, GlobalSettings.CultureInfo));
                        Stat("Capacity", await objWare.GetDisplayCapacityAsync(token).ConfigureAwait(false));
                        if (objGrade != null)
                            Stat("Grade", objGrade.CurrentDisplayName);
                        Stat("Essence left", (await _objCharacter.EssenceAsync(token: token).ConfigureAwait(false)).ToString(_objCharacter.Settings.EssenceFormat, GlobalSettings.CultureInfo));
                        break;
                    }
                    case EquipmentKind.Drug:
                    {
                        Grade objGrade = await PurchaseRules.FindGradeAsync(_objCharacter, Improvement.ImprovementSource.Drug, objRequest.GradeId, token).ConfigureAwait(false);
                        Drug objDrug = await Drug.CreateFromCatalogAsync(_objCharacter, objRequest.ItemId, objGrade, intRating, token).ConfigureAwait(false);
                        objTemp = objDrug;
                        if (objDrug != null)
                        {
                            objDrug.DiscountCost = objRequest.BlackMarket;
                            decCost = await objDrug.GetCostAsync(token).ConfigureAwait(false);
                            objAvail = await objDrug.TotalAvailTupleAsync(token: token).ConfigureAwait(false);
                        }
                        Stat("Speed", Pretty(xmlNav.SelectSingleNode("speed")?.Value));
                        Stat("Vectors", Pretty(xmlNav.SelectSingleNode("vectors")?.Value));
                        Stat("Addiction", Pretty(xmlNav.SelectSingleNode("addictiontype")?.Value));
                        Stat("Threshold", Pretty(xmlNav.SelectSingleNode("threshold")?.Value));
                        if (objGrade != null)
                            Stat("Grade", objGrade.CurrentDisplayName);
                        break;
                    }
                    case EquipmentKind.Lifestyle:
                    {
                        decCost = DataCost(xmlNav, 0).Cost * Math.Max(0, objRequest.Percentage) / 100.0m;
                        objAvail = new AvailabilityValue(0, ' ', false);
                        Stat("Monthly rent", decCost.ToString(_objCharacter.Settings.NuyenFormat, GlobalSettings.CultureInfo) + LanguageManager.GetString("String_NuyenSymbol"));
                        Stat("Starting nuyen", Pretty(xmlNav.SelectSingleNode("dice")?.Value) + "D6 × " + Pretty(xmlNav.SelectSingleNode("multiplier")?.Value));
                        Stat("Paid up front", "Nothing — rent is paid per month");
                        break;
                    }
                    default:
                    {
                        // Mods and accessories: data cost and avail at the chosen rating (legacy SelectArmorMod / SelectVehicleMod / SelectWeaponAccessory).
                        decCost = decDataCost;
                        if (objRequest.BlackMarket && BlackMarketAllowedFor(objRequest.ItemId))
                            decCost *= 0.9m;
                        if (Kind == EquipmentKind.ArmorMod)
                        {
                            Stat("Armor", Pretty(xmlNav.SelectSingleNode("armor")?.Value));
                            Stat("Capacity", Capacity(xmlNav, "armorcapacity", Math.Max(1, intRating)));
                        }
                        else if (Kind == EquipmentKind.VehicleMod)
                            Stat("Slots", Pretty(xmlNav.SelectSingleNode("slots")?.Value));
                        else if (Kind == EquipmentKind.WeaponAccessory)
                        {
                            Stat("Mount", Pretty(objRequest.Mount));
                            Stat("Accuracy", Pretty(xmlNav.SelectSingleNode("accuracy")?.Value));
                            Stat("RC", Pretty(xmlNav.SelectSingleNode("rc")?.Value));
                        }
                        break;
                    }
                }
            }
            finally
            {
                if (objTemp != null)
                    await DisposeTempAsync(objTemp).ConfigureAwait(false);
                foreach (Weapon objWeapon in lstWeapons)
                    await objWeapon.DisposeAsync().ConfigureAwait(false);
                foreach (Vehicle objVehicle in lstVehicles)
                    await DisposeTempAsync(objVehicle).ConfigureAwait(false);
            }

            if (blnVariable && decCost == 0)
                decCost = decDataCost;
            if (objRequest.Free)
                decCost = 0;
            else if (IsCareer)
                decCost = await PurchaseRules.StreetCostAsync(_objCharacter, decCost, objRequest.Markup, objAvail.Suffix, token).ConfigureAwait(false);
            objQuote.Price = decCost;
            string strFormat = _objCharacter.Settings.NuyenFormat;
            string strNuyen = LanguageManager.GetString("String_NuyenSymbol");
            objQuote.PriceText = decCost.ToString(strFormat, GlobalSettings.CultureInfo) + strNuyen;
            if (blnVariable && !objRequest.Free)
                objQuote.PriceText += decDataMax == decimal.MaxValue ? "+" : "–" + decDataMax.ToString(strFormat, GlobalSettings.CultureInfo) + strNuyen;
            if (Kind != EquipmentKind.Lifestyle)
            {
                objQuote.Avail = await objAvail.ToStringAsync(token).ConfigureAwait(false);
                objQuote.AvailValue = await objAvail.GetValueAsync(token).ConfigureAwait(false);
                objQuote.AvailSuffix = objAvail.Suffix;
                objQuote.AvailTest = await _objCharacter.AvailTestAsync(decCost, objAvail, token).ConfigureAwait(false);
            }
            if (Kind == EquipmentKind.Lifestyle)
                objQuote.Price = 0; // nothing is paid when the lifestyle is added
            return objQuote;
        }

        #endregion
    }
}
