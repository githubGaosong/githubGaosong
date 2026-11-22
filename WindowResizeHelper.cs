using System;
using System.Runtime.InteropServices;

internal static class WindowResizeHelper
{
    internal const int WmNcHitTest = 0x0084;

    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rect);

    internal static bool TryHitTest(IntPtr windowHandle, IntPtr lParam, out int hitTest)
    {
        hitTest = 0;
        if (!GetWindowRect(windowHandle, out NativeRect rect))
            return false;

        long packed = lParam.ToInt64();
        int x = unchecked((short)(packed & 0xffff));
        int y = unchecked((short)((packed >> 16) & 0xffff));

        int width = Math.Max(1, rect.Right - rect.Left);
        int height = Math.Max(1, rect.Bottom - rect.Top);
        int distanceLeft = x - rect.Left;
        int distanceRight = rect.Right - x;
        int distanceTop = y - rect.Top;
        int distanceBottom = rect.Bottom - y;

        // Keep edge hit zones narrow enough that a tiny 80x60 window still
        // has a usable center area and distinct left/right and top/bottom edges.
        int edge = Math.Clamp(Math.Min(width, height) / 5, 6, 12);
        int corner = Math.Min(20, Math.Max(edge, Math.Min(width, height) / 3));

        bool nearLeft = distanceLeft >= 0 && distanceLeft < corner;
        bool nearRight = distanceRight > 0 && distanceRight <= corner;
        bool nearTop = distanceTop >= 0 && distanceTop < corner;
        bool nearBottom = distanceBottom > 0 && distanceBottom <= corner;

        if (nearTop && nearLeft) hitTest = HtTopLeft;
        else if (nearTop && nearRight) hitTest = HtTopRight;
        else if (nearBottom && nearLeft) hitTest = HtBottomLeft;
        else if (nearBottom && nearRight) hitTest = HtBottomRight;
        else if (distanceLeft >= 0 && distanceLeft < edge) hitTest = HtLeft;
        else if (distanceRight > 0 && distanceRight <= edge) hitTest = HtRight;
        else if (distanceTop >= 0 && distanceTop < edge) hitTest = HtTop;
        else if (distanceBottom > 0 && distanceBottom <= edge) hitTest = HtBottom;

        return hitTest != 0;
    }
}
