using Microsoft.AspNetCore.Components;

namespace Harp.Toolkit.Web;

/// <summary>
/// Inline SVG icons (Lucide, ISC license) so the page needs no icon font.
/// </summary>
static class Icons
{
    static MarkupString Svg(string body) => new(
        $"<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" " +
        $"stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{body}</svg>");

    public static readonly MarkupString Open = Svg(
        "<path d=\"m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2\"/>");

    public static readonly MarkupString Download = Svg(
        "<path d=\"M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4\"/><polyline points=\"7 10 12 15 17 10\"/><line x1=\"12\" x2=\"12\" y1=\"15\" y2=\"3\"/>");

    public static readonly MarkupString Copy = Svg(
        "<rect width=\"14\" height=\"14\" x=\"8\" y=\"8\" rx=\"2\" ry=\"2\"/><path d=\"M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2\"/>");

    public static readonly MarkupString Check = Svg(
        "<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"m9 12 2 2 4-4\"/>");

    public static readonly MarkupString Error = Svg(
        "<circle cx=\"12\" cy=\"12\" r=\"10\"/><line x1=\"12\" x2=\"12\" y1=\"8\" y2=\"12\"/><line x1=\"12\" x2=\"12.01\" y1=\"16\" y2=\"16\"/>");

    public static readonly MarkupString Warning = Svg(
        "<path d=\"m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3\"/><line x1=\"12\" x2=\"12\" y1=\"9\" y2=\"13\"/><line x1=\"12\" x2=\"12.01\" y1=\"17\" y2=\"17\"/>");

    public static readonly MarkupString Badge = Svg(
        "<path d=\"M3.85 8.62a4 4 0 0 1 4.78-4.77 4 4 0 0 1 6.74 0 4 4 0 0 1 4.78 4.78 4 4 0 0 1 0 6.74 4 4 0 0 1-4.77 4.78 4 4 0 0 1-6.75 0 4 4 0 0 1-4.78-4.77 4 4 0 0 1 0-6.76Z\"/><path d=\"m9 12 2 2 4-4\"/>");

    public static readonly MarkupString Theme = Svg(
        "<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"M12 2a10 10 0 0 0 0 20z\" fill=\"currentColor\"/>");

    public static readonly MarkupString Code = Svg(
        "<polyline points=\"16 18 22 12 16 6\"/><polyline points=\"8 6 2 12 8 18\"/>");

    public static readonly MarkupString Plug = Svg(
        "<path d=\"M12 22v-5\"/><path d=\"M9 8V2\"/><path d=\"M15 8V2\"/><path d=\"M18 8v5a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V8Z\"/>");

    public static readonly MarkupString Chip = Svg(
        "<rect width=\"16\" height=\"16\" x=\"4\" y=\"4\" rx=\"2\"/><rect width=\"6\" height=\"6\" x=\"9\" y=\"9\" rx=\"1\"/><path d=\"M15 2v2\"/><path d=\"M15 20v2\"/><path d=\"M2 15h2\"/><path d=\"M2 9h2\"/><path d=\"M20 15h2\"/><path d=\"M20 9h2\"/><path d=\"M9 2v2\"/><path d=\"M9 20v2\"/>");

    public static readonly MarkupString Upload = Svg(
        "<path d=\"M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4\"/><polyline points=\"17 8 12 3 7 8\"/><line x1=\"12\" x2=\"12\" y1=\"3\" y2=\"15\"/>");

    public static readonly MarkupString GitHub = Svg(
        "<path d=\"M15 22v-4a4.8 4.8 0 0 0-1-3.5c3 0 6-2 6-5.5.08-1.25-.27-2.48-1-3.5.28-1.15.28-2.35 0-3.5 0 0-1 0-3 1.5-2.64-.5-5.36-.5-8 0C6 2 5 2 5 2c-.3 1.15-.3 2.35 0 3.5A5.403 5.403 0 0 0 4 9c0 3.5 3 5.5 6 5.5-.39.49-.68 1.05-.85 1.65-.17.6-.22 1.23-.15 1.85v4\"/><path d=\"M9 18c-4.51 2-5-2-7-2\"/>");
}
