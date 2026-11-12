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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Chummer.Backend.Enums;
using Chummer.Rules;

namespace Chummer.Desktop.ViewModels
{
    /// <summary>A button on an ability group header or card.</summary>
    public sealed class AbilityAction
    {
        public AbilityAction(string strLabel, string strTip, ICommand objCommand, bool blnEnabled = true, string strStyle = "")
        {
            Label = strLabel ?? string.Empty;
            Tip = strTip ?? string.Empty;
            Command = objCommand;
            IsEnabled = blnEnabled;
            IsPrimary = strStyle == "primary";
            IsHot = strStyle == "hot";
        }

        public string Label { get; }
        public string Tip { get; }
        public ICommand Command { get; }
        public bool IsEnabled { get; }
        public bool IsPrimary { get; }
        public bool IsHot { get; }
    }

    /// <summary>A −/value/+ control on a card (power rating, spirit force, services owed).</summary>
    public sealed class AbilityStepper
    {
        public AbilityStepper(string strLabel, string strValue, ICommand objDown, bool blnCanDown, string strDownTip, ICommand objUp, bool blnCanUp, string strUpTip)
        {
            Label = strLabel;
            Value = strValue;
            DownCommand = objDown;
            CanDown = blnCanDown;
            DownTip = strDownTip;
            UpCommand = objUp;
            CanUp = blnCanUp;
            UpTip = strUpTip;
        }

        public string Label { get; }
        public string Value { get; }
        public ICommand DownCommand { get; }
        public bool CanDown { get; }
        public string DownTip { get; }
        public ICommand UpCommand { get; }
        public bool CanUp { get; }
        public string UpTip { get; }
    }

    /// <summary>One ability card: the read-only snapshot plus what can be done with it.</summary>
    public sealed class AbilityCardViewModel
    {
        public AbilityCardViewModel(InventoryItemViewModel objItem)
        {
            Item = objItem;
        }

        public InventoryItemViewModel Item { get; }
        public List<AbilityStepper> Steppers { get; } = new List<AbilityStepper>();
        public List<AbilityAction> Actions { get; } = new List<AbilityAction>();
        public ICommand RemoveCommand { get; set; }
        public string RemoveTip { get; set; } = "Remove";
        public bool CanRemove => RemoveCommand != null;
        public bool HasSteppers => Steppers.Count > 0;
        public bool HasActions => Actions.Count > 0;
    }

    /// <summary>A section of the Abilities tab (spells, adept powers...) with its summary line and add buttons.</summary>
    public sealed class AbilityGroupViewModel
    {
        public AbilityGroupViewModel(string strTitle, string strIconKey, IEnumerable<AbilityCardViewModel> lstCards)
        {
            Title = strTitle;
            IconKey = strIconKey;
            Cards = lstCards.ToList();
        }

        public string Title { get; }
        public string IconKey { get; }
        public string Summary { get; set; } = string.Empty;
        public string EmptyText { get; set; } = string.Empty;
        public List<AbilityAction> Actions { get; } = new List<AbilityAction>();
        public List<AbilityCardViewModel> Cards { get; }
        public bool HasSummary => Summary.Length > 0;
        public bool IsEmpty => Cards.Count == 0;
        public string CountText => Cards.Count.ToString(CultureInfo.CurrentCulture);

        public InventoryGroupViewModel ToInventoryGroup() => new InventoryGroupViewModel(Title, Cards.Select(x => x.Item));

        /// <summary>The same section showing only the cards matching every search word (null when nothing matches).</summary>
        public AbilityGroupViewModel Filtered(string[] astrWords)
        {
            if (astrWords.Length == 0)
                return this;
            List<AbilityCardViewModel> lstCards = Cards.Where(x => astrWords.All(w => x.Item.SearchText.Contains(w))).ToList();
            if (lstCards.Count == 0)
                return null;
            AbilityGroupViewModel objReturn = new AbilityGroupViewModel(Title, IconKey, lstCards) { Summary = Summary };
            objReturn.Actions.AddRange(Actions);
            return objReturn;
        }
    }

    /// <summary>
    /// Builds the editable card groups for everything a runner can *do* (magic, resonance, adept powers, martial arts, critter
    /// powers, AI programs, initiation, spirits/sprites), with the add/remove/level actions legacy Chummer offers on those tabs.
    /// </summary>
    internal static class AbilityCards
    {
        private static string L => GlobalSettings.Language;
        private static CultureInfo C => GlobalSettings.CultureInfo;

        private static string Src(string strSource, string strPage) => string.IsNullOrEmpty(strSource) ? string.Empty : (strSource + " " + strPage).Trim();

        private static T Wait<T>(Task<T> objTask) => objTask.GetAwaiter().GetResult();

        private static ICommand Rule(CharacterViewModel objOwner, Func<CancellationToken, Task<RuleResult>> funcRule)
            => new AsyncRelayCommand(() => objOwner.RunRuleAsync(funcRule));

        private static ICommand Remove(CharacterViewModel objOwner, ICanRemove objItem)
            => Rule(objOwner, async t => await objItem.RemoveAsync(true, t).ConfigureAwait(false) ? RuleResult.Changed : RuleResult.Cancelled);

        public static List<AbilityGroupViewModel> Abilities(CharacterViewModel objOwner)
        {
            Character objCharacter = objOwner.Character;
            bool blnCareer = objCharacter.Created;
            int intKarma = objCharacter.Karma;
            List<AbilityGroupViewModel> lstReturn = new List<AbilityGroupViewModel>();

            if (objCharacter.ShouldHaveTradition)
            {
                var tradition = new AbilityGroupViewModel(objCharacter.RESEnabled ? "STREAM" : "TRADITION", "Icon.Spell", Array.Empty<AbilityCardViewModel>())
                {
                    Summary = objCharacter.MagicTradition.CurrentDisplayName,
                    EmptyText = objCharacter.MagicTradition.Type == TraditionType.None ? "Choose a tradition or stream." : "Drain: " + objCharacter.MagicTradition.DrainExpression
                };
                tradition.Actions.Add(new AbilityAction("CHOOSE", "Set the runner's magical tradition or resonance stream", new AsyncRelayCommand(objOwner.ChooseTraditionAsync)));
                lstReturn.Add(tradition);
            }

            // ----- spells -----
            List<Spell> lstSpellObjects = objCharacter.Spells.ToList();
            if (objCharacter.MagicianEnabled || lstSpellObjects.Count > 0 || (objCharacter.AdeptEnabled && objCharacter.AllowFreeSpells().Item1))
            {
                List<AbilityCardViewModel> lstSpells = lstSpellObjects.Select(x =>
                {
                    AbilityCardViewModel objCard = new AbilityCardViewModel(InventoryItemViewModel.Card(
                        x.CurrentDisplayName, x.DisplayCategory(L), x.Alchemical ? "Icon.Drug" : "Icon.Spell",
                        x.DisplayType(L) + "   " + x.DisplayRange(L) + "   DV " + x.DisplayDv(L),
                        x.FreeBonus ? "FREE" : x.Grade > 0 ? "GRADE " + x.Grade.ToString(C) : x.Extended ? "EXT" : x.Limited ? "LTD" : string.Empty,
                        new[]
                        {
                            new ItemStat("Type", x.DisplayType(L)), new ItemStat("Range", x.DisplayRange(L)), new ItemStat("Damage", x.DisplayDamage(L, C)),
                            new ItemStat("Duration", x.DisplayDuration(L)), new ItemStat("Drain", x.DisplayDv(L)), new ItemStat("Descriptors", x.DisplayDescriptors(L)),
                        }, x.Notes, Src(x.Source, x.Page)));
                    if (x.Grade >= 0)
                    {
                        objCard.RemoveCommand = Remove(objOwner, x);
                        objCard.RemoveTip = blnCareer ? "Forget this spell (no karma refund)" : "Remove this spell";
                    }
                    return objCard;
                }).OrderBy(x => x.Item.Category, StringComparer.CurrentCulture).ThenBy(x => x.Item.Name, StringComparer.CurrentCulture).ToList();
                AbilityGroupViewModel objGroup = new AbilityGroupViewModel("SPELLS", "Icon.Spell", lstSpells) { EmptyText = "No spells yet." };
                int intCost = objCharacter.SpellKarmaCost("Spells");
                objGroup.Summary = blnCareer
                    ? "New spell " + intCost.ToString(C) + " karma"
                    : lstSpellObjects.Count(x => !x.Alchemical && x.Category != "Rituals").ToString(C) + " / " + (objCharacter.GetAttribute("MAG").TotalValue * 2).ToString(C) + " spells known";
                objGroup.Actions.Add(new AbilityAction("LEARN SPELL", blnCareer ? "Learn a new spell for " + intCost.ToString(C) + " karma" : "Add a spell",
                                                       new AsyncRelayCommand(objOwner.LearnSpellAsync), true, "primary"));
                objGroup.Actions.Add(new AbilityAction("CREATE SPELL", "Design a custom spell", objOwner.CreateCustomSpellCommand));
                lstReturn.Add(objGroup);
            }

            // ----- adept powers -----
            List<Power> lstPowerObjects = objCharacter.Powers.ToList();
            if (objCharacter.AdeptEnabled || lstPowerObjects.Count > 0)
            {
                List<AbilityCardViewModel> lstPowers = lstPowerObjects.Select(x =>
                {
                    AbilityCardViewModel objCard = new AbilityCardViewModel(InventoryItemViewModel.Card(
                        x.CurrentDisplayName, "Adept power", "Icon.AdeptPower", x.DisplayPoints + " PP", x.TotalRating > 0 ? "R" + x.TotalRating.ToString(C) : string.Empty,
                        new[] { new ItemStat("Rating", x.TotalRating > 0 ? x.TotalRating.ToString(C) : string.Empty), new ItemStat("Power points", x.DisplayPoints), new ItemStat("Action", x.DisplayAction) },
                        x.Notes, Src(x.Source, x.Page)));
                    if (x.LevelsEnabled)
                    {
                        int intRating = x.Rating;
                        int intMax = Wait(MagicRules.GetPowerMaximumRatingAsync(x));
                        objCard.Steppers.Add(new AbilityStepper("RATING", x.TotalRating.ToString(C) + (x.FreeLevels > 0 ? " (" + x.FreeLevels.ToString(C) + " free)" : string.Empty),
                                                                Rule(objOwner, t => MagicRules.SetPowerRatingAsync(x, intRating - 1, t)), intRating > 0, "Lower the rating",
                                                                Rule(objOwner, t => MagicRules.SetPowerRatingAsync(x, intRating + 1, t)), intRating < intMax,
                                                                intRating < intMax ? "Raise the rating" : "At the maximum rating"));
                    }
                    if (x.FreeLevels == 0)
                    {
                        objCard.RemoveCommand = Rule(objOwner, t => MagicRules.RemovePowerAsync(x, t));
                        objCard.RemoveTip = "Remove this power";
                    }
                    return objCard;
                }).ToList();
                AbilityGroupViewModel objGroup = new AbilityGroupViewModel("ADEPT POWERS", "Icon.AdeptPower", lstPowers) { EmptyText = "No adept powers yet." };
                decimal decTotal = objCharacter.PowerPointsTotal;
                decimal decUsed = objCharacter.PowerPointsUsed;
                objGroup.Summary = (decTotal - decUsed).ToString("0.##", C) + " of " + decTotal.ToString("0.##", C) + " power points free";
                objGroup.Actions.Add(new AbilityAction("ADD POWER", "Add an adept power (paid with power points)", new AsyncRelayCommand(objOwner.AddPowerAsync), true, "primary"));
                if (!blnCareer && objCharacter.UseMysticAdeptPPs)
                    objGroup.Actions.Add(new AbilityAction("ALLOCATE PP", "Choose mystic adept power points; their karma cost is included in the build budget",
                        new AsyncRelayCommand(objOwner.AllocateMysticPowerPointsAsync)));
                lstReturn.Add(objGroup);
            }

            // ----- complex forms -----
            List<ComplexForm> lstFormObjects = objCharacter.ComplexForms.ToList();
            if (objCharacter.RESEnabled || lstFormObjects.Count > 0)
            {
                List<AbilityCardViewModel> lstForms = lstFormObjects.Select(x =>
                {
                    AbilityCardViewModel objCard = new AbilityCardViewModel(InventoryItemViewModel.Card(
                        x.CurrentDisplayName, "Complex form", "Icon.Sprite", "Target " + x.DisplayTarget(L) + "   FV " + x.DisplayFv(L), string.Empty,
                        new[] { new ItemStat("Target", x.DisplayTarget(L)), new ItemStat("Duration", x.DisplayDuration(L)), new ItemStat("Fade", x.DisplayFv(L)) },
                        x.Notes, Src(x.Source, x.Page)));
                    if (x.Grade >= 0)
                    {
                        objCard.RemoveCommand = Remove(objOwner, x);
                        objCard.RemoveTip = "Remove this complex form";
                    }
                    return objCard;
                }).ToList();
                AbilityGroupViewModel objGroup = new AbilityGroupViewModel("COMPLEX FORMS", "Icon.Sprite", lstForms) { EmptyText = "No complex forms yet." };
                int intCost = objCharacter.ComplexFormKarmaCost;
                objGroup.Summary = lstFormObjects.Count.ToString(C) + " / " + (objCharacter.GetAttribute("RES").TotalValue * 2).ToString(C) + " forms"
                                   + (blnCareer ? "  //  new form " + intCost.ToString(C) + " karma" : string.Empty);
                objGroup.Actions.Add(new AbilityAction("ADD FORM", blnCareer ? "Learn a complex form for " + intCost.ToString(C) + " karma" : "Add a complex form",
                                                       new AsyncRelayCommand(objOwner.AddComplexFormAsync), true, "primary"));
                lstReturn.Add(objGroup);
            }

            // ----- AI programs -----
            List<AIProgram> lstProgramObjects = objCharacter.AIPrograms.ToList();
            if (objCharacter.AdvancedProgramsEnabled || lstProgramObjects.Count > 0)
            {
                List<AbilityCardViewModel> lstPrograms = lstProgramObjects.Select(x =>
                {
                    AbilityCardViewModel objCard = new AbilityCardViewModel(InventoryItemViewModel.Card(
                        x.CurrentDisplayName, x.IsAdvancedProgram ? "Advanced program" : "AI program", "Icon.Cyberdeck", x.DisplayRequiresProgram(L), string.Empty, null,
                        x.Notes, Src(x.Source, x.Page)));
                    if (x.CanDelete)
                    {
                        objCard.RemoveCommand = Remove(objOwner, x);
                        objCard.RemoveTip = "Remove this program";
                    }
                    return objCard;
                }).ToList();
                AbilityGroupViewModel objGroup = new AbilityGroupViewModel("AI PROGRAMS", "Icon.Cyberdeck", lstPrograms) { EmptyText = "No programs yet." };
                if (blnCareer)
                    objGroup.Summary = "Program " + objCharacter.AIProgramKarmaCost.ToString(C) + " / advanced " + objCharacter.AIAdvancedProgramKarmaCost.ToString(C) + " karma";
                objGroup.Actions.Add(new AbilityAction("ADD PROGRAM", "Add an AI program", new AsyncRelayCommand(objOwner.AddAIProgramAsync), true, "primary"));
                lstReturn.Add(objGroup);
            }

            // ----- initiation / submersion -----
            List<InitiationGrade> lstGradeObjects = objCharacter.InitiationGrades.ToList().OrderBy(x => x.Grade).ToList();
            bool blnMagic = objCharacter.MAGEnabled;
            if ((objCharacter.InitiationEnabled && !objCharacter.InitiationForceDisabled) || lstGradeObjects.Count > 0)
            {
                List<Metamagic> lstMetamagics = objCharacter.Metamagics.ToList();
                List<AbilityCardViewModel> lstCards = new List<AbilityCardViewModel>();
                foreach (InitiationGrade objGrade in lstGradeObjects)
                {
                    List<string> lstTaken = lstMetamagics.Where(x => x.Grade == objGrade.Grade).Select(x => "◆ " + x.CurrentDisplayName).ToList();
                    AbilityCardViewModel objCard = new AbilityCardViewModel(InventoryItemViewModel.Card(
                        (objGrade.Technomancer ? "Submersion " : "Initiation ") + objGrade.Grade.ToString(C), objGrade.Technomancer ? "Submersion grade" : "Initiate grade", "Icon.Initiation",
                        lstTaken.Count > 0 ? string.Join("  ", lstTaken.Select(x => x.Substring(2))) : string.Join("  ", new[] { objGrade.Group ? "Group" : null, objGrade.Ordeal ? "Ordeal" : null, objGrade.Schooling ? "Schooling" : null }.Where(y => y != null)),
                        objGrade.Grade.ToString(C), null, string.Empty, string.Empty, lstTaken));
                    InitiationGrade objLoopGrade = objGrade;
                    objCard.Actions.Add(new AbilityAction(objGrade.Technomancer ? "+ ECHO" : "+ METAMAGIC",
                                                          lstTaken.Count > 0 && blnCareer ? "Add another for " + objCharacter.Settings.KarmaMetamagic.ToString(C) + " karma" : "Choose this grade's " + (objGrade.Technomancer ? "echo" : "metamagic"),
                                                          new AsyncRelayCommand(() => objOwner.AddMetamagicAsync(objLoopGrade))));
                    if (objGrade.Grade == lstGradeObjects[lstGradeObjects.Count - 1].Grade)
                    {
                        objCard.RemoveCommand = Remove(objOwner, objGrade);
                        objCard.RemoveTip = "Remove this grade";
                    }
                    lstCards.Add(objCard);
                }
                foreach (Metamagic objMetamagic in lstMetamagics.Where(x => lstGradeObjects.All(y => y.Grade != x.Grade)))
                {
                    AbilityCardViewModel objCard = new AbilityCardViewModel(InventoryItemViewModel.Card(objMetamagic.CurrentDisplayName, "Metamagic / echo", "Icon.Initiation",
                                                                                                         string.Empty, string.Empty, null, objMetamagic.Notes, Src(objMetamagic.Source, objMetamagic.Page)));
                    objCard.RemoveCommand = Remove(objOwner, objMetamagic);
                    lstCards.Add(objCard);
                }
                lstCards.AddRange(objCharacter.Arts.ToList().Select(x => new AbilityCardViewModel(InventoryItemViewModel.Card(x.CurrentDisplayName, "Art", "Icon.Initiation", string.Empty, string.Empty, null))));
                lstCards.AddRange(objCharacter.Enhancements.ToList().Select(x => new AbilityCardViewModel(InventoryItemViewModel.Card(x.CurrentDisplayName, "Enhancement", "Icon.Initiation", string.Empty, string.Empty, null))));
                AbilityGroupViewModel objGroup = new AbilityGroupViewModel(blnMagic ? "INITIATION & METAMAGIC" : "SUBMERSION & ECHOES", "Icon.Initiation", lstCards)
                {
                    EmptyText = blnMagic ? "Not initiated." : "Not submerged.",
                };
                int intGrade = blnMagic ? objCharacter.InitiateGrade : objCharacter.SubmersionGrade;
                int intCost = Wait(MagicRules.GetInitiationCostAsync(objCharacter, false, false, false));
                objGroup.Summary = "Grade " + intGrade.ToString(C) + (blnCareer ? "  //  next grade " + intCost.ToString(C) + " karma" : string.Empty);
                objGroup.Actions.Add(new AbilityAction(blnMagic ? "INITIATE" : "SUBMERGE",
                                                       (blnMagic ? "Initiate to grade " : "Submerge to grade ") + (intGrade + 1).ToString(C) + (blnCareer ? " for " + intCost.ToString(C) + " karma (less with group/ordeal/schooling)" : string.Empty),
                                                       new AsyncRelayCommand(objOwner.InitiateAsync), !blnCareer || intKarma > 0, "primary"));
                lstReturn.Add(objGroup);
            }

            // ----- martial arts -----
            {
                List<AbilityCardViewModel> lstArts = objCharacter.MartialArts.ToList().Select(x =>
                {
                    List<MartialArtTechnique> lstTechniques = x.Techniques.ToList();
                    AbilityCardViewModel objCard = new AbilityCardViewModel(InventoryItemViewModel.Card(
                        x.CurrentDisplayName, "Martial art", "Icon.MartialArt", lstTechniques.Count.ToString(C) + (lstTechniques.Count == 1 ? " technique" : " techniques"), string.Empty, null,
                        x.Notes, Src(x.Source, x.Page), lstTechniques.Select(y => "◆ " + y.CurrentDisplayName)));
                    MartialArt objArt = x;
                    int intCost = Wait(MagicRules.GetTechniqueCostAsync(objCharacter, x));
                    objCard.Actions.Add(new AbilityAction("+ TECHNIQUE", blnCareer && intCost > 0 ? "Learn a technique for " + intCost.ToString(C) + " karma" : "Learn a technique",
                                                          new AsyncRelayCommand(() => objOwner.AddTechniqueAsync(objArt))));
                    if (!x.IsQuality)
                    {
                        objCard.RemoveCommand = Remove(objOwner, x);
                        objCard.RemoveTip = "Remove this martial art";
                    }
                    return objCard;
                }).ToList();
                AbilityGroupViewModel objGroup = new AbilityGroupViewModel("MARTIAL ARTS", "Icon.MartialArt", lstArts) { EmptyText = "No martial arts." };
                objGroup.Actions.Add(new AbilityAction("ADD STYLE", blnCareer ? "Learn a martial art style (costs karma)" : "Add a martial art style",
                                                       new AsyncRelayCommand(objOwner.AddMartialArtAsync), true, string.Empty));
                lstReturn.Add(objGroup);
            }

            // ----- critter powers -----
            List<AbilityCardViewModel> lstCritter = objCharacter.CritterPowers.ToList().Select(x =>
            {
                var card = new AbilityCardViewModel(InventoryItemViewModel.Card(
                x.CurrentDisplayName, x.DisplayCategory(L), "Icon.Spirit", x.DisplayType(L) + "   " + x.DisplayAction(L), x.Rating > 0 ? "R" + x.Rating.ToString(C) : string.Empty,
                new[] { new ItemStat("Type", x.DisplayType(L)), new ItemStat("Action", x.DisplayAction(L)), new ItemStat("Range", x.DisplayRange(L)), new ItemStat("Duration", x.DisplayDuration(L)) }, x.Notes, Src(x.Source, x.Page)));
                if (x.Grade == 0) card.RemoveCommand = Remove(objOwner, x);
                card.RemoveTip = "Remove this critter power";
                return card;
            }).ToList();
            var critterGroup = new AbilityGroupViewModel("CRITTER POWERS", "Icon.Spirit", lstCritter) { EmptyText = "No critter powers." };
            critterGroup.Actions.Add(new AbilityAction("ADD CRITTER POWER", "Choose an optional power or restore a removed innate power",
                new AsyncRelayCommand(objOwner.AddCritterPowerAsync)));
            lstReturn.Add(critterGroup);

            // ----- spirits / sprites -----
            List<Spirit> lstSpiritObjects = objCharacter.Spirits.ToList();
            bool blnSpirits = objCharacter.MagicianEnabled || lstSpiritObjects.Any(x => x.EntityType == SpiritType.Spirit);
            bool blnSprites = objCharacter.RESEnabled || lstSpiritObjects.Any(x => x.EntityType == SpiritType.Sprite);
            if (blnSpirits)
                lstReturn.Add(SpiritGroup(objOwner, lstSpiritObjects, SpiritType.Spirit));
            if (blnSprites)
                lstReturn.Add(SpiritGroup(objOwner, lstSpiritObjects, SpiritType.Sprite));
            return lstReturn;
        }

        private static AbilityGroupViewModel SpiritGroup(CharacterViewModel objOwner, List<Spirit> lstAll, SpiritType eType)
        {
            Character objCharacter = objOwner.Character;
            bool blnSprite = eType == SpiritType.Sprite;
            bool blnCareer = objCharacter.Created;
            int intMaxForce = blnSprite ? objCharacter.MaxSpriteLevel : objCharacter.MaxSpiritForce;
            List<AbilityCardViewModel> lstCards = lstAll.Where(x => x.EntityType == eType).Select(x =>
            {
                Spirit objSpirit = x;
                string strName = string.IsNullOrWhiteSpace(x.CritterName) ? x.CurrentDisplayName : x.CritterName + " (" + x.CurrentDisplayName + ")";
                List<string> lstTags = new List<string>();
                if (x.Bound)
                    lstTags.Add(blnSprite ? "registered" : "bound");
                if (x.Fettered)
                    lstTags.Add(blnSprite ? "pet" : "fettered");
                AbilityCardViewModel objCard = new AbilityCardViewModel(InventoryItemViewModel.Card(
                    string.IsNullOrWhiteSpace(strName) ? (blnSprite ? "Sprite" : "Spirit") : strName, blnSprite ? "Sprite" : "Spirit", blnSprite ? "Icon.Sprite" : "Icon.Spirit",
                    (blnSprite ? "Level " : "Force ") + x.Force.ToString(C) + "   " + x.ServicesOwed.ToString(C) + (blnSprite ? " tasks" : " services") + (lstTags.Count > 0 ? "   " + string.Join(", ", lstTags) : string.Empty),
                    x.Force.ToString(C),
                    new[] { new ItemStat(blnSprite ? "Level" : "Force", x.Force.ToString(C)), new ItemStat(blnSprite ? "Tasks" : "Services", x.ServicesOwed.ToString(C)), new ItemStat(blnSprite ? "Registered" : "Bound", x.Bound ? "yes" : "no") },
                    x.Notes));
                objCard.Steppers.Add(new AbilityStepper(blnSprite ? "TASKS" : "SERVICES", x.ServicesOwed.ToString(C),
                                                        Rule(objOwner, t => MagicRules.ChangeServicesAsync(objSpirit, -1, t)), x.ServicesOwed > 0, blnSprite ? "Use a task" : "Use a service",
                                                        Rule(objOwner, t => MagicRules.ChangeServicesAsync(objSpirit, 1, t)), true, blnSprite ? "Add a task" : "Add a service"));
                if (blnCareer)
                    objCard.Steppers.Add(new AbilityStepper(blnSprite ? "LEVEL" : "FORCE", x.Force.ToString(C),
                                                            Rule(objOwner, t => MagicRules.ChangeForceAsync(objCharacter, objSpirit, -1, t)), x.Force > 1, "Lower",
                                                            Rule(objOwner, t => MagicRules.ChangeForceAsync(objCharacter, objSpirit, 1, t)), x.Force < intMaxForce, x.Force < intMaxForce ? "Raise" : "At your maximum"));
                if (blnCareer)
                    objCard.Actions.Add(new AbilityAction(x.Bound ? (blnSprite ? "REGISTERED ✔" : "BOUND ✔") : (blnSprite ? "REGISTER" : "BIND"),
                                                          x.Bound ? "Click to mark as no longer " + (blnSprite ? "registered" : "bound") : (blnSprite ? "Mark as registered" : "Mark as bound"),
                                                          Rule(objOwner, async t =>
                                                          {
                                                              await objSpirit.SetBoundAsync(!await objSpirit.GetBoundAsync(t).ConfigureAwait(false), t).ConfigureAwait(false);
                                                              return RuleResult.Changed;
                                                          })));
                if (x.AllowFettering)
                    objCard.Actions.Add(new AbilityAction(x.Fettered ? (blnSprite ? "PET ✔" : "FETTERED ✔") : (blnSprite ? "PET" : "FETTER"),
                                                          blnSprite ? "Toggle pet sprite" : "Toggle fettered (costs MAG while fettered)",
                                                          Rule(objOwner, async t =>
                                                          {
                                                              await objSpirit.SetFetteredAsync(!await objSpirit.GetFetteredAsync(t).ConfigureAwait(false), t).ConfigureAwait(false);
                                                              return RuleResult.Changed;
                                                          })));
                objCard.RemoveCommand = Rule(objOwner, t => MagicRules.RemoveSpiritAsync(objCharacter, objSpirit, t));
                objCard.RemoveTip = blnSprite ? "Release this sprite" : "Release this spirit";
                return objCard;
            }).ToList();
            AbilityGroupViewModel objGroup = new AbilityGroupViewModel(blnSprite ? "SPRITES" : "SPIRITS", blnSprite ? "Icon.Sprite" : "Icon.Spirit", lstCards)
            {
                EmptyText = blnSprite ? "No sprites compiled." : "No spirits summoned.",
            };
            int intBound = lstAll.Count(x => x.EntityType == eType && x.Bound && !x.Fettered);
            objGroup.Summary = blnSprite
                ? intBound.ToString(C) + " / " + objCharacter.RegisteredSpriteLimit.ToString(C) + " registered  //  max level " + intMaxForce.ToString(C)
                : intBound.ToString(C) + " / " + objCharacter.BoundSpiritLimit.ToString(C) + " bound  //  max force " + intMaxForce.ToString(C);
            objGroup.Actions.Add(new AbilityAction(blnSprite ? "COMPILE SPRITE" : "SUMMON SPIRIT", blnSprite ? "Add a sprite" : "Add a spirit",
                                                   new AsyncRelayCommand(() => objOwner.AddSpiritAsync(eType)), true, "primary"));
            return objGroup;
        }

        public static List<InventoryGroupViewModel> Network(Character objCharacter)
        {
            List<InventoryGroupViewModel> lstReturn = new List<InventoryGroupViewModel>();
            // Contacts, enemies and pets have their own dossier cards (ContactCardViewModel).
            List<InventoryItemViewModel> lstSpirits = objCharacter.Spirits.ToList().Select(x => InventoryItemViewModel.Card(
                string.IsNullOrWhiteSpace(x.CritterName) ? x.CurrentDisplayName : x.CritterName + " (" + x.CurrentDisplayName + ")",
                x.EntityType == SpiritType.Sprite ? "Sprite" : "Spirit", x.EntityType == SpiritType.Sprite ? "Icon.Sprite" : "Icon.Spirit",
                (x.EntityType == SpiritType.Sprite ? "Level " : "Force ") + x.Force.ToString(C) + "   " + x.ServicesOwed.ToString(C) + (x.EntityType == SpiritType.Sprite ? " tasks" : " services")
                + (x.Bound ? "   bound" : string.Empty),
                x.Force.ToString(C),
                new[] { new ItemStat(x.EntityType == SpiritType.Sprite ? "Level" : "Force", x.Force.ToString(C)), new ItemStat(x.EntityType == SpiritType.Sprite ? "Tasks" : "Services", x.ServicesOwed.ToString(C)), new ItemStat("Bound", x.Bound ? "yes" : "no") },
                x.Notes)).ToList();
            if (lstSpirits.Count > 0)
                lstReturn.Add(new InventoryGroupViewModel("SPIRITS & SPRITES", lstSpirits));
            return lstReturn;
        }
    }
}
