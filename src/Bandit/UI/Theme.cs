using Hex1b.Theming;

namespace Bandit.UI;

public static class Theme
{
    public static readonly Hex1bColor Upload    = Hex1bColor.FromRgb(0,   210, 190);
    public static readonly Hex1bColor Download  = Hex1bColor.FromRgb(80,  250, 120);
    public static readonly Hex1bColor ActiveTab = Hex1bColor.FromRgb(255, 255, 255);
    public static readonly Hex1bColor InactiveTab = Hex1bColor.FromRgb(100, 100, 120);
    public static readonly Hex1bColor Accent    = Hex1bColor.FromRgb(90,  170, 255);
    public static readonly Hex1bColor Axis      = Hex1bColor.FromRgb(60,  60,  80);
    public static readonly Hex1bColor Grid      = Hex1bColor.FromRgb(35,  35,  50);
    public static readonly Hex1bColor StatusBg  = Hex1bColor.FromRgb(20,  20,  35);
    public static readonly Hex1bColor StatusFg  = Hex1bColor.FromRgb(160, 160, 180);
    public static readonly Hex1bColor Warning   = Hex1bColor.FromRgb(255, 200,  50);
    public static readonly Hex1bColor Dim       = Hex1bColor.FromRgb(70,  70,  90);
    public static readonly Hex1bColor Background = Hex1bColor.FromRgb(12, 12,  22);
}
