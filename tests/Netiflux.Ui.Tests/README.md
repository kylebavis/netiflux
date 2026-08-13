# Netiflux.Ui.Tests

End-to-end tests for the TUI. Each test starts a **real `AppShell` on a real Terminal.Gui
main loop**, presses real keys, and asserts on what happened.

This exists because the interesting bugs in a TUI are not logic bugs. Every defect that
reached the user in this project was a focus or key-routing problem — a `FrameView`
holding focus so the `ListView` inside it never saw a keystroke, a modal setting focus
before it was initialised, an application-scope handler that consumed a key it should
have passed on. None of that is visible to a unit test that calls methods directly.

## How it works

`ShellHarness.StartAsync()` builds a shell over `FakeMinifluxClient`, runs the main loop
on a background thread, and pins the screen to 160×48 so the split-pane layout is
exercised regardless of the console running the tests.

```csharp
await using var ui = await ShellHarness.StartAsync();

await ui.PressAsync(Key.J);
await ui.PressAsync('s');

await ui.WaitForAsync(() => ui.Client.Saved.Contains(2), "entry 2 to be saved");
```

Keys go in via `Application.Keyboard.RaiseKeyDownEvent` on the loop thread. Assertions
poll with `WaitForAsync` (server state) or `WaitForUiAsync` (view state) rather than
sleeping a fixed amount — a key press kicks off work that finishes on other threads, and
fixed delays make a suite that fails under load.

If the loop thread throws, the harness surfaces that exception instead of letting every
later wait time out.

## What is not covered: modal dialogs

Anything that opens a modal — `Ctrl+T` (theme picker), `?` (help), `A` (mark all read) —
**cannot be tested here**, and tests for them will hang rather than fail.

A modal starts a nested `Application.Run`. Under a test host, stdin is redirected and at
end-of-file, so that nested loop has no input to process and never services queued
`Invoke` callbacks. The dialog opens — `TopRunnable` does become the dialog — but nothing
can then drive it or close it. Verified directly: `modalOpened=True loopResponsive=False`.

The clean fix is a driver with a feedable input source. That was built and then removed,
because Terminal.Gui 2.4.17 cannot select it: `ApplicationImpl.CreateDriver` resolves
driver names with an internal switch and never consults `DriverRegistry`, so a registered
custom driver is rejected with "Unknown driver name" even though
`DriverRegistry.IsRegistered` returns true. Two smaller traps sit behind that, worth
recording for whoever revisits this:

- enabling Terminal.Gui's `ConfigurationManager` resets `DriverRegistry` to its built-ins,
  so any custom registration must happen *after* themes are loaded;
- `IDriver` is only implemented by the internal `DriverImpl`, so constructing one by hand
  means reflection.

Until a custom driver can be selected by name, dialog behaviour has to be checked by
hand in a terminal.
