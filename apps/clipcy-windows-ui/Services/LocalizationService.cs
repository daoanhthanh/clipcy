using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Clipcy.Services;

public sealed record LanguageOption(string Code, string NativeName);

public static class LocalizationService
{
    private static readonly object Sync = new();
    private static readonly IReadOnlyList<LanguageOption> Languages = new ReadOnlyCollection<LanguageOption>(
        new[]
        {
            new LanguageOption("en", "English"),
            new LanguageOption("zh-CN", "简体中文"),
            new LanguageOption("ja", "日本語"),
            new LanguageOption("ru", "Русский"),
            new LanguageOption("ko", "한국어"),
            new LanguageOption("vi", "Tiếng Việt"),
        }
    );
    private static readonly string SystemLanguage = MatchSystemLanguage(CultureInfo.CurrentUICulture);
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs = new(StringComparer.Ordinal);
    private static IReadOnlyDictionary<string, string> _catalog = new Dictionary<string, string>();
    private static string _languageCode = "en";
    private static bool _initialized;

    public static event EventHandler? LanguageChanged;

    public static IReadOnlyList<LanguageOption> SupportedLanguages => Languages;
    public static string SystemDefaultLanguageCode => SystemLanguage;
    public static string LanguageCode
    {
        get
        {
            Initialize();
            lock (Sync)
                return _languageCode;
        }
    }

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized)
                return;

            var language = ReadPreference() ?? SystemLanguage;
            var catalog = LoadCatalog(language);
            ApplyLanguage(language, catalog);
            _initialized = true;
        }
    }

    public static void SetLanguage(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var canonicalCode = FindSupportedCode(code)
            ?? throw new ArgumentException("Unsupported UI language.", nameof(code));
        Initialize();

        bool changed;
        lock (Sync)
        {
            // Load first, then atomically persist. A failed write must leave the open UI unchanged.
            var catalog = LoadCatalog(canonicalCode);
            SavePreference(canonicalCode);
            changed = !string.Equals(_languageCode, canonicalCode, StringComparison.Ordinal);
            ApplyLanguage(canonicalCode, catalog);
        }

        if (changed)
            LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Initialize();
        lock (Sync)
            return _catalog.TryGetValue(key, out var translated) ? translated : key;
    }

    public static string Format(string formatKey, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var translated = Get(formatKey);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, translated, args);
        }
        catch (FormatException) when (!string.Equals(translated, formatKey, StringComparison.Ordinal))
        {
            // A damaged translation must not break an otherwise valid English format string.
            return string.Format(CultureInfo.CurrentCulture, formatKey, args);
        }
    }

    private static string MatchSystemLanguage(CultureInfo culture)
    {
        var neutral = culture.TwoLetterISOLanguageName;
        return neutral == "zh" ? "zh-CN" : FindSupportedCode(neutral) ?? "en";
    }

    private static string? FindSupportedCode(string code)
    {
        foreach (var option in Languages)
        {
            if (string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase))
                return option.Code;
        }
        return null;
    }

    private static string PreferencePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "clipcy", "ui-settings.json"
    );

    private static string? ReadPreference()
    {
        try
        {
            using var stream = File.OpenRead(PreferencePath);
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("language", out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return FindSupportedCode(value.GetString()!);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"Unable to read UI language preference; using system language: {ex.Message}");
        }
        return null;
    }

    private static void SavePreference(string code)
    {
        var path = PreferencePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteString("language", code);
                    writer.WriteEndObject();
                    writer.Flush();
                }
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Unable to remove temporary UI settings file: {ex.Message}");
            }
            throw;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadCatalog(string code)
    {
        if (Catalogs.TryGetValue(code, out var existing))
            return existing;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var stream = typeof(LocalizationService).Assembly.GetManifestResourceStream($"Clipcy.Localization.{code}.json")
            ?? throw new InvalidOperationException($"Embedded UI language catalog is missing: {code}");
        using var json = JsonDocument.Parse(stream);
        foreach (var property in json.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"Invalid translation for '{property.Name}' in {code}.");
            result.Add(property.Name, property.Value.GetString()!);
        }
        Catalogs.Add(code, result);
        return result;
    }

    private static void ApplyLanguage(string code, IReadOnlyDictionary<string, string> catalog)
    {
        var culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        _catalog = catalog;
        _languageCode = code;
    }
}
