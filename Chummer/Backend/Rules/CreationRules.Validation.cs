/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Backend.Attributes;
using Chummer.Backend.Equipment;
using Chummer.Backend.Enums;
using Chummer.Backend.Skills;

namespace Chummer.Rules
{
    public sealed class CreationValidation
    {
        public CreationValidation(bool valid, string errors) { IsValid = valid; Errors = errors.Trim(); }
        public bool IsValid { get; }
        public string Errors { get; }
    }

    public static partial class CreationRules
    {
        // Lifted from CharacterCreate.CheckCharacterValidity. Unused-point warnings are optional during live validation.
        public static async Task<CreationValidation> ValidateAsync(Character CharacterObject, bool confirmUnusedPoints = false, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            CharacterSettings CharacterObjectSettings = await CharacterObject.GetSettingsAsync(token).ConfigureAwait(false);
            bool blnUseArgBuildPoints = false;
            int intBuildPoints = 0;
            if (await CharacterObject.GetIgnoreRulesAsync(token).ConfigureAwait(false))
                return new CreationValidation(true, string.Empty);

            bool blnValid = true;
            using (new FetchSafelyFromObjectPool<StringBuilder>(Utils.StringBuilderPool,
                                                          out StringBuilder sbdMessage))
            {

                // Check if the character has more than 1 Martial Art, not counting qualities.
                int intMartialArts = await (await CharacterObject.GetMartialArtsAsync(token).ConfigureAwait(false))
                                           .CountAsync(objArt => !objArt.IsQuality, token).ConfigureAwait(false);
                int intMaximumMartialArts = await CharacterObjectSettings.GetMaximumMartialArtsAsync(token).ConfigureAwait(false);
                if (intMartialArts > intMaximumMartialArts)
                {
                    blnValid = false;
                    sbdMessage.AppendLine().Append('\t')
                              .AppendFastFormat(GlobalSettings.CultureInfo,
                                            await LanguageManager
                                                  .GetStringAsync("Message_InvalidPointExcess", token: token)
                                                  .ConfigureAwait(false),
                                            intMartialArts - intMaximumMartialArts)
                              .Append(await LanguageManager.GetStringAsync("String_Space", token: token)
                                                           .ConfigureAwait(false))
                              .Append(await LanguageManager.GetStringAsync("String_MartialArtsCount", token: token)
                                                           .ConfigureAwait(false));
                }

                // Check if the character has more than 5 Techniques in a Martial Art
                if (await (await CharacterObject.GetMartialArtsAsync(token).ConfigureAwait(false))
                          .AnyAsync(token).ConfigureAwait(false))
                {
                    int intTechniques
                        = await (await CharacterObject.GetMartialArtsAsync(token).ConfigureAwait(false)).SumAsync(
                            (x, t) => x.Techniques.GetCountAsync(t), token).ConfigureAwait(false);
                    int intMaximumMartialArtsTechniques = await CharacterObjectSettings.GetMaximumMartialTechniquesAsync(token).ConfigureAwait(false);
                    if (intTechniques > intMaximumMartialArtsTechniques)
                    {
                        blnValid = false;
                        sbdMessage.AppendLine().Append('\t')
                                  .AppendFastFormat(GlobalSettings.CultureInfo,
                                                await LanguageManager
                                                      .GetStringAsync("Message_InvalidPointExcess", token: token)
                                                      .ConfigureAwait(false),
                                                intTechniques - intMaximumMartialArtsTechniques)
                                  .Append(await LanguageManager.GetStringAsync("String_Space", token: token)
                                                               .ConfigureAwait(false))
                                  .Append(await LanguageManager
                                                .GetStringAsync("String_TechniquesCount", token: token)
                                                .ConfigureAwait(false));
                    }
                }

                int intQualityKarmaLimit = await CharacterObjectSettings.GetQualityKarmaLimitAsync(token).ConfigureAwait(false);
                // if positive points > 25
                if (await CharacterObject.GetPositiveQualityLimitKarmaAsync(token).ConfigureAwait(false) > intQualityKarmaLimit
                    && !await CharacterObjectSettings.GetExceedPositiveQualitiesAsync(token).ConfigureAwait(false))
                {
                    sbdMessage.AppendLine().Append('\t').AppendFastFormat(
                        GlobalSettings.CultureInfo,
                        await LanguageManager.GetStringAsync("Message_PositiveQualityLimit", token: token)
                                             .ConfigureAwait(false),
                        intQualityKarmaLimit);
                    blnValid = false;
                }

                // if negative points > 25
                if (await CharacterObject.GetNegativeQualityLimitKarmaAsync(token).ConfigureAwait(false) > intQualityKarmaLimit
                    && !await CharacterObjectSettings.GetExceedNegativeQualitiesAsync(token).ConfigureAwait(false))
                {
                    sbdMessage.AppendLine().Append('\t').AppendFastFormat(
                        GlobalSettings.CultureInfo,
                        await LanguageManager.GetStringAsync("Message_NegativeQualityLimit", token: token)
                                             .ConfigureAwait(false),
                        intQualityKarmaLimit);
                    blnValid = false;
                }

                if (await CharacterObject.GetFriendsInHighPlacesAsync(token).ConfigureAwait(false))
                {
                    ThreadSafeObservableCollection<Contact> lstContacts
                        = await CharacterObject.GetContactsAsync(token).ConfigureAwait(false);
                    // If we have Friends in High Places, then we need to account for any mixture of Friends in High Places plus contact karma discounts (e.g. from Massive Network)
                    if (await lstContacts.AnyAsync(async (x, t) => await x.GetConnectionAsync(t).ConfigureAwait(false) < 8 && await x.GetContactPointsAsync(t).ConfigureAwait(false) > 7, token)
                                         .ConfigureAwait(false)
                        // With Friends in High Places, we can only have "too high contacts" if we overspend our Friends in High Places karma on eligible contacts
                        || await lstContacts
                                 .SumAsync(async (x, t) => await x.GetConnectionAsync(t).ConfigureAwait(false) >= 8 && await x.GetContactPointsAsync(t).ConfigureAwait(false) > 7,
                                           (x, t) => x.GetContactPointsAsync(t), token).ConfigureAwait(false)
                        > 4 * await (await CharacterObject.GetAttributeAsync("CHA", token: token)
                                                          .ConfigureAwait(false)).GetValueAsync(token)
                            .ConfigureAwait(false))
                    {
                        blnValid = false;
                        sbdMessage.AppendLine().Append('\t')
                                  .Append(await LanguageManager.GetStringAsync("Message_HighContact", token: token)
                                                               .ConfigureAwait(false));
                    }
                }
                else if (await (await CharacterObject.GetContactsAsync(token).ConfigureAwait(false))
                               .AnyAsync(async (x, t) => await x.GetContactPointsAsync(t).ConfigureAwait(false) > 7, token).ConfigureAwait(false))
                {
                    blnValid = false;
                    sbdMessage.AppendLine().Append('\t')
                              .Append(await LanguageManager.GetStringAsync("Message_HighContact", token: token)
                                                           .ConfigureAwait(false));
                }

                // Check if the character has gone over the Build Point total.
                if (!blnUseArgBuildPoints)
                    intBuildPoints = await CalculateKarmaAsync(CharacterObject, token).ConfigureAwait(false);
                int intStagedPurchaseQualityPoints
                    = await (await CharacterObject.GetQualitiesAsync(token).ConfigureAwait(false))
                            .SumAsync(async (objQuality, t) =>
                                          objQuality.StagedPurchase
                                          && await objQuality.GetTypeAsync(t).ConfigureAwait(false) == QualityType.Positive
                                          && await objQuality.GetContributeToBPAsync(t).ConfigureAwait(false),
                                      (x, t) => x.GetBPAsync(t), token).ConfigureAwait(false);
                if (intBuildPoints + intStagedPurchaseQualityPoints < 0 && true)
                {
                    blnValid = false;
                    sbdMessage.AppendLine().Append('\t')
                              .AppendFastFormat(GlobalSettings.CultureInfo,
                                            await LanguageManager
                                                  .GetStringAsync("Message_InvalidPointExcess", token: token)
                                                  .ConfigureAwait(false),
                                            -(intBuildPoints + intStagedPurchaseQualityPoints))
                              .Append(await LanguageManager.GetStringAsync("String_Space", token: token)
                                                           .ConfigureAwait(false))
                              .Append(await LanguageManager.GetStringAsync("String_Karma", token: token)
                                                           .ConfigureAwait(false));
                }

                // if character has more than permitted Metagenic qualities
                int intMetagenicLimit = await CharacterObject.GetMetagenicLimitAsync(token).ConfigureAwait(false);
                if (intMetagenicLimit > 0)
                {
                    int intMetagenicNegativeQualityKarma =
                        await CharacterObject.GetMetagenicNegativeQualityKarmaAsync(token).ConfigureAwait(false);
                    if (-intMetagenicNegativeQualityKarma > intMetagenicLimit)
                    {
                        sbdMessage.AppendLine().Append('\t').AppendFastFormat(
                            GlobalSettings.CultureInfo,
                            await LanguageManager
                                  .GetStringAsync("Message_OverNegativeMetagenicQualities", token: token)
                                  .ConfigureAwait(false),
                            -intMetagenicNegativeQualityKarma, intMetagenicLimit);
                        blnValid = false;
                    }

                    int intMetagenicPositiveQualityKarma =
                        await CharacterObject.GetMetagenicPositiveQualityKarmaAsync(token).ConfigureAwait(false);
                    if (intMetagenicPositiveQualityKarma > intMetagenicLimit)
                    {
                        sbdMessage.AppendLine().Append('\t').AppendFastFormat(
                            GlobalSettings.CultureInfo,
                            await LanguageManager
                                  .GetStringAsync("Message_OverPositiveMetagenicQualities", token: token)
                                  .ConfigureAwait(false),
                            intMetagenicPositiveQualityKarma, intMetagenicLimit);
                        blnValid = false;
                    }

                    if (-intMetagenicNegativeQualityKarma != intMetagenicPositiveQualityKarma &&
                        -intMetagenicNegativeQualityKarma != intMetagenicPositiveQualityKarma - 1)
                    {
                        sbdMessage.AppendLine().Append('\t').AppendFastFormat(
                            GlobalSettings.CultureInfo,
                            await LanguageManager
                                  .GetStringAsync("Message_MetagenicQualitiesUnbalanced", token: token)
                                  .ConfigureAwait(false),
                            -intMetagenicNegativeQualityKarma,
                            intMetagenicPositiveQualityKarma - 1,
                            intMetagenicPositiveQualityKarma);
                        blnValid = false;
                    }
                }

                AttributeSection objAttributeSection
                    = await CharacterObject.GetAttributeSectionAsync(token).ConfigureAwait(false);
                ThreadSafeObservableCollection<CharacterAttrib> lstAttributes
                    = await objAttributeSection.GetAttributeListAsync(token).ConfigureAwait(false);
                // Check if the character has more attributes at their metatype max than allowed
                int intMaxNumberMaxAttributesCreate =
                    await CharacterObjectSettings.GetMaxNumberMaxAttributesCreateAsync(token).ConfigureAwait(false);
                if (intMaxNumberMaxAttributesCreate
                    < await lstAttributes.GetCountAsync(token)
                        .ConfigureAwait(false))
                {
                    int intCountAttributesAtMax
                        = await lstAttributes.CountAsync(
                                                 async (x, t) => x.MetatypeCategory
                                                            == AttributeCategory.Standard
                                                            && await x.GetAtMetatypeMaximumAsync(t)
                                                                      .ConfigureAwait(false), token)
                                             .ConfigureAwait(false);
                    if (intCountAttributesAtMax > intMaxNumberMaxAttributesCreate)
                    {
                        blnValid = false;
                        sbdMessage.AppendLine().Append('\t').AppendFastFormat(GlobalSettings.CultureInfo,
                                                                          await LanguageManager.GetStringAsync(
                                                                              "Message_TooManyAttributesAtMax",
                                                                              token: token).ConfigureAwait(false),
                                                                          intCountAttributesAtMax,
                                                                          intMaxNumberMaxAttributesCreate);
                    }
                }

                int i = CharacterObject.TotalAttributes
                        - await CalculateAttributePriorityPoints(CharacterObject, lstAttributes, token: token).ConfigureAwait(false);
                // Check if the character has gone over on Primary Attributes
                if (i < 0)
                {
                    //TODO: ATTACH TO ATTRIBUTE SECTION
                    blnValid = false;
                    sbdMessage.AppendLine().Append('\t').AppendFastFormat(GlobalSettings.CultureInfo,
                                                                      await LanguageManager.GetStringAsync(
                                                                          "Message_InvalidAttributeExcess",
                                                                          token: token).ConfigureAwait(false), -i);
                }

                ThreadSafeObservableCollection<CharacterAttrib> lstSpecialAttributes
                    = await objAttributeSection.GetSpecialAttributeListAsync(token).ConfigureAwait(false);
                i = await CharacterObject.GetTotalSpecialAsync(token).ConfigureAwait(false)
                    - await CalculateAttributePriorityPoints(CharacterObject, lstSpecialAttributes, token: token)
                        .ConfigureAwait(false);
                // Check if the character has gone over on Special Attributes
                if (i < 0)
                {
                    //TODO: ATTACH TO ATTRIBUTE SECTION
                    blnValid = false;
                    sbdMessage.AppendLine().Append('\t').AppendFastFormat(GlobalSettings.CultureInfo,
                                                                      await LanguageManager.GetStringAsync(
                                                                          "Message_InvalidSpecialExcess",
                                                                          token: token).ConfigureAwait(false), -i);
                }

                // Check if the character has gone over on Skill Groups
                SkillsSection objSkillsSection
                    = await CharacterObject.GetSkillsSectionAsync(token).ConfigureAwait(false);
                int intSkillGroupPoints = await objSkillsSection.GetSkillGroupPointsAsync(token)
                                                                .ConfigureAwait(false);
                if (intSkillGroupPoints < 0)
                {
                    blnValid = false;
                    sbdMessage.AppendLine().Append('\t').AppendFastFormat(GlobalSettings.CultureInfo,
                                                                      await LanguageManager.GetStringAsync(
                                                                          "Message_InvalidSkillGroupExcess",
                                                                          token: token).ConfigureAwait(false),
                                                                      -intSkillGroupPoints);
                }

                // Check if the character has gone over on Active Skills
                int intSkillPoints = await objSkillsSection.GetSkillPointsAsync(token)
                                                           .ConfigureAwait(false);
                if (intSkillPoints < 0)
                {
                    blnValid = false;
                    sbdMessage.AppendLine().Append('\t').AppendFastFormat(GlobalSettings.CultureInfo,
                                                                      await LanguageManager.GetStringAsync(
                                                                          "Message_InvalidActiveSkillExcess",
                                                                          token: token).ConfigureAwait(false),
                                                                      -intSkillPoints);
                }

                // Check if the character has gone over on Knowledge Skills
                int intKnoSkillPoints = await objSkillsSection
                                              .GetKnowledgeSkillPointsRemainAsync(token)
                                              .ConfigureAwait(false);
                if (intKnoSkillPoints < 0)
                {
                    blnValid = false;
                    sbdMessage.AppendLine().Append('\t').AppendFastFormat(GlobalSettings.CultureInfo,
                                                                      await LanguageManager.GetStringAsync(
                                                                          "Message_InvalidKnowledgeSkillExcess",
                                                                          token: token).ConfigureAwait(false),
                                                                      -intKnoSkillPoints);
                }

                ThreadSafeBindingList<Skill> lstSkills
                    = await objSkillsSection.GetSkillsAsync(token).ConfigureAwait(false);
                if (await lstSkills
                          .AnyAsync(
                              async (s, t) => await (await s.GetSpecializationsAsync(t).ConfigureAwait(false)).GetCountAsync(t)
                                                .ConfigureAwait(false) > 1, token)
                          .ConfigureAwait(false))
                {
                    blnValid = false;
                    string strSpace = await LanguageManager.GetStringAsync("String_Space", token: token).ConfigureAwait(false);
                    string strComma = await LanguageManager.GetStringAsync("String_Comma", token: token).ConfigureAwait(false);
                    string strLeftPar = await LanguageManager.GetStringAsync("String_LeftPar", token: token)
                                                           .ConfigureAwait(false);
                    string strRightPar = await LanguageManager.GetStringAsync("String_RightPar", token: token)
                                                           .ConfigureAwait(false);
                    sbdMessage.AppendLine().Append('\t').Append(await LanguageManager.GetStringAsync(
                                                                    "Message_InvalidActiveSkillExcessSpecializations",
                                                                    token: token).ConfigureAwait(false));
                    await lstSkills.ForEachAsync(async (objSkill, t1) =>
                    {
                        ThreadSafeObservableCollection<SkillSpecialization> lstSpecs
                            = await objSkill.GetSpecializationsAsync(t1).ConfigureAwait(false);
                        if (await lstSpecs.GetCountAsync(t1).ConfigureAwait(false) < 1)
                            return;
                        sbdMessage.AppendLine()
                            .Append(await objSkill.GetCurrentDisplayNameAsync(t1).ConfigureAwait(false),
                                strSpace, strLeftPar);
                        await sbdMessage.AppendJoinAsync(strComma + strSpace,
                            lstSpecs.Select((x, t2) => x.GetCurrentDisplayNameAsync(t2), t1), t1).ConfigureAwait(false);
                        sbdMessage.Append(strRightPar);
                    }, token).ConfigureAwait(false);
                }

                // Check if the character has gone over the Nuyen limit.
                (decimal decNuyen, decimal decStolenNuyen) = await CharacterObject.CalculateNuyenCreateModeAsync(token).ConfigureAwait(false);
                if (decNuyen < 0)
                {
                    blnValid = false;
                    sbdMessage.AppendLine().Append('\t')
                              .AppendFastFormat(
                                            await LanguageManager
                                                  .GetStringAsync("Message_InvalidNuyenExcess", token: token)
                                                  .ConfigureAwait(false),
                                            (-decNuyen).ToString(await CharacterObjectSettings.GetNuyenFormatAsync(token).ConfigureAwait(false),
                                                                 GlobalSettings.CultureInfo))
                              .Append(await LanguageManager.GetStringAsync("String_NuyenSymbol", token: token)
                                                           .ConfigureAwait(false));
                }
                if (decStolenNuyen < 0)
                {
                    blnValid = false;
                    sbdMessage.AppendLine().Append('\t').AppendFastFormat(
                                                                      await LanguageManager.GetStringAsync(
                                                                          "Message_InvalidStolenNuyenExcess",
                                                                          token: token).ConfigureAwait(false),
                                                                      (-decStolenNuyen).ToString(
                                                                          await CharacterObjectSettings.GetNuyenFormatAsync(token).ConfigureAwait(false),
                                                                          GlobalSettings.CultureInfo))
                              .Append(await LanguageManager.GetStringAsync("String_NuyenSymbol", token: token)
                                                           .ConfigureAwait(false));
                }

                // Check if the character's Essence is above 0.
                if (await (await CharacterObject.GetAttributeAsync("ESS", token: token).ConfigureAwait(false))
                          .GetMetatypeMaximumAsync(token).ConfigureAwait(false) > 0)
                {
                    decimal decEss = await CharacterObject.EssenceAsync(token: token).ConfigureAwait(false);
                    decimal decExcessEss = 0.0m;
                    // Need to split things up this way because without internal rounding, Essence can be as small as the player wants as long as it is positive
                    // And getting the smallest positive number supported by the decimal type is way trickier than just checking if it's zero or negative
                    if (await CharacterObjectSettings.GetDontRoundEssenceInternallyAsync(token).ConfigureAwait(false))
                    {
                        if (decEss < 0)
                            decExcessEss = -decEss;
                        else if (decEss == 0)
                            // Hacky, but necessary so that the player knows they need to increase their ESS
                            decExcessEss = 10.0m.Pow(-await CharacterObjectSettings.GetEssenceDecimalsAsync(token).ConfigureAwait(false));
                    }
                    else
                    {
                        decimal decMinEss = 10.0m.Pow(-await CharacterObjectSettings.GetEssenceDecimalsAsync(token).ConfigureAwait(false));
                        if (decEss < decMinEss)
                            decExcessEss = decMinEss - decEss;
                    }

                    if (decExcessEss > 0)
                    {
                        blnValid = false;
                        sbdMessage.AppendLine().Append('\t').AppendFastFormat(
                            await LanguageManager.GetStringAsync("Message_InvalidEssenceExcess", token: token)
                                                 .ConfigureAwait(false),
                            decExcessEss.ToString(await CharacterObjectSettings.GetEssenceFormatAsync(token).ConfigureAwait(false),
                                                                 GlobalSettings.CultureInfo));
                    }
                }

                if (await CharacterObject.GetShouldHaveTraditionAsync(token).ConfigureAwait(false))
                {
                    TraditionType eTraditionType = await (await CharacterObject.GetMagicTraditionAsync(token).ConfigureAwait(false)).GetTypeAsync(token).ConfigureAwait(false);
                    if (eTraditionType == TraditionType.None)
                    {
                        blnValid = false;
                        sbdMessage.AppendLine().Append('\t')
                            .Append(await LanguageManager.GetStringAsync(await CharacterObject.GetRESEnabledAsync(token).ConfigureAwait(false)
                                ? "Message_InvalidNoStream"
                                : "Message_InvalidNoTradition", token: token)
                            .ConfigureAwait(false));
                    }
                    else if (await CharacterObject.GetRESEnabledAsync(token).ConfigureAwait(false))
                    {
                        // If the character has the Technomancer Tab enabled, make sure a Stream has been selected.
                        if (eTraditionType != TraditionType.RES)
                        {
                            blnValid = false;
                            sbdMessage.AppendLine().Append('\t')
                                .Append(await LanguageManager.GetStringAsync("Message_InvalidNoStream", token: token).ConfigureAwait(false));
                        }
                    }
                    // If the character has the Spells & Spirits Tab enabled, make sure a Tradition has been selected.
                    else if (eTraditionType != TraditionType.MAG)
                    {
                        blnValid = false;
                        sbdMessage.AppendLine().Append('\t')
                            .Append(await LanguageManager.GetStringAsync("Message_InvalidNoTradition", token: token).ConfigureAwait(false));
                    }
                }

                // If the character has the Spells & Spirits Tab enabled, make sure a Tradition has been selected.
                if (await CharacterObject.GetAdeptEnabledAsync(token).ConfigureAwait(false))
                {
                    decimal decPPUsed = await CharacterObject.GetPowerPointsUsedAsync(token).ConfigureAwait(false);
                    decimal decPPTotal
                        = await CharacterObject.GetPowerPointsTotalAsync(token).ConfigureAwait(false);
                    if (decPPUsed > decPPTotal)
                    {
                        blnValid = false;
                        sbdMessage.AppendLine().Append('\t').AppendFastFormat(GlobalSettings.CultureInfo,
                                                                          await LanguageManager.GetStringAsync(
                                                                              "Message_InvalidPowerPoints",
                                                                              token: token).ConfigureAwait(false),
                                                                          decPPUsed - decPPTotal, decPPTotal);
                    }
                }

                // Check if the character has more than the permitted amount of native languages.
                int intLanguages
                    = await (await objSkillsSection.GetKnowledgeSkillsAsync(token).ConfigureAwait(false))
                            .CountAsync(
                                (objSkill, t) => objSkill.GetIsNativeLanguageAsync(t), token)
                            .ConfigureAwait(false);

                int intLanguageLimit = 1 + (await ImprovementManager
                                                  .ValueOfAsync(CharacterObject,
                                                                Improvement.ImprovementType.NativeLanguageLimit,
                                                                token: token).ConfigureAwait(false))
                    .StandardRound();

                if (intLanguages != intLanguageLimit)
                {
                    if (intLanguages > intLanguageLimit)
                    {
                        blnValid = false;
                        sbdMessage.AppendLine().Append('\t').AppendFastFormat(GlobalSettings.CultureInfo,
                                                                          await LanguageManager.GetStringAsync(
                                                                              "Message_OverLanguageLimit",
                                                                              token: token).ConfigureAwait(false),
                                                                          intLanguages, intLanguageLimit);
                    }
                    else if (await ConfirmUnusedAsync(confirmUnusedPoints,
                                 StringExtensions.FastFormat(
                                     GlobalSettings.CultureInfo,
                                     await LanguageManager.GetStringAsync(
                                             "Message_ExtraNativeLanguages",
                                             token: token)
                                         .ConfigureAwait(false),
                                     intLanguageLimit - intLanguages),
                                 await LanguageManager.GetStringAsync(
                                         "MessageTitle_ExtraNativeLanguages",
                                         token: token)
                                     .ConfigureAwait(false),
                                 MessageButtons.YesNo,
                                 MessageIcon.Warning, token: token).ConfigureAwait(false) == MessageResult.No)
                    {
                        blnValid = false;
                    }
                }

                // Check the character's equipment and make sure nothing goes over their set Maximum Availability.
                // Number of items over the specified Availability the character is allowed to have (typically from the Restricted Gear Quality).
                Dictionary<int, int> dicRestrictedGearLimits = new Dictionary<int, int>(1);
                List<Improvement> lstUsedImprovements
                    = await ImprovementManager.GetCachedImprovementListForValueOfAsync(
                                                  CharacterObject, Improvement.ImprovementType.RestrictedGear,
                                                  token: token)
                                              .ConfigureAwait(false);
                bool blnHasRestrictedGearAvailable = lstUsedImprovements.Count != 0;
                if (blnHasRestrictedGearAvailable)
                {
                    foreach (Improvement objImprovement in lstUsedImprovements)
                    {
                        int intLoopAvailability = objImprovement.Value.StandardRound();
                        if (dicRestrictedGearLimits.TryGetValue(intLoopAvailability, out int intExistingValue))
                            dicRestrictedGearLimits[intLoopAvailability] = intExistingValue + objImprovement.Rating;
                        else
                            dicRestrictedGearLimits.Add(intLoopAvailability, objImprovement.Rating);
                    }
                }

                List<int> lstToRemove = new List<int>(dicRestrictedGearLimits.Count);
                // Remove all Restricted Gear availabilities with non-positive counts
                foreach (KeyValuePair<int, int> kvpLoop in dicRestrictedGearLimits)
                {
                    if (kvpLoop.Value <= 0)
                        lstToRemove.Add(kvpLoop.Key);
                }

                foreach (int intLoop in lstToRemove)
                    dicRestrictedGearLimits.Remove(intLoop);

                using (new FetchSafelyFromObjectPool<StringBuilder>(Utils.StringBuilderPool,
                                                              out StringBuilder sbdAvailItems))
                using (new FetchSafelyFromObjectPool<StringBuilder>(Utils.StringBuilderPool,
                                                              out StringBuilder sbdRestrictedItems))
                {
                    // Gear Availability.
                    int intRestrictedCount = await (await CharacterObject.GetGearAsync(token).ConfigureAwait(false))
                                                   .SumAsync((objGear, t) => objGear.CheckRestrictedGear(
                                                           dicRestrictedGearLimits, sbdAvailItems,
                                                           sbdRestrictedItems,
                                                           t), token)
                                                   .ConfigureAwait(false)
                                             // Cyberware Availability.
                                             + await (await CharacterObject.GetCyberwareAsync(token)
                                                                           .ConfigureAwait(false)).SumAsync((objGear, t) => objGear.CheckRestrictedGear(
                                                     dicRestrictedGearLimits, sbdAvailItems, sbdRestrictedItems,
                                                     t), token).ConfigureAwait(false)
                                             // Armor Availability.
                                             + await (await CharacterObject.GetArmorAsync(token)
                                                                           .ConfigureAwait(false)).SumAsync((objGear, t) => objGear.CheckRestrictedGear(
                                                     dicRestrictedGearLimits, sbdAvailItems, sbdRestrictedItems,
                                                     t), token).ConfigureAwait(false)
                                             // Weapon Availability.
                                             + await (await CharacterObject.GetWeaponsAsync(token)
                                                                           .ConfigureAwait(false)).SumAsync((objGear, t) => objGear.CheckRestrictedGear(
                                                     dicRestrictedGearLimits, sbdAvailItems, sbdRestrictedItems,
                                                     t), token).ConfigureAwait(false)
                                             // Vehicle Availability.
                                             + await (await CharacterObject.GetVehiclesAsync(token)
                                                                           .ConfigureAwait(false)).SumAsync((objGear, t) => objGear.CheckRestrictedGear(
                                                     dicRestrictedGearLimits, sbdAvailItems, sbdRestrictedItems,
                                                     t), token).ConfigureAwait(false);

                    // Make sure the character is not carrying more items over the allowed Avail than they are allowed.
                    if (intRestrictedCount > 0)
                    {
                        blnValid = false;
                        sbdMessage.AppendLine().Append('\t').AppendFastFormat(GlobalSettings.CultureInfo,
                                                                          await LanguageManager.GetStringAsync(
                                                                                  "Message_InvalidAvail",
                                                                                  token: token)
                                                                              .ConfigureAwait(false),
                                                                          intRestrictedCount,
                                                                          await CharacterObjectSettings
                                                                              .GetMaximumAvailabilityAsync(token).ConfigureAwait(false));
                        sbdMessage.Append(sbdAvailItems);
                        if (blnHasRestrictedGearAvailable)
                        {
                            sbdMessage.AppendLine().AppendFastFormat(
                                                                 await LanguageManager.GetStringAsync(
                                                                         "Message_RestrictedGearUsed", token: token)
                                                                     .ConfigureAwait(false),
                                                                 sbdRestrictedItems.ToString());
                        }
                    }
                }

                // Check for any illegal cyberware grades
                using (new FetchSafelyFromObjectPool<StringBuilder>(Utils.StringBuilderPool,
                                                              out StringBuilder sbdIllegalCyberwareFromGrade))
                {
                    await (await CharacterObject.GetCyberwareAsync(token).ConfigureAwait(false)).ForEachAsync(
                            (objCyberware, t) =>
                                objCyberware.CheckBannedGradesAsync(sbdIllegalCyberwareFromGrade, t), token)
                        .ConfigureAwait(false);

                    await (await CharacterObject.GetVehiclesAsync(token).ConfigureAwait(false)).ForEachAsync(
                        async (objVehicle, t) =>
                        {
                            foreach (Cyberware objCyberware in objVehicle.Mods.SelectMany(
                                         objMod => objMod.Cyberware))
                            {
                                await objCyberware.CheckBannedGradesAsync(sbdIllegalCyberwareFromGrade, t)
                                                  .ConfigureAwait(false);
                            }

                            foreach (Cyberware objCyberware in objVehicle.WeaponMounts.SelectMany(
                                         objMount => objMount.Mods.SelectMany(objMod => objMod.Cyberware)))
                            {
                                await objCyberware.CheckBannedGradesAsync(sbdIllegalCyberwareFromGrade, t)
                                                  .ConfigureAwait(false);
                            }
                        }, token).ConfigureAwait(false);

                    if (sbdIllegalCyberwareFromGrade.Length > 0)
                    {
                        blnValid = false;
                        sbdMessage.AppendLine().Append('\t')
                                  .Append(await LanguageManager
                                                .GetStringAsync("Message_InvalidCyberwareGrades", token: token)
                                                .ConfigureAwait(false))
                                  .Append(sbdIllegalCyberwareFromGrade);
                    }
                }

                // Cyberware: Prototype Transhuman
                decimal decPrototypeTranshumanEssenceMax
                    = await CharacterObject.GetPrototypeTranshumanAsync(token).ConfigureAwait(false);
                if (decPrototypeTranshumanEssenceMax > 0)
                {
                    decimal decPrototypeTranshumanEssenceUsed = await CharacterObject.GetPrototypeTranshumanEssenceUsedAsync(token).ConfigureAwait(false);
                    if (decPrototypeTranshumanEssenceMax < decPrototypeTranshumanEssenceUsed)
                    {
                        blnValid = false;
                        string strEssenceFormat = await CharacterObjectSettings.GetEssenceFormatAsync(token).ConfigureAwait(false);
                        sbdMessage.AppendLine().Append('\t').AppendFastFormat(
                            await LanguageManager.GetStringAsync("Message_OverPrototypeLimit", token: token)
                                                 .ConfigureAwait(false),
                            decPrototypeTranshumanEssenceUsed.ToString(strEssenceFormat,
                                                                       GlobalSettings.CultureInfo),
                            decPrototypeTranshumanEssenceMax.ToString(strEssenceFormat,
                                                                      GlobalSettings.CultureInfo));
                    }
                }

                // Check item Capacities if the option is enabled.
                if (await CharacterObjectSettings.GetEnforceCapacityAsync(token).ConfigureAwait(false))
                {
                    List<string> lstOverCapacity = new List<string>(1);
                    // Armor Capacity.
                    foreach (Armor objArmor in await CharacterObject.GetArmorAsync(token).ConfigureAwait(false))
                    {
                        if (await objArmor.GetCapacityRemainingAsync(token).ConfigureAwait(false) < 0)
                        {
                            lstOverCapacity.Add(await objArmor.GetCurrentDisplayNameShortAsync(token)
                                .ConfigureAwait(false));
                        }

                        foreach (Gear objGear in await objArmor.GearChildren.DeepWhereAsync(
                                     x => x.Children, async (x, t) => await x.GetCapacityRemainingAsync(t).ConfigureAwait(false) < 0,
                                     token).ConfigureAwait(false))
                        {
                            lstOverCapacity.Add(await objGear.GetCurrentDisplayNameShortAsync(token)
                                .ConfigureAwait(false));
                        }

                        await objArmor.ArmorMods.ForEachAsync(async (objMod, t1) =>
                        {
                            foreach (Gear objGear in await objMod.GearChildren.DeepWhereAsync(
                                         x => x.Children,
                                         async (x, t2) => await x.GetCapacityRemainingAsync(t2).ConfigureAwait(false) <
                                                    0,
                                         t1).ConfigureAwait(false))
                            {
                                lstOverCapacity.Add(await objGear.GetCurrentDisplayNameShortAsync(t1)
                                    .ConfigureAwait(false));
                            }
                        }, token).ConfigureAwait(false);
                    }

                    foreach (Weapon objWeapon in
                             await (await CharacterObject.GetWeaponsAsync(token).ConfigureAwait(false))
                                 .DeepWhereAsync(
                                     x => x.Children,
                                     async (x, t) =>
                                         await x.WeaponAccessories.GetCountAsync(t).ConfigureAwait(false) > 0,
                                     token).ConfigureAwait(false))
                    {
                        await objWeapon.WeaponAccessories.ForEachAsync(async (objAccessory, t1) =>
                        {
                            foreach (Gear objGear in await objAccessory.GearChildren.DeepWhereAsync(
                                         x => x.Children,
                                         async (x, t2) => await x.GetCapacityRemainingAsync(t2).ConfigureAwait(false) <
                                                    0,
                                         t1).ConfigureAwait(false))
                            {
                                lstOverCapacity.Add(await objGear.GetCurrentDisplayNameShortAsync(t1)
                                    .ConfigureAwait(false));
                            }
                        }, token).ConfigureAwait(false);
                    }

                    // Gear Capacity.
                    foreach (Gear objGear in await (await CharacterObject.GetGearAsync(token).ConfigureAwait(false))
                                 .DeepWhereAsync(
                                     x => x.Children, async (x, t) => await x.GetCapacityRemainingAsync(t).ConfigureAwait(false) < 0,
                                     token).ConfigureAwait(false))
                    {
                        lstOverCapacity.Add(await objGear.GetCurrentDisplayNameShortAsync(token)
                            .ConfigureAwait(false));
                    }

                    // Cyberware Capacity.
                    foreach (Cyberware objCyberware in await (await CharacterObject.GetCyberwareAsync(token)
                                     .ConfigureAwait(false)).GetAllDescendantsAsync((x, t) => x.GetChildrenAsync(t), token)
                                 .ConfigureAwait(false))
                    {
                        if (await objCyberware.GetCapacityRemainingAsync(token).ConfigureAwait(false) < 0)
                        {
                            lstOverCapacity.Add(await objCyberware.GetCurrentDisplayNameShortAsync(token)
                                .ConfigureAwait(false));
                        }

                        foreach (Gear objGear in await objCyberware.GearChildren.DeepWhereAsync(
                                     x => x.Children, async (x, t) => await x.GetCapacityRemainingAsync(t).ConfigureAwait(false) < 0,
                                     token).ConfigureAwait(false))
                        {
                            lstOverCapacity.Add(await objGear.GetCurrentDisplayNameShortAsync(token)
                                .ConfigureAwait(false));
                        }
                    }

                    // Vehicle Capacity.
                    ThreadSafeObservableCollection<Vehicle> lstVehicles = await CharacterObject.GetVehiclesAsync(token).ConfigureAwait(false);
                    await lstVehicles.ForEachAsync(async (objVehicle, t1) =>
                    {
                        if (await CharacterObjectSettings.BookEnabledAsync("R5", t1).ConfigureAwait(false))
                        {
                            if (objVehicle.IsDrone && await CharacterObjectSettings.GetDroneModsAsync(t1)
                                    .ConfigureAwait(false))
                            {
                                if (await objVehicle.GetDroneModSlotsUsedAsync(t1).ConfigureAwait(false)
                                    > await objVehicle.GetDroneModSlotsAsync(t1).ConfigureAwait(false))
                                {
                                    lstOverCapacity.Add(await objVehicle.GetCurrentDisplayNameShortAsync(t1)
                                        .ConfigureAwait(false));
                                }
                            }
                            else if (await objVehicle.OverR5CapacityAsync(token: t1).ConfigureAwait(false))
                            {
                                lstOverCapacity.Add(await objVehicle.GetCurrentDisplayNameShortAsync(t1)
                                    .ConfigureAwait(false));
                            }
                        }
                        else if (await objVehicle.GetSlotsAsync(t1).ConfigureAwait(false) <
                                 await objVehicle.GetSlotsUsedAsync(t1).ConfigureAwait(false))
                        {
                            lstOverCapacity.Add(await objVehicle.GetCurrentDisplayNameShortAsync(t1)
                                .ConfigureAwait(false));
                        }

                        foreach (Gear objGear in await objVehicle.GearChildren.DeepWhereAsync(
                                     x => x.Children,
                                     async (x, t2) => await x.GetCapacityRemainingAsync(t2).ConfigureAwait(false) < 0,
                                     t1).ConfigureAwait(false))
                        {
                            lstOverCapacity.Add(await objGear.GetCurrentDisplayNameShortAsync(t1)
                                .ConfigureAwait(false));
                        }

                        await objVehicle.Mods.ForEachAsync(async (objMod, t2) =>
                        {
                            foreach (Cyberware objCyberware in await objMod.Cyberware.GetAllDescendantsAsync(
                                         (x, t3) => x.GetChildrenAsync(t3), t2).ConfigureAwait(false))
                            {
                                if (await objCyberware.GetCapacityRemainingAsync(t2).ConfigureAwait(false) < 0)
                                {
                                    lstOverCapacity.Add(await objCyberware
                                        .GetCurrentDisplayNameShortAsync(t2)
                                        .ConfigureAwait(false));
                                }

                                foreach (Gear objGear in await objCyberware.GearChildren.DeepWhereAsync(
                                             x => x.Children,
                                             async (x, t3) => await x.GetCapacityRemainingAsync(t3)
                                                 .ConfigureAwait(false) < 0, t2).ConfigureAwait(false))
                                {
                                    lstOverCapacity.Add(await objGear.GetCurrentDisplayNameShortAsync(t2)
                                        .ConfigureAwait(false));
                                }
                            }
                        }, t1).ConfigureAwait(false);

                        await objVehicle.WeaponMounts.ForEachAsync(async (objMount, t2) =>
                        {
                            if (await objMount.Weapons.GetCountAsync(t2).ConfigureAwait(false) >
                                objMount.WeaponCapacity)
                            {
                                lstOverCapacity.Add(await objMount.GetCurrentDisplayNameShortAsync(t2)
                                    .ConfigureAwait(false));
                            }

                            foreach (Weapon objWeapon in
                                     await objMount.Weapons.DeepWhereAsync(
                                         x => x.Children,
                                         async (x, t3) => await x.WeaponAccessories.GetCountAsync(t3)
                                             .ConfigureAwait(false) > 0, t2).ConfigureAwait(false))
                            {
                                await objWeapon.WeaponAccessories.ForEachAsync(async (objAccessory, t3) =>
                                {
                                    foreach (Gear objGear in await objAccessory.GearChildren.DeepWhereAsync(
                                                 x => x.Children,
                                                 async (x, t4) => await x.GetCapacityRemainingAsync(t4)
                                                     .ConfigureAwait(false) < 0, t3).ConfigureAwait(false))
                                    {
                                        lstOverCapacity.Add(await objGear.GetCurrentDisplayNameShortAsync(t3)
                                            .ConfigureAwait(false));
                                    }
                                }, t2).ConfigureAwait(false);
                            }

                            await objMount.Mods.ForEachAsync(async (objMod, t3) =>
                            {
                                foreach (Cyberware objCyberware in await objMod.Cyberware.GetAllDescendantsAsync(
                                             (x, t4) => x.GetChildrenAsync(t4), t3).ConfigureAwait(false))
                                {
                                    if (await objCyberware.GetCapacityRemainingAsync(t3).ConfigureAwait(false) <
                                        0)
                                    {
                                        lstOverCapacity.Add(await objCyberware
                                            .GetCurrentDisplayNameShortAsync(t3)
                                            .ConfigureAwait(false));
                                    }

                                    foreach (Gear objGear in await (await objCyberware.GetGearChildrenAsync(t3).ConfigureAwait(false)).DeepWhereAsync(
                                                 x => x.Children,
                                                 async (x, t4) => await x.GetCapacityRemainingAsync(t4)
                                                     .ConfigureAwait(false) < 0, t3).ConfigureAwait(false))
                                    {
                                        lstOverCapacity.Add(await objGear.GetCurrentDisplayNameShortAsync(t3)
                                            .ConfigureAwait(false));
                                    }
                                }
                            }, t2).ConfigureAwait(false);
                        }, t1).ConfigureAwait(false);
                    }, token).ConfigureAwait(false);

                    if (lstOverCapacity.Count > 0)
                    {
                        blnValid = false;
                        sbdMessage.AppendLine().Append('\t').AppendFastFormat(
                            GlobalSettings.CultureInfo,
                            await LanguageManager.GetStringAsync("Message_CapacityReachedValidate", token: token)
                                .ConfigureAwait(false),
                            lstOverCapacity.Count);
                        foreach (string strItem in lstOverCapacity)
                        {
                            sbdMessage.AppendLine().Append("\t- ", strItem);
                        }
                    }
                }

                //Check Drone mods for illegalities
                if (await CharacterObjectSettings.BookEnabledAsync("R5", token).ConfigureAwait(false))
                {
                    List<string> lstDronesIllegalDowngrades = new List<string>(1);
                    bool blnIllegalDowngrades = false;
                    int intIllegalDowngrades = await CharacterObject.Vehicles.SumAsync(async (objVehicle, t) =>
                    {
                        if (!objVehicle.IsDrone || !await CharacterObjectSettings.GetDroneModsAsync(t)
                                .ConfigureAwait(false))
                            return 0;
                        int intReturn = 0;
                        foreach (string strModCategory in objVehicle.Mods
                                                                    .Where(objMod => !objMod.IncludedInVehicle
                                                                               && objMod.Equipped
                                                                               && objMod.Downgrade)
                                                                    .Select(x => x.Category))
                        {
                            //Downgrades can't reduce a attribute to less than 1 (except Speed which can go to 0)
                            if (strModCategory == "Handling" &&
                                Convert.ToInt32(await objVehicle.GetTotalHandlingAsync(t).ConfigureAwait(false),
                                    GlobalSettings.InvariantCultureInfo) < 1
                                || strModCategory == "Speed" &&
                                Convert.ToInt32(await objVehicle.GetTotalSpeedAsync(t).ConfigureAwait(false),
                                    GlobalSettings.InvariantCultureInfo) < 0
                                || strModCategory == "Acceleration" &&
                                Convert.ToInt32(await objVehicle.GetTotalAccelAsync(t).ConfigureAwait(false),
                                    GlobalSettings.InvariantCultureInfo) < 1
                                || strModCategory == "Body" &&
                                await objVehicle.GetTotalBodyAsync(t).ConfigureAwait(false) < 1
                                || strModCategory == "Armor" &&
                                await objVehicle.GetTotalArmorAsync(t).ConfigureAwait(false) < 1
                                || strModCategory == "Sensor" &&
                                await objVehicle.GetCalculatedSensorAsync(t).ConfigureAwait(false) < 1)
                            {
                                blnIllegalDowngrades = true;
                                intReturn++;
                                lstDronesIllegalDowngrades.Add(objVehicle.Name);
                                break;
                            }
                        }

                        return intReturn;
                    }, token).ConfigureAwait(false);

                    if (blnIllegalDowngrades)
                    {
                        blnValid = false;
                        sbdMessage.AppendLine().Append('\t').AppendFastFormat(
                            GlobalSettings.CultureInfo,
                            await LanguageManager.GetStringAsync("Message_DroneIllegalDowngrade", token: token)
                                                 .ConfigureAwait(false),
                            intIllegalDowngrades);
                        foreach (string strItem in lstDronesIllegalDowngrades)
                        {
                            sbdMessage.AppendLine().Append("\t- ", strItem);
                        }
                    }
                }

                i = await CharacterObject.GetAttributesAsync(token).ConfigureAwait(false)
                    - await CalculateAttributePriorityPoints(CharacterObject, lstAttributes,
                                                             token: token).ConfigureAwait(false);
                // Check if the character has extra Attribute points
                if (blnValid && i > 0 && await ConfirmUnusedAsync(confirmUnusedPoints,
                        StringExtensions.FastFormat(
                            GlobalSettings.CultureInfo,
                            await LanguageManager.GetStringAsync(
                                    "Message_ExtraPoints",
                                    token: token)
                                .ConfigureAwait(false),
                            i,
                            await LanguageManager
                                .GetStringAsync(
                                    "Label_SummaryPrimaryAttributes",
                                    token: token)
                                .ConfigureAwait(false)),
                        await LanguageManager.GetStringAsync(
                                "MessageTitle_ExtraPoints",
                                token: token)
                            .ConfigureAwait(false),
                        MessageButtons.YesNo,
                        MessageIcon.Warning, token: token).ConfigureAwait(false)
                    == MessageResult.No)
                {
                    blnValid = false;
                }

                i = await CharacterObject.GetSpecialAsync(token).ConfigureAwait(false)
                    - await CalculateAttributePriorityPoints(CharacterObject, lstSpecialAttributes, token: token)
                        .ConfigureAwait(false);
                // Check if the character has extra Special Attribute points
                if (blnValid && i > 0 && await ConfirmUnusedAsync(confirmUnusedPoints,
                        StringExtensions.FastFormat(
                            GlobalSettings.CultureInfo,
                            await LanguageManager.GetStringAsync(
                                    "Message_ExtraPoints",
                                    token: token)
                                .ConfigureAwait(false),
                            i,
                            await LanguageManager
                                .GetStringAsync(
                                    "Label_SummarySpecialAttributes",
                                    token: token)
                                .ConfigureAwait(false)),
                        await LanguageManager.GetStringAsync(
                                "MessageTitle_ExtraPoints",
                                token: token)
                            .ConfigureAwait(false),
                        MessageButtons.YesNo,
                        MessageIcon.Warning, token: token).ConfigureAwait(false)
                    == MessageResult.No)
                {
                    blnValid = false;
                }

                // Check if the character has extra Skill Group points
                if (blnValid && intSkillGroupPoints > 0
                             && await ConfirmUnusedAsync(confirmUnusedPoints,
                                 StringExtensions.FastFormat(
                                     GlobalSettings.CultureInfo,
                                     await LanguageManager.GetStringAsync(
                                             "Message_ExtraPoints", token: token)
                                         .ConfigureAwait(false),
                                     intSkillGroupPoints,
                                     await LanguageManager
                                         .GetStringAsync(
                                             "Label_SummarySkillGroups",
                                             token: token).ConfigureAwait(false)),
                                 await LanguageManager.GetStringAsync(
                                         "MessageTitle_ExtraPoints", token: token)
                                     .ConfigureAwait(false),
                                 MessageButtons.YesNo,
                                 MessageIcon.Warning, token: token).ConfigureAwait(false) == MessageResult.No)
                {
                    blnValid = false;
                }

                // Check if the character has extra Active Skill points
                if (blnValid && intSkillPoints > 0 && await ConfirmUnusedAsync(confirmUnusedPoints,
                        StringExtensions.FastFormat(GlobalSettings.CultureInfo,
                            await LanguageManager.GetStringAsync("Message_ExtraPoints", token: token)
                                .ConfigureAwait(false),
                            intSkillPoints,
                            await LanguageManager
                                .GetStringAsync("Label_SummaryActiveSkills", token: token)
                                .ConfigureAwait(false)),
                        await LanguageManager.GetStringAsync("MessageTitle_ExtraPoints", token: token)
                            .ConfigureAwait(false), MessageButtons.YesNo,
                        MessageIcon.Warning, token: token).ConfigureAwait(false) == MessageResult.No)
                {
                    blnValid = false;
                }

                // Check if the character has extra Knowledge Skill points
                if (blnValid && intKnoSkillPoints > 0
                             && await ConfirmUnusedAsync(confirmUnusedPoints,
                                 StringExtensions.FastFormat(
                                     GlobalSettings.CultureInfo,
                                     await LanguageManager.GetStringAsync(
                                             "Message_ExtraPoints", token: token)
                                         .ConfigureAwait(false),
                                     intKnoSkillPoints,
                                     await LanguageManager
                                         .GetStringAsync(
                                             "Label_SummaryKnowledgeSkills",
                                             token: token).ConfigureAwait(false)),
                                 await LanguageManager.GetStringAsync(
                                         "MessageTitle_ExtraPoints", token: token)
                                     .ConfigureAwait(false),
                                 MessageButtons.YesNo,
                                 MessageIcon.Warning, token: token).ConfigureAwait(false) == MessageResult.No)
                {
                    blnValid = false;
                }

                // Check if the character has extra Contact points
                if (blnValid)
                {
                    int intContactPointsRemain
                        = await CharacterObject.GetContactPointsAsync(token).ConfigureAwait(false);
                    if (intContactPointsRemain > 0)
                    {
                        intContactPointsRemain
                            -= (await CharacterObject.GetContactPointsUsedAsync(token).ConfigureAwait(false)).Item1;
                        if (intContactPointsRemain > 0
                            && await ConfirmUnusedAsync(confirmUnusedPoints,
                                StringExtensions.FastFormat(
                                    GlobalSettings.CultureInfo,
                                    await LanguageManager.GetStringAsync(
                                            "Message_ExtraPoints", token: token)
                                        .ConfigureAwait(false),
                                    intContactPointsRemain,
                                    await LanguageManager
                                        .GetStringAsync(
                                            "String_ContactPoints", token: token)
                                        .ConfigureAwait(false)),
                                await LanguageManager.GetStringAsync(
                                        "MessageTitle_ExtraPoints", token: token)
                                    .ConfigureAwait(false),
                                MessageButtons.YesNo,
                                MessageIcon.Warning, token: token).ConfigureAwait(false) == MessageResult.No)
                        {
                            blnValid = false;
                        }
                    }
                }

                // Check if the character has extra spell points
                if (blnValid && (await CharacterObject.GetFreeSpellsAsync(token).ConfigureAwait(false) > 0
                                 || (await ImprovementManager
                                     .GetCachedImprovementListForValueOfAsync(CharacterObject,
                                         Improvement.ImprovementType.FreeSpells,
                                         token: token)
                                     .ConfigureAwait(false)).Count > 0
                                 || (await ImprovementManager
                                     .GetCachedImprovementListForValueOfAsync(CharacterObject,
                                         Improvement.ImprovementType
                                             .FreeSpellsATT,
                                         token: token).ConfigureAwait(false))
                                 .Count > 0
                                 || (await ImprovementManager
                                     .GetCachedImprovementListForValueOfAsync(CharacterObject,
                                         Improvement.ImprovementType
                                             .FreeSpellsSkill,
                                         token: token).ConfigureAwait(false))
                                 .Count > 0))
                {
                    ThreadSafeObservableCollection<Spell> lstSpells
                        = await CharacterObject.GetSpellsAsync(token).ConfigureAwait(false);
                    // Count the number of Spells the character currently has
                    int intUsedPoints = await lstSpells
                        .CountAsync(
                            spell => spell.Grade == 0 && !spell.FreeBonus,
                            token: token).ConfigureAwait(false);
                    int intTouchOnlySpells = await lstSpells
                        .CountAsync(
                            spell => spell.Grade == 0 && !spell.Alchemical
                                                      && spell.Category != "Rituals"
                                                      && (spell.Range == "T (A)"
                                                          || spell.Range == "T")
                                                      && !spell.FreeBonus, token: token)
                        .ConfigureAwait(false);

                    int intFreeSpells = await CharacterObject.GetFreeSpellsAsync(token).ConfigureAwait(false);

                    token.ThrowIfCancellationRequested();

                    if (intFreeSpells > 0)
                    {
                        // Each spell costs KarmaSpell.
                        int spellCost = await CharacterObject.SpellKarmaCostAsync("Spells", token)
                            .ConfigureAwait(false);
                        // Factor in any qualities that can be bought with spell points.
                        // It is only karma-efficient to use spell points for Mastery qualities if real spell karma cost is not greater than unmodified spell karma cost
                        int intKarmaSpell = await CharacterObjectSettings.GetKarmaSpellAsync(token)
                            .ConfigureAwait(false);
                        if (spellCost <= intKarmaSpell && intKarmaSpell != 0)
                        {
                            // Assume that every [spell cost] karma spent on a Mastery quality is paid for with a priority-given spell point instead, as that is the most karma-efficient.
                            int intMasteryQualityKarmaUsed
                                = await (await CharacterObject.GetQualitiesAsync(token).ConfigureAwait(false))
                                    .SumAsync(
                                        objQuality =>
                                            objQuality.CanBuyWithSpellPoints,
                                        (objQuality, t) => objQuality.GetBPAsync(t), token)
                                    .ConfigureAwait(false);
                            if (intMasteryQualityKarmaUsed != 0)
                            {
                                int intQualityKarmaToSpellPoints
                                    = Math.Min(
                                        intFreeSpells,
                                        intMasteryQualityKarmaUsed * await CharacterObjectSettings
                                            .GetKarmaQualityAsync(token)
                                            .ConfigureAwait(false)
                                        / intKarmaSpell);
                                intUsedPoints += intQualityKarmaToSpellPoints;
                            }
                        }
                    }

                    token.ThrowIfCancellationRequested();

                    int intLimitMod = (await ImprovementManager
                                           .ValueOfAsync(CharacterObject,
                                               Improvement.ImprovementType.SpellLimit,
                                               token: token).ConfigureAwait(false)
                                       + await ImprovementManager
                                           .ValueOfAsync(CharacterObject,
                                               Improvement.ImprovementType.FreeSpells,
                                               token: token).ConfigureAwait(false))
                        .StandardRound();
                    int intLimitModTouchOnly = 0;
                    foreach (Improvement imp in await ImprovementManager
                                 .GetCachedImprovementListForValueOfAsync(
                                     CharacterObject,
                                     Improvement.ImprovementType.FreeSpellsATT,
                                     token: token).ConfigureAwait(false))
                    {
                        token.ThrowIfCancellationRequested();
                        int intAttValue
                            = await (await CharacterObject.GetAttributeAsync(imp.ImprovedName, token: token)
                                    .ConfigureAwait(false)).GetTotalValueAsync(token)
                                .ConfigureAwait(false);
                        if (imp.UniqueName.Contains("half"))
                            intAttValue = intAttValue.DivAwayFromZero(2);
                        if (imp.UniqueName.Contains("touchonly"))
                            intLimitModTouchOnly += intAttValue;
                        else
                            intLimitMod += intAttValue;
                    }

                    foreach (Improvement imp in await ImprovementManager
                                 .GetCachedImprovementListForValueOfAsync(
                                     CharacterObject,
                                     Improvement.ImprovementType.FreeSpellsSkill,
                                     token: token).ConfigureAwait(false))
                    {
                        token.ThrowIfCancellationRequested();
                        Skill skill = await objSkillsSection.GetActiveSkillAsync(imp.ImprovedName, token)
                            .ConfigureAwait(false);
                        if (skill == null)
                            continue;
                        int intSkillValue = await skill.GetTotalBaseRatingAsync(token).ConfigureAwait(false);

                        if (imp.UniqueName.Contains("half"))
                            intSkillValue = intSkillValue.DivAwayFromZero(2);
                        if (imp.UniqueName.Contains("touchonly"))
                            intLimitModTouchOnly += intSkillValue;
                        else
                            intLimitMod += intSkillValue;
                        //TODO: I don't like this being hardcoded, even though I know full well CGL are never going to reuse this.
                        intUsedPoints -= await (await skill.GetSpecializationsAsync(token).ConfigureAwait(false))
                            .CountAsync(
                                async (spec, t) =>
                                {
                                    string strNameInner = await spec.GetNameAsync(t).ConfigureAwait(false);
                                    return await (await CharacterObject.GetSpellsAsync(t)
                                            .ConfigureAwait(false)).AnyAsync(
                                            spell => spell.Category == strNameInner && !spell.FreeBonus,
                                            t)
                                        .ConfigureAwait(false);
                                },
                                token)
                            .ConfigureAwait(false);
                    }

                    if (await CharacterObject.GetUseMysticAdeptPPsAsync(token).ConfigureAwait(false)
                        && await CharacterObjectSettings.GetPrioritySpellsAsAdeptPowersAsync(token)
                            .ConfigureAwait(false))
                    {
                        intUsedPoints += await CharacterObject.GetMysticAdeptPowerPointsAsync(token)
                            .ConfigureAwait(false);
                    }

                    intUsedPoints -= intTouchOnlySpells - Math.Max(0, intTouchOnlySpells - intLimitModTouchOnly);

                    int intPointsRemaining = intFreeSpells + intLimitMod - intUsedPoints;

                    if (intPointsRemaining > 0
                        && await ConfirmUnusedAsync(confirmUnusedPoints,
                            StringExtensions.FastFormat(
                                GlobalSettings.CultureInfo,
                                await LanguageManager.GetStringAsync(
                                        "Message_ExtraPoints", token: token)
                                    .ConfigureAwait(false),
                                intPointsRemaining,
                                await LanguageManager
                                    .GetStringAsync(
                                        "String_FreeSpells", token: token)
                                    .ConfigureAwait(false)),
                            await LanguageManager.GetStringAsync(
                                    "MessageTitle_ExtraPoints", token: token)
                                .ConfigureAwait(false),
                            MessageButtons.YesNo,
                            MessageIcon.Warning, token: token).ConfigureAwait(false) == MessageResult.No)
                    {
                        blnValid = false;
                    }
                }

                // Check if the character has extra complex form points
                if (blnValid)
                {
                    int intCFPointsRemain
                        = await CharacterObject.GetCFPLimitAsync(token).ConfigureAwait(false);
                    if (intCFPointsRemain > 0)
                    {
                        intCFPointsRemain
                            -= await (await CharacterObject.GetComplexFormsAsync(token).ConfigureAwait(false))
                                     .CountAsync(x => x.Grade == 0, token).ConfigureAwait(false);
                        if (intCFPointsRemain > 0
                            && await ConfirmUnusedAsync(confirmUnusedPoints,
                                StringExtensions.FastFormat(
                                    GlobalSettings.CultureInfo,
                                    await LanguageManager.GetStringAsync(
                                            "Message_ExtraPoints", token: token)
                                        .ConfigureAwait(false),
                                    intCFPointsRemain,
                                    await LanguageManager
                                        .GetStringAsync(
                                            "String_FreeCFs", token: token)
                                        .ConfigureAwait(false)),
                                await LanguageManager.GetStringAsync(
                                        "MessageTitle_ExtraPoints", token: token)
                                    .ConfigureAwait(false),
                                MessageButtons.YesNo,
                                MessageIcon.Warning, token: token).ConfigureAwait(false) == MessageResult.No)
                        {
                            blnValid = false;
                        }
                    }
                }


                return new CreationValidation(blnValid, sbdMessage.ToString());
            }

        }

        private static Task<MessageResult> ConfirmUnusedAsync(bool confirm, string message, string caption, MessageButtons buttons, MessageIcon icon, CancellationToken token = default)
            => confirm ? MessageBoxes.ShowAsync(message, caption, buttons, icon, token: token) : Task.FromResult(MessageResult.Yes);

        private static async Task<int> CalculateAttributePriorityPoints(Character character, IAsyncEnumerable<CharacterAttrib> attributes, CancellationToken token = default)
            => await character.GetEffectiveBuildMethodUsesPriorityTablesAsync(token).ConfigureAwait(false)
                ? await attributes.SumAsync((attribute, t) => attribute.GetSpentPriorityPointsAsync(t), token).ConfigureAwait(false) : 0;
    }
}
