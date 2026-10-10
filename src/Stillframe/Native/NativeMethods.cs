using System.Runtime.InteropServices;
using System.Text;

namespace Stillframe.Native;

internal static class NativeMethods
{
    internal const int GwlStyle = -16, GwlExStyle = -20;
    internal const long WsChild = 0x40000000, WsPopup = 0x80000000, WsExToolWindow = 0x80,
        WsExAppWindow = 0x40000, WsExNoActivate = 0x08000000, WsExLayered = 0x80000, WsExNoRedirectionBitmap = 0x00200000;
    internal const uint SwpNoActivate = 0x10, SwpNoSize = 1, SwpNoMove = 2, SwpFrameChanged = 0x20, SwpShowWindow = 0x40;
    internal delegate bool EnumProc(nint hwnd, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowEx(uint extended, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);
    [DllImport("gdi32.dll")] internal static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("user32.dll")] internal static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindow(string? cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindowEx(nint parent, nint after, string? cls, string? title);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc proc, nint lParam);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetParent(nint hwnd, nint parent);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern uint MapWindowPoints(nint from, nint to, ref Point point, uint count);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder cls, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint SendMessageTimeout(nint hwnd, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    internal static string ClassName(nint hwnd) { var result = new StringBuilder(256); GetClassName(hwnd, result, result.Capacity); return result.ToString(); }
}
