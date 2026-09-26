using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static EditSharpGUI.Scripts.UI.ContextMenu.Platform.Windows.Win32;

namespace EditSharpGUI.Scripts.UI.ContextMenu.Platform.Windows;

// measures and paints owner-drawn items with the system's own menu theme parts, so checks, bullets,
// arrows and highlights are the same anti-aliased images windows draws in its classic menus.
// light mode uses the MENU class; dark mode the class behind windows' dark menus.
// the popup frame, background and shadow stay the system's
sealed class MenuPainter : IDisposable
{
    public sealed class Item
    {
        public MenuItemKind Kind;
        public string Text = "", Hint;
        public bool Bold, Enabled = true, Checked, Radio;
        public nint Icon;
        public bool ColumnHasIcons;   // any item on the same level has an icon, so text lines up
    }

    // used only when the theme has no such colour, or themes are off
    static readonly uint DarkBackground = Rgb(0x2C, 0x2C, 0x2C);
    static readonly uint DarkText = Rgb(0xFF, 0xFF, 0xFF);
    static readonly uint DarkDisabledText = Rgb(0x6D, 0x6D, 0x6D);
    static readonly uint DarkHot = Rgb(0x41, 0x41, 0x41);
    static readonly uint DarkBorder = Rgb(0x45, 0x45, 0x45);
    static readonly uint LightBorder = Rgb(0xA0, 0xA0, 0xA0);

    readonly bool dark;
    readonly float scale;
    readonly nint theme;          // 0 with themes off
    readonly nint font, boldFont;
    readonly nint measureDc;
    readonly int textHeight;
    readonly List<Item> items = [];
    readonly Dictionary<(int part, int state, uint color), (nint bitmap, SIZE size)> glyphs = [];
    uint? background;             // light: the popup background as the system painted it, sampled on first draw

    public readonly int IconSize;

    // the brush a dark popup is filled with: the system's own dark menu background does not
    // survive owner-drawn items, so the menu gets it explicitly. 0 in light mode
    public readonly nint BackgroundBrush;

    // for the dwm border on windows 11
    public readonly uint BorderColor;

    public MenuPainter(nint hwnd, uint dpi, bool dark)
    {
        this.dark = dark;
        scale = dpi / 96f;
        IconSize = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
        theme = OpenThemeData(hwnd, dark ? "DarkMode_ImmersiveStart::Menu" : "MENU");

        if (dark)
        {
            background = ThemeColor(MENU_POPUPBACKGROUND, 0, TMT_FILLCOLOR, DarkBackground);
            BackgroundBrush = CreateSolidBrush(background.Value);
            BorderColor = ThemeColor(MENU_POPUPBORDERS, 0, TMT_FILLCOLOR, DarkBorder);
        }
        else
        {
            BorderColor = LightBorder;
        }

        font = MenuFont(dpi, bold: false);
        boldFont = MenuFont(dpi, bold: true);

        measureDc = CreateCompatibleDC(0);
        textHeight = TextSize("Xg", bold: false).cy;
    }

    public void Dispose()
    {
        DeleteDC(measureDc);
        DeleteObject(font);
        DeleteObject(boldFont);
        if (BackgroundBrush != 0) DeleteObject(BackgroundBrush);
        foreach ((nint bitmap, _) in glyphs.Values) DeleteObject(bitmap);
        if (theme != 0) CloseThemeData(theme);
    }

    // ---- the items the menu carries, by the item data win32 hands back ----

    public nuint Register(Item item)
    {
        items.Add(item);
        return (nuint)items.Count;   // 1-based, so 0 never matches
    }

    public bool TryGet(nuint data, out Item item)
    {
        bool ours = data >= 1 && data <= (nuint)items.Count;
        item = ours ? items[(int)data - 1] : null;
        return ours;
    }

    public void Clear() => items.Clear();

    // ---- metrics, at this dpi ----

    int Px(float at96) => (int)MathF.Round(at96 * scale);
    int ItemHeight => Math.Max(textHeight, IconSize) + Px(7);
    int SeparatorHeight => Px(9) | 1;   // odd, so the line has a centre row
    int CheckColumn => Px(28);
    int IconColumn => IconSize + Px(6);
    int HintGap => Px(28);
    int RightPad => Px(20);

    SIZE TextSize(string text, bool bold)
    {
        nint old = SelectObject(measureDc, bold ? boldFont : font);
        GetTextExtentPoint32W(measureDc, text, text.Length, out SIZE size);
        SelectObject(measureDc, old);
        return size;
    }

    public void Measure(Item item, out uint width, out uint height)
    {
        if (item.Kind == MenuItemKind.Separator)
        {
            width = (uint)Px(40);
            height = (uint)SeparatorHeight;
            return;
        }

        int w = CheckColumn + (item.ColumnHasIcons ? IconColumn : 0) + TextSize(item.Text, item.Bold).cx + RightPad;
        if (item.Hint is not null) w += HintGap + TextSize(item.Hint, bold: false).cx;
        width = (uint)w;
        height = (uint)ItemHeight;
    }

    // ---- painting ----

    public void Draw(Item item, in DRAWITEMSTRUCT d)
    {
        nint dc = d.hDC;
        RECT rc = d.rcItem;
        bool hot = (d.itemState & ODS_SELECTED) != 0;
        bool disabled = !item.Enabled || item.Kind == MenuItemKind.Label;
        int state = disabled ? (hot ? MPI_DISABLEDHOT : MPI_DISABLED) : (hot ? MPI_HOT : MPI_NORMAL);

        // the system paints the popup background once; every redraw of an item has to restore it
        if ((d.itemAction & ODA_DRAWENTIRE) != 0 && !hot && background is null) background = GetPixel(dc, rc.left, rc.top);
        Fill(dc, rc, background ?? GetSysColor(COLOR_MENU));

        if (item.Kind == MenuItemKind.Separator)
        {
            DrawSeparator(dc, rc);
            return;
        }

        if (hot && item.Kind != MenuItemKind.Label) DrawPart(dc, MENU_POPUPITEM, state, rc, () => Fill(dc, rc, dark ? DarkHot : GetSysColor(COLOR_HIGHLIGHT)));

        uint color = TextColor(state);
        nint old = SelectObject(dc, font);
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, color);

        if (item.Checked)
        {
            RECT gutter = new(rc.left, rc.top, rc.left + CheckColumn, rc.bottom);
            int mark = item.Radio ? (disabled ? MC_BULLETDISABLED : MC_BULLETNORMAL) : (disabled ? MC_CHECKMARKDISABLED : MC_CHECKMARKNORMAL);
            DrawGlyph(dc, MENU_POPUPCHECK, mark, gutter, color, () => DrawTextW(dc, item.Radio ? "•" : "✓", -1, ref gutter, DT_SINGLELINE | DT_VCENTER | DT_CENTER | DT_NOPREFIX));
        }

        if (item.Icon != 0)
        {
            int x = rc.left + CheckColumn;
            int y = rc.top + (rc.Height - IconSize) / 2;
            DrawIcon(dc, item.Icon, x, y, disabled);
        }

        int textLeft = rc.left + CheckColumn + (item.ColumnHasIcons ? IconColumn : 0);
        RECT text = new(textLeft, rc.top, rc.right - RightPad, rc.bottom);
        SelectObject(dc, item.Bold ? boldFont : font);
        DrawTextW(dc, item.Text, -1, ref text, DT_SINGLELINE | DT_VCENTER | DT_LEFT | DT_NOPREFIX);

        if (item.Hint is not null)
        {
            SelectObject(dc, font);
            DrawTextW(dc, item.Hint, -1, ref text, DT_SINGLELINE | DT_VCENTER | DT_RIGHT | DT_NOPREFIX);
        }

        if (item.Kind == MenuItemKind.Submenu)
        {
            RECT end = new(rc.right - RightPad, rc.top, rc.right, rc.bottom);
            SelectObject(dc, font);
            DrawGlyph(dc, MENU_POPUPSUBMENU, disabled ? MSM_DISABLED : MSM_NORMAL, end, color, () => DrawTextW(dc, "›", -1, ref end, DT_SINGLELINE | DT_VCENTER | DT_CENTER | DT_NOPREFIX));

            // the system paints its own arrow over that spot once this returns; clipping it out keeps ours
            ExcludeClipRect(dc, end.left, end.top, end.right, end.bottom);
        }

        SelectObject(dc, old);
    }

    // the theme's separator, placed so its line lands on the band's centre row
    void DrawSeparator(nint dc, RECT rc)
    {
        int centre = (rc.top + rc.bottom) / 2;
        if (theme == 0 || GetThemePartSize(theme, dc, MENU_POPUPSEPARATOR, 0, 0, TS_TRUE, out SIZE size) != 0)
        {
            Fill(dc, new RECT(rc.left + Px(1), centre, rc.right - Px(1), centre + 1), GetSysColor(COLOR_3DSHADOW));
            return;
        }

        separatorLine ??= SeparatorLineOffset(dc, size);
        RECT part = new(rc.left, centre - separatorLine.Value, rc.right, centre - separatorLine.Value + size.cy);
        DrawThemeBackground(theme, dc, MENU_POPUPSEPARATOR, 0, ref part, 0);
    }

    int? separatorLine;

    // where the line sits inside the separator image: the row with the most coverage
    int SeparatorLineOffset(nint dc, SIZE size)
    {
        int width = Math.Max(size.cx, 4);
        BITMAPINFOHEADER header = new()
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -size.cy,
            biPlanes = 1,
            biBitCount = 32,
        };
        nint bitmap = CreateDIBSection(0, ref header, DIB_RGB_COLORS, out nint bits, 0, 0);
        if (bitmap == 0) return size.cy / 2;

        nint memory = CreateCompatibleDC(dc);
        nint old = SelectObject(memory, bitmap);
        RECT rc = new(0, 0, width, size.cy);
        DrawThemeBackground(theme, memory, MENU_POPUPSEPARATOR, 0, ref rc, 0);
        GdiFlush();
        SelectObject(memory, old);
        DeleteDC(memory);

        byte[] pixels = new byte[width * size.cy * 4];
        Marshal.Copy(bits, pixels, 0, pixels.Length);
        DeleteObject(bitmap);

        int best = size.cy / 2, bestCoverage = -1;
        for (int y = 0; y < size.cy; y++)
        {
            int coverage = 0;
            for (int x = 0; x < width; x++) coverage += pixels[(y * width + x) * 4 + 3];
            if (coverage > bestCoverage) { bestCoverage = coverage; best = y; }
        }
        return best;
    }

    // a theme part over the rect, or the fallback when themes are off
    void DrawPart(nint dc, int part, int state, RECT rc, Action fallback)
    {
        if (theme == 0) { fallback(); return; }
        DrawThemeBackground(theme, dc, part, state, ref rc, 0);
    }

    // a check, bullet or arrow at its own size, centred in the rect. light mode draws the theme's image as is;
    // the dark theme class inherits the light images, so dark mode keeps their anti-aliased shape and
    // tints it with the text colour, as windows' own dark menus do
    void DrawGlyph(nint dc, int part, int state, RECT within, uint color, Action fallback)
    {
        if (theme == 0 || GetThemePartSize(theme, dc, part, state, 0, TS_TRUE, out SIZE size) != 0) { fallback(); return; }
        RECT rc = Centered(within, size.cx, size.cy);

        if (!dark)
        {
            DrawThemeBackground(theme, dc, part, state, ref rc, 0);
            return;
        }

        if (!glyphs.TryGetValue((part, state, color), out (nint bitmap, SIZE size) glyph))
        {
            glyph = (TintedPart(dc, part, state, size, color), size);
            glyphs[(part, state, color)] = glyph;
        }
        if (glyph.bitmap == 0) { fallback(); return; }

        nint source = CreateCompatibleDC(dc);
        nint old = SelectObject(source, glyph.bitmap);
        BLENDFUNCTION blend = new() { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        AlphaBlend(dc, rc.left, rc.top, size.cx, size.cy, source, 0, 0, size.cx, size.cy, blend);
        SelectObject(source, old);
        DeleteDC(source);
    }

    // the part drawn into a fresh premultiplied bitmap, then recoloured: coverage from its alpha, colour ours
    nint TintedPart(nint dc, int part, int state, SIZE size, uint color)
    {
        BITMAPINFOHEADER header = new()
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = size.cx,
            biHeight = -size.cy,
            biPlanes = 1,
            biBitCount = 32,
        };
        nint bitmap = CreateDIBSection(0, ref header, DIB_RGB_COLORS, out nint bits, 0, 0);
        if (bitmap == 0) return 0;

        nint memory = CreateCompatibleDC(dc);
        nint old = SelectObject(memory, bitmap);
        RECT rc = new(0, 0, size.cx, size.cy);
        DrawThemeBackground(theme, memory, part, state, ref rc, 0);
        GdiFlush();
        SelectObject(memory, old);
        DeleteDC(memory);

        byte r = (byte)(color & 0xFF), g = (byte)(color >> 8 & 0xFF), b = (byte)(color >> 16 & 0xFF);
        byte[] pixels = new byte[size.cx * size.cy * 4];
        Marshal.Copy(bits, pixels, 0, pixels.Length);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int a = pixels[i + 3];
            pixels[i] = (byte)(b * a / 255);
            pixels[i + 1] = (byte)(g * a / 255);
            pixels[i + 2] = (byte)(r * a / 255);
        }
        Marshal.Copy(pixels, 0, bits, pixels.Length);
        return bitmap;
    }

    void DrawIcon(nint dc, nint bitmap, int x, int y, bool disabled)
    {
        nint source = CreateCompatibleDC(dc);
        nint old = SelectObject(source, bitmap);
        BLENDFUNCTION blend = new() { BlendOp = AC_SRC_OVER, SourceConstantAlpha = (byte)(disabled ? 128 : 255), AlphaFormat = AC_SRC_ALPHA };
        AlphaBlend(dc, x, y, IconSize, IconSize, source, 0, 0, IconSize, IconSize, blend);
        SelectObject(source, old);
        DeleteDC(source);
    }

    uint TextColor(int state)
    {
        bool disabled = state is MPI_DISABLED or MPI_DISABLEDHOT;
        uint fallback = dark ? (disabled ? DarkDisabledText : DarkText)
                             : GetSysColor(disabled ? COLOR_GRAYTEXT : state == MPI_HOT ? COLOR_HIGHLIGHTTEXT : COLOR_MENUTEXT);
        return ThemeColor(MENU_POPUPITEM, state, TMT_TEXTCOLOR, fallback);
    }

    uint ThemeColor(int part, int state, int property, uint fallback)
        => theme != 0 && GetThemeColor(theme, part, state, property, out uint color) == 0 ? color : fallback;

    static void Fill(nint dc, RECT rc, uint color)
    {
        nint brush = CreateSolidBrush(color);
        FillRect(dc, ref rc, brush);
        DeleteObject(brush);
    }

    static RECT Centered(RECT within, int width, int height)
    {
        int left = within.left + (within.Width - width) / 2;
        int top = within.top + (within.Height - height) / 2;
        return new RECT(left, top, left + width, top + height);
    }

    // the user's menu font at this dpi
    static nint MenuFont(uint dpi, bool bold)
    {
        NONCLIENTMETRICSW metrics = new() { cbSize = (uint)Marshal.SizeOf<NONCLIENTMETRICSW>() };
        LOGFONTW lf = SystemParametersInfoForDpi(SPI_GETNONCLIENTMETRICS, metrics.cbSize, ref metrics, 0, dpi)
            ? metrics.lfMenuFont
            : new() { lfHeight = -(int)MathF.Round(9f * dpi / 72f), lfFaceName = "Segoe UI" };
        if (bold) lf.lfWeight = 700;
        lf.lfQuality = CLEARTYPE_QUALITY;
        return CreateFontIndirectW(ref lf);
    }
}
