# Bandit

btop-style network monitor TUI for Windows. .NET 10 + Native AOT — single-binary, fast start, low memory.

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

## Build from source

.NET 10 SDK required. AOT publish needs the MSVC linker on `PATH` — easiest path is opening Visual Studio's *Developer Command Prompt* or publishing from VS directly.

```
dotnet build src/Bandit/Bandit.csproj
dotnet publish src/Bandit/Bandit.csproj -c Release -r win-x64
```

## License

MIT.
