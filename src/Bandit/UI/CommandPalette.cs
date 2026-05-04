using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Bandit.UI;

internal sealed class CommandPalette : Window
{
    private const int MaxCompletions = 8;

    public event EventHandler<string>? Submitted;
    public event EventHandler? Cancelled;

    private readonly TextField _input;
    private readonly View _completions;
    private CommandSpec[] _matches = [];
    private int _highlight;

    public CommandPalette()
    {
        Title = " command ";
        Width = Dim.Fill();
        Height = MaxCompletions + 4;
        X = 0;
        Y = Pos.AnchorEnd(MaxCompletions + 4 + 1);
        BorderStyle = LineStyle.Rounded;
        SchemeName = "Bandit";

        _input = new TextField
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Text = "/",
        };
        _completions = new View
        {
            X = 0, Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = false,
        };
        Add(_input, _completions);

        _input.KeyDown += OnInputKey;
        _input.TextChanged += (_, _) => UpdateCompletions();
        UpdateCompletions();
    }

    public void FocusInput()
    {
        _input.SetFocus();
        _input.InsertionPoint = _input.Text.Length;
    }

    private void OnInputKey(object? sender, Key key)
    {
        switch (key.KeyCode)
        {
            case KeyCode.Esc:
                Cancelled?.Invoke(this, EventArgs.Empty);
                key.Handled = true;
                break;

            case KeyCode.CursorDown:
                if (_matches.Length > 0)
                {
                    _highlight = (_highlight + 1) % _matches.Length;
                    RenderCompletions();
                    key.Handled = true;
                }
                break;

            case KeyCode.CursorUp:
                if (_matches.Length > 0)
                {
                    _highlight = (_highlight - 1 + _matches.Length) % _matches.Length;
                    RenderCompletions();
                    key.Handled = true;
                }
                break;

            case KeyCode.Enter:
                // If the typed command is already runnable (has args, or the
                // verb takes none) submit it. Otherwise behave like Tab — pull
                // the highlighted completion into the input.
                if (LooksRunnable(_input.Text))
                {
                    Submitted?.Invoke(this, _input.Text);
                }
                else if (_matches.Length > 0)
                {
                    Complete(_matches[_highlight]);
                }
                key.Handled = true;
                break;

            case KeyCode.Tab:
                if (_matches.Length > 0)
                {
                    Complete(_matches[_highlight]);
                    key.Handled = true;
                }
                break;
        }
    }

    private static bool LooksRunnable(string text)
    {
        var parsed = CommandParser.Parse(text);
        if (parsed is null) return false;

        CommandSpec? spec = null;
        foreach (var c in CommandRegistry.All)
        {
            if (c.Name.Equals(parsed.Verb, StringComparison.OrdinalIgnoreCase))
            {
                spec = c;
                break;
            }
        }
        if (spec is null) return false;

        // Verbs that take no arg are runnable on verb match alone; verbs that
        // need an arg require non-empty arg.
        return string.IsNullOrEmpty(spec.ArgHint) || !string.IsNullOrEmpty(parsed.Arg);
    }

    private void Complete(CommandSpec spec)
    {
        _input.Text = string.IsNullOrEmpty(spec.ArgHint)
            ? $"/{spec.Name}"
            : $"/{spec.Name} ";
        _input.InsertionPoint = _input.Text.Length;
        UpdateCompletions();
    }

    private void UpdateCompletions()
    {
        var trimmed = _input.Text.Trim().TrimStart('/').Trim();
        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        // Once the user has typed past the verb (entered a space), the verb is
        // pinned — don't re-filter on the arg.
        bool typedSpace = trimmed.Contains(' ');
        var prefix = parts.Length > 0 ? parts[0] : "";

        _matches = (typedSpace ? CommandRegistry.All.Where(c => c.Name.Equals(prefix, StringComparison.OrdinalIgnoreCase)) : CommandRegistry.Match(prefix))
            .Take(MaxCompletions)
            .ToArray();

        if (_matches.Length == 0) _highlight = 0;
        else if (_highlight >= _matches.Length) _highlight = _matches.Length - 1;
        else if (_highlight < 0) _highlight = 0;

        RenderCompletions();
    }

    private void RenderCompletions()
    {
        _completions.RemoveAll();
        if (_matches.Length == 0)
        {
            _completions.Add(new ColoredLabel("  no matching commands", Theme.DimAttr)
            {
                X = 0, Y = 0, Width = Dim.Fill(),
            });
            return;
        }

        for (int i = 0; i < _matches.Length; i++)
        {
            bool active = i == _highlight;
            var match = _matches[i];
            var nameAttr = active ? Theme.ActiveTabAttr : Theme.AccentAttr;
            var descAttr = active ? Theme.SelectedPidAttr : Theme.DimAttr;

            string head = string.IsNullOrEmpty(match.ArgHint)
                ? $"  /{match.Name}"
                : $"  /{match.Name} {match.ArgHint}";
            string desc = $"  {match.Description}";

            _completions.Add(new ColoredLabel(head.PadRight(28), nameAttr)
            {
                X = 0, Y = i, Width = 28,
            });
            _completions.Add(new ColoredLabel(desc, descAttr)
            {
                X = 28, Y = i, Width = Dim.Fill(),
            });
        }
    }
}
