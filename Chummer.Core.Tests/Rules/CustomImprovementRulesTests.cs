using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Chummer.Printing;
using Newtonsoft.Json.Linq;
using Chummer.Backend.Enums;
using Chummer.Backend.Uniques;
using Chummer.Backend.Equipment;
using Chummer.DataEditing;
using Chummer.Rules;
using Chummer.Selection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Rules
{
    [TestClass, DoNotParallelize]
    public class CustomImprovementRulesTests
    {
        private sealed class PacksTextAnswers : IDialogService
        {
            public DialogOutcome Show(DialogRequest request, object owner = null, System.Threading.CancellationToken token = default)
            {
                if (request is SelectWeaponCategoryRequest)
                {
                    request.SetResult(nameof(SelectWeaponCategoryRequest.SelectedCategory), "Light Pistols");
                    return DialogOutcome.OK;
                }
                if (request is SelectTextRequest)
                {
                    request.SetResult(nameof(SelectTextRequest.SelectedValue), "Seattle");
                    return DialogOutcome.OK;
                }
                throw new InvalidOperationException("Unexpected PACKS prompt: " + request.DialogName);
            }
            public Task<DialogOutcome> ShowAsync(DialogRequest request, object owner = null, System.Threading.CancellationToken token = default)
                => Task.FromResult(Show(request, owner, token));
        }
        private Character _character;
        private CharacterSettings _settings;
        private string _key;
        private bool _confirmDelete;

        private sealed class AcceptMessages : IMessageService
        {
            public MessageResult Show(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
                MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1)
                => buttons == MessageButtons.OK ? MessageResult.OK : MessageResult.Yes;
            public Task<MessageResult> ShowAsync(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
                MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1, System.Threading.CancellationToken token = default)
                => Task.FromResult(Show(owner, message, caption, buttons, icon, defaultButton));
        }

        [TestInitialize]
        public async Task Setup()
        {
            _key = "custom-rules-test-" + Guid.NewGuid().ToString("N") + ".xml";
            using var template = new CharacterSettings();
            _settings = new CharacterSettings(template, false, _key) { BuildMethod = CharacterBuildMethod.Karma };
            _settings.BooksWritable.Add("SR5");
            SettingsManager.LoadedCharacterSettingsAsModifiable[_key] = _settings;
            _character = new Character { SettingsKey = _key };
            var selector = new MetatypeSelector(_character);
            selector.OnHeritageOrTalentChanged();
            selector.Metatype = selector.GetMetatypes().First(x => selector.GetDetails(x.Value.ToString()).Name == "Human").Value.ToString();
            selector.OnMetatypeChanged(); Assert.IsTrue(await selector.ApplyAsync());
            _confirmDelete = GlobalSettings.ConfirmDelete; GlobalSettings.ConfirmDelete = false;
        }

        [TestCleanup]
        public void Cleanup()
        {
            GlobalSettings.ConfirmDelete = _confirmDelete;
            _character.Dispose();
            SettingsManager.LoadedCharacterSettingsAsModifiable.TryRemove(_key, out _);
            _settings.Dispose();
        }

        [TestMethod]
        public async Task AttributeImprovementCanBeEditedDisabledAndRemovedWithoutLosingMetadata()
        {
            int original = _character.BOD.TotalValue;
            var input = new CustomImprovementInput { DefinitionId = "specificattribute", Name = "GM blessing", Group = "House rules", Selection = "BOD", Value = 2 };
            Assert.AreEqual(RuleResult.Changed, await CustomImprovementRules.SaveAsync(_character, input));
            Assert.AreEqual(original + 2, _character.BOD.TotalValue);
            var improvement = _character.Improvements.First(x => x.Custom);
            await improvement.SetNotesAsync("Granted by the GM.");
            await CustomImprovementRules.SetEnabledAsync(_character, improvement, false);
            Assert.AreEqual(original, _character.BOD.TotalValue);
            await CustomImprovementRules.SetEnabledAsync(_character, improvement, true);
            Assert.AreEqual(original + 2, _character.BOD.TotalValue);
            input.Value = 1;
            Assert.AreEqual(RuleResult.Changed, await CustomImprovementRules.SaveAsync(_character, input, improvement));
            Assert.AreEqual(original + 1, _character.BOD.TotalValue);
            var edited = _character.Improvements.First(x => x.Custom);
            Assert.AreEqual("Granted by the GM.", edited.Notes);
            Assert.AreEqual("House rules", edited.CustomGroup);
            Assert.IsTrue(edited.Enabled);
            Assert.AreEqual(RuleResult.Changed, await CustomImprovementRules.RemoveAsync(_character, edited));
            Assert.AreEqual(original, _character.BOD.TotalValue);
            Assert.IsFalse(_character.Improvements.Any(x => x.Custom));
            Assert.AreEqual(0, _character.ExpenseEntries.Count);
        }

        [TestMethod]
        public async Task EditingDisabledImprovementPreservesItsDisabledState()
        {
            var input = new CustomImprovementInput { DefinitionId = "specificattribute", Name = "Conditional bonus", Selection = "BOD", Value = 1 };
            await CustomImprovementRules.SaveAsync(_character, input);
            var original = _character.Improvements.First(x => x.Custom);
            await CustomImprovementRules.SetEnabledAsync(_character, original, false);
            input.Value = 2;
            await CustomImprovementRules.SaveAsync(_character, input, original);
            Assert.IsTrue(_character.Improvements.Where(x => x.Custom).All(x => !x.Enabled));
            Assert.AreEqual(1, _character.BOD.TotalValue);
        }

        [TestMethod]
        public async Task MissingImprovementTargetIsRejectedWithoutSideEffects()
        {
            int count = _character.Improvements.Count;
            var input = new CustomImprovementInput { DefinitionId = "specificattribute", Name = "Incomplete bonus", Value = 2 };
            Assert.AreEqual(RuleResult.NotAllowed, await CustomImprovementRules.SaveAsync(_character, input));
            Assert.AreEqual(count, _character.Improvements.Count);
        }

        [TestMethod]
        public async Task XmlAmendmentPreviewUsesRealOperationsAndPreservesItsInputs()
        {
            const string original = "<chummer><gears><gear><name>Flashlight</name><cost>25</cost><notes>Old</notes></gear><gear><name>Obsolete</name></gear></gears></chummer>";
            const string amendment = "<chummer><gears><gear><name>Flashlight</name><cost amendoperation='replace'>50</cost><notes amendoperation='remove'/></gear><gear amendoperation='remove'><name>Obsolete</name></gear><gear amendoperation='addnode'><name>New gear</name><cost>10</cost></gear></gears></chummer>";
            var preview = await XmlAmendmentRules.PreviewAsync(original, amendment);
            var result = new XmlDocument { XmlResolver = null }; result.LoadXml(preview.Result);
            Assert.AreEqual("50", result.SelectSingleNode("/chummer/gears/gear[name='Flashlight']/cost")?.InnerText);
            Assert.IsNull(result.SelectSingleNode("/chummer/gears/gear[name='Flashlight']/notes"));
            Assert.IsNull(result.SelectSingleNode("/chummer/gears/gear[name='Obsolete']"));
            Assert.IsNotNull(result.SelectSingleNode("/chummer/gears/gear[name='New gear']"));
            StringAssert.Contains(preview.Changes, "Flashlight"); StringAssert.Contains(preview.Changes, "50");
            StringAssert.Contains(preview.Amendment, "amendoperation=\"replace\"");
            var baseDocument = new XmlDocument { XmlResolver = null }; baseDocument.LoadXml(preview.Base);
            Assert.AreEqual("25", baseDocument.SelectSingleNode("/chummer/gears/gear[name='Flashlight']/cost")?.InnerText);
            Assert.IsNotNull(baseDocument.SelectSingleNode("/chummer/gears/gear[name='Obsolete']"));
            Assert.AreEqual(preview.Result, (await XmlAmendmentRules.PreviewAsync(original, amendment)).Result);
        }

        [TestMethod]
        public async Task XmlAmendmentsRejectInvalidRootsAndDtds()
        {
            foreach (string invalid in new[] { "<wrong/>", "<chummer>", "<!DOCTYPE chummer [<!ENTITY sample 'expanded'>]><chummer>&sample;</chummer>" })
            {
                bool rejected = false;
                try { await XmlAmendmentRules.PreviewAsync("<chummer/>", invalid); }
                catch (XmlException) { rejected = true; }
                Assert.IsTrue(rejected, "An invalid amendment was accepted: " + invalid);
            }
        }

        [TestMethod]
        public async Task EquipmentLocationsMoveRenamePersistAndDeleteWithoutRemovingItems()
        {
            var gear = new Gear(_character);
            await gear.CreateAsync(_character.LoadData("gear.xml").SelectSingleNode("/chummer/gears/gear[name = 'Flashlight']"), 0, new System.Collections.Generic.List<Weapon>());
            await _character.Gear.AddAsync(gear);
            var backpack = await EquipmentLocationRules.CreateAsync(_character, gear, "Backpack");
            var safehouse = await EquipmentLocationRules.CreateAsync(_character, gear, "Safehouse");
            Assert.AreEqual(RuleResult.Changed, await EquipmentLocationRules.AssignAsync(_character, gear, backpack));
            Assert.AreSame(backpack, gear.Location); Assert.IsTrue(backpack.Children.Contains(gear));
            Assert.AreEqual(RuleResult.Changed, await EquipmentLocationRules.AssignAsync(_character, gear, safehouse));
            Assert.IsFalse(backpack.Children.Contains(gear)); Assert.AreSame(safehouse, gear.Location);
            Assert.AreEqual(RuleResult.Changed, EquipmentLocationRules.Rename(_character, gear, safehouse, "Locker"));
            string path = Path.Combine(Path.GetTempPath(), "chummer-locations-" + Guid.NewGuid().ToString("N") + ".chum5");
            try
            {
                Assert.IsTrue(await _character.SaveAsync(path, false, false));
                using var loaded = UnitTestUtils.LoadCharacter(new FileInfo(path));
                var restored = loaded.Gear.Single(x => x.Name == "Flashlight");
                Assert.AreEqual("Locker", restored.Location.Name);
                Assert.AreEqual(safehouse.InternalId, restored.Location.InternalId);
                Assert.IsTrue(restored.Location.Children.Contains(restored));
                Assert.AreEqual(RuleResult.Changed, await EquipmentLocationRules.RemoveAsync(loaded, restored, restored.Location));
                Assert.IsNull(restored.Location); Assert.AreEqual(1, loaded.Gear.Count);
                Assert.AreEqual(1, loaded.GearLocations.Count); Assert.AreEqual(0, loaded.ExpenseEntries.Count);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public async Task EquipmentLocationsRejectCrossScopeAndInstalledPlugins()
        {
            var gear = new Gear(_character) { Name = "Bag" }; await _character.Gear.AddAsync(gear);
            var plugin = new Gear(_character) { Name = "Plugin" }; await gear.Children.AddAsync(plugin);
            Assert.IsNull(EquipmentLocationRules.LocationsFor(_character, plugin));
            var location = await EquipmentLocationRules.CreateAsync(_character, gear, "Backpack");
            var armor = new Armor(_character) { Name = "Armor" }; await _character.Armor.AddAsync(armor);
            Assert.AreEqual(RuleResult.NotAllowed, await EquipmentLocationRules.AssignAsync(_character, armor, location));
            Assert.IsNull(armor.Location);
            Assert.IsNull(await EquipmentLocationRules.CreateAsync(_character, gear, "  "));
            var vehicle = new Vehicle(_character) { Name = "Van" }; await _character.Vehicles.AddAsync(vehicle);
            var vehicleGear = new Gear(_character) { Name = "Toolkit" };
            await vehicleGear.SetParentAsync(vehicle); await vehicle.GearChildren.AddAsync(vehicleGear);
            var compartment = await EquipmentLocationRules.CreateAsync(_character, vehicleGear, "Trunk");
            Assert.IsTrue(vehicle.Locations.Contains(compartment));
            Assert.AreEqual(RuleResult.Changed, await EquipmentLocationRules.AssignAsync(_character, vehicleGear, compartment));
            Assert.AreEqual(RuleResult.NotAllowed, await EquipmentLocationRules.AssignAsync(_character, gear, compartment));
        }

        [TestMethod]
        public void CustomSpellDrainAndDescriptorsMatchAllLegacyCategories()
        {
            var combat = new CustomSpellDesign { Name = "Flame", Range = "LOS", NumberOfEffects = 2 };
            combat.SetModifier(3, true); combat.SetModifier(4, true);
            Assert.AreEqual("P", combat.Type);
            Assert.AreEqual("(F/2)+5", CustomSpellRules.Drain(combat));
            Assert.AreEqual("Indirect, Elemental", CustomSpellRules.Descriptors(combat));
            Assert.AreEqual(0, CustomSpellRules.Validate(combat).Count);
            combat.SetModifier(1, true);
            Assert.IsFalse(combat.Modifiers.Contains(2)); Assert.IsFalse(combat.Modifiers.Contains(3));
            var health = new CustomSpellDesign { Name = "Healing", Category = "Health", Type = "M", Range = "T", Duration = "P" };
            health.SetModifier(1, true);
            Assert.AreEqual("(" + LanguageManager.GetString("String_SpellDamageValue") + ")-2", CustomSpellRules.Drain(health));
            var illusion = new CustomSpellDesign { Name = "Mirage", Category = "Illusion", Type = "M", Range = "LOS", Duration = "S", Area = true };
            illusion.SetModifier(2, true); illusion.SetModifier(4, true);
            Assert.AreEqual("(F/2)+2", CustomSpellRules.Drain(illusion));
            Assert.AreEqual("Realistic, Multi-Sense, Area", CustomSpellRules.Descriptors(illusion));
            var manipulation = new CustomSpellDesign { Name = "Shaping", Category = "Manipulation", Range = "LOS", Duration = "S", NumberOfEffects = 3 };
            manipulation.SetModifier(1, true); manipulation.SetModifier(4, true); manipulation.SetModifier(6, true);
            Assert.AreEqual("(F/2)+5", CustomSpellRules.Drain(manipulation));
            var detection = new CustomSpellDesign { Name = "Sense", Category = "Detection", Type = "M", Range = "LOS" };
            detection.SetModifier(2, true); detection.SetModifier(5, true); detection.SetModifier(14, true);
            Assert.AreEqual("(F/2)+2", CustomSpellRules.Drain(detection));
            Assert.AreEqual("Passive, Extended Area", CustomSpellRules.Descriptors(detection));
            detection.SetModifier(4, true);
            Assert.IsFalse(detection.Modifiers.Contains(5)); Assert.IsTrue(detection.Modifiers.Contains(2));
        }

        [TestMethod]
        public async Task CustomSpellChargesCareerKarmaPersistsAndCanBeUndone()
        {
            await _character.SetCreatedAsync(true); _character.Karma = 30;
            var design = new CustomSpellDesign { Name = "Custom bolt", Type = "M", Range = "LOS", Limited = true };
            design.SetModifier(1, true); design.SetModifier(5, true);
            int cost = await MagicRules.GetSpellCostAsync(_character, false, false);
            var oldMessages = HostServices.Messages;
            try
            {
                HostServices.Messages = new AcceptMessages();
                Assert.AreEqual(RuleResult.Changed, await CustomSpellRules.AddAsync(_character, design));
                Assert.AreEqual(30 - cost, _character.Karma);
                var spell = _character.Spells.Single();
                Assert.AreEqual("Custom bolt", spell.Name); Assert.AreEqual("(F/2)-1", spell.DvBase);
                Assert.AreEqual("Direct", spell.Descriptors); Assert.AreEqual("S", spell.Damage); Assert.IsTrue(spell.Limited);
                var expense = _character.ExpenseEntries.Single();
                Assert.AreEqual(-cost, expense.Amount); Assert.AreEqual(KarmaExpenseType.AddSpell, expense.Undo.KarmaType);
                string path = Path.Combine(Path.GetTempPath(), "custom-spell-" + Guid.NewGuid().ToString("N") + ".chum5");
                try
                {
                    Assert.IsTrue(await _character.SaveAsync(path, false, false));
                    using var loaded = UnitTestUtils.LoadCharacter(new FileInfo(path));
                    var restored = loaded.Spells.Single();
                    Assert.AreEqual(spell.InternalId, restored.InternalId); Assert.AreEqual(spell.DvBase, restored.DvBase); Assert.AreEqual("Custom bolt", restored.Name);
                    Assert.AreEqual(RuleResult.Changed, await ExpenseRules.UndoExpenseAsync(loaded, loaded.ExpenseEntries.Single()));
                    Assert.AreEqual(0, loaded.Spells.Count); Assert.AreEqual(30, loaded.Karma);
                }
                finally { File.Delete(path); }
            }
            finally { HostServices.Messages = oldMessages; }
        }

        [TestMethod]
        public async Task CustomSpellRejectsCreationLimitAndInsufficientCareerKarma()
        {
            var design = new CustomSpellDesign { Name = "Bolt", Range = "LOS" };
            design.SetModifier(1, true); design.SetModifier(4, true);
            Assert.AreEqual(RuleResult.NotAllowed, await CustomSpellRules.AddAsync(_character, design));
            Assert.AreEqual(0, _character.Spells.Count);
            await _character.SetCreatedAsync(true); _character.Karma = 0;
            var oldMessages = HostServices.Messages;
            try
            {
                HostServices.Messages = new AcceptMessages();
                Assert.AreEqual(RuleResult.NotEnoughKarma, await CustomSpellRules.AddAsync(_character, design));
                Assert.AreEqual(0, _character.Spells.Count); Assert.AreEqual(0, _character.ExpenseEntries.Count);
            }
            finally { HostServices.Messages = oldMessages; }
        }

        [TestMethod]
        public void CustomSpellRequiresNameCategorySelectionsAndRestrictionText()
        {
            var design = new CustomSpellDesign { Restricted = true };
            Assert.AreEqual(4, CustomSpellRules.Validate(design).Count);
            design.Name = "Bolt"; design.Restriction = "Only spirits";
            design.SetModifier(1, true); design.SetModifier(5, true);
            Assert.AreEqual(0, CustomSpellRules.Validate(design).Count);
            design.Modifiers.Add(2);
            Assert.IsTrue(CustomSpellRules.Validate(design).Count > 0);
        }

        [TestMethod]
        public async Task PacksIntroKitPreservesNestedItemsQuantitiesAndStartingFundsOnReload()
        {
            var kits = await PacksCatalog.LoadAsync(_character);
            var kit = kits.Single(x => x.Name == "Intro Runner Pack");
            StringAssert.Contains(kit.Contents, "Image Link");
            var oldDialogs = HostServices.Dialogs;
            try
            {
                HostServices.Dialogs = new PacksTextAnswers();
                Assert.AreEqual(RuleResult.Changed, await PacksRules.AddAsync(_character, kit.Id, kit.Category));
            }
            finally { HostServices.Dialogs = oldDialogs; }
            Assert.AreEqual(2m, _character.NuyenBP);
            Assert.IsTrue(_character.Armor.Any(x => x.Name == "Armor Vest"));
            Assert.AreEqual(100m, _character.Gear.Single(x => x.Name == "Ammo: Regular Ammo").Quantity);
            Assert.AreEqual("Light Pistols", _character.Gear.Single(x => x.Name == "Ammo: Regular Ammo").Extra);
            Assert.IsTrue(_character.Gear.Single(x => x.Name == "Glasses").Children.Any(x => x.Name == "Image Link"));
            Assert.AreEqual(2, _character.Weapons.Single(x => x.Name == "Colt America L36").WeaponAccessories.Count(x => x.Name == "Spare Clip"));
            Assert.AreEqual(0, _character.ExpenseEntries.Count);
            string path = Path.Combine(Path.GetTempPath(), "chummer-packs-" + Guid.NewGuid().ToString("N") + ".chum5");
            try
            {
                Assert.IsTrue(await _character.SaveAsync(path, false, false));
                using var loaded = UnitTestUtils.LoadCharacter(new FileInfo(path));
                Assert.AreEqual(2m, loaded.NuyenBP);
                Assert.AreEqual(100m, loaded.Gear.Single(x => x.Name == "Ammo: Regular Ammo").Quantity);
                Assert.IsTrue(loaded.Gear.Single(x => x.Name == "Glasses").Children.Any(x => x.Name == "Image Link"));
                Assert.AreEqual(2, loaded.Weapons.Single(x => x.Name == "Colt America L36").WeaponAccessories.Count(x => x.Name == "Spare Clip"));
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public async Task PacksImplantsApplyTheirNestedBonuses()
        {
            Assert.AreEqual(RuleResult.Changed, await PacksRules.AddAsync(_character, "Basic Combat Eyes Pack", "Cyber Packs"));
            var eyes = _character.Cyberware.Single(x => x.Name == "Cybereyes Basic System");
            Assert.AreEqual(1, eyes.Rating);
            Assert.IsTrue(eyes.Children.Any(x => x.Name == "Smartlink"));
            Assert.AreEqual(4m, _character.NuyenBP);
            Assert.IsTrue(await _character.EssenceAsync() < 6m);
            Assert.IsTrue(_character.Improvements.Any(x => x.ImproveType == Improvement.ImprovementType.Smartlink));
        }

        [TestMethod]
        public async Task PacksVehicleRetainsItsGearAndModifications()
        {
            Assert.AreEqual(RuleResult.Changed, await PacksRules.AddAsync(_character, "Racing Bike Pack", "Vehicle Packs"));
            Assert.AreEqual(1, _character.Vehicles.Count);
            Assert.IsTrue(_character.Vehicles[0].Name.Length > 0);
            Assert.IsTrue(_character.NuyenBP > 0);
        }

        [TestMethod]
        public async Task PacksRejectCareerAndUnknownKitWithoutMutation()
        {
            Assert.AreEqual(RuleResult.NotAllowed, await PacksRules.AddAsync(_character, "not a kit", "Core Packs"));
            Assert.AreEqual(0, _character.Gear.Count);
            await _character.SetCreatedAsync(true);
            Assert.AreEqual(RuleResult.NotAllowed, await PacksRules.AddAsync(_character, "Intro Runner Pack", "Core Packs"));
            Assert.AreEqual(0, _character.Gear.Count);
            Assert.AreEqual(0m, _character.NuyenBP);
        }

        [TestMethod]
        public async Task LifeModuleVersionAppliesInheritedCostsAndGrantsAndCanBeRemoved()
        {
            _settings.BuildMethod = CharacterBuildMethod.LifeModule; _settings.BuildKarma = 750;
            _settings.BooksWritable.Add("RF"); _settings.OnPropertyChanged(nameof(CharacterSettings.Books));
            const string canada = "5cf12de1-b41b-4a0f-9a2a-03bf08faa3d0";
            var options = await LifeModuleRules.OptionsAsync(_character, "Nationality");
            var option = options.First(x => x.Id == canada);
            Assert.AreEqual(15, option.Karma);
            int karma = await CreationRules.CalculateKarmaAsync(_character);
            int body = _character.BOD.TotalValue;
            Assert.AreEqual(RuleResult.Changed, await LifeModuleRules.AddAsync(_character, canada));
            Assert.AreEqual(body + 1, _character.BOD.TotalValue);
            // Canada also reserves five karma for its required SINner quality through
            // FreeNegativeQualities, matching the legacy creation budget calculation.
            Assert.AreEqual(-5m, await ImprovementManager.ValueOfAsync(_character, Improvement.ImprovementType.FreeNegativeQualities));
            Assert.AreEqual(karma - 20, await CreationRules.CalculateKarmaAsync(_character));
            var module = _character.Qualities.First(x => x.Type == QualityType.LifeModule);
            Assert.AreEqual("Nationality", module.Stage);
            Assert.AreEqual("Formative Years", LifeModuleRules.NextStage(_character));
            Assert.AreEqual(RuleResult.Changed, await QualityRules.RemoveAsync(_character, module));
            Assert.AreEqual(body, _character.BOD.TotalValue);
            Assert.AreEqual(karma, await CreationRules.CalculateKarmaAsync(_character));
        }

        [TestMethod]
        public async Task CharacterExportsProduceJsonXmlAndSquadManagerOutput()
        {
            _character.Name = "Export tester"; _character.Alias = "Roundtrip";
            string json = await CharacterExporter.RenderAsync(_character, "JSON");
            Assert.IsTrue(JObject.Parse(json).SelectTokens("$..alias").Any(x => x.Value<string>() == "Roundtrip"));
            var xml = new XmlDocument { XmlResolver = null }; xml.LoadXml(await CharacterExporter.RenderAsync(_character, "XML"));
            Assert.AreEqual("Roundtrip", xml.SelectSingleNode("/characters/character/alias")?.InnerText);
            xml.LoadXml(await CharacterExporter.RenderAsync(_character, "Squad Manager"));
            Assert.AreEqual("Roundtrip", xml.SelectSingleNode("//*[local-name()='Name']")?.InnerText);
        }

        [TestMethod]
        public async Task DataArchiveContainsSelectedSettingsAndMergedXml()
        {
            _settings.Name = "Archive test";
            using var memory = new MemoryStream();
            await DataArchiveExporter.ExportAsync(_settings, memory);
            memory.Position = 0;
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
            Assert.AreEqual(Utils.BasicDataFileNames.Count + 1, archive.Entries.Count);
            var xml = new XmlDocument { XmlResolver = null };
            using (var stream = archive.GetEntry("_selectedsetting.xml").Open()) xml.Load(stream);
            Assert.AreEqual("Archive test", xml.SelectSingleNode("/settings/name")?.InnerText);
            using (var stream = archive.GetEntry("gear.xml").Open()) xml.Load(stream);
            Assert.IsNotNull(xml.SelectSingleNode("/chummer/gears/gear[name='Flashlight']"));
        }

        [TestMethod]
        public async Task HeroLabPortfolioReadsStatblocksAndPreservesCharacterIds()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".por");
            try
            {
                using (var archive = ZipFile.Open(path, ZipArchiveMode.Create, Encoding.GetEncoding(850)))
                {
                    using var stream = archive.CreateEntry("statblocks_xml/Élan.xml").Open();
                    using var writer = new StreamWriter(stream);
                    await writer.WriteAsync("<document><public><character name=\"Élan &amp; Ghost\" playername=\"Tester\"><race name=\"Human\"/></character></public></document>");
                }
                byte[] before = File.ReadAllBytes(path);
                var choices = await HeroLabPortfolio.ReadAsync(path);
                Assert.AreEqual(1, choices.Count);
                Assert.AreEqual("Élan & Ghost", choices[0].CharacterId);
                Assert.AreEqual("Human", choices[0].Metatype);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public async Task MasterIndexIncludesKnownItemsAndRespectsEnabledBooks()
        {
            var entries = await MasterIndexCatalog.LoadAsync(_settings);
            Assert.IsTrue(entries.Count > 100);
            Assert.IsTrue(entries.All(x => x.Book == "SR5"));
            var predator = entries.First(x => x.Name == "Ares Predator V" && x.File == "weapons.xml");
            Assert.AreEqual("426", predator.Page);
            Assert.AreEqual("SR5 426", predator.Source);
        }

        [TestMethod]
        public async Task TraditionSelectionUpdatesDrainAndCustomTraditionRejectsInvalidInput()
        {
            _character.MAGEnabled = true;
            var hermetic = TraditionRules.Options(_character).First(x => x.Name == "Hermetic");
            Assert.AreEqual(RuleResult.Changed, await TraditionRules.SetAsync(_character, hermetic.Id));
            Assert.AreEqual(TraditionType.MAG, _character.MagicTradition.Type);
            Assert.AreEqual("{WIL} + {LOG}", _character.MagicTradition.DrainExpression);
            Assert.AreEqual(RuleResult.NotAllowed, await TraditionRules.SetAsync(_character, Tradition.CustomMagicalTraditionGuidString, "", "invalid expression"));
            Assert.AreEqual(hermetic.Id, _character.MagicTradition.SourceIDString);
            Assert.AreEqual(RuleResult.Changed, await TraditionRules.SetAsync(_character, Tradition.CustomMagicalTraditionGuidString, "House tradition", "{WIL} + {CHA}",
                new[] { "Spirit of Fire", "Spirit of Air", "Spirit of Man", "Spirit of Water", "Spirit of Earth" }));
            Assert.AreEqual("House tradition", _character.MagicTradition.Name);
            Assert.AreEqual("Spirit of Fire", _character.MagicTradition.SpiritCombat);
        }
    }
}
