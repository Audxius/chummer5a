using System.Linq;
// Usage: Screenshots <output dir> <character file>...
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Chummer;
using Chummer.Desktop;
using Chummer.Desktop.Services;
using Chummer.Desktop.ViewModels;
using Chummer.Desktop.Views;

internal static class Program
{
    private static int Main(string[] args)
    {
        string strOut = Path.GetFullPath(args.Length > 0 ? args[0] : "screenshots");
        Directory.CreateDirectory(strOut);
        DesktopServices.Register();
        HostServices.Messages = new LoggingMessageService();
        HostServices.Dialogs = new AutoDialogService();
        using (HeadlessUnitTestSession objSession = HeadlessUnitTestSession.StartNew(typeof(HeadlessAppBuilder)))
        {
            objSession.Dispatch(async () =>
            {
                MainWindowViewModel objViewModel = new MainWindowViewModel();
                MainWindow objWindow = new MainWindow { DataContext = objViewModel, Width = 1440, Height = 900 };
                objWindow.Show();
                for (int i = 1; i < args.Length; ++i)
                    await GlobalSettings.MostRecentlyUsedCharacters.InsertAsync(0, Path.GetFullPath(args[i]));
                await objViewModel.Roster.RefreshAsync();
                await Settle();
                Save(objWindow, strOut, "01-roster");
                if (args.Length > 1)
                {
                    await objViewModel.OpenCharacterFileAsync(Path.GetFullPath(args[1]));
                    await Settle();
                    Save(objWindow, strOut, "02-overview");
                    if (objViewModel.CurrentPage is CharacterViewModel objDamaged && objDamaged.PhysicalBoxes.Count > 3)
                    {
                        objDamaged.PhysicalBoxes[3].ToggleCommand.Execute(null);
                        await Settle();
                        await Settle();
                        Save(objWindow, strOut, "02b-damage");
                        Console.WriteLine("damage: " + objDamaged.PhysicalLabel + " dirty=" + objDamaged.IsDirty);
                        objDamaged.Character.PhysicalCMFilled = 0;
                        await Settle();
                    }
                    if (objViewModel.CurrentPage is CharacterViewModel objCharacter)
                    {
                        foreach (CharacterSection eSection in new[] { CharacterSection.Attributes, CharacterSection.Skills, CharacterSection.Qualities, CharacterSection.Inventory, CharacterSection.Abilities, CharacterSection.Network, CharacterSection.Notes })
                        {
                            objCharacter.ShowSectionCommand.Execute(eSection);
                            await Settle();
                            Save(objWindow, strOut, "03-" + eSection.ToString().ToLowerInvariant());
                        }
                        // Editing: raise the first attribute that can be raised and report the karma spent.
                        objCharacter.ShowSectionCommand.Execute(CharacterSection.Attributes);
                        await Settle();
                        AttributeViewModel objRaise = objCharacter.Attributes.Concat(objCharacter.SpecialAttributes).FirstOrDefault(x => x.CanQuickRaise);
                        if (objRaise != null)
                        {
                            string strKarmaBefore = objCharacter.Karma;
                            await objRaise.QuickRaiseCommand.ExecuteAsync(null);
                            await Settle();
                            await Settle();
                            AttributeViewModel objAfter = objCharacter.Attributes.Concat(objCharacter.SpecialAttributes).First(x => x.Abbrev == objRaise.Abbrev);
                            Console.WriteLine("raise " + objRaise.Abbrev + ": " + objRaise.Display + " -> " + objAfter.Display + ", karma " + strKarmaBefore + " -> " + objCharacter.Karma);
                            Save(objWindow, strOut, "03b-attribute-raised");
                        }
                        // ---- LEDGER (career karma/nuyen log, undo, calendar) ----
                        if (objCharacter.Character.Created)
                            await LedgerScript.RunAsync(objWindow, objCharacter, strOut);
                        // ---- end LEDGER ----
                        // ===== Skills section: sorting, grouping and karma edits (SkillRules) =====
                        await SkillsSteps(objWindow, objViewModel, objCharacter, strOut);
                        // ===== end skills =====
                        // Clicking a skill opens its roll in the Dice Terminal.
                        SkillViewModel objSkill = objCharacter.AllSkills.FirstOrDefault(x => x.HasSpecPool)
                                                  ?? objCharacter.AllSkills.FirstOrDefault(x => x.IsRollable);
                        if (objSkill != null)
                        {
                            await objCharacter.RollCommand.ExecuteAsync(objSkill.HasSpecPool ? objSkill.SpecRollName : objSkill.RollName);
                            await Settle();
                            Save(objWindow, strOut, "04a-skill-roll");
                            Console.WriteLine("skill roll " + objSkill.Name + " pool " + objSkill.Pool + "/" + objSkill.SpecPool + ": " + objViewModel.Dice.Formula);
                        }
                        await objCharacter.RollCommand.ExecuteAsync(Chummer.Rolls.RollCatalog.InitiativeRoll);
                        await Settle();
                        Save(objWindow, strOut, "04b-initiative");
                        Console.WriteLine("initiative: " + objViewModel.Dice.Formula);
                        await objCharacter.RollCommand.ExecuteAsync(Chummer.Rolls.RollCatalog.DamageResistanceRoll);
                        await Settle();
                        Console.WriteLine("soak: " + objViewModel.Dice.Formula);
                        objViewModel.ShowDiceCommand.Execute(null);
                        await Settle();
                        Save(objWindow, strOut, "04-dice");
                        foreach (string strQuery in new[] { "escape", "dodge" })
                        {
                            objViewModel.Dice.Query = strQuery;
                            await Settle();
                            if (objViewModel.Dice.Results.Count > 0)
                                objViewModel.Dice.SelectedRoll = objViewModel.Dice.Results[0];
                            await Settle();
                            Save(objWindow, strOut, "05-dice-" + strQuery);
                            Console.WriteLine(strQuery + ": " + objViewModel.Dice.Results.Count + " results; formula: " + objViewModel.Dice.Formula);
                        }
                        objViewModel.Palette.OpenCommand.Execute(null);
                        objViewModel.Palette.Query = "bolt";
                        await Settle();
                        Save(objWindow, strOut, "06-search");
                        Console.WriteLine("search: " + string.Join(" | ", objViewModel.Palette.Results.Select(x => x.Title + " [" + x.Subtitle + "]")));
                        objViewModel.Palette.ActivateCommand.Execute(null);
                        await Settle();
                        Save(objWindow, strOut, "07-search-jump");
                    }
                }
                if (args.Length > 2)
                {
                    // ===== Skills section on a create-mode character =====
                    objViewModel.Palette.CloseCommand.Execute(null);
                    await objViewModel.OpenCharacterFileAsync(Path.GetFullPath(args[2]));
                    await Settle();
                    if (objViewModel.CurrentPage is CharacterViewModel objCreate)
                    {
                        objCreate.ShowSectionCommand.Execute(CharacterSection.Skills);
                        await Settle();
                        Save(objWindow, strOut, "09a-skills-create");
                        SkillViewModel objRaise = objCreate.AllSkills.FirstOrDefault(x => x.CanKarmaUp && x.Rating > 0);
                        if (objRaise != null)
                        {
                            int intBefore = objRaise.KarmaPoints;
                            await objRaise.KarmaUpCommand.ExecuteAsync(null);
                            await Settle();
                            await Settle();
                            Console.WriteLine("create karma+1 " + objRaise.Name + ": " + intBefore + " -> " + objCreate.AllSkills.First(x => x.Skill == objRaise.Skill).KarmaPoints);
                            Save(objWindow, strOut, "09b-skills-create-raised");
                        }
                    }
                    // ===== end skills create =====
                }
                return 0;
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
        Console.WriteLine("screenshots in " + strOut);
        return 0;
    }

    // ===== Skills section steps =====
    private static async Task SkillsSteps(MainWindow objWindow, MainWindowViewModel objViewModel, CharacterViewModel objCharacter, string strOut)
    {
        objCharacter.ShowSectionCommand.Execute(CharacterSection.Skills);
        await Settle();
        Save(objWindow, strOut, "08a-skills-by-pool");
        Console.WriteLine("skills by pool: " + string.Join(", ", objCharacter.SkillRows.OfType<SkillViewModel>().Take(8).Select(x => x.Name + " " + x.PoolText)));
        objCharacter.SortSkillsCommand.Execute(SkillSortMode.Name);
        await Settle();
        Save(objWindow, strOut, "08b-skills-by-name");
        objCharacter.SortSkillsCommand.Execute(SkillSortMode.DicePool);
        objCharacter.GroupSkillsByCategory = true;
        await Settle();
        Save(objWindow, strOut, "08c-skills-grouped");
        objCharacter.GroupSkillsByCategory = false;
        await Settle();
        // Career edits: improve the cheapest knowledge skill, then buy a specialization if karma allows.
        SkillViewModel objKnow = objCharacter.AllSkills.Where(x => x.IsKnowledge && x.CanImprove).OrderBy(x => x.ImproveCost).FirstOrDefault();
        if (objKnow != null)
        {
            string strBefore = objCharacter.Karma;
            await objKnow.ImproveCommand.ExecuteAsync(null);
            await Settle();
            await Settle();
            SkillViewModel objAfter = objCharacter.AllSkills.First(x => x.Skill == objKnow.Skill);
            Console.WriteLine("improve knowledge " + objKnow.Name + ": " + objKnow.Rating + " -> " + objAfter.Rating + ", karma " + strBefore + " -> " + objCharacter.Karma
                              + " // expense: " + objCharacter.Character.ExpenseEntries.ToList().OrderByDescending(x => x.Date).First().Reason);
        }
        SkillViewModel objSpec = objCharacter.AllSkills.FirstOrDefault(x => x.IsCareer && x.ShowAddSpec && x.CanAddSpec && x.Rating > 0);
        if (objSpec != null)
        {
            string strBefore = objCharacter.Karma;
            await objCharacter.RunSkillRuleAsync(t => Chummer.Rules.SkillRules.AddSpecializationCareerAsync(objCharacter.Character, objSpec.Skill,
                (x, _) => Task.FromResult(x.Count > 0 ? x[0].Value.ToString() : "Harness"), t));
            await Settle();
            await Settle();
            Console.WriteLine("add spec to " + objSpec.Name + ": " + objCharacter.AllSkills.First(x => x.Skill == objSpec.Skill).Specialization + ", karma " + strBefore + " -> " + objCharacter.Karma);
        }
        else
            Console.WriteLine("add spec: no affordable specialization at karma " + objCharacter.Karma);
        Save(objWindow, strOut, "08d-skills-after-edit");
    }
    // ===== end skills steps =====

    private static async Task Settle()
    {
        for (int i = 0; i < 20; ++i)
        {
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Save(MainWindow objWindow, string strDir, string strName)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        WriteableBitmap objFrame = objWindow.CaptureRenderedFrame();
        objFrame?.Save(Path.Combine(strDir, strName + ".png"));
        Console.WriteLine("saved " + strName);
    }
}
