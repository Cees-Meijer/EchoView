using System;
using System.Collections.Generic;
using System.Reflection;
using System.Timers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using ScottPlot.Panels;

namespace EchoView;

// ============================================================================
// Program entry point
// ============================================================================

public static class Program
{
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}

// ============================================================================
// MainWindow — UI chrome + update loop
// ============================================================================

partial class MainWindow
{
    // --- UI control instances ---
    private readonly AvaPlot _plotControl;
    private readonly CheckBox _followLatestToggle;
    private readonly Button _goToLatestButton;
    private readonly TextBlock _statusText;
    private readonly CheckBox _demoToggle;
    private readonly NumericUpDown _depthMinControl;
    private readonly NumericUpDown _depthMaxControl;
    private readonly HeatmapController _controller;
    private readonly Timer _timer;
    private bool _followLatest = true;
    private readonly DemoSensor _demo = new();

    // --------------------------------------------------------------------------
    // Constructor — this is the real one; axaml.cs calls InitializeComponent()
    // then this body runs (same partial class, merged by the compiler).
    // --------------------------------------------------------------------------
    public MainWindow()
    {
        InitializeComponent();

        // --- ScottPlot host control ---
        // Use fully-qualified Avalonia types to avoid ambiguity with ScottPlot
        // (ScottPlot has its own Orientation, HorizontalAlignment, etc.)
        _plotControl = new AvaPlot
        {
            Name = "HeatmapPlot",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            MinWidth = 480,
            MinHeight = 320
        };

        _demoToggle = new CheckBox
        {
            Content = "Demo mode",
            IsChecked = true,
            Margin = new Thickness(0, 0, 4, 0)
        };

        _followLatestToggle = new CheckBox
        {
            Content = "Follow latest",
            IsChecked = true,
            Margin = new Thickness(0, 0, 6, 0)
        };

        _goToLatestButton = new Button
        {
            Content = "Go to latest",
            Margin = new Thickness(0, 0, 6, 0)
        };

        _statusText = new TextBlock
        {
            Text = "Demo: ON | Follow latest: ON",
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 0)
        };

        // --- Depth range selectors (left of chart) ----------------------
        // Two NumericUpDown controls let the user set the minimum and
        // maximum depth shown on the Y axis.  Values change the
        // DemoSensor.DepthMin / DepthMax properties, which the
        // HeatmapController reads every update cycle.

        var depthHeader = new TextBlock
        {
            Text = "Depth range (m)",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin = new Thickness(0, 2, 0, 4)
        };

        // Create the NumericUpDown controls before constructing the panel
        // so they are non-null when added to depthPanel.Children.
        _depthMinControl = new NumericUpDown
        {
            Name = "DepthMin",
            Value = (decimal)_demo.DepthMin,
            Minimum = 0m,
            Maximum = (decimal)_demo.DepthMax - 1m,
            Increment = 1m,
            Margin = new Thickness(0, 0, 0, 2)
        };

        _depthMaxControl = new NumericUpDown
        {
            Name = "DepthMax",
            Value = (decimal)_demo.DepthMax,
            Minimum = (decimal)_demo.DepthMin + 1m,
            Maximum = 200m,
            Increment = 1m,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var depthPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 4,
            Width = 130,
            MinWidth = 110,
            Children =
            {
                depthHeader,
                _depthMinControl,
                _depthMaxControl
            }
        };

        // Wire value changes back into the demo sensor so the controller
        // picks up the new limits on the next update cycle.
        void OnDepthValueChanged(NumericUpDown cue, string name)
        {
            decimal? dv = cue.Value;
            if (!dv.HasValue) return;
            double v = (double)dv.Value;
            if (name == "depthMin")
            {
                double clamped = Math.Max(0.0, Math.Min(v, (double)_depthMaxControl.Value - 1.0));
                _demo.DepthMin = clamped;
                cue.Value = (decimal)clamped;
            }
            else if (name == "depthMax")
            {
                double clamped = Math.Min(200.0, Math.Max(v, (double)_depthMinControl.Value + 1.0));
                _demo.DepthMax = clamped;
                cue.Value = (decimal)clamped;
            }
        }

        _depthMinControl.ValueChanged += (_, _) => OnDepthValueChanged(_depthMinControl, "depthMin");
        _depthMaxControl.ValueChanged += (_, _) => OnDepthValueChanged(_depthMaxControl, "depthMax");

        // --- Layout ----------------------------------------------------
        // Two-column grid: left column = depth selectors, right column =
        // chart + top bar.
        var topBar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children =
            {
                _demoToggle,
                new TextBlock { Text = "  ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                _followLatestToggle,
                _goToLatestButton,
                new TextBlock { Text = "   ", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                _statusText
            }
        };

        var rootGrid = new Grid();
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(130, GridUnitType.Pixel)));
        rootGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        rootGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

        // Left column: depth panel (vertically centered against the chart)
        depthPanel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        rootGrid.Children.Add(depthPanel);
        Grid.SetColumn(depthPanel, 0);
        Grid.SetRow(depthPanel, 1);

        // Right column: top bar + chart
        var rightColumn = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 2,
            Children =
            {
                topBar,
                _plotControl
            }
        };

        rootGrid.Children.Add(rightColumn);
        Grid.SetColumn(rightColumn, 1);
        Grid.SetRow(rightColumn, 0);
        Grid.SetRowSpan(rightColumn, 2);

        Content = rootGrid;

        // --- Wire toggles ---
        // Avalonia CheckBox.IsCheckedChanged uses EventHandler<EventArgs>
        // so the event args carry no new-value info. Read the sender's
        // IsChecked property instead (returns a read-only Avalonia property
        // via GetValue under the hood, so it is readable at runtime).
        _demoToggle.IsCheckedChanged += (sender, _) =>
        {
            bool? checkedValue = (sender as CheckBox)?.IsChecked;
            if (checkedValue == true)
            {
                _demo.Start();
                UpdateStatus();
            }
            else
            {
                _demo.Stop();
                UpdateStatus();
            }
        };

        _followLatestToggle.IsCheckedChanged += (sender, _) =>
        {
            _followLatest = (sender as CheckBox)?.IsChecked == true;
            _controller?.SetFollowLatest(_followLatest);
            UpdateStatus();
        };

        // Button.Click is a standard routed event in Avalonia
        _goToLatestButton.Click += (_, _) => _controller.JumpToLatest();

        // --- Controller ---
        _controller = new HeatmapController(_plotControl, _demo);

        // --- Update loop (UI-dispatched timer) ---
        _timer = new Timer(120);
        _timer.Elapsed += (_, _) => Dispatcher.UIThread.Post(_controller.Update);
        _timer.Start();

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        _statusText.Text = "Demo: " + (_demoToggle.IsChecked == true ? "ON" : "OFF")
                         + " | Follow latest: " + (_followLatest ? "ON" : "OFF")
                         + " | Depth: " + _demo.DepthMin.ToString("F0") + "–" + _demo.DepthMax.ToString("F0") + " m";
    }
}

// ============================================================================
// Demo sensor — synthetic depth/velocity profiles
// ============================================================================

/// <summary>
/// Produces plausible synthetic velocity-vs-depth profiles on a timer.
///
/// Real-sensor replacement: implement IDataProvider, feed it to
/// HeatmapController, and remove (or ignore) the DemoSensor.
/// </summary>
public class DemoSensor
{
    private readonly Timer _timer;
    private readonly Random _rng = new(1234);
    private bool _running;

    public CircularProfileBuffer Buffer { get; } = new(capacity: 300);

    // Geometry
    public int DepthCount => 64;          // rows in the heatmap
    public double DepthMin { get; set; } = 0.0;        // metres (surface)
    public double DepthMax { get; set; } = 120.0;      // metres
    public double VelocityMin => -2.0;    // m/s (symmetric scale)
    public double VelocityMax => +2.0;    // m/s

    public DemoSensor()
    {
        _timer = new Timer(75); // ~13 profiles / sec
        _timer.Elapsed += OnTick;
    }

    public void Start()
    {
        _running = true;
        Buffer.Clear();
        _timer.Start();
        // Seed a few profiles so the plot isn't empty on startup.
        for (int i = 0; i < 20; i++) Buffer.Add(GenerateProfile());
    }

    public void Stop()
    {
        _running = false;
        _timer.Stop();
    }

    private void OnTick(object? _, EventArgs __)
    {
        if (!_running) return;
        Buffer.Add(GenerateProfile());
    }

    // ------------------------------------------------------------------
    // Profile generator — layered shear flow + noise + moving bottom echo
    // ------------------------------------------------------------------
    private double[] GenerateProfile()
    {
        var n = DepthCount;
        var profile = new double[n];

        double layer1 = _rng.NextDouble() * 0.5;
        double pycnocline = 0.7 * Math.Sin(_rng.NextDouble() * Math.PI * 2);

        // --- Moving bottom echo -----------------------------------------
        // The seabed depth oscillates smoothly in the 10-15 m band.
        // A slow secondary phase modulation prevents perfectly repetitive
        // motion, giving the bottom trace a more natural drift.
        const double bottomCenter = 12.5;   // metres (middle of 10-15 band)
        const double bottomAmpl   = 2.5;     // metres (so band is 10-15 m)
        const double bottomFreq1  = 0.040;   // slow primary drift (rad/profile)
        const double bottomFreq2  = 0.130;   // faster wobble
        _bottomPhase1 += bottomFreq1;
        _bottomPhase2 += bottomFreq2;
        double bottomModulation = 0.35 * Math.Sin(_bottomPhase2);
        double bottomDepth = bottomCenter
                           + bottomAmpl * (Math.Sin(_bottomPhase1) + bottomModulation);

        // Bottom reflection: a broad strong return centred on the seabed.
        // Below the seabed the signal is strongly attenuated (no penetration).
        const double bottomStrength = 2.0;
        const double bottomSpread   = 0.060;   // broader than before (more realistic)
        const double belowAttenuation = 4.0;    // how fast signal dies below seabed

        for (int i = 0; i < n; i++)
        {
            double z = (double)i / (n - 1);   // 0 = surface, 1 = bottom (120 m)
            double absDepth = z * DepthMax;    // actual depth in metres

            // Normalised seabed position
            double zBottom = bottomDepth / DepthMax;
            double dz = z - zBottom;

            // Bottom echo — gaussian centred on seabed, but asymmetric:
            // stronger above the seabed (backscatter), strongly damped below.
            double spreadAbove = bottomSpread;
            double spreadBelow = bottomSpread * belowAttenuation;
            double sigma2 = (dz <= 0) ? (spreadAbove * spreadAbove) : (spreadBelow * spreadBelow);
            double bottomEcho = bottomStrength * Math.Exp(-(dz * dz) / sigma2);

            // Seafloor texture: small high-frequency ripples on the bottom echo
            // to make the trace look less like a perfect line.
            double texture = 0.08 * bottomStrength * Math.Sin(absDepth * 18.0 + _rng.NextDouble() * 0.7)
                           * Math.Exp(-Math.Abs(dz) / spreadAbove);
            bottomEcho += texture;

            double shear = 0.2 * (z - 0.5) +
                           0.15 * Math.Sin(z * 8.0 + _rng.NextDouble() * 0.5);

            double eddy = 0.3 * Math.Sin(z * 12.0 + _rng.NextDouble() * 0.8) *
                          Math.Exp(-Math.Pow(z - 0.45, 2) / 0.02);

            double surface = (z < 0.12) ? 0.25 * (_rng.NextDouble() - 0.5) : 0.0;

            double noiseAmp = 0.06 * (1.0 - 0.5 * z);
            double noise = noiseAmp * (_rng.NextDouble() - 0.5);

            double value = layer1 + shear + eddy + surface + noise
                         + pycnocline * (1.0 - z)
                         + bottomEcho;

            profile[i] = Math.Clamp(value, VelocityMin, VelocityMax);
        }

        return profile;
    }

    // Phase accumulators for the two-frequency bottom motion.
    private double _bottomPhase1;
    private double _bottomPhase2;
}

// ============================================================================
// Circular buffer of full profiles
// ============================================================================

public class CircularProfileBuffer
{
    private readonly int _capacity;
    private readonly List<double[]> _items = new();
    private readonly object _itemsSync = new();

    public CircularProfileBuffer(int capacity)
    {
        _capacity = capacity;
    }

    public int Count
    {
        get { lock (_itemsSync) { return _items.Count; } }
    }

    public void Clear()
    {
        lock (_itemsSync) { _items.Clear(); }
    }

    public void Add(double[] profile)
    {
        lock (_itemsSync)
        {
            _items.Add((double[])profile.Clone());
            if (_items.Count > _capacity)
                _items.RemoveAt(0);
        }
    }

    public double[][] Snapshot()
    {
        lock (_itemsSync)
        {
            var result = new double[_items.Count][];
            for (int i = 0; i < _items.Count; i++)
                result[i] = (double[])_items[i].Clone();
            return result;
        }
    }

    // Sync object exposed so consumers (e.g. HeatmapController.Update)
    // can hold the same lock across Snapshot + Clear to avoid a race
    // where items added between the two calls are silently dropped.
    public object SyncRoot => _itemsSync;

    public double[][] Tail(int n)
    {
        lock (_itemsSync)
        {
            int start = Math.Max(0, _items.Count - n);
            int count = _items.Count - start;
            var result = new double[count][];
            for (int i = 0; i < count; i++)
                result[i] = (double[])_items[start + i].Clone();
            return result;
        }
    }
}

// ============================================================================
// Heatmap controller — ScottPlot plot lifecycle + scrolling logic
// ============================================================================

public class HeatmapController
{
    private readonly AvaPlot _plotControl;
    private readonly Plot _plot;
    private readonly DemoSensor _demo;

    private readonly int _depthCount;
    private readonly int _timeBins;
    private readonly double _windowSec;

    private readonly double _vmin;
    private readonly double _vmax;

    private double _elapsedSec;
    private readonly double _dt;

    private readonly double[,] _matrix;
    private int _matrixCol;
    private bool _matrixFull;

    private Heatmap? _heatmap;
    private readonly IColormap _colormap;

    private bool _followLatest = true;

    public HeatmapController(AvaPlot plotControl, DemoSensor demo)
    {
        _plotControl = plotControl;
        _plot = plotControl.Plot;
        _demo = demo;

        _depthCount = demo.DepthCount;
        _timeBins = 256;
        _windowSec = 20.0;
        _dt = 0.075;
        _vmin = demo.VelocityMin;
        _vmax = demo.VelocityMax;

        _matrix = new double[_depthCount, _timeBins];
        _matrixCol = 0;
        _matrixFull = false;
        _elapsedSec = 0.0;

        _colormap = BuildDivergingColormap();
        InitializePlot();
    }

    public void Update()
    {
        double[][] profiles;
        int count;
        lock (((CircularProfileBuffer)_demo.Buffer).SyncRoot)
        {
            var buffer = _demo.Buffer;
            profiles = buffer.Snapshot();
            buffer.Clear();
            count = profiles.Length;
        }

        if (count == 0) return;
        _elapsedSec += count * _dt;

        foreach (var profile in profiles)
            WriteProfile(profile);

        double tEnd = _elapsedSec;
        double tStart;

        if (_elapsedSec <= _windowSec)
        {
            tStart = 0.0;
        }
        else if (_followLatest)
        {
            tStart = _elapsedSec - _windowSec;
        }
        else
        {
            tStart = _plot.Axes.Bottom.Min;
            if (tStart < 0) tStart = 0;
            if (tStart > _elapsedSec - _windowSec)
                tStart = Math.Max(0, _elapsedSec - _windowSec);
        }

        _plot.XLabel("Time (s)");
        _plot.YLabel("Depth (m)");
        _plot.Title("Depth vs Time — Velocity (m/s)");

        // Y axis: depth increases downward (surface at top)
        _plot.Axes.SetLimitsY(_demo.DepthMin, _demo.DepthMax);
        _plot.Axes.Bottom.Min = tStart;
        _plot.Axes.Bottom.Max = tEnd;
        _plot.Axes.Left.Min = _demo.DepthMax;
        _plot.Axes.Left.Max = _demo.DepthMin;

        RenderHeatmap(tStart, tEnd);
        UpdateColorBar();
        _plotControl.Refresh();
    }

    public void SetFollowLatest(bool on) => _followLatest = on;

    public void JumpToLatest()
    {
        if (_elapsedSec <= 0) return;
        double tEnd = _elapsedSec;
        double tStart = Math.Max(0, tEnd - _windowSec);
        _plot.Axes.Bottom.Min = tStart;
        _plot.Axes.Bottom.Max = tEnd;
        _followLatest = true;
    }

    private void WriteProfile(double[] profile)
    {
        for (int row = 0; row < _depthCount; row++)
            _matrix[row, _matrixCol] = profile[row];

        _matrixCol++;
        if (_matrixCol >= _timeBins)
        {
            _matrixCol = 0;
            _matrixFull = true;
        }
    }

    private void RenderHeatmap(double tStart, double tEnd)
    {
        int visibleCols = (int)Math.Ceiling(_windowSec / _dt);
        visibleCols = Math.Min(visibleCols, _timeBins);

        var columns = new List<(int col, double time)>();

        if (!_matrixFull)
        {
            for (int k = 0; k < _matrixCol; k++)
            {
                double t = k * _dt;
                if (t >= tStart && t <= tEnd)
                    columns.Add((k, t));
            }
        }
        else
        {
            int newestCol = (_matrixCol - 1 + _timeBins) % _timeBins;
            double newestTime = _elapsedSec;

            for (int offset = 0; offset < _timeBins; offset++)
            {
                int col = (newestCol - offset + _timeBins) % _timeBins;
                double t = newestTime - offset * _dt;
                if (t < 0) break;
                if (t >= tStart && t <= tEnd)
                    columns.Add((col, t));
            }
        }

        if (columns.Count == 0)
        {
            _plot.Clear();
            _plot.XLabel("Time (s)");
            _plot.YLabel("Depth (m)");
            _plot.Title("Depth vs Time — Velocity (m/s)");
            _plot.Axes.SetLimitsY(_demo.DepthMin, _demo.DepthMax);
            _plot.Axes.Bottom.Min = tStart;
            _plot.Axes.Bottom.Max = tEnd;
            _plot.Axes.Left.Min = _demo.DepthMax;
            _plot.Axes.Left.Max = _demo.DepthMin;
            UpdateColorBar();
            return;
        }

        columns.Sort((a, b) => a.time.CompareTo(b.time));

        int width = columns.Count;
        int height = _depthCount;

        double[,] data = new double[height, width];
        for (int i = 0; i < width; i++)
        {
            int col = columns[i].col;
            for (int row = 0; row < height; row++)
                data[row, i] = _matrix[row, col];
        }

        if (_heatmap != null)
        {
            _plot.Remove(_heatmap);
            _heatmap = null;
        }

        _heatmap = _plot.Add.Heatmap(data);
        _heatmap.Colormap = _colormap;
        _heatmap.FlipVertically = false;
        _heatmap.Smooth = true;

        double cellWidth = (tEnd - tStart) / width;
        double cellHeight = (_demo.DepthMax - _demo.DepthMin) / height;

        _heatmap.Rectangle = new CoordinateRect(
            tStart,
            tStart + width * cellWidth,
            _demo.DepthMin,
            _demo.DepthMin + height * cellHeight
        );
    }

    private void UpdateColorBar()
    {
        // Remove existing ColorBar panels using GetPanels()
        var existingPanels = _plot.Axes.GetPanels();
        var toRemove = new List<IPanel>();
        foreach (var panel in existingPanels)
        {
            if (panel is ColorBar)
                toRemove.Add(panel);
        }
        foreach (var panel in toRemove)
            _plot.Axes.Remove(panel);

        // Add new colorbar from the heatmap's color axis
        if (_heatmap != null)
        {
            var cb = _plot.Add.ColorBar(_heatmap, Edge.Right);
            cb.MinimumSize = 20;
        }
    }

    private void InitializePlot()
    {
        _plot.Clear();
        _plot.Title("Depth vs Time — Velocity (m/s)");
        _plot.XLabel("Time (s)");
        _plot.YLabel("Depth (m)");
        _plot.Axes.SetLimitsY(_demo.DepthMin, _demo.DepthMax);
        _plot.Axes.Bottom.Min = 0;
        _plot.Axes.Bottom.Max = _windowSec;
        _plot.Axes.Left.Min = _demo.DepthMax;
        _plot.Axes.Left.Max = _demo.DepthMin;
        UpdateColorBar();
    }

    // ------------------------------------------------------------------
    // Diverging colormap: blue -> white -> red, symmetric around zero.
    // ------------------------------------------------------------------
    private static IColormap BuildDivergingColormap()
    {
        try
        {
            var builtin = GetBuiltInColormap("RdBu");
            if (builtin != null) return builtin;
        }
        catch { }

        try
        {
            var builtin = GetBuiltInColormap("BrBG");
            if (builtin != null) return builtin;
        }
        catch { }

        return new DivergingColormap();
    }

    private static IColormap? GetBuiltInColormap(string name)
    {
        var scottAsm = typeof(Plot).Assembly;

        foreach (var t in scottAsm.GetTypes())
        {
            if (!t.IsClass || t.IsAbstract) continue;
            if (t.Name.Equals("Colormaps", StringComparison.OrdinalIgnoreCase) ||
                t.Name.Equals("ColorMaps", StringComparison.OrdinalIgnoreCase))
            {
                var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                if (prop != null)
                {
                    var value = prop.GetValue(null);
                    if (value is IColormap cmap) return cmap;
                }
            }
        }

        foreach (var t in scottAsm.GetTypes())
        {
            if (!t.IsClass || t.IsAbstract) continue;
            if (t.Name.Equals("Colormaps", StringComparison.OrdinalIgnoreCase) ||
                t.Name.Equals("ColorMaps", StringComparison.OrdinalIgnoreCase))
            {
                var indexer = t.GetProperty("Item");
                if (indexer != null)
                {
                    try
                    {
                        var value = indexer.GetValue(null, new object[] { name });
                        if (value is IColormap cmap) return cmap;
                    }
                    catch { }
                }
            }
        }

        return null;
    }

    private sealed class DivergingColormap : IColormap
    {
        private static readonly ColorStop[] Stops = new[]
        {
            new ColorStop(new ScottPlot.Color(80,  80, 200), -2.0),
            new ColorStop(new ScottPlot.Color(120, 120, 255), -1.5),
            new ColorStop(new ScottPlot.Color(200, 200, 255), -0.75),
            new ColorStop(new ScottPlot.Color(255, 255, 255), 0.0),
            new ColorStop(new ScottPlot.Color(255, 200, 200), 0.75),
            new ColorStop(new ScottPlot.Color(255, 120, 120), 1.5),
            new ColorStop(new ScottPlot.Color(200, 80,  80),  2.0)
        };

        private readonly double _min;
        private readonly double _max;

        public DivergingColormap()
        {
            _min = Stops[0].Position;
            _max = Stops[Stops.Length - 1].Position;
        }

        public ScottPlot.Color GetColor(double value)
        {
            if (value <= _min) return Stops[0].Color;
            if (value >= _max) return Stops[Stops.Length - 1].Color;

            for (int i = 0; i < Stops.Length - 1; i++)
            {
                if (value >= Stops[i].Position && value <= Stops[i + 1].Position)
                {
                    double t = (value - Stops[i].Position) / (Stops[i + 1].Position - Stops[i].Position);
                    return Interpolate(Stops[i].Color, Stops[i + 1].Color, t);
                }
            }

            return Stops[Stops.Length - 1].Color;
        }

        private static ScottPlot.Color Interpolate(ScottPlot.Color a, ScottPlot.Color b, double t)
        {
            return new ScottPlot.Color(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t)
            );
        }

        public string Name => "DivergingBlueWhiteRed";
    }

    private sealed class ColorStop
    {
        public ScottPlot.Color Color { get; }
        public double Position { get; }

        public ColorStop(ScottPlot.Color color, double position)
        {
            Color = color;
            Position = position;
        }
    }
}