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
        public async Task CreationImplantEditsUpdateGradeRatingAndEffectsWithoutCareerCharges()
        {
            foreach (var entry in new[] { (Kind: EquipmentKind.Cyberware, File: "cyberware.xml", Path: "/chummer/cyberwares/cyberware", Name: "Reaction Enhancers", Attribute: "REA"),
                (Kind: EquipmentKind.Bioware, File: "bioware.xml", Path: "/chummer/biowares/bioware", Name: "Muscle Toner", Attribute: "AGI") })
            {
                var source = entry.Kind == EquipmentKind.Cyberware ? Improvement.ImprovementSource.Cyberware : Improvement.ImprovementSource.Bioware;
                var grades = await _character.GetGradesListAsync(source); string standard = grades.First(x => x.Name == "Standard").SourceIDString;
                string alpha = grades.First(x => x.Name == "Alphaware").SourceIDString;
                string id = _character.LoadData(entry.File).SelectSingleNode(entry.Path + "[name='" + entry.Name + "']/id").InnerText;
                var purchase = await PurchaseRules.PurchaseAsync(_character, new PurchaseRequest { Kind = entry.Kind, ItemId = id, GradeId = standard, Rating = 1 });
                Assert.AreEqual(RuleResult.Changed, purchase.Result); var implant = (Cyberware)purchase.Item; string instance = implant.InternalId;
                int before = _character.GetAttribute(entry.Attribute).TotalValue; decimal nuyen = _character.Nuyen;
                var quote = await ImplantUpgradeRules.QuoteAsync(_character, implant, alpha, 2, 0.5m, false);
                Assert.IsTrue(quote.CanApply); Assert.AreEqual(0m, quote.Cost);
                var edit = await ImplantUpgradeRules.ApplyAsync(_character, implant, alpha, 2, 0.5m, false);
                Assert.AreEqual(RuleResult.Changed, edit.Result); Assert.AreEqual(instance, implant.InternalId);
                Assert.AreEqual("Alphaware", implant.Grade.Name); Assert.AreEqual(2, implant.Rating);
                Assert.AreEqual(await implant.GetCalculatedESSPrototypeInvariantAsync(), quote.NewEssence, "The preview must use the proposed rating.");
                Assert.IsTrue(quote.NewEssence > quote.OldEssence, "Doubling the rating must outweigh the Alphaware discount for these implants.");
                Assert.AreEqual(before + 1, _character.GetAttribute(entry.Attribute).TotalValue);
                Assert.AreEqual(nuyen, _character.Nuyen); Assert.AreEqual(0m, _character.EssenceHole); Assert.AreEqual(0, _character.ExpenseEntries.Count);
            }
        }

        [TestMethod]
        public async Task ImplantUpgradeRejectsGradesThatRequireMissingSpecialEffects()
        {
            _settings.BooksWritable.Add("SG"); _settings.OnPropertyChanged(nameof(CharacterSettings.Books));
            var grades = await _character.GetGradesListAsync(Improvement.ImprovementSource.Cyberware);
            var restricted = grades.Where(x => x.Adapsin || x.Burnout || x.Name == "None").ToList();
            Assert.IsTrue(restricted.Any(x => x.Adapsin)); Assert.IsTrue(restricted.Any(x => x.Burnout));
            string id = _character.LoadData("cyberware.xml").SelectSingleNode("/chummer/cyberwares/cyberware[name='Datajack']/id").InnerText;
            var implant = (Cyberware)(await PurchaseRules.PurchaseAsync(_character, new PurchaseRequest { Kind = EquipmentKind.Cyberware, ItemId = id,
                GradeId = grades.First(x => x.Name == "Standard").SourceIDString })).Item;
            var options = await ImplantUpgradeRules.OptionsAsync(_character, implant);
            foreach (var grade in restricted)
            {
                Assert.IsFalse(options.Grades.Any(x => x.Value?.ToString() == grade.SourceIDString));
                Assert.IsNull(await ImplantUpgradeRules.QuoteAsync(_character, implant, grade.SourceIDString, 0, 0.5m, false));
            }
        }

        [TestMethod]
        public async Task CareerImplantUpgradeAppliesTradeInAndEssenceHoleAndRejectsLegacyGearUndo()
        {
            var grades = await _character.GetGradesListAsync(Improvement.ImprovementSource.Cyberware);
            string standard = grades.First(x => x.Name == "Standard").SourceIDString, alpha = grades.First(x => x.Name == "Alphaware").SourceIDString;
            string id = _character.LoadData("cyberware.xml").SelectSingleNode("/chummer/cyberwares/cyberware[name='Datajack']/id").InnerText;
            var implant = (Cyberware)(await PurchaseRules.PurchaseAsync(_character, new PurchaseRequest { Kind = EquipmentKind.Cyberware, ItemId = id, GradeId = standard })).Item;
            implant.Notes = "Keep install notes"; await _character.SetCreatedAsync(true); await _character.SetNuyenAsync(10000);
            var quote = await ImplantUpgradeRules.QuoteAsync(_character, implant, alpha, 0, 0.5m, false);
            Assert.AreEqual(700m, quote.Cost); Assert.AreEqual(500m, quote.TradeIn);
            var upgrade = await ImplantUpgradeRules.ApplyAsync(_character, implant, alpha, 0, 0.5m, false);
            Assert.AreEqual(RuleResult.Changed, upgrade.Result); Assert.AreEqual(9300m, _character.Nuyen); Assert.AreEqual(0.02m, _character.EssenceHole);
            Assert.AreEqual("Keep install notes", implant.Notes); Assert.IsNull(upgrade.Expense.Undo);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            try
            {
                Assert.IsTrue(await _character.SaveAsync(path, false, false));
                using var loaded = new Character(); loaded.FileName = path; Assert.IsTrue(await loaded.LoadAsync());
                var saved = await loaded.Cyberware.DeepFindByIdAsync(implant.InternalId); Assert.AreEqual("Alphaware", saved.Grade.Name);
                Assert.AreEqual("Keep install notes", saved.Notes); Assert.AreEqual(0.02m, loaded.EssenceHole); Assert.AreEqual(9300m, loaded.Nuyen);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            var legacy = new ExpenseLogEntry(_character); legacy.Create(-100m, "Old implant upgrade", ExpenseType.Nuyen, DateTime.Now);
            var undo = new ExpenseUndo(); undo.CreateNuyen(NuyenExpenseType.AddGear, implant.InternalId); legacy.Undo = undo;
            await _character.ExpenseEntries.AddWithSortAsync(legacy); await _character.ModifyNuyenAsync(-100m);
            var messages = HostServices.Messages;
            try { HostServices.Messages = new AcceptMessages(); Assert.AreEqual(RuleResult.NotAllowed, await ExpenseRules.UndoExpenseAsync(_character, legacy)); }
            finally { HostServices.Messages = messages; }
            Assert.AreEqual(9200m, _character.Nuyen); Assert.IsTrue(_character.ExpenseEntries.Contains(legacy)); Assert.AreEqual("Alphaware", implant.Grade.Name);
        }

        [TestMethod]
        public async Task ImplantReplacementHonorsParentGradeAndCapacityAndPreservesChildren()
        {
            _settings.EnforceCapacity = true;
            var grades = await _character.GetGradesListAsync(Improvement.ImprovementSource.Cyberware);
            string standard = grades.First(x => x.Name == "Standard").SourceIDString, alpha = grades.First(x => x.Name == "Alphaware").SourceIDString;
            string Id(string name) => _character.LoadData("cyberware.xml").SelectSingleNode("/chummer/cyberwares/cyberware[name='" + name + "']/id").InnerText;
            var eyes = (Cyberware)(await PurchaseRules.PurchaseAsync(_character, new PurchaseRequest { Kind = EquipmentKind.Cyberware, ItemId = Id("Cybereyes Basic System"), Rating = 1, GradeId = standard })).Item;
            var vision = (Cyberware)(await PurchaseRules.PurchaseAsync(_character, new PurchaseRequest { Kind = EquipmentKind.Cyberware, ItemId = Id("Vision Enhancement"), Rating = 1, GradeId = standard, Parent = eyes })).Item;
            await PurchaseRules.PurchaseAsync(_character, new PurchaseRequest { Kind = EquipmentKind.Cyberware, ItemId = Id("Smartlink"), GradeId = standard, Parent = eyes });
            var children = eyes.Children.ToArray();
            Assert.IsFalse((await ImplantUpgradeRules.QuoteAsync(_character, vision, standard, 3, 0.5m, false)).CanApply);
            Assert.AreEqual(RuleResult.NotAllowed, (await ImplantUpgradeRules.ApplyAsync(_character, vision, standard, 3, 0.5m, false)).Result);
            Assert.IsNull(await ImplantUpgradeRules.QuoteAsync(_character, vision, alpha, 2, 0.5m, false));
            Assert.AreEqual(RuleResult.Changed, (await ImplantUpgradeRules.ApplyAsync(_character, eyes, standard, 2, 0.5m, false)).Result);
            CollectionAssert.AreEqual(children, eyes.Children.ToArray()); Assert.AreSame(eyes, vision.Parent);
            Assert.AreEqual(RuleResult.Changed, (await ImplantUpgradeRules.ApplyAsync(_character, vision, standard, 3, 0.5m, false)).Result);
            Assert.IsFalse((await ImplantUpgradeRules.QuoteAsync(_character, eyes, standard, 1, 0.5m, false)).CanApply);
            Assert.AreEqual(2, eyes.Rating); Assert.AreEqual(3, vision.Rating); Assert.AreEqual(0, _character.ExpenseEntries.Count);
        }

        [TestMethod]
        public async Task ImplantUpgradeRejectsFundsInvalidRatingsAndCancellationBeforeChangingState()
        {
            var grades = await _character.GetGradesListAsync(Improvement.ImprovementSource.Cyberware);
            string standard = grades.First(x => x.Name == "Standard").SourceIDString, alpha = grades.First(x => x.Name == "Alphaware").SourceIDString;
            string id = _character.LoadData("cyberware.xml").SelectSingleNode("/chummer/cyberwares/cyberware[name='Reaction Enhancers']/id").InnerText;
            var implant = (Cyberware)(await PurchaseRules.PurchaseAsync(_character, new PurchaseRequest { Kind = EquipmentKind.Cyberware, ItemId = id, Rating = 1, GradeId = standard })).Item;
            await _character.SetCreatedAsync(true); await _character.SetNuyenAsync(0);
            Assert.AreEqual(RuleResult.NotEnoughNuyen, (await ImplantUpgradeRules.ApplyAsync(_character, implant, alpha, 2, 0.5m, false)).Result);
            Assert.IsNull(await ImplantUpgradeRules.QuoteAsync(_character, implant, alpha, 99, 0.5m, false));
            bool cancelled = false;
            try { await ImplantUpgradeRules.ApplyAsync(_character, implant, alpha, 2, 0.5m, true, new System.Threading.CancellationToken(true)); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert.IsTrue(cancelled); Assert.AreEqual(1, implant.Rating); Assert.AreEqual("Standard", implant.Grade.Name); Assert.AreEqual(0, _character.ExpenseEntries.Count);
            Assert.AreEqual(RuleResult.Changed, (await ImplantUpgradeRules.ApplyAsync(_character, implant, alpha, 2, 0.5m, true)).Result);
            Assert.AreEqual(0m, _character.Nuyen); Assert.AreEqual(2, implant.Rating); Assert.IsNull(_character.ExpenseEntries.Single().Undo);
        }

        [TestMethod]
        public async Task SustainedEffectsRetainSourceForceHitsAndPenaltyThroughSave()
        {
            _settings.DicePenaltySustaining = 2;
            var perception = _character.SkillsSection.Skills.First(x => x.Name == "Perception"); await perception.SetBaseAsync(4);
            int originalPool = await perception.GetPoolAsync(); Assert.IsTrue(originalPool > 2);
            var spell = new Spell(_character);
            await spell.CreateAsync(_character.LoadData("spells.xml").SelectSingleNode("/chummer/spells/spell[name='Increase Reflexes']"));
            await _character.Spells.AddAsync(spell);
            Assert.AreEqual(RuleResult.Changed, await SustainingRules.AddAsync(_character, spell, 4, 3, true));
            var sustained = _character.SustainedCollection.Single(); Assert.AreEqual(-2, _character.SustainingPenalty);
            Assert.AreEqual(originalPool - 2, await perception.GetPoolAsync());
            Assert.AreEqual(RuleResult.Changed, await SustainingRules.UpdateAsync(_character, sustained, 6, 5, false));
            Assert.AreEqual(0, _character.SustainingPenalty); Assert.AreEqual(6, sustained.Force); Assert.AreEqual(5, sustained.NetHits);
            Assert.AreEqual(originalPool, await perception.GetPoolAsync());
            await SustainingRules.UpdateAsync(_character, sustained, 6, 5, true);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            try
            {
                Assert.IsTrue(await _character.SaveAsync(path, false, false));
                using var loaded = new Character(); loaded.FileName = path; Assert.IsTrue(await loaded.LoadAsync());
                var saved = loaded.SustainedCollection.Single(); Assert.AreEqual(spell.InternalId, saved.LinkedObject.InternalId);
                Assert.AreSame(loaded.Spells.Single(), saved.LinkedObject); Assert.AreEqual(6, saved.Force); Assert.AreEqual(5, saved.NetHits); Assert.IsTrue(saved.SelfSustained);
                Assert.AreEqual(-2, loaded.SustainingPenalty);
                Assert.AreEqual(originalPool - 2, await loaded.SkillsSection.Skills.First(x => x.Name == "Perception").GetPoolAsync());
                Assert.AreEqual(RuleResult.Changed, await SustainingRules.RemoveAsync(loaded, saved)); Assert.AreEqual(0, loaded.SustainedCollection.Count);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [TestMethod]
        public async Task SustainingPenaltiesRespondToPsycheForceSupportAndSourceRemoval()
        {
            _settings.DicePenaltySustaining = 2;
            var spell = new Spell(_character);
            await spell.CreateAsync(_character.LoadData("spells.xml").SelectSingleNode("/chummer/spells/spell[name='Increase Reflexes']"));
            await _character.Spells.AddAsync(spell);
            await SustainingRules.AddAsync(_character, spell, 2, 1, true); await SustainingRules.AddAsync(_character, spell, 5, 2, true);
            Assert.AreEqual(-4, _character.SustainingPenalty);
            await SustainingRules.SetPsycheAsync(_character, true); Assert.AreEqual(-2, _character.SustainingPenalty);
            await SustainingRules.SetPsycheAsync(_character, false); Assert.AreEqual(-4, _character.SustainingPenalty);
            var support = new Improvement(_character) { ImproveSource = Improvement.ImprovementSource.Custom, ImproveType = Improvement.ImprovementType.PenaltyFreeSustain, SourceName = "Test support", Value = 3, Rating = 1, Enabled = true };
            await _character.Improvements.AddAsync(support); await _character.RefreshSustainingPenaltiesAsync();
            Assert.AreEqual(-2, _character.SustainingPenalty);
            var low = _character.SustainedCollection.First();
            await SustainingRules.UpdateAsync(_character, low, 4, 1, true); Assert.AreEqual(-4, _character.SustainingPenalty);
            await _character.Spells.RemoveAsync(spell); Assert.AreEqual(0, _character.SustainedCollection.Count); Assert.AreEqual(0, _character.SustainingPenalty);
            await _character.Spells.AddAsync(spell); await SustainingRules.AddAsync(_character, spell, 2, 1, true);
            await _character.Spells.ClearAsync(); Assert.AreEqual(0, _character.SustainedCollection.Count); Assert.AreEqual(0, _character.SustainingPenalty);
        }

        [TestMethod]
        public async Task SustainingAcceptsOwnedFormsAndPowersAndRejectsInvalidSourcesAndRanges()
        {
            _settings.DicePenaltySustaining = 2;
            var form = new ComplexForm(_character) { Name = "Test form", Duration = "S" }; await _character.ComplexForms.AddAsync(form);
            var power = new CritterPower(_character) { Name = "Test power" }; await _character.CritterPowers.AddAsync(power);
            Assert.AreEqual(RuleResult.Changed, await SustainingRules.AddAsync(_character, power, 3, 0, true)); Assert.AreEqual(0, _character.SustainingPenalty);
            var trackedPower = _character.SustainedCollection.Single(); Assert.IsFalse(trackedPower.HasSustainingPenalty); Assert.IsFalse(await trackedPower.GetHasSustainingPenaltyAsync());
            Assert.AreEqual(RuleResult.Changed, await SustainingRules.AddAsync(_character, form, 3, 1, true)); Assert.AreEqual(-2, _character.SustainingPenalty);
            var trackedForm = _character.SustainedCollection.Last(); Assert.IsTrue(trackedForm.HasSustainingPenalty); Assert.IsTrue(await trackedForm.GetHasSustainingPenaltyAsync());
            Assert.AreEqual(RuleResult.NotAllowed, await SustainingRules.AddAsync(_character, new Spell(_character), 3, 1, true));
            Assert.AreEqual(RuleResult.NotAllowed, await SustainingRules.AddAsync(_character, form, -1, 1, true));
            Assert.AreEqual(RuleResult.NotAllowed, await SustainingRules.UpdateAsync(_character, _character.SustainedCollection.Last(), 3, 101, true));
            Assert.AreEqual(2, _character.SustainedCollection.Count);
        }

        [TestMethod]
        public async Task FactoryMountCanReplaceItsNamedUnderbarrelWeaponWithoutOfferingUnrelatedWeapons()
        {
            var vehicle = new Vehicle(_character) { Name = "Factory drone" }; await _character.Vehicles.AddAsync(vehicle);
            var mount = new WeaponMount(_character, vehicle) { AllowedWeapons = "Underbarrel Grenade Launcher", IncludedInVehicle = true };
            await vehicle.WeaponMounts.AddAsync(mount);
            var catalog = new EquipmentCatalog(_character, EquipmentKind.Weapon, mount);
            var listing = catalog.GetListing(new CatalogQuery());
            Assert.IsTrue(listing.Rows.Any(x => x.Name == "Underbarrel Grenade Launcher"));
            Assert.IsTrue(listing.Rows.All(x => x.Name == "Underbarrel Grenade Launcher"));
            var request = new PurchaseRequest { Kind = EquipmentKind.Weapon, Parent = mount, ItemId = listing.Rows.First().Id };
            var purchase = await PurchaseRules.PurchaseAsync(_character, request);
            Assert.AreEqual(RuleResult.Changed, purchase.Result); Assert.AreSame(mount, ((Weapon)purchase.Item).ParentMount);
        }

        [TestMethod]
        public async Task MountRetrofitRejectsIncompatibleWeaponsAndKeepsSavedOwnershipAndNotes()
        {
            var vehicle = new Vehicle(_character) { Name = "Gun truck", Body = 12 }; await _character.Vehicles.AddAsync(vehicle); _settings.EnforceCapacity = false;
            var input = new WeaponMountConfiguration { SizeId = "a567c5d3-38b8-496a-add8-1e176384e935", ControlId = "70f99fae-470f-442c-a607-ef3465e27f37",
                FlexibilityId = "4a020775-9258-486a-a03f-7a48dd5c3bf9", VisibilityId = "582c31e0-1374-4ebb-b201-a20cb815d6dd" };
            var mount = (WeaponMount)(await WeaponMountRules.PurchaseAsync(_character, vehicle, input)).Item; mount.Notes = "Truck mount notes";
            string heavyId = _character.LoadData("weapons.xml").SelectSingleNode("/chummer/weapons/weapon[category='Heavy Machine Guns' and source='SR5']/id").InnerText;
            Assert.AreEqual(RuleResult.Changed, (await PurchaseRules.PurchaseAsync(_character, new PurchaseRequest { Kind = EquipmentKind.Weapon, Parent = mount, ItemId = heavyId })).Result);
            var gun = mount.Weapons.Single(); mount.AllowedWeapons = gun.Name;
            input.SizeId = "079a5c61-aee6-4383-81b7-32540f7a0a0b";
            Assert.IsNull(await WeaponMountRules.QuoteEditAsync(_character, mount, input));
            Assert.AreEqual(RuleResult.NotAllowed, (await WeaponMountRules.EditAsync(_character, mount, input)).Result);
            Assert.AreSame(gun, mount.Weapons.Single()); Assert.AreSame(mount, gun.ParentMount);
            input.SizeId = mount.SourceIDString; input.ControlId = "741bb6e2-0c6b-4af2-82b0-0c4b14c60367";
            Assert.AreEqual(RuleResult.Changed, (await WeaponMountRules.EditAsync(_character, mount, input)).Result);
            Assert.AreEqual(gun.Name, mount.AllowedWeapons); Assert.AreEqual("Truck mount notes", mount.Notes);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            try
            {
                Assert.IsTrue(await _character.SaveAsync(path, false, false));
                using var loaded = new Character(); loaded.FileName = path; Assert.IsTrue(await loaded.LoadAsync());
                var saved = loaded.Vehicles.Single().WeaponMounts.Single();
                Assert.AreEqual(mount.InternalId, saved.InternalId); Assert.AreEqual("Truck mount notes", saved.Notes);
                Assert.AreEqual(gun.InternalId, saved.Weapons.Single().InternalId); Assert.AreSame(saved, saved.Weapons.Single().ParentMount);
                Assert.IsTrue(saved.WeaponMountOptions.Any(x => x.SourceIDString == input.ControlId));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [TestMethod]
        public async Task MountRetrofitChargesDifferencePreservesAttachmentsAndRefundsDowngrade()
        {
            var vehicle = new Vehicle(_character) { Name = "Van", Body = 10 }; await _character.Vehicles.AddAsync(vehicle);
            _settings.EnforceCapacity = false; _settings.MultiplyForbiddenCost = false; _settings.MultiplyRestrictedCost = false;
            await _character.SetCreatedAsync(true); await _character.SetNuyenAsync(100000);
            var input = new WeaponMountConfiguration { SizeId = "079a5c61-aee6-4383-81b7-32540f7a0a0b", ControlId = "70f99fae-470f-442c-a607-ef3465e27f37",
                FlexibilityId = "4a020775-9258-486a-a03f-7a48dd5c3bf9", VisibilityId = "582c31e0-1374-4ebb-b201-a20cb815d6dd" };
            var mount = (WeaponMount)(await WeaponMountRules.PurchaseAsync(_character, vehicle, input)).Item;
            string id = mount.InternalId; mount.Notes = "Keep this note";
            var weapon = _character.LoadData("weapons.xml").SelectSingleNode("/chummer/weapons/weapon[name='Ares Predator V']/id").InnerText;
            await PurchaseRules.PurchaseAsync(_character, new PurchaseRequest { Kind = EquipmentKind.Weapon, ItemId = weapon, Parent = mount });
            var gun = mount.Weapons.Single();
            _settings.BooksWritable.Add("R5"); _settings.OnPropertyChanged(nameof(CharacterSettings.Books));
            await PurchaseRules.PurchaseAsync(_character, new PurchaseRequest { Kind = EquipmentKind.WeaponMountMod, ItemId = "6741d080-0e40-4941-a7b5-85ab3e1a9cb2", Parent = mount });
            var mod = mount.Mods.Single(); decimal balance = _character.Nuyen;
            input.ControlId = "741bb6e2-0c6b-4af2-82b0-0c4b14c60367"; input.Markup = 20;
            var quote = await WeaponMountRules.QuoteEditAsync(_character, mount, input);
            Assert.AreEqual(600m, quote.Cost); Assert.AreEqual(balance, _character.Nuyen);
            var edit = await WeaponMountRules.EditAsync(_character, mount, input);
            Assert.AreEqual(RuleResult.Changed, edit.Result); Assert.AreEqual(600m, edit.Cost); Assert.AreEqual(balance - 600, _character.Nuyen);
            Assert.AreEqual(NuyenExpenseType.ModifyVehicleWeaponMount, edit.Expense.Undo.NuyenType);
            Assert.AreEqual(id, mount.InternalId); Assert.AreSame(gun, mount.Weapons.Single()); Assert.AreSame(mod, mount.Mods.Single());
            Assert.AreSame(mount, mod.WeaponMountParent); Assert.AreEqual("Keep this note", mount.Notes);
            input.ControlId = "70f99fae-470f-442c-a607-ef3465e27f37"; input.Markup = 0;
            Assert.AreEqual(-500m, (await WeaponMountRules.QuoteEditAsync(_character, mount, input)).Cost);
            Assert.AreEqual(-500m, (await WeaponMountRules.EditAsync(_character, mount, input)).Cost);
            Assert.AreEqual(balance - 100, _character.Nuyen);
        }

        [TestMethod]
        public async Task MountRetrofitRejectsFundsAndCapacityWithoutChangingConfiguration()
        {
            var vehicle = new Vehicle(_character) { Name = "Small van", Body = 4 }; await _character.Vehicles.AddAsync(vehicle);
            _settings.EnforceCapacity = true; await _character.SetCreatedAsync(true); await _character.SetNuyenAsync(100000);
            var input = new WeaponMountConfiguration { SizeId = "079a5c61-aee6-4383-81b7-32540f7a0a0b", ControlId = "70f99fae-470f-442c-a607-ef3465e27f37",
                FlexibilityId = "4a020775-9258-486a-a03f-7a48dd5c3bf9", VisibilityId = "582c31e0-1374-4ebb-b201-a20cb815d6dd" };
            var mount = (WeaponMount)(await WeaponMountRules.PurchaseAsync(_character, vehicle, input)).Item;
            var oldOptions = mount.WeaponMountOptions.ToArray(); string size = mount.SourceIDString;
            var messages = HostServices.Messages;
            try
            {
                HostServices.Messages = new AcceptMessages(); await _character.SetNuyenAsync(0);
                input.ControlId = "741bb6e2-0c6b-4af2-82b0-0c4b14c60367";
                Assert.AreEqual(RuleResult.NotEnoughNuyen, (await WeaponMountRules.EditAsync(_character, mount, input)).Result);
                CollectionAssert.AreEqual(oldOptions, mount.WeaponMountOptions.ToArray()); Assert.AreEqual(size, mount.SourceIDString);
                await _character.SetNuyenAsync(100000); input.SizeId = "a567c5d3-38b8-496a-add8-1e176384e935";
                Assert.AreEqual(RuleResult.NotAllowed, (await WeaponMountRules.EditAsync(_character, mount, input)).Result);
                CollectionAssert.AreEqual(oldOptions, mount.WeaponMountOptions.ToArray()); Assert.AreEqual(size, mount.SourceIDString);
                Assert.AreEqual(100000m, _character.Nuyen); Assert.AreEqual(1, _character.ExpenseEntries.Count);
                mount.IncludedInVehicle = true;
                Assert.IsNull(await WeaponMountRules.QuoteEditAsync(_character, mount, input));
                Assert.AreEqual(RuleResult.NotAllowed, (await WeaponMountRules.EditAsync(_character, mount, input)).Result);
            }
            finally { HostServices.Messages = messages; }
        }

        [TestMethod]
        public async Task MountRetrofitQuoteIncludesParentSlotDependentModCostWithoutMovingIt()
        {
            var node = _character.LoadData("vehicles.xml").SelectSingleNode("/chummer/vehicles/vehicle[contains(category,'Drones') and source='SR5']");
            var vehicle = new Vehicle(_character); await vehicle.CreateAsync(node, blnCreateChildren: false, blnCreateImprovements: false, blnSkipSelectForms: true);
            await _character.Vehicles.AddAsync(vehicle); _settings.EnforceCapacity = false; _settings.DroneMods = true;
            var input = new WeaponMountConfiguration { SizeId = "079a5c61-aee6-4383-81b7-32540f7a0a0b", ControlId = "70f99fae-470f-442c-a607-ef3465e27f37",
                FlexibilityId = "4a020775-9258-486a-a03f-7a48dd5c3bf9", VisibilityId = "582c31e0-1374-4ebb-b201-a20cb815d6dd" };
            var mount = (WeaponMount)(await WeaponMountRules.PurchaseAsync(_character, vehicle, input)).Item;
            _settings.BooksWritable.Add("R5"); _settings.OnPropertyChanged(nameof(CharacterSettings.Books));
            var purchase = await PurchaseRules.PurchaseAsync(_character, new PurchaseRequest { Kind = EquipmentKind.WeaponMountMod, Parent = mount, ItemId = "dd03dc64-64e1-49a7-8836-d932cd4cb95b" });
            var mod = (VehicleMod)purchase.Item; Assert.IsNotNull(mod);
            input.SizeId = "a567c5d3-38b8-496a-add8-1e176384e935";
            var quote = await WeaponMountRules.QuoteEditAsync(_character, mount, input);
            Assert.AreEqual(7, quote.Slots); Assert.AreEqual(2800m, quote.Cost);
            Assert.AreEqual(4, mount.CalculatedSlots); Assert.AreSame(mount, mod.WeaponMountParent);
            var result = await WeaponMountRules.EditAsync(_character, mount, input);
            Assert.AreEqual(RuleResult.Changed, result.Result); Assert.AreEqual(quote.Cost, result.Cost);
            Assert.AreEqual(7, mount.CalculatedSlots); Assert.AreEqual(700m, mod.TotalCost);
        }

        [TestMethod]
        public async Task SameNameDrugRecipesKeepSeparateEffectsWhenOnePurchaseIsUndone()
        {
            await _character.SetCreatedAsync(true); await _character.SetNuyenAsync(10000);
            var design = new CustomDrugDesign { Name = "Shared recipe name" };
            design.Ingredients.Add(new CustomDrugIngredient { ComponentId = "Genius" });
            var first = await CustomDrugRules.PurchaseAsync(_character, design);
            var second = await CustomDrugRules.PurchaseAsync(_character, design);
            string secondId = ((Drug)second.Item).InternalId;
            int secondEffects = _character.Improvements.Count(x => x.SourceName == secondId);
            Assert.IsTrue(secondEffects > 0);
            var oldMessages = HostServices.Messages;
            try
            {
                HostServices.Messages = new AcceptMessages();
                Assert.AreEqual(RuleResult.Changed, await ExpenseRules.UndoExpenseAsync(_character, first.Expense));
                Assert.AreEqual(secondId, _character.Drugs.Single().InternalId);
                Assert.AreEqual(secondEffects, _character.Improvements.Count(x => x.SourceName == secondId));
                Assert.AreEqual(RuleResult.Changed, await ExpenseRules.UndoExpenseAsync(_character, second.Expense));
                Assert.AreEqual(0, _character.Drugs.Count); Assert.AreEqual(10000m, _character.Nuyen);
                Assert.IsFalse(_character.Improvements.Any(x => x.ImproveSource == Improvement.ImprovementSource.Drug));
            }
            finally { HostServices.Messages = oldMessages; }
        }

        [TestMethod]
        public async Task CustomDrugRecipeValidatesFoundationConflictsLevelsAndComponentLimits()
        {
            var design = new CustomDrugDesign { Name = "Recipe test" };
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => CustomDrugRules.QuoteAsync(_character, design));
            design.Ingredients.Add(new CustomDrugIngredient { ComponentId = "Tank", Level = 0 });
            design.Ingredients.Add(new CustomDrugIngredient { ComponentId = "Smoothtalk", Level = 2 });
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => CustomDrugRules.QuoteAsync(_character, design));
            design.Ingredients[1].Level = 1;
            var quote = await CustomDrugRules.QuoteAsync(_character, design); Assert.IsTrue(quote.Cost > 0);
            design.Ingredients.Reverse(); design.Ingredients[0].Level = 2;
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => CustomDrugRules.QuoteAsync(_character, design));
            design.Ingredients.Clear(); design.Ingredients.Add(new CustomDrugIngredient { ComponentId = "Genius" });
            for (int i = 0; i < 4; i++) design.Ingredients.Add(new CustomDrugIngredient { ComponentId = "Speed Enhancer" });
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => CustomDrugRules.QuoteAsync(_character, design));
            design.Ingredients.RemoveAt(4); Assert.IsNotNull(await CustomDrugRules.QuoteAsync(_character, design));
            design.Ingredients[1].Level = 99;
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => CustomDrugRules.QuoteAsync(_character, design));
            Assert.AreEqual(0, _character.Drugs.Count); Assert.AreEqual(0, _character.ExpenseEntries.Count);
            design.Ingredients.Clear(); design.Ingredients.Add(new CustomDrugIngredient { ComponentId = "Genius" }); design.Free = true;
            Assert.AreEqual(0m, (await CustomDrugRules.QuoteAsync(_character, design)).Cost);
            Assert.AreEqual(RuleResult.Changed, (await CustomDrugRules.PurchaseAsync(_character, design)).Result);
            Assert.AreEqual(0m, _character.Drugs.Single().TotalCost);
            Assert.AreNotEqual("0", (await CustomDrugRules.ComponentsAsync(_character)).Single(x => x.Name == "Genius").Cost);
        }

        [TestMethod]
        public async Task CustomDrugPurchaseSaveAndExpenseUndoRetainRecipeAndDoNotLeakEffects()
        {
            _settings.BooksWritable.Add("CF"); _settings.OnPropertyChanged(nameof(CharacterSettings.Books));
            await _character.SetCreatedAsync(true); await _character.SetNuyenAsync(10000);
            var design = new CustomDrugDesign { Name = "Clear thinker", Markup = 10, GradeId = "Pharmaceutical" };
            design.Ingredients.Add(new CustomDrugIngredient { ComponentId = "Genius" });
            design.Ingredients.Add(new CustomDrugIngredient { ComponentId = "Smoothtalk", Level = 1 });
            int originalImprovements = _character.Improvements.Count;
            var quote = await CustomDrugRules.QuoteAsync(_character, design);
            Assert.AreEqual(originalImprovements, _character.Improvements.Count);
            await _character.SetNuyenAsync(0);
            var oldMessages = HostServices.Messages;
            try
            {
                HostServices.Messages = new AcceptMessages();
                Assert.AreEqual(RuleResult.NotEnoughNuyen, (await CustomDrugRules.PurchaseAsync(_character, design)).Result);
                Assert.AreEqual(0, _character.Drugs.Count); Assert.AreEqual(originalImprovements, _character.Improvements.Count);
                await _character.SetNuyenAsync(10000);
                var result = await CustomDrugRules.PurchaseAsync(_character, design);
                Assert.AreEqual(RuleResult.Changed, result.Result); Assert.AreEqual(quote.Cost, result.Cost);
                Assert.AreEqual(10000m - quote.Cost, _character.Nuyen); Assert.AreEqual(NuyenExpenseType.AddDrug, result.Expense.Undo.NuyenType);
                var drug = _character.Drugs.Single(); Assert.AreEqual(1m, drug.Quantity); Assert.AreEqual(2, drug.Components.Count);
                decimal balance = _character.Nuyen;
                await _character.SetNuyenAsync(0);
                int ownedEffects = _character.Improvements.Count;
                Assert.AreEqual(RuleResult.NotEnoughNuyen, (await CustomDrugRules.PurchaseAsync(_character, design)).Result);
                Assert.AreEqual(ownedEffects, _character.Improvements.Count);
                Assert.AreEqual(RuleResult.NotEnoughNuyen, await CustomDrugRules.BuyAdditionalDoseAsync(_character, drug));
                Assert.AreEqual(1m, drug.Quantity); Assert.AreEqual(1, _character.ExpenseEntries.Count);
                await _character.SetNuyenAsync(balance);
                Assert.AreEqual(RuleResult.Changed, await CustomDrugRules.BuyAdditionalDoseAsync(_character, drug));
                Assert.AreEqual(2m, drug.Quantity);
                var additional = _character.ExpenseEntries.Single(x => !ReferenceEquals(x, result.Expense));
                Assert.AreEqual(1m, additional.Undo.Qty);
                Assert.AreEqual(RuleResult.Changed, await ExpenseRules.UndoExpenseAsync(_character, additional));
                Assert.AreEqual(1m, drug.Quantity); Assert.AreEqual(balance, _character.Nuyen);
                Assert.AreEqual(1, drug.Components.Single(x => x.Name == "Smoothtalk").Level);
                Assert.IsTrue(_character.Improvements.Where(x => x.SourceName == drug.InternalId).All(x => !x.Enabled));
                string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
                try
                {
                    Assert.IsTrue(await _character.SaveAsync(path, false, false));
                    using var loaded = new Character(); loaded.FileName = path; Assert.IsTrue(await loaded.LoadAsync());
                    var saved = loaded.Drugs.Single(); Assert.AreEqual(drug.InternalId, saved.InternalId); Assert.AreEqual("Clear thinker", saved.Name);
                    Assert.AreEqual("Pharmaceutical", saved.Grade.Name);
                    Assert.AreEqual(1, saved.Components.Single(x => x.Name == "Smoothtalk").Level);
                    Assert.AreEqual(RuleResult.Changed, await ExpenseRules.UndoExpenseAsync(loaded, loaded.ExpenseEntries.Single()));
                    Assert.AreEqual(0, loaded.Drugs.Count); Assert.AreEqual(10000m, loaded.Nuyen);
                    Assert.IsFalse(loaded.Improvements.Any(x => x.SourceName == saved.InternalId));
                }
                finally { if (File.Exists(path)) File.Delete(path); }
            }
            finally { HostServices.Messages = oldMessages; }
        }

        [TestMethod]
        public async Task ArmorExportUsesWindowsZeroCapacityFormattingAcrossLanguages()
        {
            var armor = new Armor(_character) { Name = "Capacity test", ArmorCapacity = "8" };
            var plugin = new Gear(_character) { Name = "Full capacity", ArmorCapacity = "[8]", Quantity = 1.0m };
            await armor.GearChildren.AddAsync(plugin); await _character.Armor.AddAsync(armor);
            Assert.AreEqual(0m, await armor.GetCapacityRemainingAsync());
            foreach (string language in new[] { "en-us", "de-de" })
            {
                var exported = await _character.GenerateExportXml(System.Globalization.CultureInfo.GetCultureInfo(language), language);
                Assert.AreEqual("0", exported.SelectSingleNode("/characters/character/armors/armor[name_english='Capacity test']/capacityremaining").InnerText);
            }
        }

        [TestMethod]
        public async Task AuthoredSuiteRetainsNestedImplantsAndSuiteFlagsThroughSave()
        {
            Assert.AreEqual(RuleResult.Changed, await PacksRules.AddAsync(_character, "Basic Combat Eyes Pack", "Cyber Packs"));
            var source = Improvement.ImprovementSource.Cyberware;
            var document = await CyberwareSuiteRules.ExportAsync(_character, "Combat eyes", source);
            Assert.IsNotNull(document.SelectSingleNode("/chummer/suites/suite/cyberwares/cyberware/cyberwares/cyberware[name='Smartlink']"));
            var suite = document.SelectSingleNode("/chummer/suites/suite");
            var quote = await CyberwareSuiteRules.QuoteAsync(_character, suite, source);
            Assert.IsTrue(quote.Items.Any(x => x.Contains("Smartlink"))); Assert.AreEqual(1, _character.Cyberware.Count);
            Assert.AreEqual(RuleResult.Changed, await CyberwareSuiteRules.PurchaseAsync(_character, suite, source));
            var installed = _character.Cyberware.Single(x => x.Suite);
            Assert.IsTrue(installed.Children.Single(x => x.Name == "Smartlink").Suite);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            try
            {
                Assert.IsTrue(await _character.SaveAsync(path, false, false));
                using var loaded = new Character(); loaded.FileName = path; Assert.IsTrue(await loaded.LoadAsync());
                var saved = loaded.Cyberware.Single(x => x.Suite);
                Assert.AreEqual(installed.InternalId, saved.InternalId); Assert.IsTrue(saved.Children.Single(x => x.Name == "Smartlink").Suite);
                Assert.AreEqual(quote.Cost, saved.TotalCost);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [TestMethod]
        public async Task CyberwareSuiteQuoteDiscountPurchaseAndInsufficientFundsRollbackMatch()
        {
            var document = new XmlDocument(); document.LoadXml("<suite><name>Datajack bundle</name><grade>Standard</grade><cyberwares><cyberware><name>Datajack</name></cyberware></cyberwares></suite>");
            var suite = document.DocumentElement; var source = Improvement.ImprovementSource.Cyberware;
            int improvements = _character.Improvements.Count;
            var quote = await CyberwareSuiteRules.QuoteAsync(_character, suite, source);
            Assert.IsNotNull(quote); Assert.AreEqual(900m, quote.Cost); Assert.AreEqual(0.09m, quote.Essence);
            Assert.AreEqual(0, _character.Cyberware.Count); Assert.AreEqual(improvements, _character.Improvements.Count);
            await _character.SetCreatedAsync(true); await _character.SetNuyenAsync(1);
            var oldMessages = HostServices.Messages;
            try
            {
                HostServices.Messages = new AcceptMessages();
                Assert.AreEqual(RuleResult.NotEnoughNuyen, await CyberwareSuiteRules.PurchaseAsync(_character, suite, source));
                Assert.AreEqual(0, _character.Cyberware.Count); Assert.AreEqual(improvements, _character.Improvements.Count); Assert.AreEqual(1m, _character.Nuyen);
                Assert.AreEqual(0, _character.ExpenseEntries.Count);
                await _character.SetNuyenAsync(10000);
                Assert.AreEqual(RuleResult.Changed, await CyberwareSuiteRules.PurchaseAsync(_character, suite, source));
                Assert.IsTrue(_character.Cyberware.Single().Suite); Assert.AreEqual(quote.Cost, _character.Cyberware.Single().TotalCost);
                Assert.AreEqual(9100m, _character.Nuyen); Assert.AreEqual(-900m, _character.ExpenseEntries.Single().Amount);
                var exported = await CyberwareSuiteRules.ExportAsync(_character, "My bundle", source);
                Assert.AreEqual("Datajack", exported.SelectSingleNode("/chummer/suites/suite/cyberwares/cyberware/name").InnerText);
                Assert.AreEqual("Standard", exported.SelectSingleNode("/chummer/suites/suite/grade").InnerText);
                var second = (XmlDocument)exported.CloneNode(true); second.SelectSingleNode("/chummer/suites/suite/name").InnerText = "Other bundle";
                Assert.AreEqual(2, CyberwareSuiteRules.Append(exported, second).SelectNodes("/chummer/suites/suite").Count);
                Assert.ThrowsExactly<ArgumentException>(() => CyberwareSuiteRules.Append(exported, exported));
                Assert.AreEqual(1, exported.SelectNodes("/chummer/suites/suite").Count);
                var bad = (XmlDocument)document.CloneNode(true);
                var missing = bad.CreateElement("cyberware"); var missingName = bad.CreateElement("name"); missingName.InnerText = "Missing implant";
                missing.AppendChild(missingName); bad.SelectSingleNode("/suite/cyberwares").AppendChild(missing);
                int beforeBad = _character.Improvements.Count;
                await Assert.ThrowsExactlyAsync<ArgumentException>(() => CyberwareSuiteRules.PurchaseAsync(_character, bad.DocumentElement, source));
                Assert.AreEqual(1, _character.Cyberware.Count); Assert.AreEqual(9100m, _character.Nuyen);
                Assert.AreEqual(beforeBad, _character.Improvements.Count); Assert.AreEqual(1, _character.ExpenseEntries.Count);
            }
            finally { HostServices.Messages = oldMessages; }
        }

        [TestMethod]
        public async Task NaturalWeaponRetainsDamageSkillAndSourceThroughSaveAndRemoval()
        {
            int count = _character.Weapons.Count; decimal funds = _character.Nuyen;
            var design = new NaturalWeaponDesign { Name = "Talons", DamageBase = "({STR})", DamageModifier = 2, ArmorPenetration = -3, Reach = 1 };
            Assert.AreEqual(RuleResult.Changed, await NaturalWeaponRules.AddAsync(_character, design));
            var weapon = _character.Weapons.Single(x => x.Name == "Talons");
            Assert.AreEqual("({STR})+2P", weapon.Damage); Assert.AreEqual("-3", weapon.AP); Assert.AreEqual("1", weapon.Reach);
            Assert.AreEqual("Physical", weapon.Accuracy); Assert.AreEqual("Unarmed Combat", weapon.UseSkill);
            Assert.AreEqual(0m, weapon.TotalCost); Assert.AreEqual(funds, _character.Nuyen); Assert.AreEqual(0, _character.ExpenseEntries.Count);
            design.Skill = "Hacking";
            Assert.AreEqual(RuleResult.NotAllowed, await NaturalWeaponRules.AddAsync(_character, design));
            design.Skill = "Unarmed Combat"; design.DamageBase = "{STR} + 1000";
            Assert.AreEqual(RuleResult.NotAllowed, await NaturalWeaponRules.AddAsync(_character, design));
            Assert.AreEqual(count + 1, _character.Weapons.Count);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            try
            {
                Assert.IsTrue(await _character.SaveAsync(path, false, false));
                using var loaded = new Character(); loaded.FileName = path; Assert.IsTrue(await loaded.LoadAsync());
                var saved = loaded.Weapons.Single(x => x.Name == "Talons");
                Assert.AreEqual(weapon.InternalId, saved.InternalId); Assert.AreEqual(weapon.Damage, saved.Damage); Assert.AreEqual(weapon.UseSkill, saved.UseSkill);
                await saved.DeleteWeaponAsync(); Assert.AreEqual(count, loaded.Weapons.Count);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [TestMethod]
        public async Task CustomLimitModifiersPreserveIdentityNotesAndRejectAutomaticModifierEdits()
        {
            Assert.AreEqual(RuleResult.Changed, await LimitModifierRules.SaveAsync(_character, "Careful aim", "Physical", 2, "While aiming", "GM ruling"));
            var modifier = _character.LimitModifiers.Single(); string id = modifier.InternalId;
            Assert.AreEqual("While aiming", modifier.Condition);
            Assert.AreEqual(RuleResult.Changed, await LimitModifierRules.SaveAsync(_character, "Careful work", "Mental", -1, "While crafting", modifier.Notes, modifier));
            var edited = _character.LimitModifiers.Single(); Assert.AreEqual(id, edited.InternalId);
            Assert.AreEqual("While crafting", edited.Condition); Assert.AreEqual("GM ruling", edited.Notes); Assert.AreEqual(-1, edited.Bonus);
            Assert.AreEqual(RuleResult.NotAllowed, await LimitModifierRules.SaveAsync(_character, "Invalid", "Essence", 1, "", ""));
            var automatic = new LimitModifier(_character); automatic.Create("Equipment effect", 1, "Physical", "", false);
            await _character.LimitModifiers.AddAsync(automatic);
            Assert.AreEqual(RuleResult.NotAllowed, await LimitModifierRules.RemoveAsync(_character, automatic));
            Assert.AreEqual(RuleResult.NotAllowed, await LimitModifierRules.SaveAsync(_character, "Overridden", "Social", 5, "", "", automatic));
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            try
            {
                Assert.IsTrue(await _character.SaveAsync(path, false, false));
                using var loaded = new Character(); loaded.FileName = path; Assert.IsTrue(await loaded.LoadAsync());
                var saved = loaded.LimitModifiers.Single(x => x.InternalId == id);
                Assert.AreEqual("Mental", saved.Limit); Assert.AreEqual("While crafting", saved.Condition); Assert.AreEqual("GM ruling", saved.Notes);
                Assert.AreEqual(RuleResult.Changed, await LimitModifierRules.RemoveAsync(loaded, saved));
                Assert.IsFalse(loaded.LimitModifiers.Single().CanDelete);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [TestMethod]
        public async Task PacksExportRetainsNestedSelectionsQuantitiesAndSectionChoices()
        {
            var originalDialogs = HostServices.Dialogs;
            try
            {
                HostServices.Dialogs = new PacksTextAnswers();
                Assert.AreEqual(RuleResult.Changed, await PacksRules.AddAsync(_character, "Intro Runner Pack", "Core Packs"));
                var exported = await PacksExporter.ExportAsync(_character, "Test & <kit>", new PacksExportOptions());
                var pack = exported.SelectSingleNode("/chummer/packs/pack");
                Assert.AreEqual("Test & <kit>", pack["name"].InnerText); Assert.AreEqual("Custom", pack["category"].InnerText);
                Assert.AreEqual("100", pack.SelectSingleNode("gears/gear[name='Ammo: Regular Ammo']/qty").InnerText);
                Assert.IsNotNull(pack.SelectSingleNode("gears/gear[name='Glasses']/gears/gear"));
                Assert.IsNotNull(pack.SelectSingleNode("weapons/weapon[name='Colt America L36']/accessories/accessory[name='Spare Clip']"));
                var options = new PacksExportOptions { Gear = false, Weapons = false, Attributes = false };
                var filtered = await PacksExporter.ExportAsync(_character, "Filtered", options);
                Assert.IsNull(filtered.SelectSingleNode("/chummer/packs/pack/gears"));
                Assert.IsNull(filtered.SelectSingleNode("/chummer/packs/pack/weapons"));
                Assert.IsNull(filtered.SelectSingleNode("/chummer/packs/pack/attributes"));
                var appended = PacksExporter.Append(exported, filtered);
                Assert.AreEqual(2, appended.SelectNodes("/chummer/packs/pack").Count);
                Assert.AreEqual(1, exported.SelectNodes("/chummer/packs/pack").Count);
                Assert.ThrowsExactly<ArgumentException>(() => PacksExporter.Append(exported, exported));
                Assert.AreEqual(100m, _character.Gear.Single(x => x.Name == "Ammo: Regular Ammo").Quantity);
            }
            finally { HostServices.Dialogs = originalDialogs; }
        }

        [TestMethod]
        public async Task DroneMountModQuoteAndListingUseParentSlotsWithoutMutatingMount()
        {
            _settings.DroneMods = true;
            var node = _character.LoadData("vehicles.xml").SelectSingleNode("/chummer/vehicles/vehicle[contains(category,'Drones') and source='SR5']");
            Assert.IsNotNull(node);
            var vehicle = new Vehicle(_character);
            await vehicle.CreateAsync(node, blnCreateChildren: false, blnCreateImprovements: false, blnSkipSelectForms: true);
            await _character.Vehicles.AddAsync(vehicle);
            var configuration = new WeaponMountConfiguration { SizeId = "079a5c61-aee6-4383-81b7-32540f7a0a0b", ControlId = "70f99fae-470f-442c-a607-ef3465e27f37",
                FlexibilityId = "4a020775-9258-486a-a03f-7a48dd5c3bf9", VisibilityId = "582c31e0-1374-4ebb-b201-a20cb815d6dd" };
            var mount = (WeaponMount)(await WeaponMountRules.PurchaseAsync(_character, vehicle, configuration)).Item;
            Assert.IsNotNull(mount);
            _settings.BooksWritable.Add("R5"); _settings.OnPropertyChanged(nameof(CharacterSettings.Books));
            var catalog = new EquipmentCatalog(_character, EquipmentKind.WeaponMountMod, mount);
            var request = new PurchaseRequest { Kind = EquipmentKind.WeaponMountMod, Parent = mount, ItemId = "45d84d77-5461-4441-a529-527aba347334" };
            decimal expected = mount.CalculatedSlots * 25m;
            Assert.IsTrue(expected > 0);
            Assert.IsTrue(vehicle.IsDrone, vehicle.Category);
            Assert.IsTrue(WeaponMountRules.IsModAllowed(_character, mount, catalog.Node(request.ItemId)), vehicle.Name);
            Assert.IsNotNull(_character.LoadDataXPath("vehicles.xml").SelectSingleNode("/chummer/weaponmountmods/mod[id='" + request.ItemId + "' and " + _settings.BookXPath() + "]"), _settings.BookXPath());
            Assert.AreEqual(expected, (await catalog.QuoteAsync(request)).Price);
            Assert.AreEqual(expected, catalog.GetListing(new CatalogQuery()).Rows.Single(x => x.Id == request.ItemId).Cost);
            Assert.AreEqual(0, mount.Mods.Count);
            var purchase = await PurchaseRules.PurchaseAsync(_character, request);
            Assert.AreEqual(RuleResult.Changed, purchase.Result); Assert.AreEqual(expected, purchase.Cost);
            Assert.AreEqual(expected, ((VehicleMod)purchase.Item).TotalCost);
        }

        [TestMethod]
        public async Task MountModPurchaseChecksVehicleRestrictionsAndSupportsExpenseUndo()
        {
            var vehicle = new Vehicle(_character) { Name = "Armed van", Body = 10 };
            await _character.Vehicles.AddAsync(vehicle);
            await _character.SetCreatedAsync(true); await _character.SetNuyenAsync(10000);
            _settings.EnforceCapacity = false;
            var configuration = new WeaponMountConfiguration { SizeId = "079a5c61-aee6-4383-81b7-32540f7a0a0b", ControlId = "70f99fae-470f-442c-a607-ef3465e27f37",
                FlexibilityId = "4a020775-9258-486a-a03f-7a48dd5c3bf9", VisibilityId = "582c31e0-1374-4ebb-b201-a20cb815d6dd" };
            var mount = (WeaponMount)(await WeaponMountRules.PurchaseAsync(_character, vehicle, configuration)).Item;
            _settings.BooksWritable.Add("R5"); _settings.OnPropertyChanged(nameof(CharacterSettings.Books));
            var catalog = new EquipmentCatalog(_character, EquipmentKind.WeaponMountMod, mount);
            var request = new PurchaseRequest { Kind = EquipmentKind.WeaponMountMod, Parent = mount, ItemId = "6741d080-0e40-4941-a7b5-85ab3e1a9cb2", Markup = 10 };
            var quote = await catalog.QuoteAsync(request);
            Assert.AreEqual(220m, quote.Price);
            decimal balance = _character.Nuyen;
            var purchase = await PurchaseRules.PurchaseAsync(_character, request);
            Assert.AreEqual(RuleResult.Changed, purchase.Result);
            Assert.AreEqual(quote.Price, purchase.Cost); Assert.AreEqual(balance - 220m, _character.Nuyen);
            Assert.AreSame(mount, ((VehicleMod)purchase.Item).WeaponMountParent);
            Assert.AreEqual(1, mount.Mods.Count); Assert.AreEqual(0, vehicle.Mods.Count);
            Assert.AreEqual(NuyenExpenseType.AddVehicleWeaponMountMod, purchase.Expense.Undo.NuyenType);
            request.ItemId = "45d84d77-5461-4441-a529-527aba347334";
            Assert.AreEqual(RuleResult.NotAllowed, (await PurchaseRules.PurchaseAsync(_character, request)).Result);
            Assert.AreEqual(balance - 220m, _character.Nuyen);
            var oldMessages = HostServices.Messages;
            try
            {
                HostServices.Messages = new AcceptMessages();
                Assert.AreEqual(RuleResult.Changed, await ExpenseRules.UndoExpenseAsync(_character, purchase.Expense));
                Assert.AreEqual(0, mount.Mods.Count); Assert.AreEqual(balance, _character.Nuyen);
            }
            finally { HostServices.Messages = oldMessages; }
        }

        [TestMethod]
        public async Task MountedWeaponUsesVehicleContainerCapacityAndVehicleWeaponExpenseUndo()
        {
            var vehicle = new Vehicle(_character) { Name = "Armed van", Body = 10 }; await _character.Vehicles.AddAsync(vehicle);
            await _character.SetCreatedAsync(true); await _character.SetNuyenAsync(10000); _settings.EnforceCapacity = false;
            var configuration = new WeaponMountConfiguration { SizeId = "079a5c61-aee6-4383-81b7-32540f7a0a0b", ControlId = "70f99fae-470f-442c-a607-ef3465e27f37",
                FlexibilityId = "4a020775-9258-486a-a03f-7a48dd5c3bf9", VisibilityId = "582c31e0-1374-4ebb-b201-a20cb815d6dd" };
            var mountResult = await WeaponMountRules.PurchaseAsync(_character, vehicle, configuration);
            Assert.AreEqual(RuleResult.Changed, mountResult.Result);
            var mount = (WeaponMount)mountResult.Item;
            decimal balance = _character.Nuyen;
            var weaponNode = _character.LoadData("weapons.xml").SelectSingleNode("/chummer/weapons/weapon[name='Ares Predator V']");
            var invalid = _character.LoadDataXPath("weapons.xml").SelectSingleNode("/chummer/weapons/weapon[category='Heavy Machine Guns']");
            Assert.IsNotNull(invalid);
            Assert.IsFalse(WeaponMountRules.IsWeaponAllowed(mount, invalid));
            int rootWeapons = _character.Weapons.Count;
            var request = new PurchaseRequest { Kind = EquipmentKind.Weapon, ItemId = weaponNode["id"].InnerText, Parent = mount };
            var purchase = await PurchaseRules.PurchaseAsync(_character, request);
            Assert.AreEqual(RuleResult.Changed, purchase.Result);
            var weapon = (Weapon)purchase.Item;
            Assert.AreSame(mount, weapon.ParentMount); Assert.AreSame(vehicle, weapon.ParentVehicle);
            Assert.AreEqual(1, mount.Weapons.Count); Assert.AreEqual(rootWeapons, _character.Weapons.Count);
            Assert.IsTrue(mount.IsWeaponsFull); Assert.AreEqual(balance - purchase.Cost, _character.Nuyen);
            Assert.AreEqual(NuyenExpenseType.AddVehicleWeapon, purchase.Expense.Undo.NuyenType);
            decimal afterPurchase = _character.Nuyen;
            Assert.AreEqual(RuleResult.NotAllowed, (await PurchaseRules.PurchaseAsync(_character, request)).Result);
            Assert.AreEqual(afterPurchase, _character.Nuyen); Assert.AreEqual(2, _character.ExpenseEntries.Count);
            var oldMessages = HostServices.Messages;
            try
            {
                HostServices.Messages = new AcceptMessages();
                Assert.AreEqual(RuleResult.Changed, await ExpenseRules.UndoExpenseAsync(_character, purchase.Expense));
                Assert.AreEqual(0, mount.Weapons.Count); Assert.AreEqual(1, vehicle.WeaponMounts.Count);
                Assert.AreEqual(balance, _character.Nuyen);
            }
            finally { HostServices.Messages = oldMessages; }
        }

        [TestMethod]
        public async Task WeaponMountQuoteAndCareerPurchaseUseOptionsMarkupAndUndo()
        {
            var vehicle = new Vehicle(_character) { Name = "Mount test van", Body = 10 };
            await _character.Vehicles.AddAsync(vehicle);
            await _character.SetCreatedAsync(true); await _character.SetNuyenAsync(10000);
            _settings.EnforceCapacity = false; _settings.MultiplyForbiddenCost = true; _settings.ForbiddenCostMultiplier = 2;
            var input = new WeaponMountConfiguration { SizeId = "079a5c61-aee6-4383-81b7-32540f7a0a0b", ControlId = "741bb6e2-0c6b-4af2-82b0-0c4b14c60367",
                FlexibilityId = "4a020775-9258-486a-a03f-7a48dd5c3bf9", VisibilityId = "582c31e0-1374-4ebb-b201-a20cb815d6dd", Markup = 20 };
            var quote = await WeaponMountRules.QuoteAsync(_character, vehicle, input);
            Assert.IsNotNull(quote); Assert.AreEqual(7200m, quote.Cost); Assert.AreEqual(3, quote.Slots); Assert.IsTrue(quote.CanAfford);
            Assert.AreEqual(0, vehicle.WeaponMounts.Count); Assert.AreEqual(10000m, _character.Nuyen);
            Assert.AreEqual(0, _character.ExpenseEntries.Count);
            var purchase = await WeaponMountRules.PurchaseAsync(_character, vehicle, input);
            Assert.AreEqual(RuleResult.Changed, purchase.Result); Assert.AreEqual(7200m, purchase.Cost); Assert.AreEqual(2800m, _character.Nuyen);
            var mount = (WeaponMount)purchase.Item;
            Assert.AreEqual(3, mount.WeaponMountOptions.Count); Assert.AreEqual(1, vehicle.WeaponMounts.Count);
            Assert.AreEqual(NuyenExpenseType.AddVehicleWeaponMount, purchase.Expense.Undo.NuyenType);
            string path = Path.Combine(Path.GetTempPath(), "chummer-mount-" + Guid.NewGuid().ToString("N") + ".chum5");
            var oldMessages = HostServices.Messages;
            try
            {
                Assert.IsTrue(await _character.SaveAsync(path, false, false));
                using var loaded = UnitTestUtils.LoadCharacter(new FileInfo(path));
                Assert.AreEqual(mount.InternalId, loaded.Vehicles.Single().WeaponMounts.Single().InternalId);
                Assert.AreEqual(3, loaded.Vehicles.Single().WeaponMounts.Single().WeaponMountOptions.Count);
                HostServices.Messages = new AcceptMessages();
                Assert.AreEqual(RuleResult.Changed, await ExpenseRules.UndoExpenseAsync(loaded, loaded.ExpenseEntries.Single()));
                Assert.AreEqual(0, loaded.Vehicles.Single().WeaponMounts.Count); Assert.AreEqual(10000m, loaded.Nuyen);
            }
            finally { HostServices.Messages = oldMessages; File.Delete(path); }
        }

        [TestMethod]
        public async Task WeaponMountRejectsInvalidOptionsInsufficientFundsAndCapacityWithoutCharging()
        {
            var vehicle = new Vehicle(_character) { Name = "Tiny drone", Body = 0 }; await _character.Vehicles.AddAsync(vehicle);
            await _character.SetCreatedAsync(true); await _character.SetNuyenAsync(0);
            _settings.EnforceCapacity = true;
            var input = new WeaponMountConfiguration { SizeId = "079a5c61-aee6-4383-81b7-32540f7a0a0b", ControlId = "741bb6e2-0c6b-4af2-82b0-0c4b14c60367",
                FlexibilityId = "4a020775-9258-486a-a03f-7a48dd5c3bf9", VisibilityId = "582c31e0-1374-4ebb-b201-a20cb815d6dd" };
            var oldMessages = HostServices.Messages;
            try
            {
                HostServices.Messages = new AcceptMessages();
                Assert.AreEqual(RuleResult.NotEnoughNuyen, (await WeaponMountRules.PurchaseAsync(_character, vehicle, input)).Result);
                Assert.AreEqual(0, vehicle.WeaponMounts.Count); Assert.AreEqual(0, _character.ExpenseEntries.Count);
                await _character.SetNuyenAsync(10000);
                input.SizeId = "a567c5d3-38b8-496a-add8-1e176384e935"; // Heavy: six slots exceeds the vehicle's minimum four.
                Assert.AreEqual(RuleResult.NotAllowed, (await WeaponMountRules.PurchaseAsync(_character, vehicle, input)).Result);
                Assert.AreEqual(0, vehicle.WeaponMounts.Count); Assert.AreEqual(10000m, _character.Nuyen); Assert.AreEqual(0, _character.ExpenseEntries.Count);
                input.ControlId = "not a control";
                Assert.IsNull(await WeaponMountRules.QuoteAsync(_character, vehicle, input));
                Assert.AreEqual(RuleResult.NotAllowed, (await WeaponMountRules.PurchaseAsync(_character, vehicle, input)).Result);
            }
            finally { HostServices.Messages = oldMessages; }
            var sizes = await WeaponMountRules.OptionsAsync(_character, vehicle, "Size");
            Assert.IsFalse(sizes.Any(x => x.Name == "Built-In")); Assert.IsTrue(sizes.Any(x => x.Id == input.SizeId));
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
        public async Task InventoryOrderPersistsAndRejectsPartialForeignAndNestedLists()
        {
            var first = new Gear(_character) { Name = "First", Category = "Test", Quantity = 2 };
            var second = new Gear(_character) { Name = "Second", Category = "Test" };
            var child = new Gear(_character) { Name = "Plugin", Parent = first };
            await first.Children.AddAsync(child);
            await _character.Gear.AddAsync(first); await _character.Gear.AddAsync(second);
            Assert.AreEqual(RuleResult.NotAllowed, await InventoryOrderRules.ReorderAsync(_character, new object[] { first }));
            Assert.AreEqual(RuleResult.NotAllowed, await InventoryOrderRules.ReorderAsync(_character, new object[] { first, first }));
            Assert.AreEqual(RuleResult.NotAllowed, await InventoryOrderRules.ReorderAsync(_character, new object[] { first, child }));
            using var foreign = new Character();
            var outsider = new Gear(foreign);
            Assert.AreEqual(RuleResult.NotAllowed, await InventoryOrderRules.ReorderAsync(_character, new object[] { first, outsider }));
            using var cancellation = new System.Threading.CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => InventoryOrderRules.ReorderAsync(_character, new object[] { second, first }, cancellation.Token));
            Assert.AreEqual(0, first.SortOrder);
            Assert.AreEqual(RuleResult.Changed, await InventoryOrderRules.ReorderAsync(_character, new object[] { second, first }));
            Assert.AreEqual(1, second.SortOrder); Assert.AreEqual(2, first.SortOrder);
            Assert.AreSame(child, first.Children.Single()); Assert.AreEqual(2m, first.Quantity);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            try
            {
                Assert.IsTrue(await _character.SaveAsync(path));
                using var loaded = new Character(); Assert.IsTrue(await loaded.LoadAsync(path));
                CollectionAssert.AreEqual(new[] { "Second", "First" }, loaded.Gear.OrderBy(x => x.SortOrder).Select(x => x.Name).ToArray());
                Assert.AreEqual(child.InternalId, loaded.Gear.Single(x => x.Name == "First").Children.Single().InternalId);
            }
            finally { File.Delete(path); outsider.Dispose(); }
        }

        [TestMethod]
        public async Task NestedInventoryOrderPreservesContainersAndSavedAttachments()
        {
            var bag = new Gear(_character) { Name = "Bag" };
            var other = new Gear(_character) { Name = "Other bag" };
            var first = new Gear(_character) { Name = "First plugin" };
            var second = new Gear(_character) { Name = "Second plugin", Quantity = 3 };
            var cousin = new Gear(_character) { Name = "Other plugin" };
            var nested = new Gear(_character) { Name = "Deep plugin" };
            await _character.Gear.AddAsync(bag); await _character.Gear.AddAsync(other);
            await bag.Children.AddAsync(first); await bag.Children.AddAsync(second);
            await other.Children.AddAsync(cousin); await first.Children.AddAsync(nested);
            Assert.IsTrue(InventoryOrderRules.CanOrder(_character, nested));
            Assert.AreEqual(RuleResult.NotAllowed, await InventoryOrderRules.ReorderAsync(_character, new object[] { first, cousin }));
            Assert.AreEqual(RuleResult.NotAllowed, await InventoryOrderRules.ReorderAsync(_character, new object[] { first }));
            Assert.AreEqual(RuleResult.Changed, await InventoryOrderRules.ReorderAsync(_character, new object[] { second, first }));
            Assert.AreSame(bag, first.Parent); Assert.AreSame(bag, second.Parent);
            Assert.AreEqual(3m, second.Quantity); Assert.AreSame(nested, first.Children.Single());
            Assert.AreEqual(0, cousin.SortOrder); Assert.AreEqual(0, nested.SortOrder);
            Assert.AreEqual(0, _character.ExpenseEntries.Count);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            try
            {
                Assert.IsTrue(await _character.SaveAsync(path));
                using var loaded = new Character(); Assert.IsTrue(await loaded.LoadAsync(path));
                var restored = loaded.Gear.Single(x => x.InternalId == bag.InternalId);
                CollectionAssert.AreEqual(new[] { second.InternalId, first.InternalId }, restored.Children.OrderBy(x => x.SortOrder).Select(x => x.InternalId).ToArray());
                Assert.AreEqual(nested.InternalId, restored.Children.Single(x => x.InternalId == first.InternalId).Children.Single().InternalId);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public async Task ContactRatingsValidateOwnershipBoundsAndGroupLoyaltyAndPersist()
        {
            var contact = new Contact(_character) { Name = "Street doctor" };
            await _character.Contacts.AddAsync(contact);
            Assert.AreEqual(RuleResult.Changed, await ContactRules.SetRatingAsync(_character, contact, 3, true));
            Assert.AreEqual(RuleResult.Changed, await ContactRules.SetRatingAsync(_character, contact, 4, false));
            Assert.AreEqual(RuleResult.Cancelled, await ContactRules.SetRatingAsync(_character, contact, 4, false));
            Assert.AreEqual(RuleResult.NotAllowed, await ContactRules.SetRatingAsync(_character, contact, 0, true));
            Assert.AreEqual(RuleResult.NotAllowed, await ContactRules.SetRatingAsync(_character, contact, _character.MaxContactConnection + 1, true));
            Assert.AreEqual(RuleResult.NotAllowed, await ContactRules.SetRatingAsync(_character, contact, 7, false));
            using var foreign = new Character();
            Assert.AreEqual(RuleResult.NotAllowed, await ContactRules.SetRatingAsync(foreign, contact, 2, true));
            await contact.SetIsGroupAsync(true);
            Assert.AreEqual(RuleResult.NotAllowed, await ContactRules.SetRatingAsync(_character, contact, 2, false));
            await contact.SetIsGroupAsync(false);
            using var cancellation = new System.Threading.CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => ContactRules.SetRatingAsync(_character, contact, 6, true, cancellation.Token));
            Assert.AreEqual(3, contact.Connection); Assert.AreEqual(4, contact.Loyalty);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            try
            {
                Assert.IsTrue(await _character.SaveAsync(path));
                using var loaded = new Character(); Assert.IsTrue(await loaded.LoadAsync(path));
                var restored = loaded.Contacts.Single(x => x.InternalId == contact.InternalId);
                Assert.AreEqual(3, restored.Connection); Assert.AreEqual(4, restored.Loyalty);
                Assert.AreEqual(0, _character.ExpenseEntries.Count);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public async Task ContactFileImportsPreserveLegacyTypesAndLinkedCharacterStats()
        {
            string linkedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            string parentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            using var linked = new Character { SettingsKey = _key, Name = "Companion" };
            try
            {
                await linked.SetFileNameAsync(linkedPath);
                Assert.IsTrue(await linked.SaveAsync(linkedPath));
                await CharacterRegistry.OpenCharacters.AddAsync(linked);
                foreach (var type in new[] { ContactType.Contact, ContactType.Enemy, ContactType.Pet })
                    Assert.AreEqual(RuleResult.Changed, await ContactRules.AddFromFileAsync(_character, linkedPath, type));
                Assert.AreEqual(3, _character.Contacts.Count);
                Assert.IsTrue(_character.Contacts.All(x => ReferenceEquals(linked, x.LinkedCharacter)));
                Assert.IsTrue(await _character.SaveAsync(parentPath));
                using var restored = new Character(); Assert.IsTrue(await restored.LoadAsync(parentPath));
                CollectionAssert.AreEquivalent(new[] { ContactType.Contact, ContactType.Enemy, ContactType.Pet }, restored.Contacts.Select(x => x.EntityType).ToArray());
                Assert.IsTrue(restored.Contacts.All(x => x.FileName == linkedPath && ReferenceEquals(x.LinkedCharacter, linked)));
                Assert.AreEqual(linked.BOD.TotalValue, restored.Contacts.Single(x => x.EntityType == ContactType.Pet).LinkedCharacter.BOD.TotalValue);
            }
            finally
            {
                foreach (var contact in _character.Contacts.ToArray()) await contact.SetFileNameAsync(string.Empty);
                await CharacterRegistry.OpenCharacters.RemoveAsync(linked);
                File.Delete(linkedPath); File.Delete(parentPath);
            }
        }

        [TestMethod]
        public async Task ContactFileLinkRejectsForeignOwnershipMissingFilesSelfAndCancellation()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            var contact = new Contact(_character); await _character.Contacts.AddAsync(contact);
            await _character.SetFileNameAsync(path);
            Assert.IsTrue(await _character.SaveAsync(path));
            try
            {
                Assert.AreEqual(RuleResult.NotAllowed, await ContactRules.AddFromFileAsync(_character, path, ContactType.Pet));
                Assert.AreEqual(RuleResult.NotAllowed, await ContactRules.LinkAsync(_character, contact, path));
                Assert.AreEqual(RuleResult.NotAllowed, await ContactRules.LinkAsync(_character, contact, path + ".missing"));
                using var foreign = new Character();
                Assert.AreEqual(RuleResult.NotAllowed, await ContactRules.LinkAsync(foreign, contact, path));
                using var cancelled = new System.Threading.CancellationTokenSource(); cancelled.Cancel();
                await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => ContactRules.AddFromFileAsync(_character, path, ContactType.Pet, cancelled.Token));
                Assert.AreEqual(1, _character.Contacts.Count); Assert.AreEqual(string.Empty, contact.FileName);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public async Task NotebookUsesOnlyLegacyNotesFieldAcrossCharacterSaveAndReload()
        {
            var pages = new[] { new NotebookPageData(Guid.NewGuid(), "Sessions", "Meet the fixer."), new NotebookPageData(Guid.NewGuid(), "Clues", "A blue van.") };
            string text = NotebookCodec.Serialize(pages); _character.Notes = text;
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            try
            {
                Assert.IsTrue(await _character.SaveAsync(path));
                var xml = new XmlDocument(); xml.Load(path);
                Assert.AreEqual(text, xml.SelectSingleNode("/character/notes")?.InnerText);
                Assert.AreEqual(0, xml.SelectNodes("//notebook").Count);
                using var restored = new Character(); Assert.IsTrue(await restored.LoadAsync(path));
                var loaded = NotebookCodec.Parse(restored.Notes);
                CollectionAssert.AreEqual(pages.Select(x => x.Id).ToArray(), loaded.Select(x => x.Id).ToArray());
                Assert.AreEqual("A blue van.", loaded[1].Body);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public void CritterPowerChoicesHonorBookVisibilityAndCategoryLimits()
        {
            _settings.BooksWritable.Clear(); _settings.BooksWritable.Add("SR5"); _settings.OnPropertyChanged(nameof(CharacterSettings.Books));
            var choices = MagicRules.CritterPowerOptions(_character);
            Assert.IsFalse(choices.Any(x => x.Id == "4aef932e-2d0f-4226-b00e-19c5398641bd"));
            Assert.IsFalse(choices.Any(x => x.Name == "Combat Skill"));
            Assert.IsTrue(choices.Any(x => x.Name == "Hardened Armor"));
            ImprovementManager.CreateImprovement(_character, "Mundane", Improvement.ImprovementSource.Custom, "category-test",
                Improvement.ImprovementType.LimitCritterPowerCategory, "");
            choices = MagicRules.CritterPowerOptions(_character);
            Assert.IsFalse(choices.Any(x => x.Name == "Hardened Armor"));
            Assert.IsTrue(choices.All(x => x.Details.StartsWith("Mundane", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task CritterPowerRatingAppliesEffectsAndPersistsThroughLegacySave()
        {
            var choice = MagicRules.CritterPowerOptions(_character).Single(x => x.Name == "Hardened Armor");
            int armorBefore = _character.TotalArmorRating;
            Assert.AreEqual(RuleResult.NotAllowed, await MagicRules.AddCritterPowerAsync(_character, choice.Id, 0));
            Assert.AreEqual(RuleResult.Changed, await MagicRules.AddCritterPowerAsync(_character, choice.Id, 4));
            var power = _character.CritterPowers.Single(); Assert.AreEqual(4, power.Rating);
            Assert.AreEqual(armorBefore + 4, _character.TotalArmorRating);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            try
            {
                Assert.IsTrue(await _character.SaveAsync(path));
                using var loaded = new Character(); Assert.IsTrue(await loaded.LoadAsync(path));
                Assert.AreEqual(power.InternalId, loaded.CritterPowers.Single().InternalId);
                Assert.AreEqual(armorBefore + 4, loaded.TotalArmorRating);
            }
            finally { File.Delete(path); }
            Assert.IsTrue(await power.RemoveAsync(false)); Assert.AreEqual(armorBefore, _character.TotalArmorRating);
        }

        [TestMethod]
        public async Task CareerCritterPowerChargesKarmaAndExpenseUndoRemovesIt()
        {
            _settings.BooksWritable.Add("HS"); _settings.OnPropertyChanged(nameof(CharacterSettings.Books));
            await _character.SetCreatedAsync(true); _character.Karma = 20;
            var choice = MagicRules.CritterPowerOptions(_character).Single(x => x.Id == "4aef932e-2d0f-4226-b00e-19c5398641bd");
            bool confirm = GlobalSettings.ConfirmKarmaExpense; GlobalSettings.ConfirmKarmaExpense = false;
            var messages = HostServices.Messages; HostServices.Messages = new AcceptMessages();
            try
            {
                Assert.AreEqual(RuleResult.Changed, await MagicRules.AddCritterPowerAsync(_character, choice.Id));
                Assert.AreEqual(11, _character.Karma); Assert.AreEqual(1, _character.CritterPowers.Count);
                var expense = _character.ExpenseEntries.Single(); Assert.AreEqual(-9m, expense.Amount);
                Assert.AreEqual(KarmaExpenseType.AddCritterPower, expense.Undo.KarmaType);
                Assert.AreEqual(RuleResult.Changed, await ExpenseRules.UndoExpenseAsync(_character, expense));
                Assert.AreEqual(20, _character.Karma); Assert.AreEqual(0, _character.CritterPowers.Count);
            }
            finally { GlobalSettings.ConfirmKarmaExpense = confirm; HostServices.Messages = messages; }
        }

        [TestMethod]
        public async Task CritterPowerInsufficientFundsInvalidRatingsAndUnknownIdsDoNotMutate()
        {
            _settings.BooksWritable.Add("HS"); _settings.OnPropertyChanged(nameof(CharacterSettings.Books));
            await _character.SetCreatedAsync(true); _character.Karma = 0;
            var choice = MagicRules.CritterPowerOptions(_character).Single(x => x.Id == "4aef932e-2d0f-4226-b00e-19c5398641bd");
            var messages = HostServices.Messages; HostServices.Messages = new AcceptMessages();
            int improvements = _character.Improvements.Count;
            try
            {
                Assert.AreEqual(RuleResult.NotEnoughKarma, await MagicRules.AddCritterPowerAsync(_character, choice.Id));
                Assert.AreEqual(RuleResult.NotAllowed, await MagicRules.AddCritterPowerAsync(_character, choice.Id, 1));
                Assert.AreEqual(RuleResult.NotAllowed, await MagicRules.AddCritterPowerAsync(_character, Guid.NewGuid().ToString()));
                Assert.AreEqual(0, _character.CritterPowers.Count); Assert.AreEqual(0, _character.ExpenseEntries.Count);
                Assert.AreEqual(improvements, _character.Improvements.Count); Assert.AreEqual(0, _character.Karma);
            }
            finally { HostServices.Messages = messages; }
        }

        [TestMethod]
        public void IncludedArmorModCatalogUsesRequestedCategoryInsteadOfNormalParentCategories()
        {
            _settings.BooksWritable.Add("RG"); _settings.OnPropertyChanged(nameof(CharacterSettings.Books));
            using var armor = new Armor(_character) { Name = "Ares Victory: Globetrotter Jacket", Category = "High-Fashion Armor Clothing" };
            var catalog = new EquipmentCatalog(_character, EquipmentKind.ArmorMod, armor, "Custom Liners (Rating 4)", true);
            var rows = catalog.GetListing(new CatalogQuery { Free = true }).Rows;
            Assert.IsTrue(rows.Count > 0);
            Assert.IsTrue(rows.All(x => x.Category == "Custom Liners (Rating 4)"));
            Assert.AreEqual(0, armor.ArmorMods.Count);
        }

        [TestMethod]
        public void ArmorModCatalogRejectsHelmetForTheWrongParent()
        {
            using var full = new Armor(_character) { Name = "Full Body Armor", Category = "Armor" };
            using var coat = new Armor(_character) { Name = "Lined Coat", Category = "Armor" };
            var fullChoices = new EquipmentCatalog(_character, EquipmentKind.ArmorMod, full, "General", true).GetListing(new CatalogQuery { Free = true }).Rows;
            var coatChoices = new EquipmentCatalog(_character, EquipmentKind.ArmorMod, coat, "General", true).GetListing(new CatalogQuery { Free = true }).Rows;
            Assert.IsTrue(fullChoices.Any(x => x.Name == "Full Body Armor: Helmet"));
            Assert.IsFalse(coatChoices.Any(x => x.Name == "Full Body Armor: Helmet"));
        }

        [TestMethod]
        public void QualityCatalogPreviewHonorsCustomTextWithoutCreatingAQuality()
        {
            var xml = new XmlDocument();
            xml.LoadXml("<quality><name>First Impression</name><description>Custom house rule.</description></quality>");
            int count = _character.Qualities.Count;
            Assert.AreEqual("Custom house rule.", QualityDescriptionRules.Description(xml.DocumentElement.CreateNavigator()));
            xml.LoadXml("<quality><name>First Impression</name></quality>");
            StringAssert.Contains(QualityDescriptionRules.Description(xml.DocumentElement.CreateNavigator()), "first time");
            xml.LoadXml("<quality><bonus><skillcategory><name>Social Active</name><bonus>-3</bonus><condition>First meeting</condition></skillcategory><notoriety>1</notoriety></bonus></quality>");
            string effects = QualityDescriptionRules.PreviewModifiers(xml.DocumentElement.CreateNavigator());
            StringAssert.Contains(effects, "Social Active: -3 (First meeting)");
            StringAssert.Contains(effects, "Notoriety: +1");
            Assert.AreEqual(count, _character.Qualities.Count);
        }

        [TestMethod]
        public void ReworkTraditionAttributeUsesRecordedTraditionAndFlagsAmbiguity()
        {
            _character.MAGEnabled = true;
            _character.MagicianEnabled = true;
            var context = new Chummer.Rolls.RollContext(_character);
            foreach (string attribute in new[] { "LOG", "CHA", "INT" })
            {
                _character.MagicTradition.DrainExpression = "{WIL} + {" + attribute + "}";
                var result = Chummer.Rolls.DiceExpressionEvaluator.Evaluate("{Tradition Attribute}", context);
                Assert.AreEqual(_character.GetAttribute(attribute).TotalValue, result.Value);
                Assert.AreEqual(0, result.Unknowns.Count);
                StringAssert.Contains(result.Breakdown, attribute);
            }
            _character.MagicTradition.DrainExpression = "{WIL} + {LOG} + {CHA}";
            CollectionAssert.Contains(Chummer.Rolls.DiceExpressionEvaluator.Evaluate("{Tradition Attribute}", context).Unknowns, "Tradition Attribute");
        }

        [TestMethod]
        public async Task QualityHoverUsesDescriptionAndActualConditionalModifiers()
        {
            string id = _character.LoadData("qualities.xml").SelectSingleNode("/chummer/qualities/quality[name='First Impression']/id").InnerText;
            Assert.AreEqual(RuleResult.Changed, await QualityRules.AddAsync(_character, id, 1, true));
            var quality = _character.Qualities.Single(x => x.Name == "First Impression");
            Assert.IsTrue(QualityDescriptionRules.Description(quality).Contains("first time"));
            string modifiers = QualityDescriptionRules.Modifiers(_character, quality);
            Assert.IsTrue(modifiers.Contains("+2")); Assert.IsTrue(modifiers.Contains("Meeting people the first time"));
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
