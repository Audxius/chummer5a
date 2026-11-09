using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(HeadlessAppBuilder))]

internal static class HeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Chummer.Desktop.App>()
                                                             .UseSkia()
                                                             .WithInterFont()
                                                             .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
