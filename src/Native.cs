// Windows, XInput and ProtoInput calls used by the app.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MhoCoop
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO { public int Size; public RECT Monitor; public RECT Work; public uint Flags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepad { public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LX, LY, RX, RY; }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState { public uint Packet; public XInputGamepad Pad; }

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        public const int GWL_STYLE = -16;
        public const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000;
        public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);
        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out RECT value, uint size);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int value, uint size);
        [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint index, out XInputState state);

        private static bool xinputMissing;

        // Buttons held on the pad in this XInput slot (0-3). False when no pad is connected there.
        public static bool PadButtons(int slot, out int buttons)
        {
            buttons = 0;
            if (xinputMissing) return false;
            try
            {
                XInputState s;
                if (XInputGetState((uint)slot, out s) != 0) return false;
                buttons = s.Pad.Buttons;
                return true;
            }
            catch (DllNotFoundException) { xinputMissing = true; return false; }
            catch (EntryPointNotFoundException) { xinputMissing = true; return false; }
        }

        public static string ClassOf(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static uint PidOf(IntPtr hwnd)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return pid;
        }

        public static bool ProcessAlive(uint pid)
        {
            if (pid == 0) return false;
            try
            {
                using (Process p = Process.GetProcessById((int)pid)) return !p.HasExited;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return true; }   // exists, but we may not look inside it
        }

        private static string ProcessNameOf(uint pid)
        {
            try
            {
                using (Process p = Process.GetProcessById((int)pid)) return p.ProcessName;
            }
            catch (ArgumentException) { return ""; }
            catch (InvalidOperationException) { return ""; }
        }

        // Top-level windows of the given class that belong to a process with the given name.
        // onlyPid = 0 means any process with that name.
        public static List<IntPtr> GameWindows(string className, string processName, uint onlyPid, bool includeHidden)
        {
            List<IntPtr> found = new List<IntPtr>();
            EnumWindowsProc cb = delegate(IntPtr hwnd, IntPtr lParam)
            {
                if (!includeHidden && !IsWindowVisible(hwnd)) return true;
                if (!string.Equals(ClassOf(hwnd), className, StringComparison.Ordinal)) return true;
                uint pid = PidOf(hwnd);
                if (onlyPid != 0 && pid != onlyPid) return true;
                if (!string.Equals(ProcessNameOf(pid), processName, StringComparison.OrdinalIgnoreCase)) return true;
                found.Add(hwnd);
                return true;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return found;
        }

        // Give a window the keyboard focus. Windows refuses this to background programs,
        // so fall back to the same tricks AutoHotkey uses.
        public static void Activate(IntPtr hwnd)
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, 9 /*SW_RESTORE*/);
            SetForegroundWindow(hwnd);
            if (GetForegroundWindow() == hwnd) return;

            uint ignored;
            uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out ignored);
            uint me = GetCurrentThreadId();
            if (foreground != 0 && foreground != me)
            {
                AttachThreadInput(me, foreground, true);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                AttachThreadInput(me, foreground, false);
                if (GetForegroundWindow() == hwnd) return;
            }

            keybd_event(0x12, 0, 0, UIntPtr.Zero);          // tap Alt: lifts the foreground lock
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(hwnd);
        }

        // Lift a window above other programs without leaving it pinned on top.
        public static void Raise(IntPtr hwnd)
        {
            if (!IsWindow(hwnd)) return;
            if (IsIconic(hwnd)) ShowWindow(hwnd, 9 /*SW_RESTORE*/);
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    // ProtoInputLoader64.dll, from protoloader.h in the ProtoInput source.
    internal static class Proto
    {
        private const string Loader = "ProtoInputLoader64.dll";
        private const string Hooks = "ProtoInputHooks64.dll";
        private const CallingConvention CC = CallingConvention.Cdecl;

        public const uint RegisterRawInputHookID = 0, GetRawInputDataHookID = 1, MessageFilterHookID = 2,
                          FocusHooksHookID = 10, RenameHandlesHookID = 11, XinputHookID = 12,
                          SetWindowPosHookID = 14, WindowStyleHookID = 18;
        public const uint MouseActivateFilterID = 2, WindowActivateFilterID = 3, WindowActivateAppFilterID = 4;

        [DllImport(Loader, CallingConvention = CC, CharSet = CharSet.Unicode)]
        public static extern uint RemoteLoadLibraryInjectRuntime(uint pid, string dllFolderPath);
        [DllImport(Loader, CallingConvention = CC)] public static extern void SetupState(uint handle, int instanceIndex);
        [DllImport(Loader, CallingConvention = CC)] public static extern void InstallHook(uint handle, uint hookId);
        [DllImport(Loader, CallingConvention = CC)] public static extern void EnableMessageFilter(uint handle, uint filterId);
        [DllImport(Loader, CallingConvention = CC)] public static extern void SetShowCursorWhenImageUpdated(uint handle, [MarshalAs(UnmanagedType.I1)] bool enabled);
        [DllImport(Loader, CallingConvention = CC)] public static extern void SetCursorClipOptions(uint handle, [MarshalAs(UnmanagedType.I1)] bool useFakeClipCursor);
        [DllImport(Loader, CallingConvention = CC)] public static extern void SetUseOpenXinput(uint handle, [MarshalAs(UnmanagedType.I1)] bool useOpenXinput);
        [DllImport(Loader, CallingConvention = CC)] public static extern void SetUseDinputRedirection(uint handle, [MarshalAs(UnmanagedType.I1)] bool useRedirection);
        [DllImport(Loader, CallingConvention = CC)]
        public static extern void SetupMessagesToSend(uint handle,
            [MarshalAs(UnmanagedType.I1)] bool mouseWheel, [MarshalAs(UnmanagedType.I1)] bool mouseButton,
            [MarshalAs(UnmanagedType.I1)] bool mouseMove, [MarshalAs(UnmanagedType.I1)] bool keyboard);
        [DllImport(Loader, CallingConvention = CC)]
        public static extern void StartFocusMessageLoop(uint handle, int milliseconds,
            [MarshalAs(UnmanagedType.I1)] bool wmActivate, [MarshalAs(UnmanagedType.I1)] bool wmActivateApp,
            [MarshalAs(UnmanagedType.I1)] bool wmNcActivate, [MarshalAs(UnmanagedType.I1)] bool wmSetFocus,
            [MarshalAs(UnmanagedType.I1)] bool wmMouseActivate);
        [DllImport(Loader, CallingConvention = CC)] public static extern void SetDrawFakeCursor(uint handle, [MarshalAs(UnmanagedType.I1)] bool enable);
        [DllImport(Loader, CallingConvention = CC)]
        public static extern void AllowFakeCursorOutOfBounds(uint handle, [MarshalAs(UnmanagedType.I1)] bool allowOutOfBounds, [MarshalAs(UnmanagedType.I1)] bool extendBounds);
        [DllImport(Loader, CallingConvention = CC)]
        public static extern void SetToggleFakeCursorVisibilityShortcut(uint handle, [MarshalAs(UnmanagedType.I1)] bool enabled, uint vkey);
        [DllImport(Loader, CallingConvention = CC)] public static extern void SetControllerIndex(uint handle, uint index1, uint index2, uint index3, uint index4);
        [DllImport(Loader, CallingConvention = CC)] public static extern void SetExternalFreezeFakeInput(uint handle, [MarshalAs(UnmanagedType.I1)] bool enableFreeze);
        [DllImport(Loader, CallingConvention = CC)] public static extern void SetRawInputBypass(uint handle, [MarshalAs(UnmanagedType.I1)] bool enabled);
        [DllImport(Loader, CallingConvention = CC)] public static extern void SetSetWindowPosSettings(uint handle, int posx, int posy, int width, int height);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetDllDirectory(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, [MarshalAs(UnmanagedType.LPStr)] string name);

        private static readonly string[] RequiredExports =
        {
            "RemoteLoadLibraryInjectRuntime", "SetupState", "InstallHook", "EnableMessageFilter",
            "SetShowCursorWhenImageUpdated", "SetCursorClipOptions", "SetUseOpenXinput",
            "SetUseDinputRedirection", "SetupMessagesToSend", "StartFocusMessageLoop",
            "SetDrawFakeCursor", "AllowFakeCursorOutOfBounds", "SetToggleFakeCursorVisibilityShortcut",
            "SetControllerIndex", "SetExternalFreezeFakeInput", "SetSetWindowPosSettings", "SetRawInputBypass"
        };

        private static string loadedFrom;

        // True when the folder holds the two ProtoInput files this app needs.
        public static bool FolderLooksValid(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            try { return File.Exists(Path.Combine(dir, Loader)) && File.Exists(Path.Combine(dir, Hooks)); }
            catch (ArgumentException) { return false; }
        }

        // Loads ProtoInput from the folder. folder comes back ending in a backslash, which the loader expects.
        public static bool Load(string dir, out string folder, out string error)
        {
            folder = dir ?? "";
            error = "";
            if (!FolderLooksValid(folder))
            {
                error = "ProtoInput was not found. " + Loader + " and " + Hooks + " must be in:\n" +
                        (folder.Length == 0 ? "(no folder set)" : folder) + "\n\nSet the ProtoInput folder in Settings.";
                return false;
            }
            if (!folder.EndsWith("\\")) folder += "\\";
            if (loadedFrom != null) { folder = loadedFrom; return true; }

            SetDllDirectory(folder);
            IntPtr module = LoadLibrary(Path.Combine(folder, Loader));
            if (module == IntPtr.Zero)
            {
                error = "Windows could not load " + Loader + " (error " + Marshal.GetLastWin32Error() + ").";
                return false;
            }
            foreach (string name in RequiredExports)
            {
                if (GetProcAddress(module, name) == IntPtr.Zero)
                {
                    error = "This ProtoInput build is missing '" + name + "'. Use ProtoInput 1.1.2 or newer.";
                    return false;
                }
            }
            loadedFrom = folder;
            return true;
        }
    }
}
