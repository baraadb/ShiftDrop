using Microsoft.Win32;

namespace ShiftDrop.UI;

/// <summary>Colours as ARGB, following the Windows light/dark app setting.</summary>
public sealed record Theme(
    uint Plate, uint Shadow, uint Petal, uint PetalText, uint PetalLine,
    uint Accent, uint AccentText, uint Hub, uint HubText, uint HubSub,
    uint Card, uint CardLine, uint CardText, uint CardSub, uint Track, uint Danger, uint Success)
{
    public static readonly Theme Light = new(
        Plate: 0xF2E9EDF3, Shadow: 0x14000000, Petal: 0xFFFFFFFF, PetalText: 0xFF1F2937, PetalLine: 0x14000000,
        Accent: 0xFF2563EB, AccentText: 0xFFFFFFFF, Hub: 0xFFFFFFFF, HubText: 0xFF0B1220, HubSub: 0xFF64748B,
        Card: 0xFAFFFFFF, CardLine: 0x1A0B1220, CardText: 0xFF0B1220, CardSub: 0xFF64748B, Track: 0xFFE2E8F0,
        Danger: 0xFFDC2626, Success: 0xFF16A34A);

    public static readonly Theme Dark = new(
        Plate: 0xF0161B26, Shadow: 0x30000000, Petal: 0xFF262D3B, PetalText: 0xFFE5E7EB, PetalLine: 0x18FFFFFF,
        Accent: 0xFF3B82F6, AccentText: 0xFFFFFFFF, Hub: 0xFF0F141D, HubText: 0xFFF8FAFC, HubSub: 0xFF94A3B8,
        Card: 0xF51A202B, CardLine: 0x22FFFFFF, CardText: 0xFFF1F5F9, CardSub: 0xFF94A3B8, Track: 0xFF334155,
        Danger: 0xFFF87171, Success: 0xFF4ADE80);

    /// <summary>"system", "light" or "dark", from the settings.</summary>
    public static string Override { get; set; } = "system";

    static Theme _cached = Light;
    static long _readAt = -100_000;

    /// <summary>Follows the Windows setting; re-read at most every few seconds.</summary>
    public static Theme Current
    {
        get
        {
            if (Override == "light") return Light;
            if (Override == "dark") return Dark;
            if (Environment.TickCount64 - _readAt < 3000) return _cached;
            _readAt = Environment.TickCount64;
            _cached = Light;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int v && v == 0) _cached = Dark;
            }
            catch { }
            return _cached;
        }
    }
}
