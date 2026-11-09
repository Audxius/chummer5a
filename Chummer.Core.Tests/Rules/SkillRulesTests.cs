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
using Chummer.Backend.Skills;
using Chummer.Rules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Rules
{
    [TestClass]
    public class SkillRulesTests
    {
        /// <summary>Answers every question with Yes and remembers what was asked.</summary>
        private sealed class RecordingMessageService : IMessageService
        {
            public List<string> Messages { get; } = new List<string>();

            public MessageResult Show(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
                                      MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1)
            {
                Messages.Add(message);
                return buttons == MessageButtons.OK || buttons == MessageButtons.OKCancel ? MessageResult.OK : MessageResult.Yes;
            }

            public Task<MessageResult> ShowAsync(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
                                                 MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1,
                                                 CancellationToken token = default)
                => Task.FromResult(Show(owner, message, caption, buttons, icon, defaultButton));
        }

        private static IMessageService s_objOldMessages;
        private static RecordingMessageService s_objMessages;

        [TestInitialize]
        public void Setup()
        {
            s_objOldMessages = HostServices.Messages;
            s_objMessages = new RecordingMessageService();
            HostServices.Messages = s_objMessages;
        }

        [TestCleanup]
        public void TearDown() => HostServices.Messages = s_objOldMessages;

        private static Character LoadCareer(int intKarma)
        {
            Character objCharacter = UnitTestUtils.LoadCharacter(new FileInfo(Path.Combine(AppContext.BaseDirectory, "TestFiles", "Soma (Career).chum5")));
            Assert.IsTrue(objCharacter.Created);
            objCharacter.Karma = intKarma;
            return objCharacter;
        }

        private static ExpenseLogEntry NewestKarmaEntry(Character objCharacter, int intCountBefore)
        {
            Assert.AreEqual(intCountBefore + 1, objCharacter.ExpenseEntries.Count, "exactly one expense entry");
            return objCharacter.ExpenseEntries.ToList().OrderByDescending(x => x.Date).First(x => x.Type == ExpenseType.Karma);
        }

        [TestMethod]
        public async Task CareerImproveActiveSkill()
        {
            using (Character objCharacter = LoadCareer(200))
            {
                Skill objSkill = objCharacter.SkillsSection.Skills.ToList()
                                             .First(x => !x.IsExoticSkill && x.TotalBaseRating > 0 && x.TotalBaseRating < x.RatingMaximum && x.SkillGroupObject == null);
                int intRating = objSkill.TotalBaseRating;
                int intCost = objSkill.UpgradeKarmaCost;
                int intEntries = objCharacter.ExpenseEntries.Count;

                Assert.AreEqual(RuleResult.Changed, await SkillRules.ImproveCareerAsync(objCharacter, objSkill));

                Assert.AreEqual(intRating + 1, objSkill.TotalBaseRating);
                Assert.AreEqual(200 - intCost, objCharacter.Karma);
                ExpenseLogEntry objEntry = NewestKarmaEntry(objCharacter, intEntries);
                Assert.AreEqual(-intCost, objEntry.Amount);
                string strSpace = LanguageManager.GetString("String_Space");
                Assert.AreEqual(LanguageManager.GetString("String_ExpenseActiveSkill") + strSpace + objSkill.CurrentDisplayName + strSpace
                                + intRating.ToString(GlobalSettings.CultureInfo) + strSpace + "->" + strSpace + (intRating + 1).ToString(GlobalSettings.CultureInfo),
                                objEntry.Reason);
                Assert.AreEqual(KarmaExpenseType.ImproveSkill, objEntry.Undo.KarmaType);
                Assert.AreEqual(objSkill.InternalId, objEntry.Undo.ObjectId);
                // Same confirmation as the legacy SkillControl.
                if (GlobalSettings.ConfirmKarmaExpense)
                    CollectionAssert.Contains(s_objMessages.Messages, string.Format(GlobalSettings.CultureInfo, LanguageManager.GetString("Message_ConfirmKarmaExpense"),
                                                                                    objSkill.CurrentDisplayName, intRating + 1, intCost, string.Empty));
            }
        }

        [TestMethod]
        public async Task CareerImproveRefusedWithoutKarma()
        {
            using (Character objCharacter = LoadCareer(0))
            {
                Skill objSkill = objCharacter.SkillsSection.Skills.ToList().First(x => x.TotalBaseRating > 0 && x.TotalBaseRating < x.RatingMaximum);
                int intRating = objSkill.TotalBaseRating;
                int intEntries = objCharacter.ExpenseEntries.Count;
                Assert.AreEqual(RuleResult.NotEnoughKarma, await SkillRules.ImproveCareerAsync(objCharacter, objSkill));
                Assert.AreEqual(intRating, objSkill.TotalBaseRating);
                Assert.AreEqual(0, objCharacter.Karma);
                Assert.AreEqual(intEntries, objCharacter.ExpenseEntries.Count);
            }
        }

        [TestMethod]
        public async Task CareerImproveKnowledgeSkill()
        {
            using (Character objCharacter = LoadCareer(200))
            {
                KnowledgeSkill objSkill = objCharacter.SkillsSection.KnowledgeSkills.ToList()
                                                      .First(x => x.AllowUpgrade && !x.IsNativeLanguage && x.TotalBaseRating < x.RatingMaximum);
                int intRating = objSkill.TotalBaseRating;
                int intCost = objSkill.UpgradeKarmaCost;
                int intEntries = objCharacter.ExpenseEntries.Count;

                Assert.AreEqual(RuleResult.Changed, await SkillRules.ImproveCareerAsync(objCharacter, objSkill));

                Assert.AreEqual(intRating + 1, objSkill.TotalBaseRating);
                Assert.AreEqual(200 - intCost, objCharacter.Karma);
                ExpenseLogEntry objEntry = NewestKarmaEntry(objCharacter, intEntries);
                Assert.AreEqual(-intCost, objEntry.Amount);
                StringAssert.StartsWith(objEntry.Reason, LanguageManager.GetString("String_ExpenseKnowledgeSkill"));
                Assert.AreEqual(intRating == 0 ? KarmaExpenseType.AddSkill : KarmaExpenseType.ImproveSkill, objEntry.Undo.KarmaType);
            }
        }

        [TestMethod]
        public async Task CareerAddSpecialization()
        {
            using (Character objCharacter = LoadCareer(200))
            {
                Skill objSkill = objCharacter.SkillsSection.Skills.ToList().First(x => x.CanHaveSpecs && x.TotalBaseRating > 0 && !x.IsExoticSkill && SkillRules.GetSpecializationOptions(objCharacter, x).Count > 0);
                List<ListItem> lstOptions = SkillRules.GetSpecializationOptions(objCharacter, objSkill);
                Assert.IsTrue(lstOptions.Count > 0);
                string strSpec = lstOptions[0].Value.ToString();
                int intCost = await SkillRules.GetSpecializationKarmaCostAsync(objCharacter, objSkill);
                Assert.AreEqual(objCharacter.Settings.KarmaSpecialization, intCost);
                int intSpecs = objSkill.Specializations.Count;
                int intEntries = objCharacter.ExpenseEntries.Count;

                IReadOnlyList<ListItem> lstOffered = null;
                Assert.AreEqual(RuleResult.Changed, await SkillRules.AddSpecializationCareerAsync(objCharacter, objSkill, (x, t) =>
                {
                    lstOffered = x;
                    return Task.FromResult(strSpec);
                }));

                Assert.IsNotNull(lstOffered);
                Assert.AreEqual(intSpecs + 1, objSkill.Specializations.Count);
                Assert.IsTrue(objSkill.Specializations.ToList().Any(x => x.Name == strSpec));
                Assert.AreEqual(200 - intCost, objCharacter.Karma);
                ExpenseLogEntry objEntry = NewestKarmaEntry(objCharacter, intEntries);
                Assert.AreEqual(-intCost, objEntry.Amount);
                string strSpace = LanguageManager.GetString("String_Space");
                Assert.AreEqual(LanguageManager.GetString("String_ExpenseLearnSpecialization") + strSpace + objSkill.CurrentDisplayName + strSpace
                                + LanguageManager.GetString("String_LeftPar") + strSpec + LanguageManager.GetString("String_RightPar"), objEntry.Reason);
                Assert.AreEqual(KarmaExpenseType.AddSpecialization, objEntry.Undo.KarmaType);
                // A specialization the runner has is not offered again.
                Assert.IsFalse(SkillRules.GetSpecializationOptions(objCharacter, objSkill).Any(x => x.Value.ToString() == strSpec));
            }
        }

        [TestMethod]
        public async Task CareerAddSpecializationCancelledCostsNothing()
        {
            using (Character objCharacter = LoadCareer(200))
            {
                Skill objSkill = objCharacter.SkillsSection.Skills.ToList().First(x => x.CanHaveSpecs && x.TotalBaseRating > 0);
                int intSpecs = objSkill.Specializations.Count;
                Assert.AreEqual(RuleResult.Cancelled, await SkillRules.AddSpecializationCareerAsync(objCharacter, objSkill, (x, t) => Task.FromResult<string>(null)));
                Assert.AreEqual(intSpecs, objSkill.Specializations.Count);
                Assert.AreEqual(200, objCharacter.Karma);
            }
        }

        [TestMethod]
        public async Task CareerAddKnowledgeSkillThenImprove()
        {
            using (Character objCharacter = LoadCareer(200))
            {
                int intCount = objCharacter.SkillsSection.KnowledgeSkills.Count;
                Assert.AreEqual(RuleResult.Changed, await SkillRules.AddKnowledgeSkillCareerAsync(objCharacter, (x, t) => Task.FromResult("Corporate Politics"),
                                                                                                 (x, t) => Task.FromResult("Professional")));
                Assert.AreEqual(intCount + 1, objCharacter.SkillsSection.KnowledgeSkills.Count);
                KnowledgeSkill objNew = objCharacter.SkillsSection.KnowledgeSkills.ToList().First(x => x.WritableName == "Corporate Politics");
                Assert.AreEqual(0, objNew.TotalBaseRating);
                Assert.AreEqual(200, objCharacter.Karma, "adding is free; rating 1 is bought with karma");
                int intCost = objNew.UpgradeKarmaCost;
                Assert.AreEqual(objCharacter.Settings.KarmaNewKnowledgeSkill, intCost);
                Assert.AreEqual(RuleResult.Changed, await SkillRules.ImproveCareerAsync(objCharacter, objNew));
                Assert.AreEqual(1, objNew.TotalBaseRating);
                Assert.AreEqual(200 - intCost, objCharacter.Karma);
                Assert.AreEqual(RuleResult.Changed, await SkillRules.DeleteAsync(objCharacter, objNew));
                Assert.AreEqual(intCount, objCharacter.SkillsSection.KnowledgeSkills.Count);
            }
        }

        [TestMethod]
        public async Task CareerAddExoticSkill()
        {
            using (Character objCharacter = LoadCareer(200))
            {
                List<ListItem> lstExotic = SkillRules.GetExoticSkillOptions(objCharacter);
                Assert.IsTrue(lstExotic.Any(x => x.Value.ToString() == "Exotic Ranged Weapon"));
                List<ListItem> lstSpecifics = SkillRules.GetExoticSpecificOptions(objCharacter, "Exotic Ranged Weapon");
                Assert.IsTrue(lstSpecifics.Count > 0);
                string strSpecific = lstSpecifics[0].Value.ToString();
                int intCount = objCharacter.SkillsSection.Skills.Count;
                int intEntries = objCharacter.ExpenseEntries.Count;
                Assert.AreEqual(RuleResult.Changed, await SkillRules.AddExoticSkillAsync(objCharacter, "Exotic Ranged Weapon", strSpecific));
                Assert.AreEqual(intCount + 1, objCharacter.SkillsSection.Skills.Count);
                ExoticSkill objSkill = objCharacter.SkillsSection.Skills.ToList().OfType<ExoticSkill>().First(x => x.Specific == strSpecific);
                Assert.AreEqual(1, objSkill.TotalBaseRating);
                ExpenseLogEntry objEntry = NewestKarmaEntry(objCharacter, intEntries);
                Assert.AreEqual(KarmaExpenseType.AddSkill, objEntry.Undo.KarmaType);
                Assert.AreEqual(200 + (int)objEntry.Amount, objCharacter.Karma);
                Assert.IsTrue(objEntry.Amount < 0);
                Assert.IsFalse(SkillRules.GetExoticSpecificOptions(objCharacter, "Exotic Ranged Weapon").Any(x => x.Value.ToString() == strSpecific));
            }
        }

        [TestMethod]
        public async Task CareerImproveSkillGroup()
        {
            using (Character objCharacter = LoadCareer(500))
            {
                SkillGroup objGroup = objCharacter.SkillsSection.SkillGroups.ToList().FirstOrDefault(x => x.CareerCanIncrease && x.Rating < x.RatingMaximum);
                if (objGroup == null)
                    Assert.Inconclusive("no improvable skill group on the sample");
                int intRating = objGroup.Rating;
                int intCost = objGroup.UpgradeKarmaCost;
                int intEntries = objCharacter.ExpenseEntries.Count;
                Assert.AreEqual(RuleResult.Changed, await SkillRules.ImproveGroupCareerAsync(objCharacter, objGroup));
                Assert.AreEqual(intRating + 1, objGroup.Rating);
                Assert.AreEqual(500 - intCost, objCharacter.Karma);
                ExpenseLogEntry objEntry = NewestKarmaEntry(objCharacter, intEntries);
                Assert.AreEqual(-intCost, objEntry.Amount);
                Assert.AreEqual(KarmaExpenseType.ImproveSkillGroup, objEntry.Undo.KarmaType);
                StringAssert.StartsWith(objEntry.Reason, LanguageManager.GetString("String_ExpenseSkillGroup"));
            }
        }

        [TestMethod]
        public async Task CreateModeKarmaStaysWithinLimits()
        {
            using (Character objCharacter = UnitTestUtils.LoadCharacter(new FileInfo(Path.Combine(AppContext.BaseDirectory, "TestFiles", "Soma.chum5"))))
            {
                Assert.IsFalse(objCharacter.Created);
                Skill objSkill = objCharacter.SkillsSection.Skills.ToList().First(x => x.KarmaUnlocked && x.Karma == 0 && x.FreeKarma == 0 && x.SkillGroupObject == null);
                Assert.AreEqual(RuleResult.NotAllowed, await SkillRules.ChangeKarmaAsync(objCharacter, objSkill, -1), "cannot go below the free levels");
                int intGuard = 0;
                while (await SkillRules.ChangeKarmaAsync(objCharacter, objSkill, 1) == RuleResult.Changed && ++intGuard < 20)
                {
                }
                Assert.IsTrue(objSkill.Karma + objSkill.Base <= objSkill.RatingMaximum);
                Assert.AreEqual(RuleResult.NotAllowed, await SkillRules.ChangeKarmaAsync(objCharacter, objSkill, 1), "cannot go above the maximum");
                Assert.AreEqual(RuleResult.NotAllowed, await SkillRules.ImproveCareerAsync(objCharacter, objSkill), "no karma purchases in create mode");
                while (await SkillRules.ChangeKarmaAsync(objCharacter, objSkill, -1) == RuleResult.Changed)
                {
                }
                Assert.AreEqual(0, objSkill.Karma);
            }
        }
    }
}
