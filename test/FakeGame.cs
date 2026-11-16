// Stand-in for the game, for testing the launcher without the real game: a plain window
// with the game's window class. Build it as MarvelHeroesOmega.exe (see test\README.md) and point the app's
// Game program setting at it. ProtoInput attaches to it like it does to the real game.
// It writes the options it was started with to fakegame.log next to the exe.
using System;
using System.IO;
using System.Runtime.InteropServices;

internal static class FakeGame
{
    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string lpszMenuName, lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursorW(IntPtr inst, IntPtr name);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int index);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandleW(IntPtr name);

    private static WndProc proc;

    private static int Main(string[] args)
    {
        string names = "";
        foreach (string a in args) names += " " + a;
        File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fakegame.log"),
            DateTime.Now.ToString("HH:mm:ss") + " args:" + names + Environment.NewLine);

        proc = Handle;
        WNDCLASS wc = new WNDCLASS();
        wc.lpfnWndProc = proc;
        wc.hInstance = GetModuleHandleW(IntPtr.Zero);
        wc.hCursor = LoadCursorW(IntPtr.Zero, new IntPtr(32512));
        wc.hbrBackground = GetStockObject(3);
        wc.lpszClassName = "LaunchUnrealUWindowsClient";
        if (RegisterClassW(ref wc) == 0) return 1;
        IntPtr hwnd = CreateWindowExW(0, wc.lpszClassName, "Marvel Heroes Omega", 0x10CF0000, 200, 200, 900, 700,
                                      IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) return 2;
        MSG m;
        while (GetMessageW(out m, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref m);
            DispatchMessageW(ref m);
        }
        return 0;
    }

    private static IntPtr Handle(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == 0x0002) { PostQuitMessage(0); return IntPtr.Zero; }   // WM_DESTROY
        return DefWindowProcW(hwnd, msg, w, l);
    }
}
