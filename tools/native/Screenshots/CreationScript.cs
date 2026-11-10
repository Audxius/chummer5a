using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Chummer;
using Chummer.Backend.Enums;
using Chummer.Desktop.ViewModels;
using Chummer.Desktop.Views;
using Chummer.Desktop.Views.Dialogs;
using Chummer.Rules;
using Chummer.Selection;

internal static class CreationScript
{
    public static async Task RunAsync(MainWindow window, MainWindowViewModel shell, string output, Func<Task> settle)
    {
        string key = "creation-review.xml";
        var rules = await Task.Run(() =>
        {
            using var template = new CharacterSettings();
            return new CharacterSettings(template, false, key) { BuildMethod = CharacterBuildMethod.Karma, BuildKarma = 25, KarmaCarryover = 7, NuyenCarryover = 5000 };
        });
        rules.BooksWritable.Add("SR5");
        SettingsManager.LoadedCharacterSettingsAsModifiable[key] = rules;
        var character = new Character { SettingsKey = key, StartingNuyen = 6000 };
        await Task.Run(async () =>
        {
            var selector = new MetatypeSelector(character);
            selector.OnHeritageOrTalentChanged();
            selector.Metatype = selector.GetMetatypes().First(x => selector.GetDetails(x.Value.ToString()).Name == "Human").Value.ToString();
            selector.OnMetatypeChanged();
            if (!await selector.ApplyAsync()) throw new InvalidOperationException("Metatype selection failed.");
        });
        character.FileName = Path.Combine(output, "Created Runner.chum5");
        character.Alias = "Creation test";
        await CharacterRegistry.OpenCharacters.AddAsync(character);
        await shell.OpenCharacterAsync(character);
        var runner = (CharacterViewModel)shell.CurrentPage;
        await settle();
        Save(window, output, "14a-creation-budget");
        await runner.ValidateCreationCommand.ExecuteAsync(null);
        Console.WriteLine("creation validation: " + runner.CreationErrors);
        runner.ShowSectionCommand.Execute(CharacterSection.Skills);
        await settle();
        Save(window, output, "14b-creation-skills");

        var street = new Chummer.Backend.Equipment.Lifestyle(character);
        await Task.Run(async () => await street.CreateAsync(character.LoadData("lifestyles.xml").SelectSingleNode("/chummer/lifestyles/lifestyle[name = 'Street']")));
        var dialog = new StartingNuyenWindow(new[] { street });
        dialog.Show(); await settle();
        Save(dialog, output, "14c-starting-cash"); dialog.Close();
        await street.DisposeAsync();

        await runner.RunRuleAsync(t => CustomImprovementRules.SaveAsync(character, new CustomImprovementInput
        { DefinitionId = "specificattribute", Name = "GM blessing", Group = "House rules", Selection = "BOD", Value = 2 }, token: t));
        await settle();
        runner.ShowSectionCommand.Execute(CharacterSection.Improvements); await settle();
        Save(window, output, "15a-custom-improvements");
        var improvement = character.Improvements.First(x => x.Custom);
        var editor = new CustomImprovementWindow(character, CustomImprovementRules.Definitions(character), improvement);
        editor.Show(); await settle(); Save(editor, output, "15b-custom-improvement-editor"); editor.Close();
        await runner.CustomImprovements.First().ToggleCommand.ExecuteAsync(null); await settle();
        Save(window, output, "15c-custom-improvement-disabled");
        await runner.CustomImprovements.First().ToggleCommand.ExecuteAsync(null); await settle();

        var vehicle = new Chummer.Backend.Equipment.Vehicle(character) { Name = "Scratch drone", Body = 4, DeviceRating = "3" };
        character.Vehicles.Add(vehicle);
        await runner.RunRuleAsync(_ => Task.FromResult(EquipmentConditionRules.SetDamage(vehicle, true, 3)));
        await settle();
        runner.ShowSectionCommand.Execute(CharacterSection.Inventory); await settle();
        runner.SelectedInventoryItem = runner.InventoryGroups.SelectMany(x => x.Items).First(x => x.Name == "Scratch drone");
        await settle(); Save(window, output, "16a-vehicle-damage");
        await runner.EquipmentMatrixBoxes[1].ToggleCommand.ExecuteAsync(null); await settle();
        Save(window, output, "16b-vehicle-matrix-damage");

        using (var mage = new Character { SettingsKey = key, MAGEnabled = true })
        {
            var traditions = new TraditionWindow(mage, TraditionRules.Options(mage));
            traditions.Show(); await settle(); Save(traditions, output, "17-traditions"); traditions.Close();
        }
        var index = new MasterIndexWindow(rules);
        index.Show(); await index.LoadAsync(); index.Search("Ares Predator V"); await settle();
        Save(index, output, "18-master-index"); index.Close();
        var export = new CharacterExportWindow(character);
        export.Show(); await export.GenerateAsync(); await settle();
        if (string.IsNullOrEmpty(export.Output)) throw new InvalidOperationException("Character export was empty.");
        Save(export, output, "19-character-export"); export.Close();

        const string moduleKey = "life-module-review.xml";
        var moduleRules = new CharacterSettings(rules, false, moduleKey)
        { BuildMethod = CharacterBuildMethod.LifeModule, BuildKarma = 750 };
        moduleRules.BooksWritable.Add("RF");
        moduleRules.OnPropertyChanged(nameof(CharacterSettings.Books));
        SettingsManager.LoadedCharacterSettingsAsModifiable[moduleKey] = moduleRules;
        using (var moduleCharacter = new Character { SettingsKey = moduleKey, Alias = "Life Module test" })
        {
            var modules = new LifeModuleWindow(moduleCharacter);
            modules.Show(); await modules.RefreshAsync(); await settle();
            Save(modules, output, "20a-life-module-picker"); modules.Close();
            await Task.Run(async () =>
            {
                if (await LifeModuleRules.AddAsync(moduleCharacter, "5cf12de1-b41b-4a0f-9a2a-03bf08faa3d0") != RuleResult.Changed)
                    throw new InvalidOperationException("Life Module creation failed.");
            });
            await shell.OpenCharacterAsync(moduleCharacter);
            ((CharacterViewModel)shell.CurrentPage).ShowSectionCommand.Execute(CharacterSection.Qualities);
            await settle(); Save(window, output, "20b-life-module-grants");
            await shell.OpenCharacterAsync(character);
        }

        bool oldBackup = GlobalSettings.CreateBackupOnCareer;
        try
        {
            GlobalSettings.CreateBackupOnCareer = false;
            await runner.RunRuleAsync(t => CreationRules.FinishCareerAsync(character, (_, _) => Task.FromResult<decimal?>(120), t));
            await settle(); await settle();
            if (!character.Created || character.Karma != 7 || character.Nuyen != 5120) throw new InvalidOperationException("Incorrect career conversion.");
            await runner.SaveCommand.ExecuteAsync(null);
            if (runner.IsDirty) throw new InvalidOperationException("Converted runner did not save.");
            runner.ShowSectionCommand.Execute(CharacterSection.Ledger);
            await settle();
            Save(window, output, "14d-career-ledger");
            Console.WriteLine("career conversion: karma=" + character.Karma + " nuyen=" + character.Nuyen + " expenses=" + character.ExpenseEntries.Count);
        }
        finally { GlobalSettings.CreateBackupOnCareer = oldBackup; }
    }

    private static void Save(Window window, string directory, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.CaptureRenderedFrame()?.Save(Path.Combine(directory, name + ".png"));
        Console.WriteLine("saved " + name);
    }
}
