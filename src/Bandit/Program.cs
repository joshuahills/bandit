using Bandit;
using Bandit.Platform.Windows;

var state = new AppState { IsElevated = AdminCheck.IsElevated() };
using var app = new App(state);
await app.RunAsync();
