using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Threading;
using Chummer.Desktop.ViewModels;
using Chummer.Desktop.Views.Dialogs;

internal static class StoreHarness
{
    public static async Task RunAsync(CharacterViewModel runner, string output, Func<Task> settle)
    {
        foreach (var kind in new[] { InventoryKind.Weapon, InventoryKind.Armor, InventoryKind.Gear, InventoryKind.Cyberware, InventoryKind.Vehicle, InventoryKind.Lifestyle })
        {
            var window = StoreWindow.Create(runner.Character, CharacterViewModel.SectionsFor(kind), _ => Task.CompletedTask);
            window.Show();
            await settle();
            if (kind == InventoryKind.Weapon) await window.SelectAsync(null, "Predator", "Ares Predator V");
            await settle();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.CaptureRenderedFrame()?.Save(Path.Combine(output, "11-store-" + kind + ".png"));
            window.Close();
        }
    }
}
