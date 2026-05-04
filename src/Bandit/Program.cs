using Bandit;
using Bandit.Platform.Windows;

if (args.Length > 0 && (args[0] is "-v" or "--version"))
{
    Console.WriteLine(BuildInfo.Version);
    return;
}

var state = new AppState { IsElevated = AdminCheck.IsElevated() };
using var app = new App(state);
await app.RunAsync();
