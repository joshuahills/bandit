using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Bandit.UI;

public sealed class ColoredLabel : View
{
    public ColoredLabel(string text, Attribute attribute)
    {
        TextValue = text;
        Attribute = attribute;
        CanFocus = false;
        Height = 1;
    }

    public string TextValue { get; set; }
    public Attribute Attribute { get; set; }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        SetAttribute(Attribute);
        Move(0, 0);
        foreach (var rune in TextValue.EnumerateRunes())
            AddRune(rune);
        return true;
    }
}
