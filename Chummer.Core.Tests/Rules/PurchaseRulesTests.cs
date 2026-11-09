/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Chummer.Backend.Equipment;
using Chummer.Rules;
using Chummer.Selection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Rules
{
    /// <summary>Buying equipment in career mode (legacy CharacterCareer PickGear / PickWeapon / PickArmor / AddVehicle ...).</summary>
    [TestClass]
    public class PurchaseRulesTests
    {
        private static Character Load()
            => UnitTestUtils.LoadCharacter(new FileInfo(Path.Combine(AppContext.BaseDirectory, "TestFiles", "Soma (Career).chum5")));

        private static string Id(Character objCharacter, string strFile, string strXPath, string strName)
            => objCharacter.LoadData(strFile).TryGetNodeByNameOrId(strXPath, strName)?["id"]?.InnerText;

        [TestMethod]
        public async Task BuyGearDeductsNuyenAndLogsExpense()
        {
            using (Character objCharacter = Load())
            {
                Assert.IsTrue(objCharacter.Created);
                await objCharacter.SetNuyenAsync(1000);
                int intGearBefore = objCharacter.Gear.Count;
                int intExpensesBefore = objCharacter.ExpenseEntries.Count;
                PurchaseResult objResult = await PurchaseRules.PurchaseAsync(objCharacter, new PurchaseRequest
                {
                    Kind = EquipmentKind.Gear,
                    ItemId = Id(objCharacter, "gear.xml", "/chummer/gears/gear", "Flashlight"),
                    Quantity = 2,
                });
                Assert.AreEqual(RuleResult.Changed, objResult.Result);
                Assert.AreEqual(50m, objResult.Cost);
                Assert.AreEqual(950m, objCharacter.Nuyen);
                Assert.AreEqual(intGearBefore + 1, objCharacter.Gear.Count);
                Assert.AreEqual(intExpensesBefore + 1, objCharacter.ExpenseEntries.Count);
                ExpenseLogEntry objExpense = objResult.Expense;
                Assert.IsNotNull(objExpense);
                Assert.AreEqual(-50m, objExpense.Amount);
                Assert.AreEqual(LanguageManager.GetString("String_ExpensePurchaseGear") + LanguageManager.GetString("String_Space") + ((Gear)objResult.Item).CurrentDisplayNameShort,
                                objExpense.Reason);
                Assert.AreEqual(NuyenExpenseType.AddGear, objExpense.Undo.NuyenType);
                Assert.AreEqual(((Gear)objResult.Item).InternalId, objExpense.Undo.ObjectId);
                Assert.AreEqual(2m, objExpense.Undo.Qty);
            }
        }

        [TestMethod]
        public async Task BuyWeaponWithMarkupAndFree()
        {
            using (Character objCharacter = Load())
            {
                await objCharacter.SetNuyenAsync(10000);
                string strId = Id(objCharacter, "weapons.xml", "/chummer/weapons/weapon", "Ares Predator V");
                PurchaseResult objResult = await PurchaseRules.PurchaseAsync(objCharacter, new PurchaseRequest { Kind = EquipmentKind.Weapon, ItemId = strId, Markup = 10 });
                Assert.AreEqual(RuleResult.Changed, objResult.Result);
                decimal decBase = ((Weapon)objResult.Item).TotalCost;
                Assert.AreEqual(decBase * 1.1m, objResult.Cost);
                Assert.AreEqual(10000m - decBase * 1.1m, objCharacter.Nuyen);
                Assert.AreEqual(NuyenExpenseType.AddWeapon, objResult.Expense.Undo.NuyenType);
                StringAssert.StartsWith(objResult.Expense.Reason, LanguageManager.GetString("String_ExpensePurchaseWeapon"));
                Assert.IsTrue(objCharacter.Weapons.Contains(objResult.Item));

                decimal decNuyen = objCharacter.Nuyen;
                int intExpenses = objCharacter.ExpenseEntries.Count;
                PurchaseResult objFree = await PurchaseRules.PurchaseAsync(objCharacter, new PurchaseRequest { Kind = EquipmentKind.Weapon, ItemId = strId, Free = true });
                Assert.AreEqual(RuleResult.Changed, objFree.Result);
                Assert.AreEqual(decNuyen, objCharacter.Nuyen);
                Assert.AreEqual(intExpenses, objCharacter.ExpenseEntries.Count);
                Assert.IsTrue(objCharacter.Weapons.Contains(objFree.Item));
            }
        }

        [TestMethod]
        public async Task NotEnoughNuyenIsRejected()
        {
            using (Character objCharacter = Load())
            {
                await objCharacter.SetNuyenAsync(10);
                int intArmor = objCharacter.Armor.Count;
                int intExpenses = objCharacter.ExpenseEntries.Count;
                PurchaseResult objResult = await PurchaseRules.PurchaseAsync(objCharacter, new PurchaseRequest
                {
                    Kind = EquipmentKind.Armor,
                    ItemId = Id(objCharacter, "armor.xml", "/chummer/armors/armor", "Armor Jacket"),
                });
                Assert.AreEqual(RuleResult.NotEnoughNuyen, objResult.Result);
                Assert.AreEqual(10m, objCharacter.Nuyen);
                Assert.AreEqual(intArmor, objCharacter.Armor.Count);
                Assert.AreEqual(intExpenses, objCharacter.ExpenseEntries.Count);
            }
        }

        [TestMethod]
        public async Task CatalogListsAndQuotes()
        {
            using (Character objCharacter = Load())
            {
                EquipmentCatalog objCatalog = new EquipmentCatalog(objCharacter, EquipmentKind.Weapon);
                Assert.IsTrue(objCatalog.GetCategories().Count > 3);
                CatalogListing objListing = objCatalog.GetListing(new CatalogQuery { Search = "Predator" });
                CatalogRow objRow = objListing.Rows.FirstOrDefault(x => x.Name == "Ares Predator V");
                Assert.IsNotNull(objRow);
                Assert.AreEqual(6, objRow.Stats.Length);
                ItemQuote objQuote = await objCatalog.QuoteAsync(new PurchaseRequest { Kind = EquipmentKind.Weapon, ItemId = objRow.Id });
                Assert.IsTrue(objQuote.Price > 0);
                Assert.IsTrue(objQuote.Stats.Count > 3);

                EquipmentCatalog objGear = new EquipmentCatalog(objCharacter, EquipmentKind.Gear);
                Assert.IsTrue(objGear.GetListing(new CatalogQuery { Category = "Survival Gear" }).Rows.Any(x => x.Name == "Flashlight"));
            }
        }

        [TestMethod]
        public async Task BuyCyberwareAndDrug()
        {
            using (Character objCharacter = Load())
            {
                await objCharacter.SetNuyenAsync(100000);
                PurchaseResult objResult = await PurchaseRules.PurchaseAsync(objCharacter, new PurchaseRequest
                {
                    Kind = EquipmentKind.Cyberware,
                    ItemId = Id(objCharacter, "cyberware.xml", "/chummer/cyberwares/cyberware", "Datajack"),
                });
                Assert.AreEqual(RuleResult.Changed, objResult.Result);
                Assert.IsTrue(objResult.Cost > 0);
                Assert.IsNotNull(objResult.Expense);
                Assert.AreEqual(NuyenExpenseType.AddCyberware, objResult.Expense.Undo.NuyenType);

                decimal decNuyen = objCharacter.Nuyen;
                PurchaseResult objDrug = await PurchaseRules.PurchaseAsync(objCharacter, new PurchaseRequest
                {
                    Kind = EquipmentKind.Drug,
                    ItemId = Id(objCharacter, "drugs.xml", "/chummer/drugs/drug", "Bliss"),
                });
                Assert.AreEqual(RuleResult.Changed, objDrug.Result);
                Assert.AreEqual(decNuyen - objDrug.Cost, objCharacter.Nuyen);
                Assert.AreEqual(NuyenExpenseType.AddDrug, objDrug.Expense.Undo.NuyenType);
            }
        }
    }
}
