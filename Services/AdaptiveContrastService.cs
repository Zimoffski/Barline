using System.Runtime.InteropServices;
using Barline.Native;

namespace Barline.Services;

internal enum ForegroundTone
{
    Light,
    Dark
}

/// <summary>
/// Chooses white or black text for whatever is currently behind the panel.
/// </summary>
internal sealed class AdaptiveContrastService : IDisposable
{
    // A tone switches only when the other one reads clearly better, so a background
    // close to mid-grey does not make the text blink between white and black.
    private const double SwitchContrastGain = 1.25;

    private readonly ScreenStripSampler _sampler = new();
    private ForegroundTone? _current;

    public ForegroundTone? Sample(NativeMethods.Rect region)
    {
        if (!_sampler.TryGetLuminanceRange(region, out var darkest, out var brightest))
            return null;

        // WCAG contrast of white text against the brightest point and of black text
        // against the darkest point: the tone with the better worst case wins.
        var whiteContrast = 1.05 / (brightest + 0.05);
        var blackContrast = (darkest + 0.05) / 0.05;
        var preferred = whiteContrast >= blackContrast ? ForegroundTone.Light : ForegroundTone.Dark;

        if (_current is { } current && preferred != current)
        {
            var currentContrast = current == ForegroundTone.Light ? whiteContrast : blackContrast;
            var preferredContrast = preferred == ForegroundTone.Light ? whiteContrast : blackContrast;
            if (preferredContrast < currentContrast * SwitchContrastGain)
                return current;
        }

        _current = preferred;
        return preferred;
    }

    public void Reset() => _current = null;

    public void Dispose() => _sampler.Dispose();
}

/// <summary>
/// Copies the screen area under the panel with one BitBlt and reads a grid of points.
/// DWM captures the panel's own text too, so the points sit in the empty bands above and
/// below the glyphs; sampling a glyph would make the text react to itself and blink.
/// </summary>
internal sealed class ScreenStripSampler : IDisposable
{
    private const int Columns = 9;
    private static readonly double[] RowFractions = [0.12, 0.88];
    private static readonly double[] LinearChannel = CreateLinearTable();

    private nint _memoryDc;
    private nint _bitmap;
    private nint _previousBitmap;
    private nint _bits;
    private int _width;
    private int _height;

    public bool TryGetLuminanceRange(NativeMethods.Rect region, out double darkest, out double brightest)
    {
        darkest = 1;
        brightest = 0;

        if (region.Width < Columns || region.Height < 4 || !EnsureBuffer(region.Width, region.Height))
            return false;

        var screenDc = NativeMethods.GetDC(nint.Zero);
        if (screenDc == nint.Zero)
            return false;

        try
        {
            if (!NativeMethods.BitBlt(
                    _memoryDc, 0, 0, _width, _height,
                    screenDc, region.Left, region.Top,
                    NativeMethods.SrcCopy))
            {
                return false;
            }
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(nint.Zero, screenDc);
        }

        NativeMethods.GdiFlush();

        foreach (var rowFraction in RowFractions)
        {
            var y = Math.Clamp((int)(_height * rowFraction), 0, _height - 1);
            for (var column = 0; column < Columns; column++)
            {
                var x = (int)((column + 0.5) * _width / Columns);
                var pixel = Marshal.ReadInt32(_bits, (y * _width + x) * 4);
                var luminance =
                    0.2126 * LinearChannel[(pixel >> 16) & 0xFF] +
                    0.7152 * LinearChannel[(pixel >> 8) & 0xFF] +
                    0.0722 * LinearChannel[pixel & 0xFF];

                darkest = Math.Min(darkest, luminance);
                brightest = Math.Max(brightest, luminance);
            }
        }

        return true;
    }

    public void Dispose() => ReleaseBuffer();

    private bool EnsureBuffer(int width, int height)
    {
        if (_memoryDc != nint.Zero && width == _width && height == _height)
            return true;

        ReleaseBuffer();

        _memoryDc = NativeMethods.CreateCompatibleDC(nint.Zero);
        if (_memoryDc == nint.Zero)
            return false;

        var header = new NativeMethods.BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
            Width = width,
            Height = -height, // Top-down rows.
            Planes = 1,
            BitCount = 32
        };

        _bitmap = NativeMethods.CreateDIBSection(_memoryDc, ref header, 0, out _bits, nint.Zero, 0);
        if (_bitmap == nint.Zero || _bits == nint.Zero)
        {
            ReleaseBuffer();
            return false;
        }

        _previousBitmap = NativeMethods.SelectObject(_memoryDc, _bitmap);
        _width = width;
        _height = height;
        return true;
    }

    private void ReleaseBuffer()
    {
        if (_memoryDc != nint.Zero && _previousBitmap != nint.Zero)
            NativeMethods.SelectObject(_memoryDc, _previousBitmap);
        if (_bitmap != nint.Zero)
            NativeMethods.DeleteObject(_bitmap);
        if (_memoryDc != nint.Zero)
            NativeMethods.DeleteDC(_memoryDc);

        _memoryDc = _bitmap = _previousBitmap = _bits = nint.Zero;
        _width = _height = 0;
    }

    private static double[] CreateLinearTable()
    {
        var table = new double[256];
        for (var value = 0; value < table.Length; value++)
        {
            var channel = value / 255.0;
            table[value] = channel <= 0.04045
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        return table;
    }
}
