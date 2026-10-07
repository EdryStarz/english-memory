using Microsoft.UI.Xaml;
namespace EnglishMemory.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private MainWindow? window;
    public App() { if (!Infrastructure.SingleInstance.Own()) Environment.Exit(0); EnglishMemory.Core.DataPaths.Ensure(); File.AppendAllText(Path.Combine(EnglishMemory.Core.DataPaths.Root, "startup.log"), "App initializing\n"); InitializeComponent(); DebugSettings.IsXamlResourceReferenceTracingEnabled = true; DebugSettings.XamlResourceReferenceFailed += (_, e) => File.AppendAllText(Path.Combine(EnglishMemory.Core.DataPaths.Root, "resources.log"), e.Message + Environment.NewLine); File.AppendAllText(Path.Combine(EnglishMemory.Core.DataPaths.Root, "startup.log"), "App resources ready\n"); UnhandledException += (_, e) => { File.AppendAllText(Path.Combine(EnglishMemory.Core.DataPaths.Root, "errors.log"), DateTimeOffset.Now + " " + e.Exception + Environment.NewLine); }; }
    protected override void OnLaunched(LaunchActivatedEventArgs args) { window = new MainWindow(); window.Activate(); }
}
