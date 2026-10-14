using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Barline.Models;
using Barline.Native;
using Barline.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Path = System.Windows.Shapes.Path;
using Size = System.Windows.Size;

namespace Barline;

internal sealed class MainWindow : Window
{
    private const double FontSizeDip = 12.0;
    private const double GapBeforeTrayDip = 8.0;
    private const double FallbackRightReserveDip = 225.0;
    private const double GroupGapDip = 16.0;
    private const double IconGapDip = 5.0;
    private const double NetworkPairGapDip = 10.0;
    private static readonly TimeSpan FullscreenExitSettleDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan ContrastSampleInterval = TimeSpan.FromMilliseconds(66);
    // The font of the Windows 11 clock and tray. It is a system font and is only referenced,
    // never redistributed; Windows 10 has no Segoe UI Variable and falls back to Segoe UI.
    // WPF exposes only the Regular and SemiBold instances of the variable font.
    private static readonly FontFamily UiFamily = new("Segoe UI Variable Small, Segoe UI");
    private static readonly FontWeight ValueWeight = FontWeights.SemiBold;

    private readonly StackPanel _metricsPanel;
    private readonly Border _surface;
    private readonly TextBlock _cpuLoad;
    private readonly TextBlock _cpuTemperature;
    private readonly TextBlock _gpuLoad;
    private readonly TextBlock _gpuTemperature;
    private readonly TextBlock _memoryLoad;
    private readonly TextBlock _fpsValue;
    private readonly TextBlock _downloadRate;
    private readonly TextBlock _uploadRate;
    private readonly List<TextBlock> _valueBlocks = [];
    private readonly List<TextBlock> _slashBlocks = [];
    private readonly List<Path> _icons = [];
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _fpsTimer;
    private readonly DispatcherTimer _layoutTimer;
    private readonly DispatcherTimer _contrastTimer;
    private readonly MetricsService _metrics = new();
    private readonly AdaptiveContrastService _adaptiveContrast = new();
    private readonly SolidColorBrush _primaryBrush = new();
    private readonly SolidColorBrush _secondaryBrush = new();
    private readonly SolidColorBrush _tertiaryBrush = new();
    private readonly TaskbarZOrderWatcher _taskbarZOrderWatcher;

    private nint _windowHandle;
    private nint _taskbarOwner = (nint)(-1);
    private NativeMethods.Rect? _lastStablePlacement;
    private DateTime _fullscreenExitSettleUntilUtc;
    private bool _refreshInProgress;
    private bool _fpsRefreshInProgress;
    private bool _hiddenForFullscreen;
    private double? _cpuTemperatureC;
    private double? _gpuTemperatureC;
    private ForegroundTone _adaptiveTone;
    private bool _panelRequestedVisible = true;
    private bool _alwaysOnTop;
    private bool _wasFullscreen;
    private bool _adaptiveContrastApplied;
    private int _zOrderRestoreQueued;
    private TaskbarPalette _taskbarPalette = ThemeService.GetTaskbarPalette();

    public event EventHandler<MetricsSnapshot>? SnapshotUpdated;
    public event EventHandler? PanelVisibilityChanged;
    public event EventHandler? AlwaysOnTopChanged;

    public bool IsPanelShown => _panelRequestedVisible;
    public bool IsAlwaysOnTop => _alwaysOnTop;

    public MainWindow(bool alwaysOnTop)
    {
        _alwaysOnTop = alwaysOnTop;
        _taskbarZOrderWatcher = new TaskbarZOrderWatcher(OnTaskbarLayerChanged);

        Title = AppConstants.WindowTitle;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        Left = 0;
        Top = 0;
        Width = 500;
        Height = 48;
        UseLayoutRounding = true;

        _metricsPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };

        // Icons replace the text labels; the values carry the visual weight and the
        // temperatures stay secondary until they become a warning. Every icon and cell has a
        // fixed position and width: nothing moves or overlaps whatever the values are.
        var percentWidth = MeasureCellWidth("100%", ValueWeight);
        var temperatureWidth = MeasureCellWidth("100\u00B0", FontWeights.Normal);
        var rateWidth = MeasureCellWidth("9999", ValueWeight);
        var fpsWidth = MeasureCellWidth("999", ValueWeight);

        AddIcon(StripIcons.Cpu);
        _cpuLoad = AddValue(percentWidth, TextAlignment.Right);
        AddSlash();
        _cpuTemperature = AddTemperature(temperatureWidth);
        AddGap(GroupGapDip);

        AddIcon(StripIcons.Gpu);
        _gpuLoad = AddValue(percentWidth, TextAlignment.Right);
        AddSlash();
        _gpuTemperature = AddTemperature(temperatureWidth);
        AddGap(GroupGapDip);

        AddIcon(StripIcons.Memory);
        _memoryLoad = AddValue(percentWidth, TextAlignment.Right);
        AddGap(GroupGapDip);

        // Megabits per second without a unit label; download first.
        AddIcon(StripIcons.Download, rightMargin: 2);
        _downloadRate = AddValue(rateWidth, TextAlignment.Left);
        AddGap(NetworkPairGapDip);
        AddIcon(StripIcons.Upload, rightMargin: 2);
        _uploadRate = AddValue(rateWidth, TextAlignment.Left);
        AddGap(GroupGapDip);

        // FPS grows from its left edge.
        AddIcon(StripIcons.FrameRate);
        _fpsValue = AddValue(fpsWidth, TextAlignment.Left);

        _surface = new Border
        {
            Child = _metricsPanel,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };

        ApplyTheme();

        Content = _surface;

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        _fpsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _fpsTimer.Tick += async (_, _) => await RefreshFramesPerSecondAsync();

        _layoutTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _layoutTimer.Tick += (_, _) => MaintainPanelPosition();

        // Contrast is sampled only while "always on top" is enabled; 15 checks per second
        // are visually instant and far cheaper than reading the screen on every frame.
        _contrastTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = ContrastSampleInterval
        };
        _contrastTimer.Tick += (_, _) => UpdateAdaptiveContrast();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public void SetPanelVisible(bool visible)
    {
        if (_panelRequestedVisible == visible)
            return;

        _panelRequestedVisible = visible;

        if (_windowHandle != nint.Zero)
        {
            if (visible)
                MaintainPanelPosition();
            else
                NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwHide);

            UpdateContrastSampling();
        }

        PanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAlwaysOnTop(bool enabled)
    {
        if (_alwaysOnTop == enabled)
            return;

        _alwaysOnTop = enabled;

        if (_windowHandle != nint.Zero && _panelRequestedVisible)
            MaintainPanelPosition();

        UpdateContrastSampling();
        AlwaysOnTopChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string> CreateDiagnosticsFileAsync()
    {
        var report = await Task.Run(_metrics.CreateHardwareDiagnostics);
        return Logger.WriteDiagnostics(report);
    }

    private TextBlock AddText(string text, FontWeight weight, double width, TextAlignment alignment)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            Width = width,
            FontFamily = UiFamily,
            FontSize = FontSizeDip,
            FontWeight = weight,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = alignment,
            TextWrapping = TextWrapping.NoWrap,
            // A value can never spill into its neighbour, even with an unexpected font.
            ClipToBounds = !double.IsNaN(width),
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };

        Typography.SetNumeralStyle(textBlock, FontNumeralStyle.Lining);
        Typography.SetNumeralAlignment(textBlock, FontNumeralAlignment.Tabular);
        TextOptions.SetTextFormattingMode(textBlock, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(textBlock, TextRenderingMode.Auto);

        _metricsPanel.Children.Add(textBlock);
        return textBlock;
    }

    private TextBlock AddValue(double width, TextAlignment alignment)
    {
        var textBlock = AddText(MetricsFormatter.Missing, ValueWeight, width, alignment);
        _valueBlocks.Add(textBlock);
        return textBlock;
    }

    private TextBlock AddTemperature(double width) =>
        AddText(MetricsFormatter.Missing, FontWeights.Normal, width, TextAlignment.Left);

    private void AddSlash()
    {
        _slashBlocks.Add(AddText("/", FontWeights.Normal, 9, TextAlignment.Center));
    }

    private void AddIcon(string geometry, double rightMargin = IconGapDip)
    {
        var icon = new Path
        {
            Data = Geometry.Parse(geometry),
            Width = StripIcons.Size,
            Height = StripIcons.Size,
            Stretch = Stretch.None,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, rightMargin, 0),
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };

        _metricsPanel.Children.Add(icon);
        _icons.Add(icon);
    }

    private void AddGap(double width)
    {
        _metricsPanel.Children.Add(new Border
        {
            Width = width,
            IsHitTestVisible = false
        });
    }

    // Fixed cells sized by the widest value keep neighbours still while numbers change.
    private static double MeasureCellWidth(string widestText, FontWeight weight)
    {
        var text = new FormattedText(
            widestText,
            System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(UiFamily, FontStyles.Normal, weight, FontStretches.Normal),
            FontSizeDip,
            Brushes.White,
            pixelsPerDip: 1.0);

        return Math.Ceiling(text.WidthIncludingTrailingWhitespace) + 2;
    }

    private void SetValue(TextBlock textBlock, string text)
    {
        textBlock.Text = text;
        textBlock.Foreground = text == MetricsFormatter.Missing ? _secondaryBrush : _primaryBrush;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;

        var exStyle = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle).ToInt64();
        exStyle |= NativeMethods.WsExToolWindow |
                   NativeMethods.WsExNoActivate |
                   NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle, (nint)exStyle);

        if (HwndSource.FromHwnd(_windowHandle) is { } source)
            source.AddHook(WindowMessageHook);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        MaintainPanelPosition();
        _layoutTimer.Start();
        _refreshTimer.Start();
        _fpsTimer.Start();
        UpdateContrastSampling();
        await RefreshAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _layoutTimer.Stop();
        _refreshTimer.Stop();
        _fpsTimer.Stop();
        _contrastTimer.Stop();
        _taskbarZOrderWatcher.Dispose();
        _adaptiveContrast.Dispose();
        _metrics.Dispose();
    }

    private nint WindowMessageHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg is NativeMethods.WmDisplayChange or NativeMethods.WmSettingChange or NativeMethods.WmDpiChanged)
        {
            if (msg is NativeMethods.WmDisplayChange or NativeMethods.WmDpiChanged)
                _lastStablePlacement = null;

            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                ApplyTheme();
                MaintainPanelPosition();
            }));
        }

        return nint.Zero;
    }

    private async Task RefreshAsync()
    {
        if (_refreshInProgress)
            return;

        _refreshInProgress = true;
        try
        {
            var snapshot = await Task.Run(_metrics.ReadSnapshot);
            RenderSnapshot(snapshot);
            SnapshotUpdated?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private async Task RefreshFramesPerSecondAsync()
    {
        if (_fpsRefreshInProgress)
            return;

        _fpsRefreshInProgress = true;
        try
        {
            // Resolving the foreground process and starting PresentMon stay off the UI thread.
            var framesPerSecond = await Task.Run(_metrics.ReadForegroundFramesPerSecond);
            SetValue(_fpsValue, MetricsFormatter.FramesPerSecond(framesPerSecond));
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
        }
        finally
        {
            _fpsRefreshInProgress = false;
        }
    }

    private void RenderSnapshot(MetricsSnapshot snapshot)
    {
        _cpuTemperatureC = snapshot.CpuTemperatureC;
        _gpuTemperatureC = snapshot.GpuTemperatureC;
        SetValue(_cpuLoad, MetricsFormatter.Percent(snapshot.CpuLoadPercent));
        _cpuTemperature.Text = MetricsFormatter.Temperature(snapshot.CpuTemperatureC);
        SetValue(_gpuLoad, MetricsFormatter.Percent(snapshot.GpuLoadPercent));
        _gpuTemperature.Text = MetricsFormatter.Temperature(snapshot.GpuTemperatureC);
        SetValue(_memoryLoad, MetricsFormatter.Percent(snapshot.MemoryLoadPercent));
        SetValue(_downloadRate, MetricsFormatter.DataRate(snapshot.DownloadBytesPerSecond));
        SetValue(_uploadRate, MetricsFormatter.DataRate(snapshot.UploadBytesPerSecond));
        UpdateTemperatureBrushes();
    }

    private void ApplyTheme()
    {
        _taskbarPalette = ThemeService.GetTaskbarPalette();

        foreach (var textBlock in _valueBlocks)
            SetValue(textBlock, textBlock.Text);
        foreach (var slash in _slashBlocks)
            slash.Foreground = _tertiaryBrush;
        foreach (var icon in _icons)
            icon.Fill = _secondaryBrush;

        if (!_adaptiveContrastApplied)
        {
            SetForegroundColors(
                GetBrushColor(_taskbarPalette.Primary),
                GetBrushColor(_taskbarPalette.Secondary),
                GetBrushColor(_taskbarPalette.Separator));
        }

        UpdateTemperatureBrushes();
    }

    private void UpdateTemperatureBrushes()
    {
        _cpuTemperature.Foreground = GetTemperatureBrush(_cpuTemperatureC);
        _gpuTemperature.Foreground = GetTemperatureBrush(_gpuTemperatureC);
    }

    private Brush GetTemperatureBrush(double? temperature)
    {
        if (!temperature.HasValue)
            return _secondaryBrush;

        // Over arbitrary content the warning colours come from the palette made for that
        // background: white text means a dark background, black text a light one.
        var palette = !_adaptiveContrastApplied
            ? _taskbarPalette
            : _adaptiveTone == ForegroundTone.Light
                ? ThemeService.DarkTaskbar
                : ThemeService.LightTaskbar;

        if (temperature.Value >= 85)
            return palette.Danger;
        if (temperature.Value >= 70)
            return palette.Warning;
        return _secondaryBrush;
    }

    private void UpdateContrastSampling()
    {
        var sample = _alwaysOnTop && _panelRequestedVisible && _windowHandle != nint.Zero;
        if (sample)
        {
            if (!_contrastTimer.IsEnabled)
                _contrastTimer.Start();
            UpdateAdaptiveContrast();
            return;
        }

        _contrastTimer.Stop();
        UpdateAdaptiveContrast();
    }

    private void UpdateAdaptiveContrast()
    {
        var useAdaptiveContrast = _alwaysOnTop &&
                                  _panelRequestedVisible &&
                                  _lastStablePlacement.HasValue;

        if (!useAdaptiveContrast)
        {
            if (_adaptiveContrastApplied)
            {
                _adaptiveContrastApplied = false;
                _adaptiveContrast.Reset();
                ApplyTheme();
            }

            return;
        }

        var tone = _adaptiveContrast.Sample(_lastStablePlacement!.Value);
        if (!tone.HasValue)
            return;

        if (_adaptiveContrastApplied && _adaptiveTone == tone.Value)
            return;

        _adaptiveContrastApplied = true;
        _adaptiveTone = tone.Value;
        var color = tone.Value == ForegroundTone.Light ? Colors.White : Colors.Black;
        SetForegroundColors(color, color, color);
        UpdateTemperatureBrushes();
    }

    private void SetForegroundColors(
        Color primary,
        Color secondary,
        Color tertiary)
    {
        SetBrushColor(_primaryBrush, primary);
        SetBrushColor(_secondaryBrush, secondary);
        SetBrushColor(_tertiaryBrush, tertiary);
    }

    private static void SetBrushColor(SolidColorBrush brush, Color target)
    {
        if (brush.Color != target)
            brush.Color = target;
    }

    private static Color GetBrushColor(Brush brush)
    {
        return brush is SolidColorBrush solidBrush ? solidBrush.Color : Colors.White;
    }

    private void MaintainPanelPosition()
    {
        if (!_panelRequestedVisible || _windowHandle == nint.Zero)
            return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY;

        MaintainTaskbarPosition(scaleX, scaleY);
    }

    private void MaintainTaskbarPosition(double scaleX, double scaleY)
    {
        if (!TaskbarLocator.TryGetPrimaryTaskbar(out var taskbarHandle, out var taskbar))
            return;

        _taskbarZOrderWatcher.Watch(taskbarHandle);

        SetTaskbarOwner(taskbarHandle);

        var fullscreen = FullscreenDetector.IsForegroundFullscreenOnPrimaryMonitor(_windowHandle);
        var nowUtc = DateTime.UtcNow;

        if (fullscreen)
        {
            _wasFullscreen = true;

            // Without "always on top" the panel belongs to the taskbar layer, and the
            // taskbar itself goes under a fullscreen window.
            if (!_alwaysOnTop)
            {
                HideForFullscreen();
                return;
            }

            if (_lastStablePlacement is { } stablePlacement)
            {
                PlaceWindow(stablePlacement);
                return;
            }
        }
        else if (_wasFullscreen)
        {
            _wasFullscreen = false;
            _fullscreenExitSettleUntilUtc = nowUtc + FullscreenExitSettleDelay;
        }

        if (!fullscreen && nowUtc < _fullscreenExitSettleUntilUtc &&
            _lastStablePlacement is { } settlingPlacement)
        {
            PlaceWindow(settlingPlacement);
            return;
        }

        var heightDip = Math.Max(1, taskbar.Height / scaleY);
        _surface.Measure(new Size(double.PositiveInfinity, heightDip));

        var widthDip = Math.Ceiling(_surface.DesiredSize.Width) + 2;
        var widthPx = Math.Max(1, (int)Math.Ceiling(widthDip * scaleX));
        var heightPx = Math.Max(1, taskbar.Height);

        Width = widthDip;
        Height = heightDip;

        var gapPx = (int)Math.Round(GapBeforeTrayDip * scaleX);
        var fallbackReservePx = (int)Math.Round(FallbackRightReserveDip * scaleX);

        var rightAnchor = taskbar.Right - fallbackReservePx;
        if (TaskbarLocator.TryGetNotificationAreaRect(out var tray) &&
            tray.Left > taskbar.Left && tray.Left < taskbar.Right)
        {
            rightAnchor = tray.Left - gapPx;
        }

        var x = Math.Max(taskbar.Left + gapPx, rightAnchor - widthPx);
        var y = taskbar.Top;

        var placement = new NativeMethods.Rect
        {
            Left = x,
            Top = y,
            Right = x + widthPx,
            Bottom = y + heightPx
        };

        if (!fullscreen || !_lastStablePlacement.HasValue)
            _lastStablePlacement = placement;

        PlaceWindow(placement);
    }

    private void SetTaskbarOwner(nint owner)
    {
        if (_taskbarOwner == owner)
            return;

        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlHwndParent, owner);
        _taskbarOwner = owner;
    }

    private void HideForFullscreen()
    {
        if (_hiddenForFullscreen)
            return;

        _hiddenForFullscreen = true;
        NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwHide);
    }

    private void PlaceWindow(NativeMethods.Rect placement)
    {
        _hiddenForFullscreen = false;
        NativeMethods.SetWindowPos(
            _windowHandle,
            NativeMethods.HwndTopmost,
            placement.Left,
            placement.Top,
            placement.Width,
            placement.Height,
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpShowWindow |
            NativeMethods.SwpNoOwnerZOrder);
    }

    private void OnTaskbarLayerChanged()
    {
        if (Interlocked.Exchange(ref _zOrderRestoreQueued, 1) != 0)
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
        {
            Interlocked.Exchange(ref _zOrderRestoreQueued, 0);

            if (_panelRequestedVisible && !_hiddenForFullscreen && _lastStablePlacement is { } placement)
                PlaceWindow(placement);
        }));
    }
}
