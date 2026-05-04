using Hex1b;
using Hex1b.Theming;
using Hex1b.Widgets;

namespace Bandit.UI;

internal static class UiExtensions
{
    public static Hex1bWidget Text<TParent>(this WidgetContext<TParent> ctx, string text, Hex1bColor fg)
        where TParent : Hex1bWidget
        => new ThemePanelWidget(t => t.Set(GlobalTheme.ForegroundColor, fg), ctx.Text(text));
}
