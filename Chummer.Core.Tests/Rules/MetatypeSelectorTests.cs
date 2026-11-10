using System.Linq;
using System;
using System.Threading.Tasks;
using Chummer.Backend.Enums;
using Chummer.Selection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Rules
{
    [TestClass]
    public class MetatypeSelectorTests
    {
        [TestMethod]
        public async Task KarmaSelectionUsesIsolatedPresetAndCreatesRacialLimits()
        {
            string key = "native-test-" + Guid.NewGuid().ToString("N") + ".xml";
            using var template = new CharacterSettings();
            using var preset = new CharacterSettings(template, false, key);
            preset.BuildMethod = CharacterBuildMethod.Karma;
            preset.BooksWritable.Add("SR5");
            SettingsManager.LoadedCharacterSettingsAsModifiable[key] = preset;
            try
            {
                using var character = new Character { SettingsKey = key };
                var selector = new MetatypeSelector(character);
                Assert.IsFalse(selector.UsesPriorityTables);
                selector.OnHeritageOrTalentChanged();
                Assert.IsTrue(selector.GetMetatypes().Count > 0);
                Assert.IsTrue(await selector.ApplyAsync());
                Assert.IsTrue(character.BOD.MetatypeMaximum > 0);
                Assert.IsFalse(character.Created);
            }
            finally { SettingsManager.LoadedCharacterSettingsAsModifiable.TryRemove(key, out _); }
        }

        [TestMethod]
        public void PriorityChangesKeepUniqueLettersAndValidMetatypes()
        {
            using var character = new Character();
            if (!character.EffectiveBuildMethodUsesPriorityTables) Assert.Inconclusive("Default preset is not priority based.");
            var selector = new MetatypeSelector(character);
            selector.OnHeritageOrTalentChanged();
            foreach (var option in selector.GetPriorityOptions(MetatypeSelector.Heritage))
            {
                selector.SetPriority(MetatypeSelector.Heritage, option.Value.ToString());
                Assert.AreEqual(5, MetatypeSelector.PriorityCategories.Select(selector.GetPriority).Distinct().Count());
                Assert.IsTrue(selector.GetMetatypes().Any(x => x.Value.ToString() == selector.Metatype));
            }
        }

        [TestMethod]
        public async Task SelectedMetatypeAppliesToFreshRunner()
        {
            using var character = new Character();
            var selector = new MetatypeSelector(character);
            selector.OnHeritageOrTalentChanged();
            Assert.IsTrue(selector.GetMetatypes().Count > 0);
            Assert.IsTrue(await selector.ApplyAsync());
            Assert.IsFalse(character.Created);
            Assert.IsFalse(string.IsNullOrEmpty(character.Metatype));
            Assert.IsTrue(character.BOD.MetatypeMaximum > 0);
            Assert.IsTrue(character.EDG.MetatypeMaximum > 0);
        }
    }
}
