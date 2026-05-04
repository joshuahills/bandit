using Terminal.Gui.ViewBase;

namespace Bandit.UI;

public interface IScreen
{
    int Index { get; }
    string Title { get; }
    View Build();
    void Refresh();
}
