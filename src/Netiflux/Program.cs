using System.Reflection;
using Netiflux.Core;
using Netiflux.Core.Configuration;
using Netiflux.Core.State;
using Netiflux.Services;
using Netiflux.Theming;
using Netiflux.Ui;
using Terminal.Gui.App;

namespace Netiflux;

internal static class Program
{
    /// <summary>
    /// The version stamped at build time. Release builds set this from the git tag, so
    /// what a user reports here maps exactly to a published release.
    /// </summary>
    private static string CurrentVersion
    {
        get
        {
            var informational = System.Reflection.Assembly
                .GetEntryAssembly()?
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (string.IsNullOrWhiteSpace(informational))
            {
                return "unknown";
            }

            // Strip the "+<commit>" suffix the SDK appends by default.
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }
    }

    private static int Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        if (args.Contains("--version") || args.Contains("-v"))
        {
            Console.WriteLine($"netiflux {CurrentVersion}");
            return 0;
        }

        NetifluxConfig config;
        try
        {
            config = ConfigStore.Load();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (args.Contains("--list-themes"))
        {
            ThemeCatalog.Initialize(config.Theme);
            foreach (var name in ThemeCatalog.GetThemeNames())
            {
                Console.WriteLine(name == ThemeCatalog.CurrentTheme ? $"* {name}" : $"  {name}");
            }

            return 0;
        }

        if (args.Contains("--where"))
        {
            Console.WriteLine($"config:  {ConfigStore.ConfigFilePath}");
            Console.WriteLine($"themes:  {ThemeCatalog.UserThemesPath}");
            Console.WriteLine($"saved:   {SavedEntryStore.DefaultPath}");
            return 0;
        }

        var credentials = ResolveCredentials(config);
        if (credentials is null)
        {
            return 1;
        }

        var (baseUri, token) = credentials.Value;

        if (args.Contains("--check"))
        {
            return RunConnectionCheck(baseUri, token);
        }

        var warnings = ThemeCatalog.Initialize(config.Theme);

        using var client = new MinifluxClient(baseUri, token);
        var savedStore = SavedEntryStore.Load();
        var session = new ReaderSession(client, config, savedStore);

        var app = Application.Create();
        app.Init();

        try
        {
            using var shell = new AppShell(session);
            shell.AttachTo(app);
            shell.QuitRequested += (_, _) => app.RequestStop(shell);

            foreach (var warning in warnings)
            {
                shell.Status.Show(warning, ToastKind.Bad);
            }

            // Kick the first load off once the main loop is running, so the window is
            // already painted while the network call is in flight.
            app.Invoke(() => _ = shell.InitialLoadAsync());

            app.Run(shell);
            shell.CancelPendingWork();
        }
        finally
        {
            savedStore.Flush();
            (app as IDisposable)?.Dispose();
        }

        return 0;
    }

    /// <summary>
    /// Verifies the server is reachable and the token works, without starting the UI.
    /// Useful when first setting the app up, or when something has stopped working and
    /// you want to know whether the problem is here or on the server.
    /// </summary>
    private static int RunConnectionCheck(Uri baseUri, string token)
    {
        using var client = new MinifluxClient(baseUri, token);

        try
        {
            Console.WriteLine($"Server:  {baseUri}");

            var user = client.GetMeAsync().GetAwaiter().GetResult();
            Console.WriteLine($"User:    {user.Username} (id {user.Id})");

            var categories = client.GetCategoriesAsync().GetAwaiter().GetResult();
            var unread = categories.Sum(c => c.TotalUnread);
            Console.WriteLine($"Feeds:   {categories.Sum(c => c.FeedCount)} in {categories.Count} categories");
            Console.WriteLine($"Unread:  {unread}");

            Console.WriteLine();
            Console.WriteLine("Connection OK.");
            Console.WriteLine();
            Console.WriteLine("Note: whether \"save to third-party service\" works depends on an integration");
            Console.WriteLine("being enabled in Miniflux under Settings -> Integrations. That cannot be probed");
            Console.WriteLine("without actually saving an entry, so press 's' on an article to confirm it.");
            return 0;
        }
        catch (MinifluxException ex)
        {
            Console.Error.WriteLine($"Failed: {ex.UserMessage}");
            return 1;
        }
    }

    /// <summary>
    /// Resolves the server URL and token, explaining precisely what is missing rather
    /// than dropping the user into an empty UI that silently fails to load.
    /// </summary>
    private static (Uri BaseUri, string Token)? ResolveCredentials(NetifluxConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ServerUrl))
        {
            Console.Error.WriteLine("No Miniflux server configured.");
            Console.Error.WriteLine();
            PrintSetupHelp();
            return null;
        }

        if (!Uri.TryCreate(config.ServerUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            Console.Error.WriteLine($"\"{config.ServerUrl}\" is not a valid http(s) URL.");
            return null;
        }

        string? token;
        try
        {
            token = ConfigStore.ResolveTokenAsync(config).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return null;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("No API token available.");
            Console.Error.WriteLine();
            PrintSetupHelp();
            return null;
        }

        return (baseUri, token);
    }

    private static void PrintSetupHelp()
    {
        Console.Error.WriteLine($"Create {ConfigStore.ConfigFilePath} with:");
        Console.Error.WriteLine();
        Console.Error.WriteLine("""
            {
              "server_url": "https://reader.example.com",
              "api_token_command": "op read op://Private/miniflux/token"
            }
            """);
        Console.Error.WriteLine();
        Console.Error.WriteLine("Use \"api_token\" instead of \"api_token_command\" to store the token directly,");
        Console.Error.WriteLine("or set NETIFLUX_URL and NETIFLUX_TOKEN in the environment.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Create a token in Miniflux under Settings -> API Keys.");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            netiflux — a terminal client for Miniflux

            Usage: netiflux [options]

              --check           Verify the server URL and API token, then exit
              --list-themes     List available themes and exit
              --where           Print config and state file locations
              -v, --version     Print the version and exit
              -h, --help        Show this help

            Configuration is read from the config file, then overridden by
            NETIFLUX_URL / NETIFLUX_TOKEN / NETIFLUX_THEME in the environment.
            """);
    }
}
