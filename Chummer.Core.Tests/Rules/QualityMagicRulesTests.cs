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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Rules;
using Chummer.Selection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Rules
{
    /// <summary>
    /// Karma effects of the lifted quality/magic rules on a career character, checked against the formulas of the legacy forms.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class QualityMagicRulesTests
    {
        private sealed class YesMessageService : IMessageService
        {
            public List<string> Captions { get; } = new List<string>();

            public MessageResult Show(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
                                      MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1)
            {
                Captions.Add(caption ?? string.Empty);
                return buttons == MessageButtons.OK ? MessageResult.OK : MessageResult.Yes;
            }

            public Task<MessageResult> ShowAsync(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
                                                 MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1,
                                                 CancellationToken token = default)
                => Task.FromResult(Show(owner, message, caption, buttons, icon, defaultButton));
        }

        private IMessageService _objOldMessages;
        private YesMessageService _objMessages;
        private Character _objCharacter;

        [TestInitialize]
        public void Load()
        {
            _objOldMessages = HostServices.Messages;
            _objMessages = new YesMessageService();
            HostServices.Messages = _objMessages;
            _objCharacter = UnitTestUtils.LoadCharacter(new FileInfo(Path.Combine(AppContext.BaseDirectory, "TestFiles", "Soma (Career).chum5")));
            _objCharacter.Karma = 500;
        }

        [TestCleanup]
        public void Cleanup()
        {
            _objCharacter?.Dispose();
            HostServices.Messages = _objOldMessages;
        }

        private int CareerQualityCost(int intKarma)
            => intKarma * _objCharacter.Settings.KarmaQuality * (_objCharacter.Settings.DontDoubleQualityPurchases ? 1 : 2);

        private static string Id(Character objCharacter, string strFile, string strXPath, string strName)
            => objCharacter.LoadDataXPath(strFile).SelectSingleNode(strXPath + "[name = " + strName.CleanXPath() + "]/id")?.Value;

        [TestMethod]
        public async Task AddPositiveQualityChargesDoubleKarmaInCareer()
        {
            string strId = Id(_objCharacter, "qualities.xml", "/chummer/qualities/quality", "Analytical Mind");
            int intExpenses = _objCharacter.ExpenseEntries.Count;
            Assert.AreEqual(RuleResult.Changed, await QualityRules.AddAsync(_objCharacter, strId));
            Assert.AreEqual(500 - CareerQualityCost(5), _objCharacter.Karma);
            Assert.AreEqual(intExpenses + 1, _objCharacter.ExpenseEntries.Count);
            Assert.IsTrue(_objCharacter.Qualities.Any(x => x.Name == "Analytical Mind"));
            // The picker shows the same cost.
            Assert.AreEqual(CareerQualityCost(5), new QualitySelector(_objCharacter).KarmaCost(strId, 1));
        }

        [TestMethod]
        public async Task BuyOffNegativeQualityCostsDoubleKarmaInCareer()
        {
            string strId = Id(_objCharacter, "qualities.xml", "/chummer/qualities/quality", "Combat Paralysis");
            Assert.AreEqual(RuleResult.Changed, await QualityRules.AddAsync(_objCharacter, strId));
            Assert.AreEqual(500, _objCharacter.Karma, "Taking a negative quality in career mode gives no karma.");
            Quality objQuality = _objCharacter.Qualities.First(x => x.Name == "Combat Paralysis");
            int intExpected = 12 * _objCharacter.Settings.KarmaQuality * (_objCharacter.Settings.DontDoubleQualityRefunds ? 1 : 2);
            Assert.AreEqual(intExpected, await QualityRules.GetCareerRemovalCostAsync(_objCharacter, objQuality));
            Assert.AreEqual(RuleResult.Changed, await QualityRules.RemoveAsync(_objCharacter, objQuality));
            Assert.AreEqual(500 - intExpected, _objCharacter.Karma);
            Assert.IsFalse(_objCharacter.Qualities.Any(x => x.Name == "Combat Paralysis"));
        }

        [TestMethod]
        public async Task BuyOffFailsWithoutKarma()
        {
            string strId = Id(_objCharacter, "qualities.xml", "/chummer/qualities/quality", "Combat Paralysis");
            await QualityRules.AddAsync(_objCharacter, strId);
            _objCharacter.Karma = 3;
            Quality objQuality = _objCharacter.Qualities.First(x => x.Name == "Combat Paralysis");
            Assert.AreEqual(RuleResult.NotEnoughKarma, await QualityRules.RemoveAsync(_objCharacter, objQuality));
            Assert.AreEqual(3, _objCharacter.Karma);
            Assert.IsTrue(_objCharacter.Qualities.Contains(objQuality));
        }

        [TestMethod]
        public void QualityPickerListsCategories()
        {
            QualitySelector objSelector = new QualitySelector(_objCharacter);
            Assert.IsTrue(objSelector.Categories.Count >= 3);
            List<ListItem> lstNegative = objSelector.Options(new QualityFilter { Category = "Negative", OnlyAvailable = false });
            Assert.IsTrue(lstNegative.Count > 0);
            Assert.IsTrue(lstNegative.All(x => !objSelector.IsPositive(x.Value.ToString())));
            List<ListItem> lstSearch = objSelector.Options(new QualityFilter { Category = QualitySelector.ShowAll, Search = "Analytical", OnlyAvailable = false });
            Assert.IsTrue(lstSearch.Any(x => x.Name == "Analytical Mind"));
        }

        [TestMethod]
        public async Task LearnSpellCostsSpellKarma()
        {
            string strId = Id(_objCharacter, "spells.xml", "/chummer/spells/spell", "Clout");
            int intCost = await MagicRules.GetSpellCostAsync(_objCharacter, false, false);
            Assert.IsTrue(intCost > 0);
            Assert.AreEqual(RuleResult.Changed, await MagicRules.LearnSpellAsync(_objCharacter, strId));
            Assert.AreEqual(500 - intCost, _objCharacter.Karma);
            Assert.IsTrue(_objCharacter.Spells.Any(x => x.Name == "Clout"));
        }

        [TestMethod]
        public async Task InitiationCostFollowsSettings()
        {
            CharacterSettings objSettings = _objCharacter.Settings;
            int intGrade = _objCharacter.InitiateGrade;
            int intExpected = ((objSettings.KarmaInitiationFlat + (intGrade + 1) * objSettings.KarmaInitiation) * 1.0m).StandardRound();
            Assert.AreEqual(intExpected, await MagicRules.GetInitiationCostAsync(_objCharacter, false, false, false));
            int intGroup = ((objSettings.KarmaInitiationFlat + (intGrade + 1) * objSettings.KarmaInitiation) * (1.0m - objSettings.KarmaMAGInitiationGroupPercent)).StandardRound();
            Assert.AreEqual(intGroup, await MagicRules.GetInitiationCostAsync(_objCharacter, true, false, false));
            if (intGrade + 1 <= _objCharacter.GetAttribute("MAG").TotalValue)
            {
                Assert.AreEqual(RuleResult.Changed, await MagicRules.InitiateAsync(_objCharacter, false, false, false));
                Assert.AreEqual(intGrade + 1, _objCharacter.InitiateGrade);
                Assert.AreEqual(500 - intExpected, _objCharacter.Karma);
            }
        }
    }
}
