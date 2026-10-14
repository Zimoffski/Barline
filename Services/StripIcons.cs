namespace Barline.Services;

/// <summary>
/// Minimal filled icons on a 16×16 grid. Edges sit on whole pixels so the shapes stay
/// crisp at 100% scale; holes use the even-odd fill rule of the path markup.
/// Every icon has an even pixel height centred on y = 9, the optical centre of the digits
/// (cap height 8 px, centred one pixel below the middle of the box), so icons and numbers
/// share one horizontal axis.
/// </summary>
internal static class StripIcons
{
    public const double Size = 16;

    // Processor, rows 3–14: solid package with two pins on every side.
    public const string Cpu =
        "M5,5 H11 A1,1 0 0 1 12,6 V12 A1,1 0 0 1 11,13 H5 A1,1 0 0 1 4,12 V6 A1,1 0 0 1 5,5 Z " +
        "M6,3 H7 V5 H6 Z M9,3 H10 V5 H9 Z M6,13 H7 V15 H6 Z M9,13 H10 V15 H9 Z " +
        "M2,7 H4 V8 H2 Z M2,10 H4 V11 H2 Z M12,7 H14 V8 H12 Z M12,10 H14 V11 H12 Z";

    // Graphics card, rows 5–12 (same as the digits): solid board, fan opening, connector tab.
    public const string Gpu =
        "M2,5 H14 A1,1 0 0 1 15,6 V11 A1,1 0 0 1 14,12 H2 A1,1 0 0 1 1,11 V6 A1,1 0 0 1 2,5 Z " +
        "M8,8.5 A2,2 0 1 0 4,8.5 A2,2 0 1 0 8,8.5 Z " +
        "M3,12 H9 V13 H3 Z";

    // Memory module, rows 5–12: solid stick and contacts split by the key notch.
    public const string Memory =
        "M2,5 H14 A1,1 0 0 1 15,6 V9 A1,1 0 0 1 14,10 H2 A1,1 0 0 1 1,9 V6 A1,1 0 0 1 2,5 Z " +
        "M2,11 H7 V13 H2 Z M9,11 H14 V13 H9 Z";

    // Frame rate, rows 4–13: solid speedometer dome with the needle cut out.
    public const string FrameRate =
        "M2.4,14 A6.5,6.5 0 1 1 13.6,14 Z " +
        "M7.3,11.4 L11.6,6.7 L8.7,12.6 Z";

    // Network traffic, rows 4–13: solid arrows.
    public const string Download = "M7,4 H9 V10.2 L11.6,7.6 L13,9 L8,14 L3,9 L4.4,7.6 L7,10.2 Z";
    public const string Upload = "M7,14 H9 V7.8 L11.6,10.4 L13,9 L8,4 L3,9 L4.4,10.4 L7,7.8 Z";
}
