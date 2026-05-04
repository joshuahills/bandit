using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Bandit.UI;

internal sealed class CommandPalette : Window
{
    public event EventHandler<string>? Submitted;
    public event EventHandler? Cancelled;

    private readonly TextField _input;

    public CommandPalette()
    {
        Title = " command ";
        Width = 60;
        Height = 3;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Rounded;
        SchemeName = "Bandit";

        _input = new TextField
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Text = "/",
        };
        Add(_input);
        _input.KeyDown += OnInputKey;
    }

    public void FocusInput() => _input.SetFocus();

    private void OnInputKey(object? sender, Key key)
    {
        if (key.KeyCode == KeyCode.Enter)
        {
            Submitted?.Invoke(this, _input.Text);
            key.Handled = true;
        }
        else if (key.KeyCode == KeyCode.Esc)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
            key.Handled = true;
        }
    }
}
