using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace RoMulti
{
    public sealed class AntiAfkPulseResult
    {
        public int PulsedCount { get; set; }
        public int FocusFailedCount { get; set; }
        public bool DeferredBecauseUserBusy { get; set; }
        public string PatternUsed { get; set; }
        public string Summary { get; set; }
    }

    public sealed class PeekCaptureItem : IDisposable
    {
        public RobloxInstanceInfo Instance { get; set; }
        public RawDualCrop RawCrop { get; set; }
        public void Dispose()
        {
            if (RawCrop != null) { RawCrop.Dispose(); RawCrop = null; }
        }
    }

    public sealed class SmartPeekBatchResult : IDisposable
    {
        public bool DeferredBecauseUserBusy { get; set; }
        public List<PeekCaptureItem> Items { get; set; }
        public SmartPeekBatchResult()
        {
            Items = new List<PeekCaptureItem>();
        }
        public void Dispose()
        {
            foreach (var it in Items) it.Dispose();
            Items.Clear();
        }
    }

    public static class AntiAfkKeeper
    {
        internal const int MainIdleMs = 120000;

        private static readonly Random Rng = new Random();
        // Per-PID 4-step patrol state so the character ACTUALLY STAYS at a new coordinate (~12 studs away)
        // between pulses, defeating server-side periodic position checks that catch TinyTask/return-to-spot loops!
        // Step 0: Move W (stay North) -> Step 1: Move S (stay Origin) -> Step 2: Move D (stay East) -> Step 3: Move A (stay Origin)
        private static readonly Dictionary<int, int> PatrolStepByPid = new Dictionary<int, int>();

        // Virtual keys + PS/2 hardware scan codes
        private const ushort VK_MENU = 0x12;
        private const ushort VK_W = 0x57;
        private const ushort VK_A = 0x41;
        private const ushort VK_S = 0x53;
        private const ushort VK_D = 0x44;
        private const ushort VK_R = 0x52;
        private const ushort VK_SPACE = 0x20;
        private const ushort VK_RETURN = 0x0D;
        private const ushort VK_ESCAPE = 0x1B;
        private const ushort VK_OEM_2 = 0xBF; // '/'

        private const ushort SCAN_ALT = 0x38;
        private const ushort SCAN_W = 0x11;
        private const ushort SCAN_A = 0x1E;
        private const ushort SCAN_S = 0x1F;
        private const ushort SCAN_D = 0x20;
        private const ushort SCAN_R = 0x13;
        private const ushort SCAN_SPACE = 0x39;
        private const ushort SCAN_SLASH = 0x35;
        private const ushort SCAN_ENTER = 0x1C;
        private const ushort SCAN_ESC = 0x01;

        private static readonly int[] WatchedActiveKeys = new[]
        {
            0x01, 0x02, // LMB, RMB
            0x57, 0x41, 0x53, 0x44, // W A S D
            0x20, // Space
            0x51, 0x45, 0x52, 0x5A, 0x58, 0x43, 0x56, // Q E R Z X C V
            0x10 // Shift
        };

        public static TimeSpan GetRandomizedNextDelay(int baseMinutes)
        {
            int clampedMin = Math.Max(3, Math.Min(14, baseMinutes));
            // Add human jitter (-45s to +45s) so pulses never repeat on a robotic TinyTask period
            int jitterSec;
            lock (Rng)
            {
                jitterSec = Rng.Next(-45, 46);
            }
            int totalSec = Math.Max(120, clampedMin * 60 + jitterSec);
            return TimeSpan.FromSeconds(totalSec);
        }

        public static uint GetSystemIdleMs()
        {
            var lii = new NativeMethods.LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.LASTINPUTINFO));
            if (NativeMethods.GetLastInputInfo(ref lii))
            {
                return unchecked((uint)Environment.TickCount - lii.dwTime);
            }
            return 0;
        }

        public static bool IsUserActivelyHoldingInput(int minIdleMs)
        {
            foreach (int vk in WatchedActiveKeys)
            {
                if ((NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0)
                    return true;
            }

            if (minIdleMs > 0)
            {
                uint idleMs = GetSystemIdleMs();
                if (idleMs < (uint)minIdleMs)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Executes a randomized, human-like movement + server-action + camera drag pulse:
        /// 1. Neutral viewport click (unfocuses Roblox TextChatService '/' input trap + registers click)
        /// 2. True one-way alternating RMB camera drag (never mistaken for M2 click)
        /// 3. Larger 4-step Two-Point Patrol (~18-22 studs, stays at new coordinate between pulses) + Jump
        /// 4. Safe server RemoteEvent toggle: Weapon Unsheathe/Sheathe ('R' -> 'R')
        /// 5. Optional Safezone Character Reset (Esc -> R -> Enter)
        /// Properly waits after restoring minimized windows and re-minimizes/tucks them back cleanly.
        /// </summary>
        public static AntiAfkPulseResult ExecuteQuickPulse(
            IList<RobloxInstanceInfo> targetInstances,
            bool respectActiveUser,
            bool wakeChatAfterPulse = true,
            bool resetCharacter = false,
            bool allowMainRole = false)
        {
            var res = new AntiAfkPulseResult();
            if (targetInstances == null || targetInstances.Count == 0)
            {
                res.Summary = "No target Roblox instances with visible windows to pulse.";
                return res;
            }

            if (respectActiveUser && IsUserActivelyHoldingInput(minIdleMs: allowMainRole ? MainIdleMs : 1200))
            {
                res.DeferredBecauseUserBusy = true;
                res.Summary = "User actively pressing controls — deferring Anti-AFK pulse briefly.";
                return res;
            }

            NativeMethods.MSG dummyMsg;
            NativeMethods.PeekMessage(out dummyMsg, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);

            IntPtr prevForeground = NativeMethods.GetForegroundWindow();
            NativeMethods.POINT prevCursor;
            bool hadCursor = NativeMethods.GetCursorPos(out prevCursor);

            int pulsed = 0;
            int failedFocus = 0;
            string lastPattern = "";

            try
            {
                foreach (var inst in targetInstances)
                {
                    if (inst.WindowHandle == IntPtr.Zero)
                        continue;
                    if (!allowMainRole && inst.Role != InstanceRole.AfkSaver)
                        continue;

                    IntPtr hwnd = inst.WindowHandle;
                    bool wasMinimized = NativeMethods.IsIconic(hwnd);
                    bool wasForeground = (hwnd == prevForeground);

                    if (!EnsureWindowFocused(hwnd))
                    {
                        failedFocus++;
                        continue;
                    }

                    // Minimized + EcoQoS + trimmed-RAM Roblox needs ~350ms after SW_RESTORE to accept input cleanly
                    Thread.Sleep(wasMinimized ? 350 : RandInt(130, 175));

                    int step;
                    lock (PatrolStepByPid)
                    {
                        if (!PatrolStepByPid.TryGetValue(inst.ProcessId, out step)) step = 0;
                        PatrolStepByPid[inst.ProcessId] = (step + 1) % 4;
                    }

                    // Click neutral viewport FIRST to guarantee Roblox chat bar ('/') is unfocused (fixes Jungle trap!)
                    // then rotate camera with a genuine one-way RMB drag (alternating left/right across steps).
                    NativeMethods.RECT r;
                    bool hasRect = NativeMethods.GetWindowRect(hwnd, out r);
                    if (hasRect)
                    {
                        SendSafeViewportClick(r);
                        Thread.Sleep(RandInt(40, 70));
                        SendCameraNudge(step);
                    }

                    // 4-step Two-Point Patrol (~18-22 studs displacement, STAYS at new position between pulses)
                    int holdMs = RandInt(1050, 1260);
                    switch (step)
                    {
                        case 0:
                            lastPattern = "Patrol -> North + Jump + Unsheathe";
                            HoldKey(VK_W, SCAN_W, holdMs);
                            Thread.Sleep(RandInt(55, 100));
                            HoldKey(VK_SPACE, SCAN_SPACE, RandInt(85, 130));
                            break;

                        case 1:
                            lastPattern = "Patrol -> Origin(N) + Jump + Unsheathe";
                            HoldKey(VK_S, SCAN_S, holdMs);
                            Thread.Sleep(RandInt(55, 100));
                            HoldKey(VK_SPACE, SCAN_SPACE, RandInt(85, 130));
                            break;

                        case 2:
                            lastPattern = "Patrol -> East + Jump + Unsheathe";
                            HoldKey(VK_D, SCAN_D, holdMs);
                            Thread.Sleep(RandInt(55, 100));
                            HoldKey(VK_SPACE, SCAN_SPACE, RandInt(85, 130));
                            break;

                        default:
                            lastPattern = "Patrol -> Origin(E) + Jump + Unsheathe";
                            HoldKey(VK_A, SCAN_A, holdMs);
                            Thread.Sleep(RandInt(55, 100));
                            HoldKey(VK_SPACE, SCAN_SPACE, RandInt(85, 130));
                            break;
                    }

                    // Safe server-replicated gameplay action: Unsheathe -> Sheathe weapon ('R' -> 'R')
                    // Fires genuine game RemoteEvents without moving out of safezone or opening blocking menus.
                    Thread.Sleep(RandInt(80, 130));
                    HoldKey(VK_R, SCAN_R, 50);
                    Thread.Sleep(RandInt(260, 360));
                    HoldKey(VK_R, SCAN_R, 50);
                    Thread.Sleep(RandInt(60, 100));

                    if (resetCharacter && inst.Role == InstanceRole.AfkSaver)
                    {
                        SendCharacterReset();
                        lastPattern += " + CharReset";
                    }

                    if (wakeChatAfterPulse)
                    {
                        SendWakeChatSlashEnter();
                        // Immediately click neutral viewport so Jungle's TextChatService box can NEVER stay focused!
                        if (hasRect)
                        {
                            Thread.Sleep(45);
                            SendSafeViewportClick(r);
                        }
                    }

                    // Restore the minimized state after pulsing either role.
                    if (wasMinimized && !wasForeground)
                    {
                        Thread.Sleep(50);
                        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
                    }

                    pulsed++;
                }
            }
            finally
            {
                if (prevForeground != IntPtr.Zero)
                    EnsureWindowFocused(prevForeground);
                if (hadCursor)
                    NativeMethods.SetCursorPos(prevCursor.X, prevCursor.Y);
            }

            res.PulsedCount = pulsed;
            res.FocusFailedCount = failedFocus;
            res.PatternUsed = lastPattern;
            res.Summary = failedFocus > 0
                ? string.Format("Anti-AFK pulse: {0} OK, {1} focus blocked (try Run as Admin).", pulsed, failedFocus)
                : string.Format("Anti-AFK humanized pulse OK on {0} instance(s) [{1}{2}] — focus restored.",
                    pulsed, lastPattern, wakeChatAfterPulse ? " + Chat Wake" : "");
            return res;
        }

        /// <summary>
        /// Ultra-brief (~0.3s/scout) Smart Auto-Peek: wakes minimized or occluded AFK Saver scouts,
        /// hovers chat + taps / -> Enter to un-fade dark chat and force fresh D3D11 rendering,
        /// grabs raw crops immediately, puts the scout back (re-minimized or behind Main),
        /// and restores Main focus & cursor — so OCR runs cleanly in the background!
        /// </summary>
        public static SmartPeekBatchResult PeekAndCaptureScouts(IList<RobloxInstanceInfo> afkScouts, bool respectActiveUser)
        {
            var batch = new SmartPeekBatchResult();
            if (afkScouts == null || afkScouts.Count == 0)
                return batch;

            if (respectActiveUser && IsUserActivelyHoldingInput(minIdleMs: 1400))
            {
                batch.DeferredBecauseUserBusy = true;
                return batch;
            }

            NativeMethods.MSG dummyMsg;
            NativeMethods.PeekMessage(out dummyMsg, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);

            IntPtr prevForeground = NativeMethods.GetForegroundWindow();
            NativeMethods.POINT prevCursor;
            bool hadCursor = NativeMethods.GetCursorPos(out prevCursor);

            try
            {
                foreach (var scout in afkScouts)
                {
                    if (scout.Role != InstanceRole.AfkSaver || scout.WindowHandle == IntPtr.Zero)
                        continue;

                    IntPtr hwnd = scout.WindowHandle;
                    // Never flash/minimize the window the user is actively looking at right now
                    if (hwnd == prevForeground)
                    {
                        var directRaw = ChatOcrEngine.CaptureRawCropsSync(hwnd, scout.PlaceId);
                        batch.Items.Add(new PeekCaptureItem { Instance = scout, RawCrop = directRaw });
                        continue;
                    }

                    bool wasMinimized = NativeMethods.IsIconic(hwnd);

                    if (!EnsureWindowFocused(hwnd))
                        continue;

                    // Minimized + EcoQoS + trimmed-RAM Roblox needs ~320ms after SW_RESTORE
                    // so Windows finishes restore animation and Roblox D3D11 resumes frame presentation!
                    Thread.Sleep(wasMinimized ? 320 : 120);

                    // Hover mouse inside BOTH top-left (Jungle) and bottom-left (1st Kingdom) chat boxes
                    // WITH genuine MOUSEEVENTF_MOVE micro-wiggles so Roblox fires MouseEnter/InputChanged
                    // and un-fades chat even on age-locked "Unlock chat" accounts!
                    NativeMethods.RECT r;
                    bool hasRect = NativeMethods.GetWindowRect(hwnd, out r);
                    if (hasRect)
                    {
                        int chatX = r.Left + Math.Min(110, Math.Max(40, (r.Right - r.Left) / 4));
                        int topY = r.Top + Math.Min(95, Math.Max(55, (r.Bottom - r.Top) / 4));
                        int botY = r.Bottom - Math.Min(90, Math.Max(50, (r.Bottom - r.Top) / 4));

                        WiggleHoverAt(chatX, topY);
                        WiggleHoverAt(chatX, botY);
                    }

                    // Tap / -> Enter to wake dark chat box on standard accounts (harmless if chat is locked)
                    SendWakeChatSlashEnter();
                    // Give D3D11 time to present the freshly un-faded dark chat frame before PrintWindow
                    Thread.Sleep(180);

                    // Grab fresh D3D11 frame right now while awake (PlaceId-aware: captures ONLY the active chat corner!)
                    var raw = ChatOcrEngine.CaptureRawCropsSync(hwnd, scout.PlaceId);
                    batch.Items.Add(new PeekCaptureItem
                    {
                        Instance = scout,
                        RawCrop = raw
                    });

                    // Click neutral viewport right after capture so '/' can NEVER leave Jungle chat input trapped focused!
                    if (hasRect)
                    {
                        SendSafeViewportClick(r);
                    }

                    // Put scout right back where it was (re-minimized or tucked behind Main)
                    if (wasMinimized)
                    {
                        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
                    }
                    else
                    {
                        NativeMethods.SetWindowPos(
                            hwnd,
                            NativeMethods.HWND_BOTTOM,
                            0, 0, 0, 0,
                            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                    }

                    Thread.Sleep(50);
                }
            }
            finally
            {
                if (prevForeground != IntPtr.Zero)
                    EnsureWindowFocused(prevForeground);
                if (hadCursor)
                    NativeMethods.SetCursorPos(prevCursor.X, prevCursor.Y);
            }

            return batch;
        }

        public static bool WakeChatOnWindowOnce(IntPtr targetHwnd)
        {
            if (targetHwnd == IntPtr.Zero) return false;

            NativeMethods.MSG dummyMsg;
            NativeMethods.PeekMessage(out dummyMsg, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);

            IntPtr prevForeground = NativeMethods.GetForegroundWindow();
            NativeMethods.POINT prevCursor;
            bool hadCursor = NativeMethods.GetCursorPos(out prevCursor);
            bool wasMinimized = NativeMethods.IsIconic(targetHwnd);

            try
            {
                if (!EnsureWindowFocused(targetHwnd))
                    return false;

                Thread.Sleep(wasMinimized ? 320 : 120);

                NativeMethods.RECT r;
                bool hasRect = NativeMethods.GetWindowRect(targetHwnd, out r);
                if (hasRect)
                {
                    int chatX = r.Left + Math.Min(110, Math.Max(40, (r.Right - r.Left) / 4));
                    int topY = r.Top + Math.Min(95, Math.Max(55, (r.Bottom - r.Top) / 4));
                    int botY = r.Bottom - Math.Min(90, Math.Max(50, (r.Bottom - r.Top) / 4));
                    WiggleHoverAt(chatX, topY);
                    WiggleHoverAt(chatX, botY);
                }

                SendWakeChatSlashEnter();
                Thread.Sleep(180);
                if (hasRect)
                    SendSafeViewportClick(r);
                return true;
            }
            finally
            {
                if (prevForeground != IntPtr.Zero && prevForeground != targetHwnd)
                    EnsureWindowFocused(prevForeground);
                if (hadCursor)
                    NativeMethods.SetCursorPos(prevCursor.X, prevCursor.Y);
            }
        }

        private static void WiggleHoverAt(int x, int y)
        {
            NativeMethods.SetCursorPos(x, y);
            Thread.Sleep(35);
            SendMouseFlags(NativeMethods.MOUSEEVENTF_MOVE, 6, 3);
            Thread.Sleep(45);
            SendMouseFlags(NativeMethods.MOUSEEVENTF_MOVE, -6, -3);
            Thread.Sleep(45);
        }

        private static void SendSafeViewportClick(NativeMethods.RECT r)
        {
            // Click neutral center-right viewport (away from left chat boxes, topbar icons, and bottom hotbar)
            // so TextChatService chat input is 100% unfocused before any movement keys are sent.
            int w = Math.Max(100, r.Right - r.Left);
            int h = Math.Max(100, r.Bottom - r.Top);
            int cx = r.Left + (int)(w * 0.65) + RandInt(-14, 15);
            int cy = r.Top + (int)(h * 0.42) + RandInt(-12, 13);

            NativeMethods.SetCursorPos(cx, cy);
            Thread.Sleep(25);
            SendMouseFlags(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0);
            Thread.Sleep(30);
            SendMouseFlags(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0);
            Thread.Sleep(25);
        }

        private static void SendCameraNudge(int patrolStep)
        {
            // Genuine one-way alternating RMB drag (alternates +X / -X across patrol steps)
            // so net displacement is > 40px and Roblox NEVER mistakes it for an M2 ability click!
            int dir = (patrolStep % 2 == 0) ? 1 : -1;
            int dx = RandInt(45, 78) * dir;
            int dy = RandInt(-8, 9);

            SendMouseFlags(NativeMethods.MOUSEEVENTF_RIGHTDOWN, 0, 0);
            Thread.Sleep(RandInt(35, 55));
            SendMouseFlags(NativeMethods.MOUSEEVENTF_MOVE, dx / 2, dy / 2);
            Thread.Sleep(RandInt(35, 55));
            SendMouseFlags(NativeMethods.MOUSEEVENTF_MOVE, dx - (dx / 2), dy - (dy / 2));
            Thread.Sleep(RandInt(40, 70));
            SendMouseFlags(NativeMethods.MOUSEEVENTF_RIGHTUP, 0, 0);
            Thread.Sleep(RandInt(30, 55));
        }

        private static void SendCharacterReset()
        {
            // Esc -> R -> Enter resets character cleanly in safezone (resets Clover Retribution's internal AFK flags)
            HoldKey(VK_ESCAPE, SCAN_ESC, 50);
            Thread.Sleep(180);
            HoldKey(VK_R, SCAN_R, 50);
            Thread.Sleep(160);
            HoldKey(VK_RETURN, SCAN_ENTER, 50);
            Thread.Sleep(120);
        }

        private static void SendWakeChatSlashEnter()
        {
            HoldKey(VK_OEM_2, SCAN_SLASH, 45);
            Thread.Sleep(160);
            HoldKey(VK_RETURN, SCAN_ENTER, 45);
            Thread.Sleep(55);
        }

        private static void HoldKey(ushort vk, ushort scanCode, int durationMs)
        {
            SendKey(vk, scanCode, keyUp: false);
            Thread.Sleep(durationMs);
            SendKey(vk, scanCode, keyUp: true);
        }

        private static int RandInt(int minInclusive, int maxExclusive)
        {
            lock (Rng)
            {
                return Rng.Next(minInclusive, maxExclusive);
            }
        }

        private static bool EnsureWindowFocused(IntPtr targetHwnd)
        {
            if (targetHwnd == IntPtr.Zero) return false;
            if (NativeMethods.IsIconic(targetHwnd))
                NativeMethods.ShowWindow(targetHwnd, NativeMethods.SW_RESTORE);

            if (NativeMethods.GetForegroundWindow() == targetHwnd)
                return true;

            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

            uint myThread = NativeMethods.GetCurrentThreadId();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr curFg = NativeMethods.GetForegroundWindow();
                uint unusedPid;
                uint fgThread = curFg != IntPtr.Zero ? NativeMethods.GetWindowThreadProcessId(curFg, out unusedPid) : 0;
                uint targetThread = NativeMethods.GetWindowThreadProcessId(targetHwnd, out unusedPid);

                bool attachedFg = false;
                bool attachedTarget = false;
                try
                {
                    if (fgThread != 0 && fgThread != myThread)
                        attachedFg = NativeMethods.AttachThreadInput(myThread, fgThread, true);
                    if (targetThread != 0 && targetThread != myThread && targetThread != fgThread)
                        attachedTarget = NativeMethods.AttachThreadInput(myThread, targetThread, true);

                    if (attempt > 0)
                    {
                        SendKey(VK_MENU, SCAN_ALT, keyUp: false);
                        SendKey(VK_MENU, SCAN_ALT, keyUp: true);
                    }

                    NativeMethods.ShowWindow(targetHwnd, NativeMethods.SW_SHOW);
                    NativeMethods.BringWindowToTop(targetHwnd);
                    NativeMethods.SetForegroundWindow(targetHwnd);
                }
                finally
                {
                    if (attachedTarget) NativeMethods.AttachThreadInput(myThread, targetThread, false);
                    if (attachedFg) NativeMethods.AttachThreadInput(myThread, fgThread, false);
                }

                for (int w = 0; w < 6; w++)
                {
                    if (NativeMethods.GetForegroundWindow() == targetHwnd)
                        return true;
                    Thread.Sleep(25);
                }
            }

            return NativeMethods.GetForegroundWindow() == targetHwnd;
        }

        private static void SendKey(ushort vk, ushort scanCode, bool keyUp)
        {
            var inputs = new NativeMethods.INPUT[1];
            inputs[0].type = NativeMethods.INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = vk;
            inputs[0].U.ki.wScan = scanCode;
            inputs[0].U.ki.dwFlags = NativeMethods.KEYEVENTF_SCANCODE | (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0u);
            inputs[0].U.ki.time = 0;
            inputs[0].U.ki.dwExtraInfo = UIntPtr.Zero;

            NativeMethods.SendInput(1, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        private static void SendMouseFlags(uint flags, int dx, int dy)
        {
            var inputs = new NativeMethods.INPUT[1];
            inputs[0].type = NativeMethods.INPUT_MOUSE;
            inputs[0].U.mi.dx = dx;
            inputs[0].U.mi.dy = dy;
            inputs[0].U.mi.dwFlags = flags;
            NativeMethods.SendInput(1, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }
    }
}
