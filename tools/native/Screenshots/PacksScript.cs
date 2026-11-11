using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Chummer;
using Chummer.Backend.Enums;
using Chummer.Backend.Equipment;
using Chummer.Desktop.ViewModels;
using Chummer.Desktop.Views;
using Chummer.Desktop.Views.Dialogs;
using Chummer.Rules;
using Chummer.Selection;

internal static class PacksScript
{
    private sealed class ConsoleProgress : IProgress<string> { public void Report(string value) => Console.WriteLine(value); }
    public static async Task RunAsync(MainWindow window, MainWindowViewModel shell, string output, Func<Task> settle)
    {
        const string key = "packs-review.xml";
        using var template = new CharacterSettings();
        var settings = new CharacterSettings(template, false, key) { BuildMethod = CharacterBuildMethod.Karma, BuildKarma = 800 };
        settings.BooksWritable.Add("SR5"); settings.OnPropertyChanged(nameof(CharacterSettings.Books));
        SettingsManager.LoadedCharacterSettingsAsModifiable[key] = settings;
        var character = new Character { SettingsKey = key, Alias = "PACKS test" };
        await Task.Run(async () =>
        {
            var selector = new MetatypeSelector(character); selector.OnHeritageOrTalentChanged();
            selector.Metatype = selector.GetMetatypes().First(x => selector.GetDetails(x.Value.ToString()).Name == "Human").Value.ToString();
            selector.OnMetatypeChanged();
            if (!await selector.ApplyAsync()) throw new InvalidOperationException("Metatype selection failed.");
        });
        await CharacterRegistry.OpenCharacters.AddAsync(character); await shell.OpenCharacterAsync(character);
        var runner = (CharacterViewModel)shell.CurrentPage;
        var spellDesign = new CustomSpellDesign { Name = "Prismatic burst", Range = "LOS", Area = true, NumberOfEffects = 2 };
        spellDesign.SetModifier(3, true); spellDesign.SetModifier(4, true);
        var spellPicker = new CustomSpellWindow(spellDesign); spellPicker.Show(); await settle();
        Save(spellPicker, output, "22a-custom-spell-designer"); spellPicker.Close();
        character.IgnoreRules = true;
        await runner.RunRuleAsync(t => CustomSpellRules.AddAsync(character, spellDesign, t));
        character.IgnoreRules = false;
        runner.ShowSectionCommand.Execute(CharacterSection.Abilities); await settle();
        Save(window, output, "22b-custom-spell");
        var kits = await Task.Run(() => PacksCatalog.LoadAsync(character));
        var picker = new PacksWindow(kits); picker.Search("Intro Runner"); picker.Show(); await settle();
        Save(picker, output, "21a-packs-picker"); picker.Close();
        await runner.RunRuleAsync(t => PacksRules.AddAsync(character, "Intro Runner Pack", "Core Packs", t, new ConsoleProgress()));
        runner.ShowSectionCommand.Execute(CharacterSection.Inventory); await settle();
        runner.SelectedInventoryItem = runner.InventoryGroups.SelectMany(x => x.Items).First(x => x.Name == "Glasses");
        var glasses = character.Gear.Single(x => x.Name == "Glasses");
        await runner.RunRuleAsync(async t =>
        {
            var location = await EquipmentLocationRules.CreateAsync(character, glasses, "Backpack", t);
            return await EquipmentLocationRules.AssignAsync(character, glasses, location, t);
        });
        await settle(); Save(window, output, "21b-packs-inventory");
        await runner.RunRuleAsync(t => PacksRules.AddAsync(character, "Basic Combat Eyes Pack", "Cyber Packs", t));
        await settle(); Save(window, output, "21c-packs-implants");
        await runner.RunRuleAsync(t => PacksRules.AddAsync(character, "Racing Bike Pack", "Vehicle Packs", t));
        await settle(); Save(window, output, "21d-packs-vehicle");
        var vehicle = character.Vehicles.Single();
        var mountPicker = new WeaponMountWindow(character, vehicle, _ => Task.FromResult(new PurchaseResult(RuleResult.Cancelled)));
        mountPicker.Show(); await mountPicker.LoadAsync(); await settle();
        if (mountPicker.Quote == null) throw new InvalidOperationException("Weapon mount preview was empty.");
        var mountConfiguration = mountPicker.Configuration;
        Save(mountPicker, output, "24a-weapon-mount-builder"); mountPicker.Close();
        WeaponMount mount = null;
        await runner.RunRuleAsync(async t =>
        {
            var purchase = await WeaponMountRules.PurchaseAsync(character, vehicle, mountConfiguration, t);
            mount = purchase.Item as WeaponMount; return purchase.Result;
        });
        if (mount == null) throw new InvalidOperationException("Weapon mount purchase failed.");
        await settle();
        runner.SelectedInventoryItem = runner.InventoryGroups.SelectMany(x => x.Items).SelectMany(x => x.Children).First(x => x.IconKey == "Icon.Item.WeaponMount");
        await settle(); Save(window, output, "24b-weapon-mount-inventory");
        string pistol = character.LoadData("weapons.xml").SelectSingleNode("/chummer/weapons/weapon[name='Ares Predator V']/id").InnerText;
        await runner.RunRuleAsync(async t => (await PurchaseRules.PurchaseAsync(character,
            new PurchaseRequest { Kind = EquipmentKind.Weapon, ItemId = pistol, Parent = mount }, t)).Result);
        await settle(); Save(window, output, "24c-mounted-weapon");
        if (mount.Weapons.Count != 1) throw new InvalidOperationException("Mounted weapon was not attached.");
        settings.BooksWritable.Add("R5"); settings.OnPropertyChanged(nameof(CharacterSettings.Books));
        var modStore = StoreWindow.Create(character, new[] { new StoreSection(EquipmentKind.WeaponMountMod, "Mount modifications", mount) }, null);
        modStore.Show(); await settle(); Save(modStore, output, "24d-mount-mod-store"); modStore.Close();
        await runner.RunRuleAsync(async t => (await PurchaseRules.PurchaseAsync(character,
            new PurchaseRequest { Kind = EquipmentKind.WeaponMountMod, ItemId = "6741d080-0e40-4941-a7b5-85ab3e1a9cb2", Parent = mount }, t)).Result);
        await settle(); Save(window, output, "24e-mount-mod-inventory");
        if (mount.Mods.Count != 1) throw new InvalidOperationException("Mount modification was not attached.");
        var retrofit = new WeaponMountWindow(character, vehicle, async configuration =>
        {
            PurchaseResult result = null;
            await runner.RunRuleAsync(async t => { result = await WeaponMountRules.EditAsync(character, mount, configuration, t); return result.Result; });
            return result;
        }, mount);
        retrofit.Show(); await retrofit.LoadAsync();
        var retrofitInput = retrofit.Configuration; retrofitInput.ControlId = "70f99fae-470f-442c-a607-ef3465e27f37";
        await retrofit.ConfigureAsync(retrofitInput); await settle();
        if (retrofit.Quote?.Cost != -500m) throw new InvalidOperationException("Mount retrofit did not quote the expected downgrade difference.");
        Save(retrofit, output, "30a-mount-retrofit"); await retrofit.SaveAsync(); await settle(); Save(window, output, "30b-retrofitted-mount");
        var exporter = new PacksExportWindow(character); exporter.Show(); await exporter.GenerateAsync("Street starter kit"); await settle();
        Save(exporter, output, "25-packs-export");
        if (!exporter.Output.Contains("Street starter kit")) throw new InvalidOperationException("PACKS export was empty.");
        exporter.Close();
        var limits = new LimitModifiersWindow(runner); limits.Show(); await limits.ReloadAsync();
        await limits.SaveAsync("Careful aim", "Physical", 2, "While aiming"); await settle();
        Save(limits, output, "26-limit-modifiers"); limits.Close();
        if (!character.LimitModifiers.Any(x => x.Name == "Careful aim")) throw new InvalidOperationException("Limit modifier was not saved.");
        var natural = new NaturalWeaponWindow(runner); natural.Show(); await natural.LoadAsync(); await settle();
        if (natural.SelectedSkill != "Unarmed Combat") throw new InvalidOperationException("Default natural-weapon skill was not selected.");
        Save(natural, output, "27a-natural-weapon-builder");
        await natural.SaveAsync(new NaturalWeaponDesign { Name = "Talons", DamageBase = "({STR})", DamageModifier = 2, ArmorPenetration = -3, Reach = 1 });
        await settle(); Save(window, output, "27b-natural-weapon-inventory");
        if (!character.Weapons.Any(x => x.Name == "Talons")) throw new InvalidOperationException("Natural weapon was not saved.");
        Console.WriteLine("loading suite dialog");
        var suites = new CyberwareSuiteWindow(runner); suites.Show(); await suites.LoadAsync();
        Console.WriteLine("authoring suite"); await suites.CreateAsync("Combat eyes bundle"); await settle();
        if (!suites.PreviewText.Contains("Combat eyes bundle")) throw new InvalidOperationException("Authored suite preview was empty.");
        Save(suites, output, "28-cyberware-suite"); suites.Close();
        var recipe = new CustomDrugDesign { Name = "Clear thinker" };
        recipe.Ingredients.Add(new CustomDrugIngredient { ComponentId = "Genius" });
        recipe.Ingredients.Add(new CustomDrugIngredient { ComponentId = "Smoothtalk", Level = 1 });
        var drugBuilder = new CustomDrugWindow(runner); drugBuilder.Show(); await drugBuilder.ConfigureAsync(recipe); await settle();
        if (drugBuilder.Quote == null) throw new InvalidOperationException("Custom drug preview failed.");
        Save(drugBuilder, output, "29a-custom-drug-builder"); await drugBuilder.PurchaseAsync(); await settle();
        if (!character.Drugs.Any(x => x.Name == "Clear thinker")) throw new InvalidOperationException("Custom drug purchase failed.");
        runner.SelectedInventoryItem = runner.InventoryGroups.SelectMany(x => x.Items).First(x => x.Name == "Clear thinker");
        await settle(); Save(window, output, "29b-custom-drug-inventory");
        var sustained = new SustainedEffectsWindow(runner); sustained.Show(); await sustained.LoadAsync();
        await sustained.SaveAsync(character.Spells.Single(x => x.Name == "Prismatic burst"), 4, 3, true); await settle();
        Save(sustained, output, "31-sustained-effects"); sustained.Close();
        if (character.SustainingPenalty >= 0 || character.SustainedCollection.Count != 1) throw new InvalidOperationException("Sustained effect did not apply a dice-pool penalty.");
        var eyes = character.Cyberware.First(x => x.Name.StartsWith("Cybereyes", StringComparison.Ordinal));
        string eyesId = eyes.InternalId;
        var eyeChildren = eyes.Children.Select(x => x.InternalId).ToArray();
        var upgrade = new ImplantUpgradeWindow(runner, eyes); upgrade.Show();
        await upgrade.ConfigureAsync("Alphaware", 2); await settle();
        Save(upgrade, output, "32a-implant-upgrade");
        await upgrade.ApplyAsync(); await settle();
        if (eyes.InternalId != eyesId || eyes.Grade.Name != "Alphaware" || eyes.Rating != 2
            || !eyeChildren.SequenceEqual(eyes.Children.Select(x => x.InternalId)))
            throw new InvalidOperationException("Implant edit did not preserve the implant and its attachments.");
        Save(window, output, "32b-upgraded-implant");
        runner.SelectedInventoryItem = runner.InventoryGroups.SelectMany(x => x.Items).First(x => x.Name == "Glasses");
        string movedId = runner.SelectedInventoryItem.Id;
        var expectedOrder = runner.InventoryGroups.Single(x => x.Items.Any(i => i.Id == movedId)).Items.Select(x => x.Id).ToList();
        int previousIndex = expectedOrder.IndexOf(movedId);
        expectedOrder.RemoveAt(previousIndex); expectedOrder.Insert(previousIndex - 1, movedId);
        runner.InventoryFilter = "Glasses";
        await runner.MoveInventoryUpCommand.ExecuteAsync(null); await settle();
        if (character.Gear.Single(x => x.InternalId == movedId).SortOrder <= 0
            || runner.SelectedInventoryItem?.Id != movedId
            || !expectedOrder.SequenceEqual(character.Gear.OrderBy(x => x.SortOrder).Select(x => x.InternalId)))
            throw new InvalidOperationException("Inventory ordering did not persist or retain the selection.");
        runner.InventoryFilter = ""; runner.InventoryKind = InventoryKind.Gear; await settle();
        Save(window, output, "33-inventory-order");
        string flareId = character.LoadData("cyberware.xml").SelectSingleNode("/chummer/cyberwares/cyberware[name='Flare Compensation']/id").InnerText;
        await runner.RunRuleAsync(async t => (await PurchaseRules.PurchaseAsync(character, new PurchaseRequest {
            Kind = EquipmentKind.Cyberware, ItemId = flareId, Parent = eyes, GradeId = eyes.Grade.SourceIDString }, t)).Result);
        await settle();
        runner.InventoryKind = InventoryKind.Cyberware;
        var eyeCard = runner.InventoryGroups.SelectMany(x => x.Items).Single(x => x.Id == eyesId);
        var implantChildren = eyeCard.Children.Where(x => eyes.Children.Any(c => c.InternalId == x.Id)).ToList();
        if (implantChildren.Count < 2) throw new InvalidOperationException("Nested ordering fixture needs two implant attachments.");
        string movedChild = implantChildren[0].Id, nextChild = implantChildren[1].Id;
        runner.SelectedInventoryItem = implantChildren[0];
        await runner.MoveInventoryDownCommand.ExecuteAsync(null); await settle();
        if (eyes.Children.Single(x => x.InternalId == movedChild).SortOrder <= eyes.Children.Single(x => x.InternalId == nextChild).SortOrder)
            throw new InvalidOperationException("Nested order was not persisted.");
        var refreshedEyes = runner.InventoryGroups.SelectMany(x => x.Items).Single(x => x.Id == eyesId);
        if (refreshedEyes.Children.First(x => eyes.Children.Any(c => c.InternalId == x.Id)).Id != nextChild || runner.SelectedInventoryItem.Id != movedChild)
            throw new InvalidOperationException("Nested order was not displayed or selection was lost.");
        var inspector = window.GetVisualDescendants().OfType<TabControl>().Single(x => x.Items.OfType<TabItem>().Any(i => (i.Header as TextBlock)?.Text == "Manage"));
        inspector.SelectedIndex = 1; await settle(); Save(window, output, "34-nested-inventory-manage");
        runner.SelectedInventoryItem = refreshedEyes; inspector.SelectedIndex = 0; await settle(); Save(window, output, "35-inventory-details");
        window.Width = 980; window.Height = 720; await settle();
        var compactFilter = window.GetVisualDescendants().OfType<ComboBox>().Single(x => x.Classes.Contains("compactInventoryFilter"));
        if (!compactFilter.IsVisible) throw new InvalidOperationException("Compact category selector was not shown.");
        compactFilter.SelectedItem = runner.InventoryKinds.Single(x => x.Kind == InventoryKind.Gear); await settle();
        if (runner.InventoryKind != InventoryKind.Gear) throw new InvalidOperationException("Compact category selector did not filter inventory.");
        compactFilter.SelectedItem = runner.InventoryKinds.Single(x => x.Kind == InventoryKind.Cyberware); await settle();
        Save(window, output, "36-inventory-compact");
        window.Width = 1440; window.Height = 900; await settle();
        foreach (string label in new[] { "Inventory tools ▾", "Creation tools ▾", "Tools & settings ▾", "Output ▾" })
        {
            var button = window.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == label
                || x.Content is Panel panel && panel.Children.OfType<TextBlock>().Any(t => t.Text == label));
            var menu = button.Flyout as MenuFlyout ?? throw new InvalidOperationException(label + " has no menu.");
            menu.ShowAt(button); await settle();
            if (menu.Items.OfType<MenuItem>().Any(x => x.Command == null)) throw new InvalidOperationException(label + " contains an unbound action.");
            menu.Hide();
        }
        if (character.Gear.Single(x => x.Name == "Ammo: Regular Ammo").Quantity != 100 || character.Vehicles.Count != 1)
            throw new InvalidOperationException("PACKS items were not granted correctly.");
        Console.WriteLine("PACKS grants verified: " + character.Gear.Count + " gear, " + character.Weapons.Count + " weapons, " + character.Cyberware.Count + " implants, " + character.Vehicles.Count + " vehicles.");
    }
    private static void Save(Window window, string directory, string name)
    {
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.CaptureRenderedFrame()?.Save(Path.Combine(directory, name + ".png"));
        Console.WriteLine("saved " + name);
    }
}
