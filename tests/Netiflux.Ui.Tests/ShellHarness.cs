using System.Drawing;
using Netiflux.Core.Configuration;
using Netiflux.Core.Models;
using Netiflux.Core.State;
using Netiflux.Services;
using Netiflux.Theming;
using Netiflux.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Netiflux.Ui.Tests;

/// <summary>
/// Runs a real <see cref="AppShell"/> against a real Terminal.Gui main loop, on the
/// headless driver, and lets a test press keys and assert on what happened.
/// <para>
/// The main loop runs on its own thread. Every key is delivered through the driver's
/// input processor and every assertion reads view state back on that same thread, so
/// tests never touch the UI from outside it.
/// </para>
/// </summary>
public sealed class ShellHarness : IAsyncDisposable
{
    /// <summary>
    /// Terminal.Gui 2.4.17 resolves driver names with an internal switch and never
    /// consults <see cref="Terminal.Gui.Drivers.DriverRegistry"/>, so a custom driver
    /// cannot be selected by name. Until that is fixed, tests run on the stock
    /// cross-platform driver.
    /// </summary>
    private const string DriverName = "dotnet";

    /// <summary>Wide enough that the shell uses its three-pane split layout.</summary>
    public const int ScreenWidth = 160;

    public const int ScreenHeight = 48;

    private static readonly Lock ThemeGate = new();
    private static bool _themesLoaded;

    private readonly IApplication _app;
    private readonly Task _runTask;
    private readonly string _stateDir;

    private ShellHarness(
        IApplication app,
        AppShell shell,
        FakeMinifluxClient client,
        ReaderSession session,
        Task runTask,
        string stateDir)
    {
        _app = app;
        _runTask = runTask;
        _stateDir = stateDir;
        Shell = shell;
        Client = client;
        Session = session;
    }

    public AppShell Shell { get; }

    public FakeMinifluxClient Client { get; }

    public ReaderSession Session { get; }

    /// <summary>True when a modal dialog is on top of the shell. Read from the UI thread.</summary>
    public bool IsModalOpen => !ReferenceEquals(_app.TopRunnable, Shell);

    /// <summary>Starts a shell preloaded with <paramref name="entries"/> and waits for first paint.</summary>
    public static async Task<ShellHarness> StartAsync(
        IEnumerable<Entry>? entries = null,
        NetifluxConfig? config = null)
    {
        EnsureThemesLoaded();

        var stateDir = Path.Combine(Path.GetTempPath(), "netiflux-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stateDir);

        var client = new FakeMinifluxClient(entries?.ToList() ?? DefaultEntries());
        var effectiveConfig = config ?? new NetifluxConfig { PageSize = 50 };
        var store = SavedEntryStore.Load(Path.Combine(stateDir, "saved.json"));
        var session = new ReaderSession(client, effectiveConfig, store);

        var app = Application.Create();

        app.Init(DriverName);

        // Pin the geometry. Otherwise tests inherit whatever console runs them — an
        // 80-column window puts the shell into single-pane mode and changes behaviour.
        app.Driver?.SetScreenSize(ScreenWidth, ScreenHeight);
        app.Screen = new Rectangle(0, 0, ScreenWidth, ScreenHeight);

        var shell = new AppShell(session);
        shell.AttachTo(app);

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var iterations = 0;

        void OnIteration(object? sender, EventArgs<IApplication?> e)
        {
            // A couple of iterations in, the shell is laid out and painted.
            if (Interlocked.Increment(ref iterations) == 3)
            {
                ready.TrySetResult();
            }
        }

        app.Iteration += OnIteration;

        var runTask = Task.Run(() => app.Run(shell, null));

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        app.Iteration -= OnIteration;

        var harness = new ShellHarness(app, shell, client, session, runTask, stateDir);

        // Load entries on the UI thread, then let the resulting redraw settle.
        await harness.OnUiThreadAsync(() => shell.InitialLoadAsync()).ConfigureAwait(false);
        await harness.SettleAsync().ConfigureAwait(false);

        return harness;
    }

    /// <summary>Presses a key and waits for the UI to come to rest.</summary>
    public async Task PressAsync(Key key)
    {
        // RaiseKeyDownEvent drives the key through the normal focus routing. Going in via
        // the driver's input processor instead throws on this driver and kills the loop.
        Post(() => _app.Keyboard.RaiseKeyDownEvent(key));
        await SettleAsync().ConfigureAwait(false);
    }

    public Task PressAsync(char character) => PressAsync(new Key(character));

    public async Task PressAsync(params Key[] keys)
    {
        foreach (var key in keys)
        {
            await PressAsync(key).ConfigureAwait(false);
        }
    }

    /// <summary>Types a string one character at a time, as a user would.</summary>
    public async Task TypeAsync(string text)
    {
        foreach (var character in text)
        {
            await PressAsync(character).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Polls until <paramref name="condition"/> holds. Key presses start server calls that
    /// finish on other threads, so assertions have to wait for an outcome rather than
    /// assume a fixed delay was long enough — that is the difference between a reliable
    /// suite and one that fails under load.
    /// </summary>
    public async Task WaitForAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

        while (DateTime.UtcNow < deadline)
        {
            ThrowIfLoopDied();

            if (condition())
            {
                return;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        Assert.Fail($"Timed out waiting for: {because}");
    }

    /// <summary>Polls until a value read from the UI thread satisfies <paramref name="condition"/>.</summary>
    public async Task WaitForUiAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

        while (DateTime.UtcNow < deadline)
        {
            ThrowIfLoopDied();

            if (await ReadAsync(condition).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        Assert.Fail($"Timed out waiting for: {because}");
    }

    /// <summary>Reads view state on the UI thread.</summary>
    public async Task<T> ReadAsync<T>(Func<T> read)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        Post(() =>
        {
            try
            {
                completion.TrySetResult(read());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }

    private void Post(Action action) => _app.Invoke(action);

    private async Task OnUiThreadAsync(Func<Task> work)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Post(async void () =>
        {
            try
            {
                await work().ConfigureAwait(true);
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits out the shell's 120 ms settle debounce plus the fake server round-trip, then
    /// waits for the loop to come round again so any queued redraw has been applied.
    /// </summary>
    public async Task SettleAsync()
    {
        await Task.Delay(220).ConfigureAwait(false);
        await NextIterationAsync().ConfigureAwait(false);
        await Task.Delay(60).ConfigureAwait(false);
        await NextIterationAsync().ConfigureAwait(false);
    }

    private async Task NextIterationAsync()
    {
        ThrowIfLoopDied();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => completion.TrySetResult());

        var finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(5)))
            .ConfigureAwait(false);

        if (finished != completion.Task)
        {
            ThrowIfLoopDied();
            throw new TimeoutException(
                "The Terminal.Gui main loop stopped responding, but did not report a fault.");
        }
    }

    /// <summary>
    /// Surfaces an exception thrown on the loop thread. Without this a crashed loop just
    /// looks like every subsequent wait timing out, which hides the real cause.
    /// </summary>
    private void ThrowIfLoopDied()
    {
        if (_runTask.IsFaulted)
        {
            throw new InvalidOperationException(
                "The Terminal.Gui main loop faulted.", _runTask.Exception?.GetBaseException());
        }

        if (_runTask.IsCompleted)
        {
            throw new InvalidOperationException("The Terminal.Gui main loop exited unexpectedly.");
        }
    }

    /// <summary>Themes are global to the process, so load them once for the whole test run.</summary>
    private static void EnsureThemesLoaded()
    {
        lock (ThemeGate)
        {
            if (_themesLoaded)
            {
                return;
            }

            ThemeCatalog.Initialize("Netiflux Dark");
            _themesLoaded = true;
        }
    }

    public static List<Entry> DefaultEntries(int count = 12) =>
        Enumerable.Range(1, count).Select(i => new Entry
        {
            Id = i,
            FeedId = 1,
            Title = $"Article {i}",
            Url = $"https://example.org/{i}",
            Content = "<p>Body of article " + i + ".</p>",
            Status = EntryStatus.Unread,
            PublishedAt = DateTimeOffset.UtcNow.AddHours(-i),
            Feed = new Feed { Id = 1, Title = "Example Feed" }
        }).ToList();

    public async ValueTask DisposeAsync()
    {
        try
        {
            Post(() => _app.RequestStop(Shell));
            await _runTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            // The loop is already gone; nothing further to unwind.
        }

        try
        {
            Shell.Dispose();
            (_app as IDisposable)?.Dispose();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // Teardown races are not test failures.
        }

        try
        {
            if (Directory.Exists(_stateDir))
            {
                Directory.Delete(_stateDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp cleanup is best effort.
        }
    }
}
