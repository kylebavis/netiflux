using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Netiflux.Core.Configuration;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace Netiflux.Theming;

/// <summary>
/// Owns Netiflux's theme story: merges the bundled palettes with any the user wrote,
/// hands them to Terminal.Gui's <see cref="TuiConfigurationBuilder"/>, and resolves the
/// app-specific scheme names the UI draws with.
/// </summary>
/// <remarks>
/// Terminal.Gui 2.5.0 expects <c>Themes</c> and <c>Schemes</c> as objects keyed by name
/// and settings nested rather than dotted. <c>themes.json</c> is written in the older
/// array / dotted form, so <see cref="BuildMergedConfig"/> translates it (and passes the
/// new form through unchanged).
/// </remarks>
public static class ThemeCatalog
{
    private const string BundledResourceName = "Netiflux.Resources.themes.json";

    /// <summary>Theme applied when the configured one is missing or unparseable.</summary>
    public const string FallbackTheme = "Netiflux Dark";

    private static readonly Dictionary<string, Scheme> SchemeCache = new(StringComparer.Ordinal);
    private static bool _enabled;

    /// <summary>File the user can create to add or override themes.</summary>
    public static string UserThemesPath => Path.Combine(ConfigStore.ConfigDirectory, "themes.json");

    /// <summary>
    /// Loads themes and activates <paramref name="preferredTheme"/>. Must run before the
    /// first view is constructed so schemes resolve correctly on the initial draw.
    /// </summary>
    /// <returns>Warnings worth surfacing; empty when everything loaded cleanly.</returns>
    public static IReadOnlyList<string> Initialize(string? preferredTheme)
    {
        var warnings = new List<string>();

        try
        {
            TuiConfigurationBuilder.Shared.RuntimeConfig = BuildMergedConfig(warnings);
            TuiConfigurationBuilder.Shared.ApplyToStaticFacades();
            _enabled = true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            warnings.Add($"Theme definitions could not be loaded ({ex.Message}); using built-in defaults.");
        }

        Apply(preferredTheme, warnings);
        return warnings;
    }

    /// <summary>Switches the active theme and clears cached scheme lookups.</summary>
    public static void Apply(string? themeName, List<string>? warnings = null)
    {
        if (!_enabled)
        {
            return;
        }

        var available = GetThemeNames();
        var target = themeName;

        if (string.IsNullOrWhiteSpace(target) || !available.Contains(target))
        {
            if (!string.IsNullOrWhiteSpace(target))
            {
                warnings?.Add($"Theme \"{target}\" was not found; falling back to \"{FallbackTheme}\".");
            }

            target = available.Contains(FallbackTheme) ? FallbackTheme : available.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        ThemeManager.Theme = target;
        SchemeCache.Clear();
    }

    public static IReadOnlyList<string> GetThemeNames() =>
        _enabled ? ThemeManager.GetThemeNames() : [];

    public static string CurrentTheme =>
        _enabled ? ThemeManager.GetCurrentThemeName() : FallbackTheme;

    /// <summary>
    /// Resolves one of Netiflux's own scheme names (EntryUnread, Sidebar, …). Themes that
    /// do not define it — including Terminal.Gui's stock themes — fall back to Base, so a
    /// user can pick any installed theme and still get a usable, if plainer, UI.
    /// </summary>
    public static Scheme Resolve(string schemeName)
    {
        if (SchemeCache.TryGetValue(schemeName, out var cached))
        {
            return cached;
        }

        var resolved = TryGetScheme(schemeName)
                       ?? TryGetScheme(nameof(Schemes.Base))
                       ?? new Scheme();

        SchemeCache[schemeName] = resolved;
        return resolved;
    }

    private static Scheme? TryGetScheme(string name)
    {
        if (!_enabled)
        {
            return null;
        }

        try
        {
            return SchemeManager.TryGetScheme(name, out var scheme) ? scheme : null;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Clears memoised schemes. Call after any runtime theme change.</summary>
    public static void InvalidateCache() => SchemeCache.Clear();

    /// <summary>
    /// Combines bundled themes with the user's <c>themes.json</c>. A user theme sharing a
    /// bundled name replaces it outright, which is the least surprising rule: you edit a
    /// copy of a theme and it takes effect.
    /// </summary>
    private static string BuildMergedConfig(List<string> warnings)
    {
        var themesByName = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var (name, node) in ReadThemes(LoadBundledJson(), "bundled themes", warnings))
        {
            if (!themesByName.ContainsKey(name))
            {
                order.Add(name);
            }

            themesByName[name] = node;
        }

        if (File.Exists(UserThemesPath))
        {
            string userJson;
            try
            {
                userJson = File.ReadAllText(UserThemesPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not read {UserThemesPath}: {ex.Message}");
                userJson = "";
            }

            if (!string.IsNullOrWhiteSpace(userJson))
            {
                foreach (var (name, node) in ReadThemes(userJson, UserThemesPath, warnings))
                {
                    if (!themesByName.ContainsKey(name))
                    {
                        order.Add(name);
                    }

                    themesByName[name] = node;
                }
            }
        }

        var themes = new JsonObject();
        foreach (var name in order)
        {
            if (themesByName[name] is JsonObject body)
            {
                themes[name] = NormalizeTheme(body);
            }
        }

        return new JsonObject { ["Themes"] = themes }.ToJsonString();
    }

    /// <summary>
    /// Rewrites one theme body into the nested form: <c>Schemes</c> as an object rather
    /// than an array of single-key objects, and <c>Window.DefaultBorderStyle</c>-style keys
    /// as <c>Window: { DefaultBorderStyle }</c>.
    /// </summary>
    private static JsonObject NormalizeTheme(JsonObject body)
    {
        var result = new JsonObject();

        foreach (var (key, value) in body)
        {
            if (key == "Schemes" && value is JsonArray schemeList)
            {
                var schemes = new JsonObject();
                foreach (var wrapper in schemeList.OfType<JsonObject>())
                {
                    foreach (var (schemeName, scheme) in wrapper)
                    {
                        schemes[schemeName] = scheme?.DeepClone();
                    }
                }

                result[key] = schemes;
                continue;
            }

            var dot = key.IndexOf('.');
            if (dot <= 0)
            {
                result[key] = value?.DeepClone();
                continue;
            }

            var section = key[..dot];
            if (result[section] is not JsonObject nested)
            {
                nested = new JsonObject();
                result[section] = nested;
            }

            nested[key[(dot + 1)..]] = value?.DeepClone();
        }

        return result;
    }

    private static IEnumerable<(string Name, JsonNode? Body)> ReadThemes(
        string json,
        string source,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            yield break;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (JsonException ex)
        {
            warnings.Add($"Invalid JSON in {source}: {ex.Message}");
            yield break;
        }

        // Accept both the array form ([{ "Name": { … } }]) and the object form.
        var wrappers = root?["Themes"] switch
        {
            JsonArray array => array.OfType<JsonObject>(),
            JsonObject obj => [obj],
            _ => []
        };

        foreach (var wrapper in wrappers)
        {
            foreach (var (name, body) in wrapper)
            {
                yield return (name, body);
            }
        }
    }

    private static string LoadBundledJson()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundledResourceName)
                           ?? throw new InvalidOperationException(
                               $"Embedded resource {BundledResourceName} is missing from the build.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
