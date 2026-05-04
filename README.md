# Bandit

[![CI](https://github.com/joshuahills/bandit/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/joshuahills/bandit/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/joshuahills/bandit?label=release&color=blue)](https://github.com/joshuahills/bandit/releases/latest)
[![NuGet](https://img.shields.io/nuget/v/Bandit.Cli?label=nuget&color=004880)](https://www.nuget.org/packages/Bandit.Cli)
[![NuGet downloads](https://img.shields.io/nuget/dt/Bandit.Cli?color=004880)](https://www.nuget.org/packages/Bandit.Cli)
[![License: MIT](https://img.shields.io/github/license/joshuahills/bandit)](LICENSE)
![Platform](https://img.shields.io/badge/platform-windows-0078d6)
![.NET](https://img.shields.io/badge/.NET-10-512bd4)

btop-style network monitor TUI for Windows.

## Install

Three options. Pick whichever matches your existing tooling.

### GitHub Release (single `.exe`)

Grab `Bandit.exe` from the [latest release](https://github.com/joshuahills/bandit/releases/latest) and drop it on your `PATH`.

### Scoop

```
scoop bucket add bandit https://github.com/joshuahills/bandit
scoop install bandit/bandit
```

The bucket lives in this repo at [`bucket/bandit.json`](bucket/bandit.json) and updates automatically on each release.

### .NET tool

Requires the .NET 10 SDK.

```
dotnet tool install -g Bandit.Cli
```

The tool command is `bandit` (not `Bandit.Cli`).

## Usage

```
bandit            # launch the TUI
bandit -v         # print version and exit
bandit --version
```

### Keys

| Key | Action |
|-----|--------|
| `1`–`9` | Switch screen |
| `[` / `]` | Cycle timescale ← / → |
| Click | Tabs and timescale options are mouse-clickable |
| `q` / `Ctrl+C` | Quit |

### Elevation

System-wide bandwidth charts work without admin. Per-process tracking (Processes screen) requires running from an elevated terminal — it uses ETW, which is admin-only on Windows.

## License

MIT.
