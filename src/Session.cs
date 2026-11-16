// One couch co-op session: start both games, tile them, attach ProtoInput, then watch the gamepads
// until both games close.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MhoCoop
{
    internal delegate void StatusHandler(string title, string detail);

    internal sealed class Session
    {
        public const string WindowClass = "LaunchUnrealUWindowsClient";
        private const int AppearWaitMs = 90000;       // how long a game window may take to appear
        private const int XI_RB = 0x0200;             // right shoulder button
        private const int XI_DPAD_UD = 0x0003;        // D-pad up or down
        private const uint VK_HOME = 0x24;

        private struct Box { public int X, Y, W, H; }

        private readonly Config cfg;
        private readonly StatusHandler report;
        private readonly object logLock = new object();
        private volatile bool cancelled, stopWatching;

        private IntPtr hwnd1, hwnd2;                  // 1 = left game, 2 = right game
        private uint pid1, pid2;
        private uint proto1, proto2;                  // ProtoInput handles, 0 = not attached
        private Box target1, target2;                 // borderless rectangles
        private string processName = "", protoFolder = "";

        public string Error = "";

        public Session(Config config, StatusHandler status)
        {
            cfg = config;
            report = status;
        }

        public bool Cancelled { get { return cancelled; } }
        public void Cancel() { cancelled = true; }
        public void Stop() { stopWatching = true; }

        // ---- log (never contains command lines) ----
        private void Log(string text)
        {
            try
            {
                lock (logLock)
                    File.AppendAllText(Config.LogPath, DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // Waits, but returns false early if the user cancels.
        private bool Pause(int milliseconds)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < milliseconds)
            {
                if (cancelled) return false;
                Thread.Sleep(50);
            }
            return !cancelled;
        }

        // ---- launch ----
        public bool Launch()
        {
            try
            {
                Directory.CreateDirectory(Config.DataDir);
                File.Delete(Config.LogPath);
            }
            catch (Exception) { }
            try
            {
                bool ok = LaunchSteps();
                if (!ok) Log(cancelled ? "cancelled" : "stopped: " + Error.Replace("\n", " "));
                return ok;
            }
            catch (Exception ex)
            {
                Error = "Something unexpected went wrong:\n" + ex.Message;
                Log(ex.ToString());
                return false;
            }
        }

        private bool LaunchSteps()
        {
            Log("gamepad " + cfg.LeftPad + " left, gamepad " + cfg.RightPad + " right");
            if (!File.Exists(cfg.GameExe))
            {
                Error = "The game was not found:\n" + (cfg.GameExe.Length == 0 ? "(not set)" : cfg.GameExe) + "\n\nSet it in Settings.";
                return false;
            }
            processName = Path.GetFileNameWithoutExtension(cfg.GameExe);
            if (!Proto.Load(cfg.ProtoDir, out protoFolder, out Error)) return false;

            Rectangle screen = Screen.PrimaryScreen.Bounds;
            int resX = screen.Width / 2, resY = screen.Height;

            report("Starting the left game", "Gamepad " + cfg.LeftPad + ".");
            hwnd1 = StartGame("left", resX, resY);
            if (hwnd1 == IntPtr.Zero) return false;
            pid1 = Native.PidOf(hwnd1);
            if (!WaitForLoad("left")) return false;

            report("Starting the right game", "Gamepad " + cfg.RightPad + ".");
            hwnd2 = StartGame("right", resX, resY);
            if (hwnd2 == IntPtr.Zero) return false;
            pid2 = Native.PidOf(hwnd2);
            if (!WaitForLoad("right")) return false;

            report("Arranging the windows", "Each game gets half of the screen.");
            if (!Tile()) return false;

            report("Linking the gamepads", "Gamepad " + cfg.LeftPad + " to the left game, gamepad " + cfg.RightPad + " to the right game.");
            return Attach();
        }

        private string BuildArguments(string side, int resX, int resY)
        {
            StringBuilder sb = new StringBuilder();
            string extra = (side == "left" ? cfg.LeftArgs : cfg.RightArgs).Trim();
            if (extra.Length > 0) sb.Append(extra).Append(' ');
            sb.Append("-ResX=").Append(resX).Append(" -ResY=").Append(resY).Append(" -windowed");
            if (cfg.Server.Trim().Length > 0) sb.Append(" -siteconfigurl=").Append(cfg.Server.Trim());
            return sb.ToString();
        }

        // Starts one game ("left" or "right") and returns its window, or IntPtr.Zero (with Error set unless cancelled).
        private IntPtr StartGame(string side, int resX, int resY)
        {
            HashSet<IntPtr> before = new HashSet<IntPtr>(Native.GameWindows(WindowClass, processName, 0, false));
            ProcessStartInfo psi = new ProcessStartInfo(cfg.GameExe, BuildArguments(side, resX, resY));
            psi.WorkingDirectory = Path.GetDirectoryName(cfg.GameExe);
            psi.UseShellExecute = false;
            try
            {
                using (Process started = Process.Start(psi)) { }
            }
            catch (Exception ex)
            {
                Error = "Windows could not start the game:\n" + ex.Message;
                return IntPtr.Zero;
            }

            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < AppearWaitMs)
            {
                if (!Pause(500)) return IntPtr.Zero;
                foreach (IntPtr h in Native.GameWindows(WindowClass, processName, 0, false))
                {
                    if (!before.Contains(h))
                    {
                        Log(side + " game window appeared after " + (sw.ElapsedMilliseconds / 1000) + " s");
                        return h;
                    }
                }
            }
            Error = "The " + side + " game's window did not appear within " + (AppearWaitMs / 1000) + " seconds.";
            return IntPtr.Zero;
        }

        // Gives a game time to finish loading before the next step.
        private bool WaitForLoad(string side)
        {
            for (int left = cfg.LoadWait; left > 0; left--)
            {
                report("Letting the " + side + " game load", left + (left == 1 ? " second left." : " seconds left."));
                if (!Pause(1000)) return false;
            }
            return true;
        }

        // ---- tile: two halves of the area above the taskbar ----
        // The game shrinks any window taller than that area (and narrows it to match),
        // and it keeps its old position when it resizes, so size comes first and position second.
        private bool Tile()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int halfW = wa.Width / 2, workH = wa.Height;
            int edge = InvisibleBorder(hwnd1);        // Windows pads each side of a window with an unseen border

            // With title bars kept, make the visible edges meet. For borderless, make the game's picture
            // exactly half the screen wide now, so it already matches the window that gets pinned afterwards.
            int tileW = cfg.Borderless ? halfW + FrameWidth(hwnd1) : halfW + 2 * edge;
            const uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE;

            Native.SetWindowPos(hwnd1, IntPtr.Zero, wa.Left, wa.Top, tileW, workH, flags);
            Native.SetWindowPos(hwnd2, IntPtr.Zero, wa.Left + halfW, wa.Top, tileW, workH, flags);
            if (!Pause(2500)) return false;           // let the games finish resizing

            Native.SetWindowPos(hwnd1, IntPtr.Zero, wa.Left - edge, wa.Top, 0, 0, flags | Native.SWP_NOSIZE);
            Native.SetWindowPos(hwnd2, IntPtr.Zero, wa.Left + halfW - edge, wa.Top, 0, 0, flags | Native.SWP_NOSIZE);
            Log("tiled: half width " + halfW + ", height " + workH + ", unseen border " + edge);
            return true;
        }

        // How much wider the whole window is than the game picture inside it (left + right borders).
        private static int FrameWidth(IntPtr hwnd)
        {
            Native.RECT w, c;
            if (!Native.GetWindowRect(hwnd, out w) || !Native.GetClientRect(hwnd, out c)) return 0;
            int extra = (w.Right - w.Left) - c.Right;
            return extra > 0 && extra < 200 ? extra : 0;
        }

        // How much taller the whole window is than the picture inside it (title bar plus borders).
        private static int FrameHeight(IntPtr hwnd)
        {
            Native.RECT w, c;
            if (!Native.GetWindowRect(hwnd, out w) || !Native.GetClientRect(hwnd, out c)) return 0;
            int extra = (w.Bottom - w.Top) - c.Bottom;
            return extra > 0 && extra < 400 ? extra : 0;
        }

        // Width in pixels of the unseen border Windows adds to the left of a window.
        private static int InvisibleBorder(IntPtr hwnd)
        {
            Native.RECT seen, whole;
            if (Native.DwmGetWindowAttribute(hwnd, 9 /*DWMWA_EXTENDED_FRAME_BOUNDS*/, out seen, 16) != 0) return 0;
            if (!Native.GetWindowRect(hwnd, out whole)) return 0;
            int edge = seen.Left - whole.Left;
            return edge > 0 && edge < 64 ? edge : 0;
        }

        // ---- attach ProtoInput ----
        private bool Attach()
        {
            if (cfg.Borderless) PlanBorderless();

            proto1 = Apply(pid1, 1, (uint)cfg.LeftPad, target1);
            proto2 = Apply(pid2, 2, (uint)cfg.RightPad, target2);
            Thread.Sleep(1500);                       // give each game a moment to read the last settings

            if (cfg.Borderless)
            {
                // The hooks only stop the game from undoing this. The first strip and resize come from here.
                if (proto1 != 0) MakeBorderless(hwnd1, target1);
                if (proto2 != 0) MakeBorderless(hwnd2, target2);
                Thread.Sleep(3000);
                if (proto1 != 0) Log("left now: " + Describe(hwnd1));
                if (proto2 != 0) Log("right now: " + Describe(hwnd2));
            }

            if (proto1 == 0 || proto2 == 0)
            {
                string who = proto1 == 0 && proto2 == 0 ? "either game" : (proto1 == 0 ? "the left game" : "the right game");
                Error = "ProtoInput could not be attached to " + who + ".\n\n" +
                        "If the game runs as administrator, run this app the same way. " +
                        "Some antivirus programs also block this step.";
                return false;
            }
            return true;
        }

        // Mirrors Launch() in ProtoInputHost's Gui.cpp. Returns the ProtoInput handle, or 0 if it failed.
        private uint Apply(uint pid, int instanceIndex, uint controllerIndex, Box target)
        {
            uint h = Proto.RemoteLoadLibraryInjectRuntime(pid, protoFolder);
            if (h == 0)
            {
                Log("attach failed for instance " + instanceIndex);
                return 0;
            }
            Proto.SetupState(h, instanceIndex);

            if (cfg.WheelPerWindow)
            {
                // ProtoInput takes over raw input from the game. With no device assigned and bypass off, none is
                // passed on; the watcher later switches bypass on for whichever game the cursor is over.
                Proto.InstallHook(h, Proto.RegisterRawInputHookID);
                Proto.InstallHook(h, Proto.GetRawInputDataHookID);
                Proto.SetRawInputBypass(h, false);
            }

            Proto.InstallHook(h, Proto.MessageFilterHookID);
            Proto.SetShowCursorWhenImageUpdated(h, false);
            Proto.SetCursorClipOptions(h, false);
            Proto.InstallHook(h, Proto.FocusHooksHookID);
            Proto.InstallHook(h, Proto.RenameHandlesHookID);

            // The controller index goes before the Xinput hook: the hook starts at index 0 ("no controller"),
            // and even a moment of that makes the game pop up "Gamepad disconnected".
            Proto.SetUseOpenXinput(h, true);
            Proto.SetUseDinputRedirection(h, false);
            Proto.SetControllerIndex(h, controllerIndex, 0, 0, 0);
            Proto.InstallHook(h, Proto.XinputHookID);

            Proto.EnableMessageFilter(h, Proto.MouseActivateFilterID);
            Proto.EnableMessageFilter(h, Proto.WindowActivateFilterID);
            Proto.EnableMessageFilter(h, Proto.WindowActivateAppFilterID);

            Proto.SetupMessagesToSend(h, true, true, true, true);
            Proto.StartFocusMessageLoop(h, 5, true, false, false, true, true);

            Proto.SetDrawFakeCursor(h, true);
            Proto.AllowFakeCursorOutOfBounds(h, false, false);
            Proto.SetToggleFakeCursorVisibilityShortcut(h, false, VK_HOME);

            Proto.SetControllerIndex(h, controllerIndex, 0, 0, 0);
            Proto.SetExternalFreezeFakeInput(h, false);

            if (cfg.Borderless)
            {
                // Every time the game tries to move, resize or restyle its window, it gets this rectangle
                // and no title bar instead.
                Proto.SetSetWindowPosSettings(h, target.X, target.Y, target.W, target.H);
                Proto.InstallHook(h, Proto.SetWindowPosHookID);
                Proto.InstallHook(h, Proto.WindowStyleHookID);
            }
            Log("instance " + instanceIndex + ": attached, gamepad " + controllerIndex + (cfg.Borderless ? ", borderless" : "") +
                (cfg.WheelPerWindow ? ", wheel per window" : ""));
            return h;
        }

        // The game always sizes its picture as if its window still had a title bar, and refuses any size where
        // "picture + title bar" would not fit above the taskbar. A taller window makes it stretch the picture,
        // and clicks then land above where they are drawn. So the borderless window is exactly as tall as the
        // tallest picture the game accepts: the usable height minus the title bar it believes it has.
        private void PlanBorderless()
        {
            Native.MONITORINFO mi = new Native.MONITORINFO();
            mi.Size = Marshal.SizeOf(typeof(Native.MONITORINFO));
            Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd1, 2 /*nearest*/), ref mi);

            int width = mi.Work.Right - mi.Work.Left;
            int height = mi.Work.Bottom - mi.Work.Top;
            int half = width / 2;
            int frameTall = Math.Max(FrameHeight(hwnd1), FrameHeight(hwnd2));
            int pictureTall = height - frameTall;

            target1.X = mi.Work.Left;        target1.Y = mi.Work.Top; target1.W = half;         target1.H = pictureTall;
            target2.X = mi.Work.Left + half; target2.Y = mi.Work.Top; target2.W = width - half; target2.H = pictureTall;
            Log("usable area " + width + "x" + height + ", title bar allowance " + frameTall + ", each game " + half + "x" + pictureTall);
        }

        private static void MakeBorderless(IntPtr hwnd, Box target)
        {
            long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64();
            style &= ~(Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU);
            Native.SetWindowLongPtr(hwnd, Native.GWL_STYLE, new IntPtr(style));
            Native.SetWindowPos(hwnd, IntPtr.Zero, target.X, target.Y, target.W, target.H,
                                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        }

        private static string Describe(IntPtr hwnd)
        {
            Native.RECT w, c;
            Native.GetWindowRect(hwnd, out w);
            Native.GetClientRect(hwnd, out c);
            bool caption = (Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64() & Native.WS_CAPTION) == Native.WS_CAPTION;
            return "window " + (w.Right - w.Left) + "x" + (w.Bottom - w.Top) + " at (" + w.Left + "," + w.Top + "), game area " +
                   c.Right + "x" + c.Bottom + ", title bar " + (caption ? "still present" : "removed");
        }

        // ---- watch: runs until both games have closed ----
        // RB on either pad brings both games to the front. D-pad up or down puts the mouse cursor over that
        // pad's game. With "wheel per window", raw mouse input reaches only the game under the cursor.
        public void Watch()
        {
            Log("watching: left pid " + pid1 + ", right pid " + pid2);
            int[] previousRb = new int[4], previousDpad = new int[4];
            bool route = cfg.WheelPerWindow && proto1 != 0 && proto2 != 0;
            bool alive1 = true, alive2 = true;
            int routed = 0;                           // 0 = not decided yet, 1 = left, 2 = right
            int tick = 0;

            while (!stopWatching)
            {
                try
                {
                    if (tick++ % 100 == 0)            // every 2 seconds
                    {
                        alive1 = Native.ProcessAlive(pid1);
                        alive2 = Native.ProcessAlive(pid2);
                        if (!alive1 && !alive2)
                        {
                            Log("both games have closed");
                            return;
                        }
                        hwnd1 = CurrentWindow(pid1, hwnd1);
                        hwnd2 = CurrentWindow(pid2, hwnd2);
                    }

                    if (route)
                    {
                        Native.POINT p;
                        if (Native.GetCursorPos(out p))
                        {
                            uint pid = Native.PidOf(Native.GetAncestor(Native.WindowFromPoint(p), 2 /*GA_ROOT*/));
                            int over = pid == pid1 ? 1 : (pid == pid2 ? 2 : 0);
                            if (over != 0 && over != routed)   // over neither game: leave things as they were
                            {
                                if (alive1) Proto.SetRawInputBypass(proto1, over == 1);
                                if (alive2) Proto.SetRawInputBypass(proto2, over == 2);
                                routed = over;
                            }
                        }
                    }

                    for (int slot = 0; slot < 4; slot++)
                    {
                        int buttons;
                        if (!Native.PadButtons(slot, out buttons))
                        {
                            previousRb[slot] = 0;
                            previousDpad[slot] = 0;
                            continue;
                        }
                        int rb = (buttons & XI_RB) != 0 ? 1 : 0;
                        if (rb == 1 && previousRb[slot] == 0) Log("RB on gamepad " + (slot + 1) + ": " + ShowBoth(slot + 1));
                        previousRb[slot] = rb;

                        int dpad = (buttons & XI_DPAD_UD) != 0 ? 1 : 0;
                        if (dpad == 1 && previousDpad[slot] == 0) Log("D-pad on gamepad " + (slot + 1) + ": " + CursorToPadWindow(slot + 1));
                        previousDpad[slot] = dpad;
                    }
                }
                catch (Exception ex)
                {
                    Log("watcher error: " + ex.Message);
                }
                Thread.Sleep(20);
            }
            Log("stopped by the user; games left running");
        }

        // The game's window for this process: the one already known if it still exists, otherwise whatever
        // game window that process has now (it can re-create its window while going borderless).
        private IntPtr CurrentWindow(uint pid, IntPtr known)
        {
            if (Native.IsWindow(known)) return known;
            List<IntPtr> found = Native.GameWindows(WindowClass, processName, pid, true);
            if (found.Count > 0)
            {
                Log("pid " + pid + " has a new game window");
                return found[0];
            }
            return known;
        }

        // Raise whichever game window is hidden. Keyboard and mouse focus only moves when no game window
        // has it; then it goes to the window of the pad that pressed RB.
        public string ShowBoth(int padIndex)
        {
            bool need1 = !IsShowing(hwnd1), need2 = !IsShowing(hwnd2);
            IntPtr active = Native.GetForegroundWindow();
            bool gameActive = active == hwnd1 || active == hwnd2;
            if (!need1 && !need2 && gameActive) return "both games already in front, nothing to do";

            string did = "";
            if (need1) { Native.Raise(hwnd1); did += "raised left; "; }
            if (need2) { Native.Raise(hwnd2); did += "raised right; "; }
            if (!gameActive)
            {
                IntPtr target = padIndex == cfg.RightPad ? hwnd2 : hwnd1;
                if (!Native.IsWindow(target)) target = target == hwnd1 ? hwnd2 : hwnd1;
                if (Native.IsWindow(target))
                {
                    Native.Activate(target);
                    did += "gave focus to " + (target == hwnd2 ? "right" : "left");
                }
            }
            return did;
        }

        // True when the middle of the game window is not covered by another program.
        private bool IsShowing(IntPtr hwnd)
        {
            if (!Native.IsWindow(hwnd)) return true;  // that game is closed: nothing to raise
            if (Native.IsIconic(hwnd)) return false;
            Native.RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return true;
            Native.POINT centre;
            centre.X = (r.Left + r.Right) / 2;
            centre.Y = (r.Top + r.Bottom) / 2;
            uint pid = Native.PidOf(Native.GetAncestor(Native.WindowFromPoint(centre), 2 /*GA_ROOT*/));
            return pid == pid1 || pid == pid2;
        }

        // Put the mouse cursor in the middle of the window that belongs to this pad, so the mouse wheel
        // targets that game. Does nothing if the cursor is already over that window.
        private string CursorToPadWindow(int padIndex)
        {
            IntPtr target;
            if (padIndex == cfg.LeftPad) target = hwnd1;
            else if (padIndex == cfg.RightPad) target = hwnd2;
            else return "pad not assigned to a game";
            if (!Native.IsWindow(target)) return "that game's window is gone";
            if (Native.IsIconic(target)) return "that game is minimized";
            Native.RECT r;
            Native.POINT p;
            if (!Native.GetWindowRect(target, out r) || !Native.GetCursorPos(out p)) return "could not read positions";
            if (p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom) return "cursor already over that game";
            int x = (r.Left + r.Right) / 2, y = (r.Top + r.Bottom) / 2;
            return "cursor moved to " + x + "," + y + (Native.SetCursorPos(x, y) ? "" : " FAILED");
        }
    }
}
