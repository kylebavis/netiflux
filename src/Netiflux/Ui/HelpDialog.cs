using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Netiflux.Ui;

/// <summary>The key map, shown with <c>?</c>.</summary>
public static class HelpDialog
{
    public static void Show(View owner, GlyphSet glyphs)
    {
        var dialog = new Dialog
        {
            Title = "Netiflux — keys",
            Width = Dim.Percent(70),
            Height = Dim.Percent(80),
            SchemeName = "Dialog"
        };

        var text = new Markdown
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1),
            SchemeName = "Dialog",
            ShowCopyButtons = false,
            UseThemeBackground = true,
            Text = BuildHelpText(glyphs)
        };

        var close = new Button { Text = "Close", IsDefault = true };
        close.Accepting += (_, _) => dialog.RequestStop();
        dialog.AddButton(close);

        dialog.Add(text);
        owner.App?.Run(dialog);
        dialog.Dispose();
    }

    private static string BuildHelpText(GlyphSet g) =>
        $$"""
        ## Moving

        | Key | Action |
        |---|---|
        | `j` / `k` | Next / previous entry — scrolls the article when reading |
        | `Space` | Mark the row and advance; pages down while reading |
        | `G` | Jump to the last entry |
        | `g g` | Jump to the first entry |
        | `Enter` / `o` | Open the entry in the reader |
        | `Esc` | Clear the selection, or leave the reader |
        | `Tab` | Cycle sidebar → list → reader |

        ## Triage

        | Key | Action |
        |---|---|
        | `s` | **Save to your bookmark service** (Miniflux integration) |
        | `m` | Toggle read / unread |
        | `f` | Toggle star |
        | `v` | Open the original in your browser |
        | `F` | Ask Miniflux to scrape the full article text |
        | `A` | Mark everything in this view as read (asks first) |

        Actions apply to the marked rows when there are any, otherwise to the row under
        the cursor. Nothing has to be opened first — `s` on a highlighted row saves it,
        and `Space Space Space s` pushes three articles in one go.

        By default an entry is marked read as soon as the cursor settles on it in the
        list, so moving through with `j` clears as you go. Set `auto_mark_read` in the
        config to `on-open`, `on-scroll-end`, or `never` to change that.

        ## Views

        | Key | Action |
        |---|---|
        | `g u` | Unread |
        | `g t` | Today |
        | `g s` | Starred |
        | `g a` | All |
        | `/` | Search — opens a bar at the bottom; `Enter` runs it, `Esc` cancels |
        | `r` | Reload this view |
        | `R` | Ask the server to poll every feed |

        `Esc` unwinds one layer at a time: it clears a marked selection first, then leaves
        the reader, then leaves search results and puts back the view you came from.

        The sidebar lists only feeds that have unread entries, plus any that are failing
        to parse (shown with a trailing `!`). `Enter` on the row at the bottom of the
        list expands it to every feed, and again to collapse it.

        ## Display

        | Key | Action |
        |---|---|
        | `z` | Zen mode — reader fills the window |
        | `\` or `Ctrl+B` | Show / hide the sidebar |
        | `Ctrl+T` | Change theme |
        | `?` or `F1` | This help |
        | `q` or `Ctrl+Q` | Quit Netiflux |

        ## Row markers

        | Mark | Meaning |
        |---|---|
        | `{{g.Unread}}` | Unread |
        | `{{g.Read}}` | Read |
        | `{{g.Starred}}` | Starred |
        | `{{g.Saved}}` | Pushed to your bookmark service |
        | `{{g.SavedAndStarred}}` | Both saved and starred |
        | `{{g.Selected}}` | Row is marked for a batch action |

        Miniflux does not report which entries you have pushed to a bookmark service, so
        Netiflux keeps that record locally. Clear it by deleting `saved-entries.json`
        from the config directory.
        """;
}
