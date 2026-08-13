using System.Collections.ObjectModel;
using Netiflux.Theming;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Netiflux.Ui;

/// <summary>
/// Lists the available themes. Moving the cursor applies the theme immediately so the
/// choice can be judged against the real UI rather than a name in a list; cancelling
/// puts the original back.
/// </summary>
public static class ThemePickerDialog
{
    /// <summary>Returns the chosen theme name, or null when the user cancelled.</summary>
    public static string? Prompt(View owner, string currentTheme)
    {
        var names = ThemeCatalog.GetThemeNames().ToList();
        if (names.Count == 0)
        {
            return null;
        }

        var dialog = new Dialog
        {
            Title = "Theme",
            Width = Dim.Percent(40),
            Height = Dim.Percent(60),
            SchemeName = "Dialog"
        };

        var list = new ListView
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1),
            SchemeName = "Dialog"
        };

        list.SetSource(new ObservableCollection<string>(names));

        var startIndex = Math.Max(0, names.IndexOf(currentTheme));
        list.SelectedItem = startIndex;

        string? result = null;

        list.ValueChanged += (_, args) =>
        {
            if (args.NewValue is { } index && index >= 0 && index < names.Count)
            {
                ThemeCatalog.Apply(names[index]);
                owner.SetNeedsDraw();
                dialog.SetNeedsDraw();
            }
        };

        void Accept()
        {
            var index = list.SelectedItem ?? startIndex;
            result = names[Math.Clamp(index, 0, names.Count - 1)];
            dialog.RequestStop();
        }

        // Enter has to be taken on the list itself. With focus in the list it never
        // reaches the default button, so the dialog closed with no result — which the
        // cancel path then treated as "restore the previous theme". Pressing Enter on a
        // theme appeared to reset it.
        list.KeyDown += (_, key) =>
        {
            if (key == Key.Enter)
            {
                Accept();
                key.Handled = true;
                return;
            }

            if (key == Key.Esc)
            {
                result = null;
                dialog.RequestStop();
                key.Handled = true;
            }
        };

        var apply = new Button { Text = "Use", IsDefault = true };
        apply.Accepting += (_, _) => Accept();

        var cancel = new Button { Text = "Cancel" };
        cancel.Accepting += (_, _) => dialog.RequestStop();

        dialog.AddButton(apply);
        dialog.AddButton(cancel);

        var hint = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = 1,
            Text = "↑↓ preview · Enter apply · Esc cancel",
            SchemeName = "Accent",
            CanFocus = false
        };

        list.Y = 1;
        list.Height = Dim.Fill(1);

        dialog.Add(hint, list);

        // Focus has to wait until the dialog is initialised; setting it beforehand is
        // silently lost and leaves the arrow keys doing nothing.
        dialog.Initialized += (_, _) => list.SetFocus();

        owner.App?.Run(dialog);
        dialog.Dispose();

        if (result is null)
        {
            // Cancelled — restore whatever was active when the dialog opened.
            ThemeCatalog.Apply(currentTheme);
            owner.SetNeedsDraw();
        }

        return result;
    }
}
