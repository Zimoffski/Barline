using System.Globalization;

namespace Barline.Services;

/// <summary>
/// User-facing texts of the tray menu and notifications. Russian for a Russian Windows UI,
/// English everywhere else. The diagnostics report is always English, because it is meant
/// to be attached to bug reports.
/// </summary>
internal static class Strings
{
    private static readonly bool Russian =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

    private static string T(string english, string russian) => Russian ? russian : english;

    public static string TrayTooltip => T("system monitor", "системный монитор");
    public static string MenuRunning => T("System monitor is running", "Системный монитор работает");
    public static string MenuShowPanel => T("Show panel", "Показать панель");
    public static string MenuHidePanel => T("Hide panel", "Скрыть панель");
    public static string MenuAlwaysOnTop => T("Show above all windows", "Показывать поверх всех окон");
    public static string MenuStartWithWindows => T("Start with Windows", "Запускать вместе с Windows");
    public static string MenuStartWithWindowsElevated =>
        T("Start with Windows (sensor access)", "Запускать с Windows (доступ к датчикам)");
    public static string MenuGetSensorAccess =>
        T("Get access to temperature and FPS…", "Получить доступ к температуре и FPS…");
    public static string MenuSensorAccessGranted => T("Sensor access: granted", "Доступ к датчикам: получен");
    public static string MenuDiagnostics => T("Sensor diagnostics…", "Диагностика датчиков…");
    public static string MenuDiagnosticsRunning => T("Collecting diagnostics…", "Собираю диагностику…");
    public static string MenuExit => T("Exit", "Выход");

    public static string AlreadyRunning =>
        T("Barline is already running and lives in the system tray.",
          "Приложение уже работает и находится в системном трее.");
    public static string TemperatureUnavailableTitle =>
        T("Temperature is not available yet", "Температура пока недоступна");
    public static string TemperatureUnavailableText =>
        T($"Open the {AppConstants.ProductName} menu and choose “{MenuGetSensorAccess}”.",
          $"Откройте меню {AppConstants.ProductName} и выберите «{MenuGetSensorAccess}».");
    public static string StartupNeedsAdministrator =>
        T("Autostart with sensor access cannot be turned off without administrator rights.",
          "Автозапуск с доступом к датчикам нельзя отключить без прав администратора.");
    public static string StartupFailed =>
        T("Could not change autostart. Details are in error.log.",
          "Не удалось изменить автозапуск. Подробности записаны в error.log.");
    public static string DisplayModeFailed =>
        T("Could not change the display mode.", "Не удалось изменить режим отображения.");
    public static string DiagnosticsFailed =>
        T("Could not create the sensor diagnostics.", "Не удалось создать диагностику датчиков.");
}
