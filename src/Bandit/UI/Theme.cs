using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Bandit.UI;

public static class Theme
{
    public static readonly Color Upload      = new(0,   210, 190);
    public static readonly Color Download    = new(80,  250, 120);
    public static readonly Color ActiveTab   = new(255, 255, 255);
    public static readonly Color InactiveTab = new(100, 100, 120);
    public static readonly Color Accent      = new(90,  170, 255);
    public static readonly Color Axis        = new(60,  60,  80);
    public static readonly Color Grid        = new(35,  35,  50);
    public static readonly Color StatusBg    = new(20,  20,  35);
    public static readonly Color StatusFg    = new(160, 160, 180);
    public static readonly Color Warning     = new(255, 200,  50);
    public static readonly Color Dim         = new(70,  70,  90);
    public static readonly Color Background  = new(12,  12,  22);

    public static readonly Attribute UploadAttr   = new(Upload, Background);
    public static readonly Attribute DownloadAttr = new(Download, Background);
    public static readonly Attribute AccentAttr   = new(Accent, Background);
    public static readonly Attribute AxisAttr     = new(Axis, Background);
    public static readonly Attribute GridAttr     = new(Grid, Background);
    public static readonly Attribute StatusAttr   = new(StatusFg, StatusBg);
    public static readonly Attribute WarningAttr  = new(Warning, Background);
    public static readonly Attribute DimAttr      = new(Dim, Background);
    public static readonly Attribute ActiveTabAttr   = new(ActiveTab, Background);
    public static readonly Attribute InactiveTabAttr = new(InactiveTab, Background);
}
