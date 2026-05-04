using Bandit.Data.Collectors;
using Terminal.Gui.ViewBase;

namespace Bandit.UI.Screens;

public sealed class ProcessScreen(AppState state, ProcessNetworkCollector collector) : IScreen
{
    public int Index => 1;
    public string Title => "Processes";

    public void Refresh() { }

    public View Build()
    {
        _ = state;

        var container = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = false,
        };

        if (!collector.IsAvailable)
        {
            (string text, Terminal.Gui.Drawing.Attribute attr)[] lines =
            [
                ("", Theme.StatusAttr),
                ("  Per-process network monitoring requires administrator privileges.", Theme.WarningAttr),
                ("", Theme.StatusAttr),
                ("  Re-launch Bandit from an elevated terminal:", Theme.StatusAttr),
                ("", Theme.StatusAttr),
                ("    Run as Administrator → bandit.exe", Theme.DimAttr),
            ];

            for (int i = 0; i < lines.Length; i++)
            {
                container.Add(new ColoredLabel(lines[i].text, lines[i].attr)
                {
                    X = 0,
                    Y = i,
                    Width = Dim.Fill(),
                });
            }
            return container;
        }

        container.Add(new ColoredLabel("  Per-process data collection coming soon.", Theme.StatusAttr)
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
        });
        return container;
    }
}
