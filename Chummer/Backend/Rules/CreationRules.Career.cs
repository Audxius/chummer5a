/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.XPath;
using Chummer.Backend.Attributes;
using Chummer.Backend.Equipment;
using Chummer.Backend.Enums;

namespace Chummer.Rules
{
    public static partial class CreationRules
    {
        /// <summary>CharacterCreate.ValidateCharacter + SaveCharacterAsCreated: validate, confirm carryovers,
        /// select starting cash, add the starting ledger entries and preserve non-retroactive essence costs.</summary>
        public static async Task<RuleResult> FinishCareerAsync(Character character,
            Func<IReadOnlyList<Lifestyle>, CancellationToken, Task<decimal?>> chooseStartingCash,
            CancellationToken token = default)
        {
            if (await character.GetCreatedAsync(token).ConfigureAwait(false)) return RuleResult.NotAllowed;
            if (await MessageBoxes.ShowAsync(await LanguageManager.GetStringAsync("Message_ConfirmCreate", token: token).ConfigureAwait(false),
                    await LanguageManager.GetStringAsync("MessageTitle_ConfirmCreate", token: token).ConfigureAwait(false),
                    MessageButtons.YesNo, MessageIcon.Question, token: token).ConfigureAwait(false) != MessageResult.Yes)
                return RuleResult.Cancelled;

            CreationValidation validation = await ValidateAsync(character, true, token).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                if (!string.IsNullOrEmpty(validation.Errors))
                    await MessageBoxes.ShowAsync(await LanguageManager.GetStringAsync("Message_InvalidBeginning", token: token).ConfigureAwait(false)
                        + validation.Errors, await LanguageManager.GetStringAsync("MessageTitle_Invalid", token: token).ConfigureAwait(false),
                        MessageButtons.OK, MessageIcon.Error, token: token).ConfigureAwait(false);
                return RuleResult.NotAllowed;
            }
            CharacterSettings settings = await character.GetSettingsAsync(token).ConfigureAwait(false);
            int karma = await CalculateKarmaAsync(character, token).ConfigureAwait(false);
            int karmaLimit = await settings.GetKarmaCarryoverAsync(token).ConfigureAwait(false);
            decimal nuyen = (await character.CalculateNuyenCreateModeAsync(token).ConfigureAwait(false)).Item1;
            decimal nuyenLimit = await settings.GetNuyenCarryoverAsync(token).ConfigureAwait(false);
            if (karma > karmaLimit)
            {
                bool priority = await character.GetEffectiveBuildMethodUsesPriorityTablesAsync(token).ConfigureAwait(false);
                string message = string.Format(GlobalSettings.CultureInfo,
                    await LanguageManager.GetStringAsync(priority ? "Message_ExtraKarma" : "Message_NoExtraKarma", token: token).ConfigureAwait(false), karma, karmaLimit);
                if (await MessageBoxes.ShowAsync(message, await LanguageManager.GetStringAsync("MessageTitle_ExtraKarma", token: token).ConfigureAwait(false),
                        MessageButtons.YesNo, MessageIcon.Warning, token: token).ConfigureAwait(false) != MessageResult.Yes)
                    return RuleResult.Cancelled;
            }
            if (nuyen > nuyenLimit)
            {
                string format = await settings.GetNuyenFormatAsync(token).ConfigureAwait(false);
                string message = string.Format(GlobalSettings.CultureInfo, await LanguageManager.GetStringAsync("Message_ExtraNuyen", token: token).ConfigureAwait(false),
                    nuyen.ToString(format, GlobalSettings.CultureInfo), nuyenLimit.ToString(format, GlobalSettings.CultureInfo));
                if (await MessageBoxes.ShowAsync(message, await LanguageManager.GetStringAsync("MessageTitle_ExtraNuyen", token: token).ConfigureAwait(false),
                        MessageButtons.YesNo, MessageIcon.Warning, token: token).ConfigureAwait(false) != MessageResult.Yes)
                    return RuleResult.Cancelled;
            }

            if (GlobalSettings.CreateBackupOnCareer)
            {
                Directory.CreateDirectory(Utils.GetBackupSavesFolderPath);
                string name = Path.GetFileNameWithoutExtension(character.FileName);
                if (string.IsNullOrWhiteSpace(name)) name = character.Alias;
                if (string.IsNullOrWhiteSpace(name)) name = Guid.NewGuid().ToString("N");
                foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
                string suffix = " (" + await LanguageManager.GetStringAsync("Title_CreateMode", token: token).ConfigureAwait(false) + ")";
                string extension = character.FileName.EndsWith(".chum5lz", Utils.PathStringComparison) ? ".chum5lz" : ".chum5";
                if (!await character.SaveAsync(Path.Combine(Utils.GetBackupSavesFolderPath, name + suffix + extension),
                        false, false, token: token).ConfigureAwait(false)) return RuleResult.Cancelled;
            }

            Lifestyle street = null;
            List<Lifestyle> lifestyles = character.Lifestyles.ToList();
            if (lifestyles.Count == 0)
            {
                street = new Lifestyle(character);
                await street.CreateAsync((await character.LoadDataAsync("lifestyles.xml", token: token).ConfigureAwait(false))
                    .SelectSingleNode("/chummer/lifestyles/lifestyle[name = 'Street']"), token).ConfigureAwait(false);
                lifestyles.Add(street);
            }
            decimal? starting;
            try { starting = await chooseStartingCash(lifestyles, token).ConfigureAwait(false); }
            catch { if (street != null) await street.DisposeAsync().ConfigureAwait(false); throw; }
            if (starting == null)
            {
                if (street != null) await street.DisposeAsync().ConfigureAwait(false);
                return RuleResult.Cancelled;
            }
            if (street != null) await character.Lifestyles.AddAsync(street, token).ConfigureAwait(false);
            await character.SetNuyenAsync(Math.Min(nuyen, nuyenLimit) + Math.Max(starting.Value, 0), token).ConfigureAwait(false);
            await character.SetKarmaAsync(Math.Min(karma, karmaLimit), token).ConfigureAwait(false);

            if (character.Karma != 0)
            {
                ExpenseLogEntry entry = new ExpenseLogEntry(character);
                entry.Create(character.Karma, await LanguageManager.GetStringAsync("Label_SelectBP_StartingKarma", token: token).ConfigureAwait(false), ExpenseType.Karma, DateTime.Now);
                ExpenseUndo undo = new ExpenseUndo(); undo.CreateKarma(KarmaExpenseType.ManualAdd, string.Empty); entry.Undo = undo;
                await character.ExpenseEntries.AddWithSortAsync(entry, token: token).ConfigureAwait(false);
            }
            await AddShapeshifterAttributesAsync(character, token).ConfigureAwait(false);
            ExpenseLogEntry cash = new ExpenseLogEntry(character);
            cash.Create(character.Nuyen, await LanguageManager.GetStringAsync("Title_LifestyleNuyen", token: token).ConfigureAwait(false), ExpenseType.Nuyen, DateTime.Now);
            ExpenseUndo cashUndo = new ExpenseUndo(); cashUndo.CreateNuyen(NuyenExpenseType.ManualAdd, string.Empty); cash.Undo = cashUndo;
            await character.ExpenseEntries.AddWithSortAsync(cash, token: token).ConfigureAwait(false);

            foreach (Cyberware ware in character.Cyberware.GetAllDescendants(x => x.Children))
                await ware.SaveNonRetroactiveEssenceModifiersAsync(token).ConfigureAwait(false);
            foreach (Vehicle vehicle in character.Vehicles)
            {
                foreach (Cyberware ware in vehicle.Mods.SelectMany(x => x.Cyberware).GetAllDescendants(x => x.Children))
                    await ware.SaveNonRetroactiveEssenceModifiersAsync(token).ConfigureAwait(false);
                foreach (Cyberware ware in vehicle.WeaponMounts.SelectMany(x => x.Mods).SelectMany(x => x.Cyberware).GetAllDescendants(x => x.Children))
                    await ware.SaveNonRetroactiveEssenceModifiersAsync(token).ConfigureAwait(false);
            }
            await character.SetCreatedAsync(true, token: token).ConfigureAwait(false);
            return RuleResult.Changed;
        }

        private static async Task AddShapeshifterAttributesAsync(Character character, CancellationToken token)
        {
            if (character.MetatypeCategory != "Shapeshifter") return;
            XPathNavigator data = await character.LoadDataXPathAsync("metatypes.xml", token: token).ConfigureAwait(false);
            string path = "/chummer/metatypes/metatype[translate(id, 'ABCDEF', 'abcdef') = " + character.MetatypeGuid.ToString("D").CleanXPath()
                + "]/metavariants/metavariant[translate(id, 'ABCDEF', 'abcdef') = " + character.MetavariantGuid.ToString("D").CleanXPath() + "]";
            List<CharacterAttrib> added = new List<CharacterAttrib>();
            try
            {
                foreach (CharacterAttrib original in character.AttributeSection.AttributeList.ToList())
                {
                    CharacterAttrib attribute = new CharacterAttrib(character, original.Abbrev, AttributeCategory.Shapeshifter);
                    added.Add(attribute);
                    await AttributeSection.CopyAttributeAsync(original, attribute, path, data, token).ConfigureAwait(false);
                }
                foreach (CharacterAttrib attribute in added)
                    await character.AttributeSection.AttributeList.AddAsync(attribute, token).ConfigureAwait(false);
            }
            catch
            {
                foreach (CharacterAttrib attribute in added)
                {
                    await character.AttributeSection.AttributeList.RemoveAsync(attribute, CancellationToken.None).ConfigureAwait(false);
                    await attribute.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }
    }
}
