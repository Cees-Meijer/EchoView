# EchoView — Live Scrolling Depth/Time Heatmap

A minimal Avalonia desktop application rendering a single live scrolling
heatmap with **ScottPlot 5**:

- **X** = time (seconds, left → right)
- **Y** = depth (metres, surface at top, increasing downward)
- **colour** = velocity (m/s), symmetric diverging scale centred on zero

The heatmap fills from left to right during the first ~20 s, then scrolls
left so the visible window always shows the latest data (unless "Follow
latest" is off, in which case the view stays where the user left it).

## Quick start (Windows)

Open a terminal in the project root and run:

```bash
dotnet restore
dotnet run
```

Or, if you prefer a published self-contained output:

```bash
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
publish/EchoView.exe
```

The executable requires the **.NET 10.0 runtime** on the target machine.

## What you get

| Element | Behaviour |
|---------|-----------|
| One heatmap chart | Livescrolling, left-to-right fill then scroll |
| Demo mode toggle | Synthetic depth/velocity profiles when no real sensor is attached |
| Follow latest toggle | Locks the visible time window to the most recent data (or freezes it) |
| Go to latest button | One-shot jump to the newest time |
| Colour bar | Symmetric diverging scale, blue (cold / negative) → white (zero) → red (hot / positive) |
| Status text | Reports current toggle states |

## Architecture (kept simple so real data can replace the demo generator)

```
Program.cs
├── Program.Main / BuildAvaloniaApp          entry point (Avalonia bootstrap)
├── MainWindow                                 partial class — UI chrome only
├── DemoSensor                                 IDataProvider-shaped synthetic data source
├── CircularProfileBuffer                     ring buffer of depth profiles
└── HeatmapController                         ScottPlot plot lifecycle + timer loop
```

- **`DemoSensor`** implements the same shape any real sensor would: `Buffer`
  (a `CircularProfileBuffer`), `DepthCount`, `DepthMin/Max`, and
  `VelocityMin/Max`. To swap in real data, implement IDataProvider, feed
  it into `HeatmapController`, and disable the DemoSensor toggle.
- **`HeatmapController`** owns the ScottPlot `Plot` and the circular matrix
  that backs the heatmap. `Update()` is called on a UI-dispatched timer and
  is the only place that mutates the plot — all incoming data passes through
  `WriteProfile()` first. Keeping that surface stable means you can replace
  the data source without touching the rendering code.
- **`MainWindow`** is a small partial class that only knows about UI wiring
  (toggles, button, status text) and owns the timer. It delegates every
  chart decision to `HeatmapController`.

### Data flow

1. A timer fires in `DemoSensor` (75 ms interval) and appends a
   double[] depth profile to `CircularProfileBuffer`.
2. The UI timer (120 ms) drains the buffer via `Snapshot()`, writes every
   profile through `WriteProfile()` into the circular matrix, advances
   `_elapsedSec`, decides the visible time window (fill vs. scroll vs.
   frozen), rebuilds the heatmap data array, replaces or updates the
   `Heatmap` plottable, refreshes the colour bar, and calls
   `AvaPlot.Refresh()`.
3. The colour bar is rebuilt each frame from the heatmap's colour axis so
   it stays in sync without manual range management.

## Key ScottPlot 5 notes

- Host control: `ScottPlot.Avalonia.AvaPlot` (not `PlotView`).
- Heatmap: `_plot.Add.Heatmap(data)` returns a `Heatmap`; set
  `Colormap`, `FlipVertically`, `Smooth`, and `Rectangle` (a
  `CoordinateRect`) on it.
- Axes: `_plot.Axes.Bottom.Min / Max`, `_plot.Axes.Left.Min / Max`,
  `_plot.Axes.SetLimitsY(min, max)`, `_plot.XLabel("…")`,
  `_plot.YLabel("…")`.
- Colour bar: `_plot.Add.ColorBar(heatmap, Edge.Right)`; remove old bars
  via `_plot.Axes.GetPanels()` + `_plot.Axes.Remove(panel)`.
- Redraw: `AvaPlot.Refresh()`.
- Include `using ScottPlot.Panels;` and `using System.Reflection;` where
  needed.

## Requirements

- .NET 10.0 SDK to build (target framework is `net10.0`).
- Avalonia 12.0.5 + ScottPlot 5.1.59 (pinned in `EchoView.csproj`).
- Compiled bindings enabled (`AvaloniaUseCompiledBindingsByDefault=true`).

## Files

| File | Purpose |
|------|---------|
|| `EchoView.csproj` | Project file, package references, net10.0, compiled bindings |
| `App.axaml` / `App.axaml.cs` | Avalonia application resources + bootstrap |
| `MainWindow.axaml` / `MainWindow.axaml.cs` | MainWindow XAML + partial-class stub calling `InitializeComponent()` |
| `Program.cs` | Entry point, `MainWindow` partial (UI wiring), demo sensor, buffer, and heatmap controller |
| `app.manifest` | Windows application manifest (requested by the project template) |
| `README.md` | This file |

## No real sensor yet?

Leave "Demo mode" **checked** — the app generates plausible layered shear-flow
profiles with noise continuously. The heatmap will fill left to right for the
first 20 s, then scroll. Toggle "Follow latest" off to freeze the view,
then hit "Go to latest" to snap back.
