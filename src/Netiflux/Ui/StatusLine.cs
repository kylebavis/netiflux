using Netiflux.Theming;
using Terminal.Gui.ViewBase;

namespace Netiflux.Ui;

public enum ToastKind { Info, Good, Bad }

/// <summary>
/// The bottom line: normally a compact key legend, temporarily a status message.
/// <para>
/// Triage runs on muscle memory, and the one thing muscle memory cannot supply is
/// confirmation that a save actually reached the server. So the toast is deliberately
/// loud (its own colour, full line) and the legend returns on its own afterwards.
/// </para>
/// </summary>
public sealed class StatusLine : View
{
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(4);

    private string _legend = "";
    private string _toast = "";
    private ToastKind _toastKind = ToastKind.Info;
    private object? _toastTimeout;

    public StatusLine()
    {
        Height = 1;
        Width = Dim.Fill();
        CanFocus = false;
    }

    /// <summary>The persistent key legend shown when no toast is active.</summary>
    public string Legend
    {
        get => _legend;
        set
        {
            _legend = value ?? "";
            SetNeedsDraw();
        }
    }

    public void Show(string message, ToastKind kind = ToastKind.Info)
    {
        _toast = message ?? "";
        _toastKind = kind;
        SetNeedsDraw();

        CancelPendingTimeout();

        if (string.IsNullOrEmpty(_toast) || App is not { } app)
        {
            return;
        }

        _toastTimeout = app.AddTimeout(ToastDuration, () =>
        {
            _toast = "";
            _toastTimeout = null;
            SetNeedsDraw();
            return false;
        });
    }

    public void ClearToast() => Show("");

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var width = Viewport.Width;
        if (width <= 0)
        {
            return true;
        }

        var showingToast = !string.IsNullOrEmpty(_toast);
        var scheme = showingToast
            ? ThemeCatalog.Resolve(_toastKind switch
            {
                ToastKind.Good => "StatusGood",
                ToastKind.Bad => "StatusBad",
                _ => "Accent"
            })
            : ThemeCatalog.Resolve("Sidebar");

        SetAttribute(scheme.Normal);

        var text = showingToast ? _toast : _legend;
        var line = text.Length >= width ? text[..width] : text.PadRight(width);

        Move(0, 0);
        AddStr(line);
        return true;
    }

    private void CancelPendingTimeout()
    {
        if (_toastTimeout is null)
        {
            return;
        }

        App?.RemoveTimeout(_toastTimeout);
        _toastTimeout = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelPendingTimeout();
        }

        base.Dispose(disposing);
    }
}
