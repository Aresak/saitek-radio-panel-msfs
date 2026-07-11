using System.Reflection;
using System.Runtime.InteropServices;
using Serilog;
using Serilog.Events;

namespace CustomRadioPanel.App.Services;

/// <summary>
/// Sets up rolling file logging so users can attach useful logs to GitHub issues.
/// Logs go to %AppData%\CustomRadioPanel\logs\. Our own code logs at Debug; framework noise is
/// kept to Warning. Also captures otherwise-unhandled exceptions.
/// </summary>
public static class AppLogging
{
    public static string LogsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CustomRadioPanel", "logs");

    public static void Initialize()
    {
        Directory.CreateDirectory(LogsDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("CustomRadioPanel", LogEventLevel.Debug)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(LogsDirectory, "radiopanel-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10_000_000,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
            Log.CloseAndFlush();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
        Log.Information("==================== CustomRadioPanel starting ====================");
        Log.Information("Version {Version} | {OS} | .NET {Framework}",
            version, RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription);
    }
}
