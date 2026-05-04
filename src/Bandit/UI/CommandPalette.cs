using Terminal.Gui.App;
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
        // Anchor full-width at the bottom of the screen, just above the
        // app status bar, like a vim/emacs minibuffer.
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
        UpdateCompletions();
    }

    public void FocusInput() => _input.SetFocus();

    private void OnInputKey(object? sender, Key key)
    {
        switch (key.KeyCode)
        {
            case KeyCode.Enter:
                Submitted?.Invoke(this, _input.Text);
                key.Handled = true;
                break;
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
            case KeyCode.Tab:
                if (_matches.Length > 0)
                {
                    _input.Text = $"/{_matches[_highlight].Name} ";
                    UpdateCompletions();
                    key.Handled = true;
                }
                break;
            default:
                // Recompute completions after the key has had a chance to
                // mutate the TextField's Text (we read the field on the next
                // event-loop tick).
                Application.Invoke(UpdateCompletions);
                break;
        }
    }

    private void UpdateCompletions()
    {
        var text = _input.Text;
        var verb = text.Trim().TrimStart('/').Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var prefix = verb.Length > 0 ? verb[0] : "";
        _matches = CommandRegistry.Match(prefix).Take(MaxCompletions).ToArray();
        _highlight = Math.Clamp(_highlight, 0, Math.Max(0, _matches.Length - 1));
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
