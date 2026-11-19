using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Hark.Core
{
    public sealed class AppSettings
    {
        public const int DefaultSnoozeMinutes = 5;
        public const string DefaultOpenHotkey = "Ctrl+Alt+H";

        public int SnoozeMinutes { get; set; } = DefaultSnoozeMinutes;
        public string OpenHotkey { get; set; } = DefaultOpenHotkey;
        public bool Paused { get; set; }
    }

    /// <summary>Loads and saves settings.json. A missing or broken file never stops the app.</summary>
    public sealed class SettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep "Ctrl+Shift+O" readable; local file only
        };

        private readonly string _filePath;

        public SettingsStore(string filePath)
        {
            _filePath = filePath;
        }

        public AppSettings Load()
        {
            AppSettings settings = null;
            if (File.Exists(_filePath))
            {
                try
                {
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), JsonOptions);
                }
                catch (JsonException)
                {
                    settings = null;
                }
            }
            settings = settings ?? new AppSettings();
            if (settings.SnoozeMinutes < 1)
            {
                settings.SnoozeMinutes = AppSettings.DefaultSnoozeMinutes;
            }
            if (string.IsNullOrWhiteSpace(settings.OpenHotkey))
            {
                settings.OpenHotkey = AppSettings.DefaultOpenHotkey;
            }
            return settings;
        }

        public void Save(AppSettings settings)
        {
            var folder = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
    }
}
