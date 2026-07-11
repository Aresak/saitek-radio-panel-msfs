using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomRadioPanel.Core.Config;

/// <summary>Loads and saves <see cref="AppConfig"/> as JSON under %AppData%\CustomRadioPanel.</summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<ConfigService> _log;
    private readonly string _path;

    public ConfigService(ILogger<ConfigService>? log = null)
    {
        _log = log ?? NullLogger<ConfigService>.Instance;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CustomRadioPanel");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "appsettings.json");
    }

    public string FilePath => _path;

    public AppConfig Current { get; private set; } = new();

    /// <summary>Reads config from disk (writing defaults on first run). Returns the loaded instance.</summary>
    public AppConfig Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                Current = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            }
            else
            {
                Current = new AppConfig();
                Save(Current);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to read config; using defaults.");
            Current = new AppConfig();
        }

        return Current;
    }

    public void Save(AppConfig config)
    {
        Current = config;
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to save config.");
        }
    }
}
