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

// Toolkit-agnostic requests for every dialog the engine opens. Member names, types and constructor defaults
// deliberately mirror the legacy WinForms forms (the WinForms host maps them by name); only the members the engine
// actually uses are exposed. Generated from tools/native/UiExtractor --analyze-dialogs.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Chummer.Backend.Equipment;

namespace Chummer
{
    public sealed class SelectItemRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedItem), nameof(SelectedName) };
        public override string DialogName => "SelectItem";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public bool AllowAutoSelect { get => Get(true); set => Set(value); }
        public string Description { get => Get<string>(); set => Set(value); }
        public string SelectedItem { get => Get<string>(); set => Set(value); }
        public string SelectedName => Get<string>();
        public void ForceItem(string strForceItem) => RecordCall(nameof(ForceItem), strForceItem);
        public void SetDropdownItemsMode(IEnumerable<ListItem> lstItems) => RecordCall(nameof(SetDropdownItemsMode), lstItems?.ToList());
        public void SetGeneralItemsMode(IEnumerable<ListItem> lstItems) => RecordCall(nameof(SetGeneralItemsMode), lstItems?.ToList());
        public void SetRestrictedMode(Character objCharacter) => RecordCall(nameof(SetRestrictedMode), objCharacter);
    }

    public sealed class SelectNumberRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedValue) };
        public SelectNumberRequest(int intDecimalPlaces = 2) : base(intDecimalPlaces) { }
        public override string DialogName => "SelectNumber";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public bool AllowCancel { get => Get(true); set => Set(value); }
        public string Description { get => Get<string>(); set => Set(value); }
        public decimal Maximum { get => Get<decimal>(); set => Set(value); }
        public decimal Minimum { get => Get<decimal>(); set => Set(value); }
        public decimal SelectedValue => Get<decimal>();
    }

    public sealed class SelectTextRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedValue) };
        public override string DialogName => "SelectText";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string Description { get => Get<string>(); set => Set(value); }
        public bool PreventXPathErrors { get => Get<bool>(); set => Set(value); }
        public string SelectedValue { get => Get<string>(); set => Set(value); }
    }

    public sealed class SelectAttributeRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedAttribute) };
        public SelectAttributeRequest(params string[] lstAttributeAbbrevs) : base(new object[] { lstAttributeAbbrevs }) { }
        public override string DialogName => "SelectAttribute";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string Description { get => Get<string>(); set => Set(value); }
        public string SelectedAttribute => Get<string>();
    }

    public sealed class SelectSkillRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedSkill) };
        public SelectSkillRequest(Character objCharacter, string strSource = "") : base(objCharacter, strSource) { }
        public override string DialogName => "SelectSkill";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string Description { get => Get<string>(); set => Set(value); }
        public string ExcludeCategory { get => Get<string>(); set => Set(value); }
        public string ExcludeSkill { get => Get<string>(); set => Set(value); }
        public string ExcludeSkillGroup { get => Get<string>(); set => Set(value); }
        public XmlNode LimitToCategories { get => Get<XmlNode>(); set => Set(value); }
        public string LimitToSkill { get => Get<string>(); set => Set(value); }
        public string LinkedAttribute { get => Get<string>(); set => Set(value); }
        public int MaximumRating { get => Get<int>(); set => Set(value); }
        public int MinimumRating { get => Get<int>(); set => Set(value); }
        public string OnlyCategory { get => Get<string>(); set => Set(value); }
        public string OnlySkillGroup { get => Get<string>(); set => Set(value); }
        public string SelectedSkill => Get<string>();
    }

    public sealed class SelectSkillGroupRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedSkillGroup) };
        public SelectSkillGroupRequest(Character objCharacter) : base(objCharacter) { }
        public override string DialogName => "SelectSkillGroup";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string Description { get => Get<string>(); set => Set(value); }
        public string ExcludeCategory { get => Get<string>(); set => Set(value); }
        public string OnlyGroup { get => Get<string>(); set => Set(value); }
        public string SelectedSkillGroup => Get<string>();
    }

    public sealed class SelectSpellCategoryRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedCategory) };
        public SelectSpellCategoryRequest(Character objCharacter) : base(objCharacter) { }
        public override string DialogName => "SelectSpellCategory";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string Description { get => Get<string>(); set => Set(value); }
        public string SelectedCategory => Get<string>();
        public void SetExcludeCategories(IEnumerable<string> lstCategories) => RecordCall(nameof(SetExcludeCategories), lstCategories?.ToList());
    }

    public sealed class SelectSideRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedSide) };
        public override string DialogName => "SelectSide";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string Description { get => Get<string>(); set => Set(value); }
        public string SelectedSide => Get<string>();
        public void ForceValue(string strSide) => RecordCall(nameof(ForceValue), strSide);
    }

    public sealed class SelectMetamagicRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedMetamagic) };
        public SelectMetamagicRequest(Character objCharacter, InitiationGrade objGrade) : base(objCharacter, objGrade) { }
        public override string DialogName => "SelectMetamagic";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string SelectedMetamagic => Get<string>();
    }

    public sealed class SelectMentorSpiritRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedMentor), nameof(Choice1), nameof(Choice2) };
        public SelectMentorSpiritRequest(Character objCharacter, string strXmlFile = "mentors.xml") : base(objCharacter, strXmlFile) { }
        public override string DialogName => "SelectMentorSpirit";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string ForcedMentor { get => Get<string>(); set => Set(value); }
        public string SelectedMentor => Get<string>();
        public string Choice1 => Get<string>();
        public string Choice2 => Get<string>();
    }

    public sealed class SelectBuildMethodRequest : DialogRequest
    {
        public SelectBuildMethodRequest(Character objCharacter, bool blnUseCurrentValues = false) : base(objCharacter, blnUseCurrentValues) { }
        public override string DialogName => "SelectBuildMethod";
        public override IReadOnlyCollection<string> ResultNames => Array.Empty<string>();
    }

    public sealed class SelectMetatypePriorityRequest : DialogRequest
    {
        public SelectMetatypePriorityRequest(Character objCharacter, string strXmlFile = "metatypes.xml") : base(objCharacter, strXmlFile) { }
        public override string DialogName => "SelectMetatypePriority";
        public override IReadOnlyCollection<string> ResultNames => Array.Empty<string>();
    }

    public sealed class SelectMetatypeKarmaRequest : DialogRequest
    {
        public SelectMetatypeKarmaRequest(Character objCharacter) : base(objCharacter) { }
        public override string DialogName => "SelectMetatypeKarma";
        public override IReadOnlyCollection<string> ResultNames => Array.Empty<string>();
    }

    public sealed class SelectAIProgramRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedProgram) };
        public SelectAIProgramRequest(Character objCharacter, bool blnAdvancedProgramAllowed = true, bool blnInherentProgram = false)
            : base(objCharacter, blnAdvancedProgramAllowed, blnInherentProgram) { }
        public override string DialogName => "SelectAIProgram";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string SelectedProgram => Get<string>();
    }

    public sealed class SelectWeaponCategoryRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedCategory) };
        public SelectWeaponCategoryRequest(Character objCharacter) : base(objCharacter) { }
        public override string DialogName => "SelectWeaponCategory";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string Description { get => Get<string>(); set => Set(value); }
        public string OnlyCategory { get => Get<string>(); set => Set(value); }
        public string WeaponType { get => Get<string>(); set => Set(value); }
        public string SelectedCategory => Get<string>();
    }

    public sealed class SelectSpellRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedSpell) };
        public SelectSpellRequest(Character objCharacter) : base(objCharacter) { }
        public override string DialogName => "SelectSpell";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string ForceSpellName { get => Get<string>(); set => Set(value); }
        public bool IgnoreRequirements { get => Get<bool>(); set => Set(value); }
        public string LimitCategory { get => Get<string>(); set => Set(value); }
        public string SelectedSpell => Get<string>();
    }

    public sealed class SelectPowerRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedPower) };
        public SelectPowerRequest(Character objCharacter) : base(objCharacter) { }
        public override string DialogName => "SelectPower";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public bool ForBonus { get => Get<bool>(); set => Set(value); }
        public bool IgnoreLimits { get => Get<bool>(); set => Set(value); }
        public string LimitToPowers { get => Get<string>(); set => Set(value); }
        public int LimitToRating { get => Get<int>(); set => Set(value); }
        public decimal PointsPerLevel { get => Get<decimal>(); set => Set(value); }
        public string SelectedPower => Get<string>();
    }

    public sealed class SelectOptionalPowerRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedPower), nameof(SelectedPowerExtra) };
        public SelectOptionalPowerRequest(Character objCharacter, params ValueTuple<string, string>[] lstPowerExtraPairs)
            : base(objCharacter, lstPowerExtraPairs) { }
        public override string DialogName => "SelectOptionalPower";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string Description { get => Get<string>(); set => Set(value); }
        public string SelectedPower => Get<string>();
        public string SelectedPowerExtra => Get<string>();
    }

    public sealed class SelectLimitRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedLimit), nameof(SelectedDisplayLimit) };
        public SelectLimitRequest(params string[] lstLimits) : base(new object[] { lstLimits }) { }
        public override string DialogName => "SelectLimit";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string Description { get => Get<string>(); set => Set(value); }
        public string SelectedLimit => Get<string>();
        public string SelectedDisplayLimit => Get<string>();
    }

    public sealed class SelectComplexFormRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedComplexForm) };
        public SelectComplexFormRequest(Character objCharacter) : base(objCharacter) { }
        public override string DialogName => "SelectComplexForm";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string SelectedComplexForm => Get<string>();
    }

    /// <summary>Mirrors SelectArt.Mode.</summary>
    public enum SelectArtMode
    {
        Art = 0,
        Enhancement,
        Enchantment,
        Ritual
    }

    public sealed class SelectArtRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedItem) };
        public SelectArtRequest(Character objCharacter, SelectArtMode eWindowMode) : base(objCharacter, eWindowMode) { }
        public override string DialogName => "SelectArt";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string SelectedItem => Get<string>();
    }

    public sealed class SelectArmorModRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedArmorMod) };
        public SelectArmorModRequest(Character objCharacter, Armor objParentNode = null) : base(objCharacter, objParentNode) { }
        public override string DialogName => "SelectArmorMod";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public string AllowedCategories { get => Get<string>(); set => Set(value); }
        public bool ExcludeGeneralCategory { get => Get<bool>(); set => Set(value); }
        public string SelectedArmorMod => Get<string>();
    }

    public sealed class SelectMartialArtRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(SelectedMartialArt), nameof(AddAgain) };
        public SelectMartialArtRequest(Character objCharacter) : base(objCharacter) { }
        public override string DialogName => "SelectMartialArt";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        public bool AddAgain => Get<bool>();
        public string SelectedMartialArt => Get<string>();
    }

    public sealed class SelectDiceHitsRequest : DialogRequest
    {
        private static readonly string[] s_astrResults = { nameof(Result) };
        public override string DialogName => "SelectDiceHits";
        public override IReadOnlyCollection<string> ResultNames => s_astrResults;
        /// <summary>Window title.</summary>
        public string Text { get => Get<string>(); set => Set(value); }
        public string Description { get => Get<string>(); set => Set(value); }
        public int Result => Get<int>();

        public Task SetDiceAsync(int value, CancellationToken token = default)
        {
            if (token.IsCancellationRequested)
                return Task.FromCanceled(token);
            RecordCall(nameof(SetDiceAsync), value, CancellationToken.None);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Opens global settings so the user can link a PDF reader or a sourcebook PDF (legacy EditGlobalSettings).
    /// </summary>
    public sealed class EditGlobalSettingsRequest : DialogRequest
    {
        public override string DialogName => "EditGlobalSettings";
        public override IReadOnlyCollection<string> ResultNames => Array.Empty<string>();

        public Task DoLinkPdf(string strCode, CancellationToken token = default)
        {
            if (token.IsCancellationRequested)
                return Task.FromCanceled(token);
            RecordCall(nameof(DoLinkPdf), strCode, CancellationToken.None);
            return Task.CompletedTask;
        }

        public Task DoLinkPdfReader(CancellationToken token = default)
        {
            if (token.IsCancellationRequested)
                return Task.FromCanceled(token);
            RecordCall(nameof(DoLinkPdfReader), CancellationToken.None);
            return Task.CompletedTask;
        }
    }
}
