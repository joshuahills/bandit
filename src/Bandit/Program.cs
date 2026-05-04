using System.Reflection;
using Bandit;
using Bandit.Platform.Windows;

if (args.Length > 0 && (args[0] is "-v" or "--version"))
{
    var info = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";
    Console.WriteLine(info.Split('+')[0]);
    return;
}

var state = new AppState { IsElevated = AdminCheck.IsElevated() };
using var app = new App(state);
await app.RunAsync();
