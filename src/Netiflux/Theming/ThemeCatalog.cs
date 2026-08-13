using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Netiflux.Core.Configuration;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace Netiflux.Theming;

/// <summary>
/// Owns Netiflux's theme story: merges the bundled palettes with any the user wrote,
/// hands them to Terminal.Gui's <see cref="ConfigurationManager"/>, and resolves the
/// app-specific scheme names the UI draws with.
/// </summary>
/// <remarks>
/// This uses the <c>ConfigurationManager</c> / <c>ThemeManager</c> statics, which
/// Terminal.Gui 2.4.17 marks obsolete in favour of <c>TuiConfigurationBuilder</c>. The
/// replacement is not working yet in that release: its <c>ThemeManager.ThemeNames</c>
/// returns only "Default" and <c>SwitchTheme</c> fails, even for the library's own
/// built-in themes, while the legacy path loads and switches everything correctly.
/// Revisit when the new API becomes functional.
/// </remarks>
#pragma warning disable CS0618 // Obsolete configuration API — see the remarks above.
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
            ConfigurationManager.RuntimeConfig = BuildMergedConfig(warnings);
            ConfigurationManager.Enable(ConfigLocations.All);
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
        ConfigurationManager.Apply();
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

        var array = new JsonArray();
        foreach (var name in order)
        {
            array.Add(new JsonObject { [name] = themesByName[name]?.DeepClone() });
        }

        return new JsonObject { ["Themes"] = array }.ToJsonString();
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

        if (root?["Themes"] is not JsonArray themes)
        {
            yield break;
        }

        foreach (var element in themes)
        {
            if (element is not JsonObject wrapper)
            {
                continue;
            }

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
#pragma warning restore CS0618
