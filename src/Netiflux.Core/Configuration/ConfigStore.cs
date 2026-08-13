using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Netiflux.Core.Configuration;

/// <summary>
/// Locates, loads and saves <see cref="NetifluxConfig"/>, and resolves the API token from
/// whichever source the user configured.
/// </summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Directory holding config and local state. Honours <c>NETIFLUX_CONFIG_DIR</c>, then
    /// the platform convention: <c>%APPDATA%\netiflux</c> on Windows, <c>$XDG_CONFIG_HOME/netiflux</c>
    /// (or <c>~/.config/netiflux</c>) elsewhere.
    /// </summary>
    public static string ConfigDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("NETIFLUX_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                return overridden;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "netiflux");
            }

            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdg))
            {
                return Path.Combine(xdg, "netiflux");
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "netiflux");
        }
    }

    public static string ConfigFilePath => Path.Combine(ConfigDirectory, "config.json");

    public static bool Exists => File.Exists(ConfigFilePath);

    public static NetifluxConfig Load()
    {
        var config = ReadFile();
        ApplyEnvironmentOverrides(config);
        return config;
    }

    private static NetifluxConfig ReadFile()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return NetifluxConfig.CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<NetifluxConfig>(json, ReadOptions)
                   ?? NetifluxConfig.CreateDefault();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Could not read {ConfigFilePath}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Environment wins over the file so a shell can point the app at a different instance
    /// without editing anything. <c>MINIFLUX_*</c> is accepted too, since those names are
    /// already common in miniflux tooling.
    /// </summary>
    private static void ApplyEnvironmentOverrides(NetifluxConfig config)
    {
        var url = FirstNonEmpty(
            Environment.GetEnvironmentVariable("NETIFLUX_URL"),
            Environment.GetEnvironmentVariable("MINIFLUX_URL"),
            Environment.GetEnvironmentVariable("MINIFLUX_ENDPOINT"));

        if (url is not null)
        {
            config.ServerUrl = url;
        }

        var token = FirstNonEmpty(
            Environment.GetEnvironmentVariable("NETIFLUX_TOKEN"),
            Environment.GetEnvironmentVariable("MINIFLUX_API_KEY"),
            Environment.GetEnvironmentVariable("MINIFLUX_TOKEN"));

        if (token is not null)
        {
            config.ApiToken = token;
        }

        var theme = Environment.GetEnvironmentVariable("NETIFLUX_THEME");
        if (!string.IsNullOrWhiteSpace(theme))
        {
            config.Theme = theme;
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    public static void Save(NetifluxConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Directory.CreateDirectory(ConfigDirectory);

        // Write-then-move so an interrupted save cannot truncate a good config.
        var temp = ConfigFilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, WriteOptions));
        File.Move(temp, ConfigFilePath, overwrite: true);

        RestrictToCurrentUser(ConfigFilePath);
    }

    /// <summary>
    /// Tightens permissions on the config file, which may hold a token. On Unix this is
    /// chmod 600; on Windows the file inherits the user-profile ACL, which is already
    /// user-scoped, so there is nothing extra to do.
    /// </summary>
    private static void RestrictToCurrentUser(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // Best effort — a permissions failure should not stop the app from starting.
        }
    }

    /// <summary>
    /// Resolves the token to use, preferring an already-populated value (config file or
    /// environment) and otherwise running <see cref="NetifluxConfig.ApiTokenCommand"/>.
    /// </summary>
    public static async Task<string?> ResolveTokenAsync(NetifluxConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!string.IsNullOrWhiteSpace(config.ApiToken))
        {
            return config.ApiToken.Trim();
        }

        if (string.IsNullOrWhiteSpace(config.ApiTokenCommand))
        {
            return null;
        }

        return await RunTokenCommandAsync(config.ApiTokenCommand, ct).ConfigureAwait(false);
    }

    private static async Task<string> RunTokenCommandAsync(string command, CancellationToken ct)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var startInfo = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(isWindows ? "/c" : "-c");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start the token command.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var reason = string.IsNullOrWhiteSpace(stderr) ? $"exit code {process.ExitCode}" : stderr.Trim();
            throw new InvalidOperationException($"Token command failed: {reason}");
        }

        var token = stdout.Trim();
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Token command produced no output.");
        }

        return token;
    }
}
