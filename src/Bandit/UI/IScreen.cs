using Hex1b;
using Hex1b.Widgets;

namespace Bandit.UI;

public interface IScreen
{
    int Index { get; }
    string Title { get; }
    Hex1bWidget Build(RootContext ctx);
}
