using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Backend.Enums;
using Chummer.Rules;
using Chummer.Selection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Rules
{
    [TestClass, DoNotParallelize]
    public class CreationRulesTests
    {
        private sealed class Messages : IMessageService
        {
            public List<string> Text { get; } = new List<string>();
            public bool Accept { get; set; } = true;
            public MessageResult Show(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
                MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1)
            {
                Text.Add(message);
                return buttons == MessageButtons.OK ? MessageResult.OK : Accept ? MessageResult.Yes : MessageResult.No;
            }
            public Task<MessageResult> ShowAsync(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
                MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1, CancellationToken token = default)
                => Task.FromResult(Show(owner, message, caption, buttons, icon, defaultButton));
        }

        private string _key;
        private CharacterSettings _settings;
        private IMessageService _oldMessages;
        private bool _oldBackup;
        private Messages _messages;

        [TestInitialize]
        public void Setup()
        {
            _key = "creation-test-" + Guid.NewGuid().ToString("N") + ".xml";
            using var template = new CharacterSettings();
            _settings = new CharacterSettings(template, false, _key) { BuildMethod = CharacterBuildMethod.Karma, BuildKarma = 25, KarmaCarryover = 7, NuyenCarryover = 5000 };
            _settings.BooksWritable.Add("SR5");
            SettingsManager.LoadedCharacterSettingsAsModifiable[_key] = _settings;
            _oldMessages = HostServices.Messages; _messages = new Messages(); HostServices.Messages = _messages;
            _oldBackup = GlobalSettings.CreateBackupOnCareer; GlobalSettings.CreateBackupOnCareer = false;
        }

        [TestCleanup]
        public void Cleanup()
        {
            HostServices.Messages = _oldMessages;
            GlobalSettings.CreateBackupOnCareer = _oldBackup;
            SettingsManager.LoadedCharacterSettingsAsModifiable.TryRemove(_key, out _);
            _settings.Dispose();
        }

        private async Task<Character> Human()
        {
            var character = new Character { SettingsKey = _key };
            var selector = new MetatypeSelector(character);
            selector.OnHeritageOrTalentChanged();
            selector.Metatype = selector.GetMetatypes().First(x => selector.GetDetails(x.Value.ToString()).Name == "Human").Value.ToString();
            selector.OnMetatypeChanged();
            Assert.IsTrue(await selector.ApplyAsync());
            return character;
        }

        [TestMethod]
        public async Task KarmaBudgetChargesAttributeAndSkillAdvancementWithoutChangingCareerFunds()
        {
            using var character = await Human();
            int baseline = await CreationRules.CalculateKarmaAsync(character);
            Assert.AreEqual(25, baseline);
            character.BOD.Karma = 1;
            Assert.AreEqual(15, await CreationRules.CalculateKarmaAsync(character), "BOD 1 -> 2 costs 10 karma.");
            var skill = character.SkillsSection.Skills.First(x => x.Name == "Pistols");
            skill.Karma = 1;
            Assert.AreEqual(13, await CreationRules.CalculateKarmaAsync(character), "A new active skill costs 2 karma.");
            Assert.AreEqual(0, character.Karma, "Creation calculation must not rewrite career balances.");
            Assert.AreEqual(0m, character.Nuyen);
        }

        [TestMethod]
        public async Task ValidationRejectsOverspentBuildAndAllowsIgnoreRulesOverride()
        {
            using var character = await Human();
            character.BOD.Karma = 4;
            CreationValidation result = await CreationRules.ValidateAsync(character);
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Length > 0);
            character.IgnoreRules = true;
            Assert.IsTrue((await CreationRules.ValidateAsync(character)).IsValid);
        }

        [TestMethod]
        public async Task CancelStartingCashLeavesDraftAndCollectionsUnchanged()
        {
            using var character = await Human();
            int lifestyles = character.Lifestyles.Count;
            int expenses = character.ExpenseEntries.Count;
            RuleResult result = await CreationRules.FinishCareerAsync(character, (choices, _) =>
            {
                Assert.AreEqual("Street", choices.Single().BaseLifestyle);
                return Task.FromResult<decimal?>(null);
            });
            Assert.AreEqual(RuleResult.Cancelled, result);
            Assert.IsFalse(character.Created);
            Assert.AreEqual(lifestyles, character.Lifestyles.Count);
            Assert.AreEqual(expenses, character.ExpenseEntries.Count);
            Assert.AreEqual(0m, character.Nuyen);
        }

        [TestMethod]
        public async Task FinishingCapsCarryoverCreatesUndoableLedgerAndSurvivesSaveReload()
        {
            using var character = await Human();
            character.StartingNuyen = 6000;
            RuleResult result = await CreationRules.FinishCareerAsync(character, (_, _) => Task.FromResult<decimal?>(120));
            Assert.AreEqual(RuleResult.Changed, result);
            Assert.IsTrue(character.Created);
            Assert.AreEqual(7, character.Karma);
            Assert.AreEqual(5120m, character.Nuyen);
            Assert.AreEqual(1, character.Lifestyles.Count);
            var karma = character.ExpenseEntries.First(x => x.Type == ExpenseType.Karma);
            Assert.AreEqual(7m, karma.Amount);
            Assert.AreEqual(KarmaExpenseType.ManualAdd, karma.Undo.KarmaType);
            var cash = character.ExpenseEntries.First(x => x.Type == ExpenseType.Nuyen);
            Assert.AreEqual(5120m, cash.Amount);
            Assert.AreEqual(NuyenExpenseType.ManualAdd, cash.Undo.NuyenType);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            try
            {
                Assert.IsTrue(await character.SaveAsync(path, false, false));
                using var loaded = new Character();
                Assert.IsTrue(await loaded.LoadAsync(path));
                Assert.IsTrue(loaded.Created);
                Assert.AreEqual(7, loaded.Karma);
                Assert.AreEqual(5120m, loaded.Nuyen);
                Assert.AreEqual(2, loaded.ExpenseEntries.Count);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public async Task EquipmentDamageClampsAndSurvivesSaveReload()
        {
            using var character = await Human();
            var vehicle = new Chummer.Backend.Equipment.Vehicle(character) { Name = "Test drone", Body = 4, DeviceRating = "3" };
            var gear = new Chummer.Backend.Equipment.Gear(character) { Name = "Test commlink", DeviceRating = "3" };
            character.Vehicles.Add(vehicle); character.Gear.Add(gear);
            Assert.AreEqual(RuleResult.Changed, EquipmentConditionRules.SetDamage(vehicle, true, int.MaxValue));
            Assert.AreEqual(vehicle.PhysicalCM, vehicle.PhysicalCMFilled);
            Assert.AreEqual(RuleResult.Changed, EquipmentConditionRules.SetDamage(vehicle, true, -20));
            Assert.AreEqual(0, vehicle.PhysicalCMFilled);
            EquipmentConditionRules.SetDamage(vehicle, true, 3);
            EquipmentConditionRules.SetDamage(vehicle, false, 2);
            EquipmentConditionRules.SetDamage(gear, false, 1);
            Assert.AreEqual(RuleResult.NotAllowed, EquipmentConditionRules.SetDamage(gear, true, 1));
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chum5");
            try
            {
                Assert.IsTrue(await character.SaveAsync(path, false, false));
                using var loaded = new Character();
                Assert.IsTrue(await loaded.LoadAsync(path));
                Assert.AreEqual(3, loaded.Vehicles.Single().PhysicalCMFilled);
                Assert.AreEqual(2, loaded.Vehicles.Single().MatrixCMFilled);
                Assert.AreEqual(1, loaded.Gear.Single().MatrixCMFilled);
            }
            finally { File.Delete(path); }
        }

        [TestMethod]
        public async Task DecliningCareerConfirmationDoesNotRollOrMutateRunner()
        {
            using var character = await Human();
            _messages.Accept = false;
            bool prompted = false;
            RuleResult result = await CreationRules.FinishCareerAsync(character, (_, _) => { prompted = true; return Task.FromResult<decimal?>(100); });
            Assert.AreEqual(RuleResult.Cancelled, result);
            Assert.IsFalse(prompted);
            Assert.IsFalse(character.Created);
            Assert.AreEqual(0, character.Lifestyles.Count);
            Assert.AreEqual(0, character.ExpenseEntries.Count);
        }
    }
}
