/*  This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */

using System;
using System.Collections.Generic;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>
    /// The legacy EditCharacterSettings form as data: which <see cref="CharacterSettings"/> property each control edited, its
    /// label (the control's language Tag), its suffix label, limits and Enabled/Visible bindings, grouped like the legacy group
    /// boxes. Extracted from Chummer/Forms/EditCharacterSettings.cs (SetupDataBindings) and its Designer file.
    /// </summary>
    internal static class CharacterSettingsSchema
    {
        public static IReadOnlyList<SettingGroup> BasicOptions(CharacterSettingsEditorViewModel h)
        {
            return new[]
            {
                new SettingGroup("Label_CharacterOptions_CharacterCreationSettings", new SettingOption[]
                {
                    h.BuildMethodOption,
                    h.PriorityTableOption,
                    Text(h, "PriorityArray", "Label_SelectBP_Priorities", null, x => x.Length == 5, blnExpression: false, strVisibleBy: "BuildMethodIsPriority"),
                    Num(h, "SumtoTen", "Label_SelectBP_SumToX", null, 0, 100, strVisibleBy: "BuildMethodIsSumtoTen"),
                    Num(h, "BuildKarma", "Label_SelectBP_StartingKarma", null, 0, 1000000),
                    Num(h, "MaximumAvailability", "Label_SelectBP_MaxAvail", null, 0, 100),
                    Num(h, "QualityKarmaLimit", "Label_SelectBP_QualityKarmaLimit", null, 0, 1000000),
                    Num(h, "NuyenMaximumBP", "Label_SelectBP_MaxNuyen", null, 0, 1000000),
                    Text(h, "ChargenKarmaToNuyenExpression", "Label_CharacterOptions_NuyenExpression", null,
                         x => CommonFunctions.IsCharacterAttributeXPathValidOrNull(x.Replace("{Karma}", "1").Replace("{PriorityNuyen}", "1"))),
                    Num(h, "KarmaCarryover", "Label_Options_Carryover_Karma", null, 0, 1000000),
                    Num(h, "NuyenCarryover", "Label_Options_Carryover_Nuyen", null, 0, 10000000000, 2),
                    Text(h, "GameplayOptionName", "Label_GameplayOptionName", null, null, "Tip_GameplayOptionName", blnExpression: false),
                    Num(h, "MaxNumberMaxAttributesCreate", "Label_CharacterOptions_MaxNumberMaxAttributes", null, 0, 8),
                    Num(h, "MaxSkillRatingCreate", "Label_CharacterOptions_MaxSkillRating", null, 0, 12, strMaximumBy: "MaxSkillRating"),
                    Num(h, "MaxKnowledgeSkillRatingCreate", "Label_CharacterOptions_MaxKnowledgeSkillRating", null, 0, 12, strMaximumBy: "MaxKnowledgeSkillRating"),
                    Text(h, "ContactPointsExpression", "String_ContactPoints", null, Expression),
                    Text(h, "KnowledgePointsExpression", "String_KnowledgePoints", null, Expression),
                    h.AllowedGradesOption,
                }),
                new SettingGroup("Label_CharacterOptions_OptionsOfficialRules", new SettingOption[]
                {
                    Bool(h, "EnforceCapacity", "Checkbox_Option_EnforceCapacity"),
                    Bool(h, "LicenseRestricted", "Checkbox_Options_LicenseRestricted"),
                    Bool(h, "DroneMods", "Checkbox_Options_Dronemods"),
                    Bool(h, "DroneModsMaximumPilot", "Checkbox_Options_Dronemods_Pilot"),
                    Bool(h, "RestrictRecoil", "Checkbox_Options_UseRestrictionsToRecoilCompensation"),
                    Bool(h, "StrictSkillGroupsInCreateMode", "Checkbox_Options_StrictSkillGroups", "Tip_OptionStrictSkillGroups"),
                    Bool(h, "AllowPointBuySpecializationsOnKarmaSkills", "Checkbox_Options_AllowPointBuySpecializationsOnKarmaSkills"),
                    Bool(h, "AllowFreeGrids", "Checkbox_Options_AllowFreeGrids"),
                }),
                new SettingGroup("String_Encumbrance", new SettingOption[]
                {
                    Num(h, "WeightDecimals", "Label_CharacterOptions_WeightDecimalPlaces", null, 0, 28),
                    Text(h, "LiftLimitExpression", "Label_CharacterOptions_LiftLimit", null, Expression),
                    Text(h, "CarryLimitExpression", "Label_CharacterOptions_CarryLimit", null, Expression),
                    Text(h, "EncumbranceIntervalExpression", "Label_CharacterOptions_EncumbranceIntervalLeft", "Label_CharacterOptions_EncumbranceIntervalRight", Expression),
                    new NoteSettingOption(h, "Label_CharacterOptions_EncumbrancePenaltiesHeader"),
                    Toggle(h, "DoEncumbrancePenaltyPhysicalLimit", "Checkbox_CharacterOptions_EncumbrancePenaltyPhysicalLimit", "EncumbrancePenaltyPhysicalLimit", null),
                    Toggle(h, "DoEncumbrancePenaltyMovementSpeed", "Checkbox_CharacterOptions_EncumbrancePenaltyMovementSpeed", "EncumbrancePenaltyMovementSpeed", "\"%\""),
                    Toggle(h, "DoEncumbrancePenaltyAgility", "Checkbox_CharacterOptions_EncumbrancePenaltyAgility", "EncumbrancePenaltyAgility", null),
                    Toggle(h, "DoEncumbrancePenaltyReaction", "Checkbox_CharacterOptions_EncumbrancePenaltyReaction", "EncumbrancePenaltyReaction", null),
                    // Legacy bound this check box to DoEncumbrancePenaltyReaction by mistake (EditCharacterSettings.cs SetupDataBindings).
                    Toggle(h, "DoEncumbrancePenaltyWoundModifier", "Checkbox_CharacterOptions_EncumbrancePenaltyWoundModifier", "EncumbrancePenaltyWoundModifier", null),
                }),
                new SettingGroup("Label_CharacterOptions_Cyberlimbs", new SettingOption[]
                {
                    h.LimbCountOption,
                    Bool(h, "DontUseCyberlimbCalculation", "Checkbox_Options_UseCyberlimbCalculation"),
                    Bool(h, "CyberlegMovement", "Checkbox_Options_CyberlegMovement", "Tip_OptionsCyberlegMovement"),
                    new ToggleNumberSettingOption(h, "CyberlimbAttributeBonusCapOverride", "Checkbox_Options_CyberlimbAttributeBonusCap",
                                                  new NumberSettingOption(h, "CyberlimbAttributeBonusCap", "\"+\"", null, 1, 100)),
                    h.RedlinerOption,
                }),
                new SettingGroup("Label_CharacterOptions_DecimalsAndRounding", new SettingOption[]
                {
                    Num(h, "MinNuyenDecimals", "Label_Options_NuyenDecimalsMinimum", null, 0, 28),
                    Num(h, "MaxNuyenDecimals", "Label_Options_NuyenDecimalsMaximum", null, 0, 28),
                    Num(h, "EssenceDecimals", "Label_Options_EssenceDecimals", null, 2, 28),
                    Bool(h, "DontRoundEssenceInternally", "Checkbox_Option_DontRoundEssenceInternally"),
                    Text(h, "EssenceModifierPostExpression", "Label_Options_EssenceModifierPostExpression", null,
                         x => CommonFunctions.IsCharacterAttributeXPathValidOrNull(x.Replace("{Modifier}", "1.0"))),
                }),
                new SettingGroup("String_InitiativeDice", new SettingOption[]
                {
                    Range(h, "Label_OtherInit", "MinInitiativeDice", "MaxInitiativeDice"),
                    Range(h, "Label_OtherAstralInit", "MinAstralInitiativeDice", "MaxAstralInitiativeDice"),
                    Range(h, "Label_OtherMatrixInitVRCold", "MinColdSimInitiativeDice", "MaxColdSimInitiativeDice"),
                    Range(h, "Label_OtherMatrixInitVRHot", "MinHotSimInitiativeDice", "MaxHotSimInitiativeDice"),
                }),
                new SettingGroup("Tab_MartialArts", new SettingOption[]
                {
                    Num(h, "MaximumMartialArts", "Label_Options_MaximumMartialArts", null, 0, 100),
                    Num(h, "MaximumMartialTechniques", "Label_Options_MaximumMartialTechniques", null, 0, 100),
                }),
            };
        }

        public static IReadOnlyList<SettingGroup> KarmaCosts(CharacterSettingsEditorViewModel h)
        {
            return new[]
            {
                // Legacy shows one three-column table; its columns become cards here.
                new SettingGroup("Tab_Options_General", new SettingOption[]
                {
                    Karma(h, "KarmaSpecialization", "Label_Options_NewSpecialization"),
                    Karma(h, "KarmaKnowledgeSpecialization", "Label_Options_NewKnoSpecialization"),
                    Karma(h, "KarmaNewKnowledgeSkill", "Label_Options_NewKnowledgeSkill"),
                    Karma(h, "KarmaNewActiveSkill", "Label_Options_NewActiveSkill"),
                    Karma(h, "KarmaNewSkillGroup", "Label_Options_NewSkillGroup"),
                    Karma(h, "KarmaImproveKnowledgeSkill", "Label_Options_ImproveKnowledgeSkill", "Label_Options_NewRating"),
                    Karma(h, "KarmaImproveActiveSkill", "Label_Options_ImproveActiveSkill", "Label_Options_NewRating"),
                    Karma(h, "KarmaImproveSkillGroup", "Label_Options_ImproveSkillGroup", "Label_Options_NewRating"),
                    Karma(h, "KarmaAttribute", "Label_Options_ImproveAttribute", "Label_Options_NewRating"),
                    Karma(h, "KarmaContact", "Label_Options_Contacts", "Label_Options_ConnectionLoyalty"),
                    Karma(h, "KarmaQuality", "Label_Options_Qualities", "Label_Options_BPCost"),
                    Num(h, "MetatypeCostsKarmaMultiplier", "Label_Options_MetatypesCostKarma", null, 1, 10),
                    Num(h, "NuyenPerBPWftM", "Label_Options_WftM_Nuyen", "Label_Options_PerKarmaSpent", 0, 99999),
                    Num(h, "NuyenPerBPWftP", "Label_Options_WftP_Nuyen", "Label_Options_PerKarmaGained", 0, 99999),
                    Karma(h, "KarmaTechnique", "Label_Options_MartialArtTechnique"),
                }),
                new SettingGroup("Label_CharacterOptions_MagicAndResonance", new SettingOption[]
                {
                    Karma(h, "KarmaJoinGroup", "Label_Options_JoinGroup"),
                    Karma(h, "KarmaLeaveGroup", "Label_Options_LeaveGroup"),
                    Karma(h, "KarmaSpell", "Label_Options_NewSpell"),
                    Karma(h, "KarmaSpirit", "Label_Options_Spirit", "Label_Options_ServicesOwed"),
                    Karma(h, "KarmaSpiritFettering", "Label_Options_SpiritFettering", "Label_Options_Force"),
                    Karma(h, "KarmaMysticAdeptPowerPoint", "Label_Options_KarmaMysticAdeptPowerPoint"),
                    Karma(h, "KarmaNewComplexForm", "Label_Options_NewComplexForm"),
                    Karma(h, "KarmaNewAIProgram", "Label_Options_NewAIProgram"),
                    Karma(h, "KarmaNewAIAdvancedProgram", "Label_Options_NewAIAdvancedProgram"),
                    // Legacy: "Initiation / Submersion ( [x] x New Rating) + [y]"
                    new RangeSettingOption(h, "Label_Options_Initiation", Num(h, "KarmaInitiation", null, null, 0, 100), Num(h, "KarmaInitiationFlat", null, null, 0, 100))
                    {
                        PrefixText = "(",
                        BetweenText = SettingOption.Text("Label_Options_NewRatingPlus")
                    },
                    Karma(h, "KarmaMetamagic", "Label_Options_Metamagics"),
                }),
                new SettingGroup("Label_SummaryFoci", new SettingOption[]
                {
                    Karma(h, "KarmaAlchemicalFocus", "Label_Options_AlchemicalFocus", "Label_Options_Force"),
                    Karma(h, "KarmaBanishingFocus", "Label_Options_BanishingFocus", "Label_Options_Force"),
                    Karma(h, "KarmaBindingFocus", "Label_Options_BindingFocus", "Label_Options_Force"),
                    Karma(h, "KarmaCenteringFocus", "Label_Options_CenteringFocus", "Label_Options_Force"),
                    Karma(h, "KarmaCounterspellingFocus", "Label_Options_CounterspellingFocus", "Label_Options_Force"),
                    Karma(h, "KarmaDisenchantingFocus", "Label_Options_DisenchantingFocus", "Label_Options_Force"),
                    Karma(h, "KarmaFlexibleSignatureFocus", "Label_Options_FlexibleSignatureFocus", "Label_Options_Force"),
                    Karma(h, "KarmaMaskingFocus", "Label_Options_MaskingFocus", "Label_Options_Force"),
                    Karma(h, "KarmaPowerFocus", "Label_Options_PowerFocus", "Label_Options_Force"),
                    Karma(h, "KarmaQiFocus", "Label_Options_QiFocus", "Label_Options_Force"),
                    Karma(h, "KarmaRitualSpellcastingFocus", "Label_Options_RitualSpellcastingFocus", "Label_Options_Force"),
                    Karma(h, "KarmaSpellcastingFocus", "Label_Options_SpellcastingFocus", "Label_Options_Force"),
                    Karma(h, "KarmaSpellShapingFocus", "Label_Options_SpellShapingFocus", "Label_Options_Force"),
                    Karma(h, "KarmaSummoningFocus", "Label_Options_SummoningFocus", "Label_Options_Force"),
                    Karma(h, "KarmaSustainingFocus", "Label_Options_SustainingFocus", "Label_Options_Force"),
                    Karma(h, "KarmaWeaponFocus", "Label_Options_WeaponFocus", "Label_Options_Force"),
                }),
            };
        }

        public static IReadOnlyList<SettingGroup> HouseRules(CharacterSettingsEditorViewModel h)
        {
            return new[]
            {
                new SettingGroup("String_Qualities", new SettingOption[]
                {
                    Bool(h, "DontDoubleQualityPurchases", "Checkbox_Options_DontDoubleQualityPurchases", "Tip_OptionsDontDoubleQualityPurchases"),
                    Bool(h, "DontDoubleQualityRefunds", "Checkbox_Options_DontDoubleNegativeQualityRefunds", "Tip_OptionsDontDoubleQualityRefunds"),
                    Bool(h, "ExceedPositiveQualities", "Checkbox_Options_ExceedPositiveQualities"),
                    Bool(h, "ExceedPositiveQualitiesCostDoubled", "Checkbox_Options_ExceedPositiveQualitiesCostDoubled", strEnabledBy: "ExceedPositiveQualities"),
                    Bool(h, "ExceedNegativeQualities", "Checkbox_Options_ExceedNegativeQualities"),
                    Bool(h, "ExceedNegativeQualitiesNoBonus", "Checkbox_Options_ExceedNegativeQualitiesLimit", strEnabledBy: "ExceedNegativeQualities"),
                }),
                new SettingGroup("Label_Attributes", new SettingOption[]
                {
                    Bool(h, "ESSLossReducesMaximumOnly", "Checkbox_Options_EssenceLossReducesMaximum"),
                    Bool(h, "UnclampAttributeMinimum", "Checkbox_Options_UnclampAttributeMinimum"),
                    Bool(h, "AllowCyberwareESSDiscounts", "Checkbox_Options_AllowCyberwareESSDiscounts"),
                    Bool(h, "ReverseAttributePriorityOrder", "Checkbox_Options_ReverseAttributePriorityOrder"),
                    Bool(h, "AlternateMetatypeAttributeKarma", "Checkbox_Option_AlternateMetatypeAttributeKarma"),
                    Bool(h, "UseCalculatedPublicAwareness", "Checkbox_Options_UseCalculatedPublicAwareness", "Tip_PublicAwareness"),
                    new ToggleNumberSettingOption(h, "DroneArmorMultiplierEnabled", "Checkbox_Options_DroneArmorMultiplier",
                                                  new NumberSettingOption(h, "DroneArmorMultiplier", "\"×\"", null, 2, 10)),
                }),
                new SettingGroup("Tab_Skills", new SettingOption[]
                {
                    Num(h, "MaxSkillRating", "Label_CharacterOptions_MaxSkillRating", null, 1, 100),
                    Num(h, "MaxKnowledgeSkillRating", "Label_CharacterOptions_MaxKnowledgeSkillRating", null, 1, 100),
                    Bool(h, "SpecializationsBreakSkillGroups", "Checkbox_Options_SpecializationsBreakSkillGroups"),
                    Bool(h, "CompensateSkillGroupKarmaDifference", "Checkbox_Options_CompensateSkillGroupKarmaDifference"),
                    Bool(h, "AllowSkillRegrouping", "Checkbox_Options_SkillRegroup"),
                    Bool(h, "UsePointsOnBrokenGroups", "Checkbox_Options_PointsOnBrokenGroups"),
                    Bool(h, "FreeMartialArtSpecialization", "Checkbox_Option_FreeMartialArtSpecialization"),
                }),
                new SettingGroup("Label_CharacterOptions_Combat", new SettingOption[]
                {
                    Bool(h, "NoArmorEncumbrance", "Checkbox_Options_NoArmorEncumbrance"),
                    Bool(h, "UncappedArmorAccessoryBonuses", "Checkbox_Options_UncappedArmorAccessoryBonuses"),
                    Bool(h, "UnarmedImprovementsApplyToWeapons", "Checkbox_Options_UnarmedSkillImprovements", "Tip_OptionsUnarmedSkillImprovements"),
                }),
                new SettingGroup("Label_CharacterOptions_MagicAndResonance", new SettingOption[]
                {
                    Bool(h, "IgnoreArt", "Checkbox_Options_IgnoreArt", "Tip_OptionsIgnoreArt"),
                    Bool(h, "MysAdeptSecondMAGAttribute", "Checkbox_Options_MysAdeptSecondMAGAttribute", strEnabledBy: "MysAdeptSecondMAGAttributeEnabled"),
                    Bool(h, "MysAdeptAllowPpCareer", "Checkbox_Option_AllowMysadPowerPointCareer", strEnabledBy: "!MysAdeptSecondMAGAttribute"),
                    Bool(h, "PrioritySpellsAsAdeptPowers", "Checkbox_Option_PrioritySpellsAsAdeptPowers", strEnabledBy: "!MysAdeptSecondMAGAttribute"),
                    Bool(h, "ExtendAnyDetectionSpell", "Checkbox_Options_ExtendAnyDetectionSpell"),
                    Bool(h, "AllowLimitedSpellsForBareHandedAdept", "Checkbox_Options_AllowLimitedSpellsForBareHandedAdept"),
                    Bool(h, "IncreasedImprovedAbilityMultiplier", "Checkbox_Options_IncreasedImprovedAbilityModifier"),
                    Bool(h, "SpecialKarmaCostBasedOnShownValue", "Checkbox_Options_SpecialKarmaCost"),
                    Bool(h, "AllowInitiationInCreateMode", "Checkbox_Options_AllowInitiation", "Tip_OptionsAllowInitiation"),
                    Bool(h, "AllowTechnomancerSchooling", "Checkbox_Options_AllowTechnomancerSchooling"),
                    Bool(h, "IgnoreComplexFormLimit", "Checkbox_Options_IgnoreComplexFormLimit", "Tip_OptionsIgnoreComplexFormLimit"),
                    Text(h, "BoundSpiritExpression", "Label_CharacterOptions_BoundSpiritLimit", null, Expression),
                    Text(h, "RegisteredSpriteExpression", "Label_CharacterOptions_RegisteredSpriteLimit", null, Expression),
                }),
                new SettingGroup("Label_CharacterOptions_4eAdaptations", new SettingOption[]
                {
                    Bool(h, "EnableEnemyTracking", "Checkbox_Options_Enable4eStyleEnemyTracking"),
                    Num(h, "KarmaEnemy", "Label_Options_KarmaGainedFromEnemies", "Label_Options_ConnectionLoyalty", 0, 100, strEnabledBy: "EnableEnemyTracking"),
                    Bool(h, "EnemyKarmaQualityLimit", "Checkbox_Options_EnemyKarmaQualityLimit", strEnabledBy: "EnableEnemyTracking"),
                    Bool(h, "MoreLethalGameplay", "Checkbox_Options_MoreLethalGameplace"),
                }),
            };
        }

        internal static bool Expression(string strExpression) => CommonFunctions.IsCharacterAttributeXPathValidOrNull(strExpression);

        private static BoolSettingOption Bool(ISettingOptionHost h, string strProperty, string strLabel, string strTip = null, string strEnabledBy = null)
            => new BoolSettingOption(h, strProperty, strLabel, strTip) { EnabledBy = strEnabledBy };

        private static NumberSettingOption Num(ISettingOptionHost h, string strProperty, string strLabel, string strSuffix, decimal decMin, decimal decMax,
                                               int intDecimals = 0, string strVisibleBy = null, string strEnabledBy = null, string strMaximumBy = null)
            => new NumberSettingOption(h, strProperty, strLabel, strSuffix, decMin, decMax, intDecimals)
            {
                VisibleBy = strVisibleBy,
                EnabledBy = strEnabledBy,
                MaximumBy = strMaximumBy
            };

        private static NumberSettingOption Karma(ISettingOptionHost h, string strProperty, string strLabel, string strSuffix = null)
            => Num(h, strProperty, strLabel, strSuffix, 0, 100);

        private static TextSettingOption Text(ISettingOptionHost h, string strProperty, string strLabel, string strSuffix, Func<string, bool> funcValidate,
                                              string strTip = null, bool blnExpression = true, string strVisibleBy = null)
            => new TextSettingOption(h, strProperty, strLabel, strSuffix, funcValidate, strTip, blnExpression) { VisibleBy = strVisibleBy };

        private static ToggleNumberSettingOption Toggle(ISettingOptionHost h, string strToggle, string strLabel, string strNumber, string strSuffix)
            => new ToggleNumberSettingOption(h, strToggle, strLabel, new NumberSettingOption(h, strNumber, null, strSuffix, 1, 100));

        private static RangeSettingOption Range(ISettingOptionHost h, string strLabel, string strMinimum, string strMaximum)
            => new RangeSettingOption(h, strLabel,
                                      new NumberSettingOption(h, strMinimum, null, null, 0, 99),
                                      new NumberSettingOption(h, strMaximum, null, null, 0, 99) { MinimumBy = strMinimum });
    }
}
