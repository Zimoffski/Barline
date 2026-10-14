using System.Diagnostics;
using Barline.Models;
using Forms = System.Windows.Forms;

namespace Barline.Services;

internal sealed class TrayIconController : IDisposable
{
    private readonly MainWindow _window;
    private readonly System.Windows.Application _application;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Forms.ToolStripMenuItem _togglePanelItem;
    private readonly Forms.ToolStripMenuItem _alwaysOnTopItem;
    private readonly Forms.ToolStripMenuItem _startupItem;
    private readonly Forms.ToolStripMenuItem _sensorAccessItem;
    private readonly Forms.ToolStripMenuItem _diagnosticsItem;

    private int _missingTemperatureSamples;
    private bool _temperatureHintShown;
    private bool _disposed;

    public TrayIconController(MainWindow window, System.Windows.Application application)
    {
        _window = window;
        _application = application;

        _statusItem = new Forms.ToolStripMenuItem(Strings.MenuRunning)
        {
            Enabled = false
        };

        _togglePanelItem = new Forms.ToolStripMenuItem();
        _togglePanelItem.Click += (_, _) => _window.SetPanelVisible(!_window.IsPanelShown);

        _alwaysOnTopItem = new Forms.ToolStripMenuItem(Strings.MenuAlwaysOnTop)
        {
            CheckOnClick = false
        };
        _alwaysOnTopItem.Click += (_, _) => ToggleAlwaysOnTop();

        _startupItem = new Forms.ToolStripMenuItem(Strings.MenuStartWithWindows)
        {
            CheckOnClick = false
        };
        _startupItem.Click += (_, _) => ToggleStartup();

        _sensorAccessItem = new Forms.ToolStripMenuItem();
        _sensorAccessItem.Click += (_, _) => RestartWithSensorAccess();

        _diagnosticsItem = new Forms.ToolStripMenuItem(Strings.MenuDiagnostics);
        _diagnosticsItem.Click += async (_, _) => await OpenDiagnosticsAsync();

        var exitItem = new Forms.ToolStripMenuItem(Strings.MenuExit);
        exitItem.Click += (_, _) => _application.Shutdown();

        _menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = true,
            RenderMode = Forms.ToolStripRenderMode.System
        };
        _menu.Items.AddRange([
            _statusItem,
            new Forms.ToolStripSeparator(),
            _togglePanelItem,
            _alwaysOnTopItem,
            _startupItem,
            new Forms.ToolStripSeparator(),
            _sensorAccessItem,
            _diagnosticsItem,
            new Forms.ToolStripSeparator(),
            exitItem
        ]);
        _menu.Opening += (_, _) => RefreshMenuState();

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = TrayIconFactory.Create(ThemeService.IsLightTaskbar()),
            Text = $"{AppConstants.ProductName} — {Strings.TrayTooltip}",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                _window.SetPanelVisible(!_window.IsPanelShown);
        };

        _window.SnapshotUpdated += OnSnapshotUpdated;
        _window.PanelVisibilityChanged += OnPanelVisibilityChanged;
        _window.AlwaysOnTopChanged += OnAlwaysOnTopChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        RefreshMenuState();
    }

    // The monochrome tray glyph follows the taskbar: white on dark, black on light.
    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category is Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.VisualStyle)
            RedrawTrayIcon();
    }

    // The tray icon size follows the DPI of the primary display.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => RedrawTrayIcon();

    private void RedrawTrayIcon()
    {
        if (_disposed)
            return;

        var previous = _notifyIcon.Icon;
        _notifyIcon.Icon = TrayIconFactory.Create(ThemeService.IsLightTaskbar());
        previous?.Dispose();
    }

    public void NotifyAlreadyRunning()
    {
        _window.SetPanelVisible(true);
        ShowBalloon(AppConstants.ProductName, Strings.AlreadyRunning);
    }

    private void OnSnapshotUpdated(object? sender, MetricsSnapshot snapshot)
    {
        _statusItem.Text =
            $"CPU {MetricsFormatter.Percent(snapshot.CpuLoadPercent)} {MetricsFormatter.Temperature(snapshot.CpuTemperatureC)}" +
            $"   GPU {MetricsFormatter.Percent(snapshot.GpuLoadPercent)} {MetricsFormatter.Temperature(snapshot.GpuTemperatureC)}" +
            $"   FPS {MetricsFormatter.FramesPerSecond(snapshot.ForegroundFramesPerSecond)}";

        var tooltip =
            $"CPU {MetricsFormatter.Percent(snapshot.CpuLoadPercent)} {MetricsFormatter.Temperature(snapshot.CpuTemperatureC)} · " +
            $"GPU {MetricsFormatter.Percent(snapshot.GpuLoadPercent)} {MetricsFormatter.Temperature(snapshot.GpuTemperatureC)}";
        _notifyIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];

        if (snapshot.CpuTemperatureC.HasValue || snapshot.GpuTemperatureC.HasValue)
        {
            _missingTemperatureSamples = 0;
            return;
        }

        _missingTemperatureSamples++;
        if (_missingTemperatureSamples >= 5 &&
            !_temperatureHintShown &&
            !ElevationService.IsAdministrator)
        {
            _temperatureHintShown = true;
            ShowBalloon(
                Strings.TemperatureUnavailableTitle,
                Strings.TemperatureUnavailableText);
        }
    }

    private void OnPanelVisibilityChanged(object? sender, EventArgs e) => RefreshMenuState();

    private void OnAlwaysOnTopChanged(object? sender, EventArgs e) => RefreshMenuState();

    private void RefreshMenuState()
    {
        _togglePanelItem.Text = _window.IsPanelShown ? Strings.MenuHidePanel : Strings.MenuShowPanel;
        _alwaysOnTopItem.Checked = _window.IsAlwaysOnTop;
        _startupItem.Text = StartupManager.UsesElevatedStartup()
            ? Strings.MenuStartWithWindowsElevated
            : Strings.MenuStartWithWindows;
        _startupItem.Checked = StartupManager.IsEnabled();

        var isAdministrator = ElevationService.IsAdministrator;
        _sensorAccessItem.Text = isAdministrator
            ? Strings.MenuSensorAccessGranted
            : Strings.MenuGetSensorAccess;
        _sensorAccessItem.Enabled = !isAdministrator;
    }

    private void ToggleStartup()
    {
        try
        {
            var enabled = !StartupManager.IsEnabled();
            var applied = StartupManager.SetEnabled(enabled);
            RefreshMenuState();

            if (!applied)
                ShowBalloon(AppConstants.ProductName, Strings.StartupNeedsAdministrator);
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
            ShowBalloon(AppConstants.ProductName, Strings.StartupFailed);
        }
    }

    private void ToggleAlwaysOnTop()
    {
        try
        {
            var enabled = !_window.IsAlwaysOnTop;
            UserSettings.AlwaysOnTop = enabled;
            _window.SetAlwaysOnTop(enabled);
            RefreshMenuState();
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
            ShowBalloon(AppConstants.ProductName, Strings.DisplayModeFailed);
        }
    }

    private void RestartWithSensorAccess()
    {
        if (ElevationService.IsAdministrator)
            return;

        if (ElevationService.TryStartElevatedReplacement())
            _application.Shutdown();
    }

    private async Task OpenDiagnosticsAsync()
    {
        _diagnosticsItem.Enabled = false;
        _diagnosticsItem.Text = Strings.MenuDiagnosticsRunning;

        try
        {
            var path = await _window.CreateDiagnosticsFileAsync();

            // Explorer hands the file to the already running (unelevated) shell, so the
            // editor does not inherit administrator rights from the monitor.
            var explorer = new ProcessStartInfo
            {
                FileName = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "explorer.exe"),
                UseShellExecute = false
            };
            explorer.ArgumentList.Add(path);
            Process.Start(explorer);
        }
        catch (Exception ex)
        {
            Logger.Write(ex);
            ShowBalloon(AppConstants.ProductName, Strings.DiagnosticsFailed);
        }
        finally
        {
            _diagnosticsItem.Text = Strings.MenuDiagnostics;
            _diagnosticsItem.Enabled = true;
        }
    }

    private void ShowBalloon(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _window.SnapshotUpdated -= OnSnapshotUpdated;
        _window.PanelVisibilityChanged -= OnPanelVisibilityChanged;
        _window.AlwaysOnTopChanged -= OnAlwaysOnTopChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
        _menu.Dispose();
    }

}
