/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Backend.Attributes;
using Chummer.Backend.Equipment;
using Chummer.Backend.Enums;
using Chummer.Backend.Skills;

namespace Chummer.Rules
{
    public static partial class CreationRules
    {
        /// <summary>Legacy CharacterCreate.CalculateBPandRefreshBPDisplays, with display-only branches removed.
        /// Includes racial costs, contacts, qualities, attributes, skills, spells, foci, spirits and initiation.</summary>
        public static async Task<int> CalculateKarmaAsync(Character CharacterObject, CancellationToken token = default)
        {
            CharacterSettings CharacterObjectSettings = await CharacterObject.GetSettingsAsync(token).ConfigureAwait(false);
            IAsyncDisposable locker = await CharacterObject.LockObject.EnterReadLockAsync(token).ConfigureAwait(false);
            try
            {
                int intKarmaPointsRemain = await CharacterObjectSettings.GetBuildKarmaAsync(token).ConfigureAwait(false);
                int intMetatypeBP;
                if (!await CharacterObject.GetEffectiveBuildMethodUsesPriorityTablesAsync(token).ConfigureAwait(false))
                {
                    // Subtract the BP used for Metatype.
                    intMetatypeBP = await CharacterObject.GetMetatypeBPAsync(token).ConfigureAwait(false)
                                    * await CharacterObjectSettings.GetMetatypeCostsKarmaMultiplierAsync(token)
                                        .ConfigureAwait(false);
                }
                else
                {
                    intMetatypeBP = await CharacterObject.GetMetatypeBPAsync(token).ConfigureAwait(false);
                }

                intKarmaPointsRemain -= intMetatypeBP;

                token.ThrowIfCancellationRequested();

                // ------------------------------------------------------------------------------
                // Calculate the points used by Contacts.
                bool blnFriendsInHighPlaces
                    = await CharacterObject.GetFriendsInHighPlacesAsync(token).ConfigureAwait(false);
                int intContactPoints = await CharacterObject.GetContactPointsAsync(token).ConfigureAwait(false);
                int intFriendsInHighPlacesPoints = blnFriendsInHighPlaces
                    ? await (await CharacterObject.GetAttributeAsync("CHA", token: token).ConfigureAwait(false))
                        .GetValueAsync(token).ConfigureAwait(false) * 4
                    : 0;
                (int intPointsInContacts, int intHighPlacesFriends) =
                    await CharacterObject.GetContactPointsUsedAsync(token).ConfigureAwait(false);

                intKarmaPointsRemain -= Math.Max(0, intPointsInContacts - intContactPoints) +
                                        Math.Max(0, intHighPlacesFriends - intFriendsInHighPlacesPoints);

                token.ThrowIfCancellationRequested();
                // ------------------------------------------------------------------------------
                // Calculate the BP used by Qualities.
                int intLifeModuleQualities = 0;
                int intKarmaQuality = await CharacterObjectSettings.GetKarmaQualityAsync(token).ConfigureAwait(false);
                ThreadSafeObservableCollection<Quality> lstQualities
                    = await CharacterObject.GetQualitiesAsync(token).ConfigureAwait(false);
                {
                    await lstQualities.ForEachAsync(async (objLoopQuality, t) =>
                    {
                        if (!await objLoopQuality.GetContributeToBPAsync(t).ConfigureAwait(false))
                            return;
                        int intLoopCost = await objLoopQuality.GetBPAsync(t).ConfigureAwait(false) *
                                          await objLoopQuality.GetLevelsAsync(t).ConfigureAwait(false) *
                                          intKarmaQuality;
                        if (await objLoopQuality.GetTypeAsync(t).ConfigureAwait(false) == QualityType.LifeModule)
                        {
                            intLifeModuleQualities += intLoopCost;

                        }

                    }, token: token).ConfigureAwait(false);

                }

                int intQualityPointsUsed = intLifeModuleQualities
                                           - await CharacterObject.GetNegativeQualityKarmaAsync(token: token)
                                               .ConfigureAwait(false)
                                           + await CharacterObject.GetPositiveQualityKarmaAsync(token)
                                               .ConfigureAwait(false);

                intKarmaPointsRemain -= intQualityPointsUsed;
                // Changelings must either have a balanced negative and positive number of metagenic qualities, or have 1 more point of positive than negative.
                // If the latter, karma is used to balance them out.
                if (await CharacterObject.GetMetagenicPositiveQualityKarmaAsync(token).ConfigureAwait(false)
                    + await CharacterObject.GetMetagenicNegativeQualityKarmaAsync(token).ConfigureAwait(false) == 1)
                    intKarmaPointsRemain--;

                token.ThrowIfCancellationRequested();

                // ------------------------------------------------------------------------------
                // Update Primary Attributes and Special Attributes values.
                int intAttributePointsUsed
                    = await CalculateAttributeBP(
                        await (await CharacterObject.GetAttributeSectionAsync(token).ConfigureAwait(false))
                            .GetAttributeListAsync(token).ConfigureAwait(false), token: token).ConfigureAwait(false);
                intAttributePointsUsed
                    += await CalculateAttributeBP(
                            await (await CharacterObject.GetAttributeSectionAsync(token).ConfigureAwait(false))
                                .GetSpecialAttributeListAsync(token).ConfigureAwait(false), token: token)
                        .ConfigureAwait(false);
                intKarmaPointsRemain -= intAttributePointsUsed;

                token.ThrowIfCancellationRequested();

                // ------------------------------------------------------------------------------
                // Include the BP used by Martial Arts.
                int intMartialArtsPoints = 0;
                {
                    int intKarmaTechnique
                        = await CharacterObjectSettings.GetKarmaTechniqueAsync(token).ConfigureAwait(false);
                    await (await CharacterObject.GetMartialArtsAsync(token).ConfigureAwait(false)).ForEachAsync(
                        async (objMartialArt, t1) =>
                        {
                            t1.ThrowIfCancellationRequested();
                            if (objMartialArt.IsQuality)
                                return;
                            int intLoopCost = objMartialArt.Cost;
                            intMartialArtsPoints += intLoopCost;

                            intMartialArtsPoints += Math.Max(await objMartialArt.Techniques.GetCountAsync(t1).ConfigureAwait(false) - 1, 0)
                                                        * intKarmaTechnique;
                        }, token).ConfigureAwait(false);

                    token.ThrowIfCancellationRequested();

                }

                intKarmaPointsRemain -= intMartialArtsPoints;

                SkillsSection objSkillSection =
                    await CharacterObject.GetSkillsSectionAsync(token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();
                // ------------------------------------------------------------------------------
                // Calculate the BP used by Skill Groups.
                int intSkillGroupsPoints = await objSkillSection.SkillGroups
                    .SumAsync((x, t) => x.GetCurrentKarmaCostAsync(t),
                        token: token).ConfigureAwait(false);
                intKarmaPointsRemain -= intSkillGroupsPoints;

                token.ThrowIfCancellationRequested();
                // ------------------------------------------------------------------------------
                // Calculate the BP used by Active Skills.
                int skillPointsKarma = await objSkillSection.Skills
                    .SumAsync((x, t) => x.GetCurrentKarmaCostAsync(t),
                        token: token).ConfigureAwait(false);
                intKarmaPointsRemain -= skillPointsKarma;

                token.ThrowIfCancellationRequested();
                // ------------------------------------------------------------------------------
                // Calculate the points used by Knowledge Skills.
                int knowledgeKarmaUsed = await objSkillSection.KnowledgeSkills
                    .SumAsync((x, t) => x.GetCurrentKarmaCostAsync(t),
                        token: token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();
                //TODO: Remaining is named USED?
                intKarmaPointsRemain -= knowledgeKarmaUsed;

                token.ThrowIfCancellationRequested();
                // ------------------------------------------------------------------------------
                // Calculate the BP used by Resources/Nuyen.
                int intNuyenBP = (await CharacterObject.GetNuyenBPAsync(token).ConfigureAwait(false)).StandardRound();

                intKarmaPointsRemain -= intNuyenBP;

                token.ThrowIfCancellationRequested();
                // ------------------------------------------------------------------------------
                // Calculate the BP used by Spells.
                int intSpellPointsUsed = 0;
                int intRitualPointsUsed = 0;
                int intPrepPointsUsed = 0;
                if (await CharacterObject.GetMagicianEnabledAsync(token).ConfigureAwait(false)
                    || await CharacterObject.GetAdeptEnabledAsync(token).ConfigureAwait(false)
                    || (await ImprovementManager
                        .GetCachedImprovementListForValueOfAsync(CharacterObject,
                            Improvement.ImprovementType.FreeSpells, token: token)
                        .ConfigureAwait(false)).Count > 0
                    || (await ImprovementManager
                        .GetCachedImprovementListForValueOfAsync(CharacterObject,
                            Improvement.ImprovementType.FreeSpellsATT,
                            token: token).ConfigureAwait(false)).Count > 0
                    || (await ImprovementManager
                        .GetCachedImprovementListForValueOfAsync(CharacterObject,
                            Improvement.ImprovementType.FreeSpellsSkill,
                            token: token).ConfigureAwait(false)).Count > 0)
                {
                    ThreadSafeObservableCollection<Spell> lstSpells
                        = await CharacterObject.GetSpellsAsync(token).ConfigureAwait(false);
                    // Count the number of Spells the character currently has and make sure they do not try to select more Spells than they are allowed.
                    int spells = await lstSpells
                        .CountAsync(
                            spell => spell.Grade == 0 && !spell.Alchemical && spell.Category != "Rituals"
                                     && !spell.FreeBonus, token: token).ConfigureAwait(false);
                    int intTouchOnlySpells = await lstSpells
                        .CountAsync(
                            spell => spell.Grade == 0 && !spell.Alchemical
                                                      && spell.Category != "Rituals"
                                                      && (spell.Range == "T (A)"
                                                          || spell.Range == "T")
                                                      && !spell.FreeBonus, token: token)
                        .ConfigureAwait(false);
                    int rituals = await lstSpells
                        .CountAsync(
                            spell => spell.Grade == 0 && !spell.Alchemical && spell.Category == "Rituals"
                                     && !spell.FreeBonus, token: token).ConfigureAwait(false);
                    int preps = await lstSpells
                        .CountAsync(spell => spell.Grade == 0 && spell.Alchemical && !spell.FreeBonus,
                            token: token).ConfigureAwait(false);

                    token.ThrowIfCancellationRequested();

                    // Each spell costs KarmaSpell.
                    int spellCost = await CharacterObject.SpellKarmaCostAsync("Spells", token).ConfigureAwait(false);
                    int ritualCost = await CharacterObject.SpellKarmaCostAsync("Rituals", token).ConfigureAwait(false);
                    int prepCost = await CharacterObject.SpellKarmaCostAsync("Preparations", token)
                        .ConfigureAwait(false);
                    int intFreeSpells = await CharacterObject.GetFreeSpellsAsync(token).ConfigureAwait(false);
                    int intQualityKarmaToSpellPoints = 0;

                    token.ThrowIfCancellationRequested();

                    // Factor in any qualities that can be bought with spell points.
                    // It is only karma-efficient to use spell points for Mastery qualities if real spell karma cost is not greater than unmodified spell karma cost
                    int intKarmaSpell = await CharacterObjectSettings.GetKarmaSpellAsync(token).ConfigureAwait(false);
                    if (spellCost <= intKarmaSpell && intFreeSpells > 0 && intKarmaSpell != 0)
                    {
                        // Assume that every [spell cost] karma spent on a Mastery quality is paid for with a priority-given spell point instead, as that is the most karma-efficient.
                        int intMasteryQualityKarmaUsed = await lstQualities.SumAsync(
                                objQuality =>
                                    objQuality.CanBuyWithSpellPoints,
                                (objQuality, t) => objQuality.GetBPAsync(t), token)
                            .ConfigureAwait(false);
                        if (intMasteryQualityKarmaUsed != 0)
                        {
                            intQualityKarmaToSpellPoints
                                = Math.Min(
                                    intFreeSpells,
                                    intMasteryQualityKarmaUsed * intKarmaQuality
                                    / intKarmaSpell);
                            spells += intQualityKarmaToSpellPoints;
                        }
                    }

                    token.ThrowIfCancellationRequested();

                    int intLimitMod = (await ImprovementManager
                                           .ValueOfAsync(CharacterObject, Improvement.ImprovementType.SpellLimit,
                                               token: token).ConfigureAwait(false)
                                       + await ImprovementManager
                                           .ValueOfAsync(CharacterObject, Improvement.ImprovementType.FreeSpells,
                                               token: token).ConfigureAwait(false)).StandardRound();
                    int intLimitModTouchOnly = 0;
                    foreach (Improvement imp in await ImprovementManager
                                 .GetCachedImprovementListForValueOfAsync(
                                     CharacterObject, Improvement.ImprovementType.FreeSpellsATT,
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
                                     CharacterObject, Improvement.ImprovementType.FreeSpellsSkill,
                                     token: token).ConfigureAwait(false))
                    {
                        token.ThrowIfCancellationRequested();
                        Skill skill = await objSkillSection.GetActiveSkillAsync(imp.ImprovedName, token)
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
                        spells -= await skill.Specializations.CountAsync(
                                async (spec, t1) =>
                                    await (await CharacterObject.GetSpellsAsync(t1)
                                            .ConfigureAwait(false)).AnyAsync(
                                            async (spell, t2) =>
                                                spell.Category ==
                                                await spec.GetNameAsync(t2).ConfigureAwait(false) &&
                                                !spell.FreeBonus,
                                            t1)
                                        .ConfigureAwait(false),
                                token)
                            .ConfigureAwait(false);
                    }

                    token.ThrowIfCancellationRequested();

                    int intPPBought = await CharacterObject.GetMysticAdeptPowerPointsAsync(token).ConfigureAwait(false);
                    if (intPPBought > 0)
                    {
                        if (await CharacterObjectSettings.GetPrioritySpellsAsAdeptPowersAsync(token)
                                .ConfigureAwait(false))
                        {
                            spells += Math.Min(intFreeSpells, intPPBought);
                            intPPBought = Math.Max(0, intPPBought - intFreeSpells);
                        }

                        intAttributePointsUsed = intPPBought * await CharacterObjectSettings
                            .GetKarmaMysticAdeptPowerPointAsync(token)
                            .ConfigureAwait(false);
                        intKarmaPointsRemain -= intAttributePointsUsed;
                    }

                    spells -= intTouchOnlySpells - Math.Max(0, intTouchOnlySpells - intLimitModTouchOnly);

                    int spellPoints = intFreeSpells + intLimitMod;
                    int ritualPoints = intFreeSpells + intLimitMod;
                    int prepPoints = intFreeSpells + intLimitMod;
                    for (int i = intFreeSpells + intLimitMod; i > 0; i--)
                    {
                        token.ThrowIfCancellationRequested();
                        if (spells > 0)
                        {
                            spells--;
                            spellPoints--;
                        }
                        else if (rituals > 0)
                        {
                            rituals--;
                            ritualPoints--;
                        }
                        else if (preps > 0)
                        {
                            preps--;
                            prepPoints--;
                        }
                        else
                        {
                            break;
                        }
                    }

                    intKarmaPointsRemain -= Math.Max(0, spells) * spellCost;
                    intKarmaPointsRemain -= Math.Max(0, rituals) * ritualCost;
                    intKarmaPointsRemain -= Math.Max(0, preps) * prepCost;

                    intSpellPointsUsed += Math.Max(Math.Max(0, spells) * spellCost, 0);
                    intRitualPointsUsed += Math.Max(Math.Max(0, rituals) * ritualCost, 0);
                    intPrepPointsUsed += Math.Max(Math.Max(0, preps) * prepCost, 0);

                }

                token.ThrowIfCancellationRequested();
                // ------------------------------------------------------------------------------
                // Calculate binding costs for foci and bonded stacked foci.
                intKarmaPointsRemain -= await CharacterObject.Foci.SumAsync((focus, t) => focus.BindingKarmaCostAsync(t), token: token).ConfigureAwait(false);
                intKarmaPointsRemain -= await CharacterObject.StackedFoci.SumAsync(focus => focus.Bonded, (focus, t) => focus.GetBindingCostAsync(t), token).ConfigureAwait(false);

                // Calculate the BP used by Spirits and Sprites.
                int intSpiritPointsUsed = 0;
                int intSpritePointsUsed = 0;
                intKarmaPointsRemain -= await (await CharacterObject.GetSpiritsAsync(token).ConfigureAwait(false))
                    .SumAsync(
                        async (objSpirit, t) =>
                        {
                            int intLoopKarma = await objSpirit.GetServicesOwedAsync(t).ConfigureAwait(false)
                                               * await CharacterObjectSettings.GetKarmaSpiritAsync(t)
                                                   .ConfigureAwait(false);
                            // Each Sprite costs KarmaSpirit x Services Owed.
                            int intReturn = intLoopKarma;
                            if (await objSpirit.GetEntityTypeAsync(t).ConfigureAwait(false) == SpiritType.Spirit)
                            {
                                intSpiritPointsUsed += intLoopKarma;
                                // Each Fettered Spirit costs 3 x Force.
                                if (await objSpirit.GetFetteredAsync(t).ConfigureAwait(false))
                                {
                                    int intTemp = await objSpirit.GetForceAsync(t).ConfigureAwait(false)
                                                  * await CharacterObjectSettings.GetKarmaSpiritFetteringAsync(t)
                                                      .ConfigureAwait(false);
                                    intReturn += intTemp;
                                    intSpiritPointsUsed += intTemp;
                                }
                            }
                            else
                            {
                                intSpritePointsUsed += intLoopKarma;
                            }

                            return intReturn;
                        }, token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();
                // ------------------------------------------------------------------------------
                // Calculate the BP used by Complex Forms.
                int intFormsPointsUsed = await (await CharacterObject.GetComplexFormsAsync(token).ConfigureAwait(false))
                    .CountAsync(x => x.Grade == 0, token).ConfigureAwait(false);
                int intCfpLimit = await CharacterObject.GetCFPLimitAsync(token).ConfigureAwait(false);
                if (intFormsPointsUsed > intCfpLimit)
                    intKarmaPointsRemain -= (intFormsPointsUsed - intCfpLimit)
                                            * await CharacterObject.GetComplexFormKarmaCostAsync(token)
                                                .ConfigureAwait(false);

                token.ThrowIfCancellationRequested();
                // ------------------------------------------------------------------------------
                // Calculate the BP used by Programs and Advanced Programs.
                int intAIAdvancedProgramPointsUsed = 0;
                int intAINormalProgramPointsUsed
                    = await (await CharacterObject.GetAIProgramsAsync(token).ConfigureAwait(false)).SumAsync(
                        (objProgram, t) =>
                        {
                            t.ThrowIfCancellationRequested();
                            if (!objProgram.CanDelete)
                                return 0;
                            if (objProgram.IsAdvancedProgram)
                                ++intAIAdvancedProgramPointsUsed;
                            else
                                return 1;
                            return 0;
                        }, token).ConfigureAwait(false);
                int intKarmaCost = 0;
                int intNumAdvancedProgramPointsAsNormalPrograms = 0;
                int intAiNormalProgramLimit
                    = await CharacterObject.GetAINormalProgramLimitAsync(token).ConfigureAwait(false);
                int intAiAdvancedProgramLimit
                    = await CharacterObject.GetAIAdvancedProgramLimitAsync(token).ConfigureAwait(false);
                int intAiNormalProgramKarmaCost
                    = await CharacterObject.GetAIProgramKarmaCostAsync(token).ConfigureAwait(false);
                int intAiAdvancedProgramKarmaCost
                    = await CharacterObject.GetAIAdvancedProgramKarmaCostAsync(token).ConfigureAwait(false);
                if (intAINormalProgramPointsUsed > intAiNormalProgramLimit)
                {
                    if (intAIAdvancedProgramPointsUsed < intAiAdvancedProgramLimit)
                    {
                        intNumAdvancedProgramPointsAsNormalPrograms = Math.Min(
                            intAINormalProgramPointsUsed - intAiNormalProgramLimit,
                            intAiAdvancedProgramLimit - intAIAdvancedProgramPointsUsed);
                        intAINormalProgramPointsUsed -= intNumAdvancedProgramPointsAsNormalPrograms;
                    }

                    if (intAINormalProgramPointsUsed > intAiNormalProgramLimit)
                        intKarmaCost += (intAINormalProgramPointsUsed - intAiNormalProgramLimit)
                                        * intAiNormalProgramKarmaCost;
                }

                if (intAIAdvancedProgramPointsUsed > intAiAdvancedProgramLimit)
                {
                    intKarmaCost += (intAIAdvancedProgramPointsUsed - intAiAdvancedProgramLimit)
                                    * intAiAdvancedProgramKarmaCost;
                }

                intKarmaPointsRemain -= intKarmaCost;

                token.ThrowIfCancellationRequested();
                // ------------------------------------------------------------------------------
                // Calculate the BP used by Initiation.
                int intInitiationPoints
                    = await (await CharacterObject.GetInitiationGradesAsync(token).ConfigureAwait(false)).SumAsync(
                        async (objGrade, t) =>
                        {
                            int intLoop = await objGrade.GetKarmaCostAsync(t).ConfigureAwait(false);
                            // Add the Karma cost of extra Metamagic/Echoes to the Initiation cost.
                            int metamagicKarma
                                = Math.Max(
                                    await (await CharacterObject.GetMetamagicsAsync(t).ConfigureAwait(false))
                                        .CountAsync(x => x.Grade == objGrade.Grade, token: t).ConfigureAwait(false)
                                    - 1,
                                    0);
                            intLoop
                                += await CharacterObjectSettings.GetKarmaMetamagicAsync(t).ConfigureAwait(false)
                                   * metamagicKarma;
                            return intLoop;
                        }, token).ConfigureAwait(false);

                // Add the Karma cost of extra Metamagic/Echoes to the Initiation cost.
                intInitiationPoints += await (await CharacterObject.GetEnhancementsAsync(token).ConfigureAwait(false))
                    .GetCountAsync(token).ConfigureAwait(false) * 2;
                intInitiationPoints += await (await CharacterObject.GetPowersAsync(token).ConfigureAwait(false))
                    .SumAsync((objPower, t) => objPower.Enhancements.GetCountAsync(t),
                        token).ConfigureAwait(false) * 2;

                // Joining a Network does not cost Karma for Technomancers, so this only applies to Magicians/Adepts.
                // Check to see if the character is a member of a Group.
                if (await CharacterObject.GetGroupMemberAsync(token).ConfigureAwait(false)
                    && await CharacterObject.GetMAGEnabledAsync(token).ConfigureAwait(false))
                    intInitiationPoints
                        += await CharacterObjectSettings.GetKarmaJoinGroupAsync(token).ConfigureAwait(false);

                intKarmaPointsRemain -= intInitiationPoints;

                // Add the Karma cost of any Critter Powers.
                intKarmaPointsRemain -= await (await CharacterObject.GetCritterPowersAsync(token).ConfigureAwait(false))
                    .SumAsync(
                        objPower => objPower.Karma, token).ConfigureAwait(false);

                return intKarmaPointsRemain;
            }
            finally { await locker.DisposeAsync().ConfigureAwait(false); }
        }

        private static Task<int> CalculateAttributeBP(IAsyncEnumerable<CharacterAttrib> attributes, CancellationToken token = default)
            => attributes.SumAsync((attribute, t) => attribute.GetTotalKarmaCostAsync(t), token);
    }
}
