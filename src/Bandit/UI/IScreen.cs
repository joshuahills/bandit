namespace Bandit.UI;

using Terminal.Gui.ViewBase;

public interface IScreen
{
    int Index { get; }
    string Title { get; }
    View Build();
    void Refresh();
}
