using Avalonia;
using RagnaModManager.Core.Database;
using RagnaModManager.Core.Platform;

namespace RagnaModManager.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--verify-native-dependencies")
        {
            return VerifyNativeDependencies(args[1]);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static int VerifyNativeDependencies(string reportPath)
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"RagnaModManager-native-check-{Guid.NewGuid():N}");

        try
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            new ManagerDatabase(AppPaths.Create(dataRoot)).Initialize();
            File.WriteAllText(reportPath, "Native dependency smoke check passed.");
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(reportPath, exception.ToString());
            return 1;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
