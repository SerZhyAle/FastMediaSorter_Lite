<#
.SYNOPSIS
    The Win32 interop layer of the DynAudit stand: the ping, key, clipboard, GDI, window
    and power primitives every probe is built on. No product code is touched or patched;
    everything here measures from outside.

.DESCRIPTION
    One Add-Type per process, compiled once and cached by .NET. Every function is safe to
    call from the harness process only; none of them modify the target process. The single
    exception by design is SendKey, which injects into the session (the stand is allowed to
    own windows and inject input - specification 209 section 4; scenes therefore run only on
    the isolated account with no other work open in that session).
#>
#requires -Version 5.1
$script:DynAuditNativeTypeName = 'DynAudit.Native'

function Use-DynAuditNative {
    if (-not ('DynAudit.Native' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DynAudit
{
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static class Native
    {
        public const uint WM_NULL = 0x0000;
        public const uint WM_CLOSE = 0x0010;
        public const uint WM_APP = 0x8000;
        public const uint SMTO_ABORTIFHUNG = 0x0002;
        public const uint SMTO_BLOCK = 0x0001;
        public const uint CF_UNICODETEXT = 13;
        public const uint GR_GDIOBJECTS = 0;
        public const uint GR_USEROBJECTS = 1;
        public const uint PROCESS_QUERY_INFORMATION = 0x0400;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
            uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static bool Minimize(IntPtr hwnd) { return ShowWindow(hwnd, 6); }   // SW_MINIMIZE
        public static bool RestoreW(IntPtr hwnd) { return ShowWindow(hwnd, 9); } // SW_RESTORE

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        // Renders the window itself (PW_RENDERFULLCONTENT: the DWM frame too, caption
        // included) into the caller's bitmap - no screen coordinates, no foreground
        // requirement, no covering window in the way.
        public static bool PrintWindowFull(IntPtr hwnd, IntPtr hdc) { return PrintWindow(hwnd, hdc, 2); }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool LockWorkStation();

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern bool SetSuspendState(bool Hibernate, bool ForceCritical, bool DisableWakeEvent);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern UIntPtr GlobalSize(IntPtr hMem);

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public KEYBDINPUT ki;
            public uint pad1, pad2;
        }

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // --- ping -------------------------------------------------------------------
        // SendMessageTimeout(WM_NULL, SMTO_ABORTIFHUNG): 0 means the window did not answer
        // in time - either hung or the timeout hit. The wall time of the call is measured
        // by the caller with a stopwatch.
        public static bool Ping(IntPtr hwnd, uint timeoutMs)
        {
            IntPtr result;
            IntPtr ret = SendMessageTimeout(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero,
                SMTO_ABORTIFHUNG, timeoutMs, out result);
            return ret != IntPtr.Zero;
        }

        public static bool PostClose(IntPtr hwnd) { return PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }

        public static bool SendAppMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            return SendMessage(hwnd, msg, wParam, lParam);
        }

        // --- injected key -------------------------------------------------------------
        public static uint SendKey(ushort vk)
        {
            INPUT[] down = new INPUT[1];
            down[0].type = INPUT_KEYBOARD;
            down[0].ki.wVk = vk;
            INPUT[] up = new INPUT[1];
            up[0].type = INPUT_KEYBOARD;
            up[0].ki.wVk = vk;
            up[0].ki.dwFlags = KEYEVENTF_KEYUP;
            uint n = SendInput(1, down, Marshal.SizeOf(typeof(INPUT)));
            n += SendInput(1, up, Marshal.SizeOf(typeof(INPUT)));
            return n;
        }

        // --- window text and class ----------------------------------------------------
        public static string GetText(IntPtr hwnd)
        {
            var sb = new StringBuilder(512);
            int n = GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString(0, n);
        }

        public static string GetClass(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            int n = GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString(0, n);
        }

        public static uint GetStyle(IntPtr hwnd) { return GetWindowLong(hwnd, -16); }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        public static RECT Rect(IntPtr hwnd) { RECT r; GetWindowRect(hwnd, out r); return r; }

        public static IntPtr[] ChildrenOf(IntPtr hwnd)
        {
            var list = new List<IntPtr>();
            EnumChildWindows(hwnd, delegate(IntPtr h, IntPtr l) { list.Add(h); return true; }, IntPtr.Zero);
            return list.ToArray();
        }

        // Every visible top-level window of one process: the dialog scan of the err probe
        // walks these - a window that is not the main one and carries text is a dialog.
        public static IntPtr[] TopWindowsOf(uint pid)
        {
            var list = new List<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (ProcessIdOf(h) == pid) list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list.ToArray();
        }

        public static uint ProcessIdOf(IntPtr hwnd) { uint pid; GetWindowThreadProcessId(hwnd, out pid); return pid; }

        // --- process handles and GUI resources -----------------------------------------
        public static IntPtr OpenProc(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
            if (h == IntPtr.Zero) h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            return h;
        }

        public static void CloseProc(IntPtr h) { if (h != IntPtr.Zero) CloseHandle(h); }

        public static uint GuiResources(IntPtr hProcess, uint kind)
        {
            uint n = GetGuiResources(hProcess, kind);
            return n;   // 0xFFFFFFFF on failure; the caller treats 0xFFFFFFFF as not-read
        }

        // --- clipboard -------------------------------------------------------------------
        public static bool OpenClip() { return OpenClipboard(IntPtr.Zero); }
        public static bool CloseClip() { return CloseClipboard(); }
        public static bool EmptyClip() { return EmptyClipboard(); }

        public static string GetClipText()
        {
            if (!OpenClipboard(IntPtr.Zero)) return null;
            try
            {
                IntPtr h = GetClipboardData(CF_UNICODETEXT);
                if (h == IntPtr.Zero) return null;
                IntPtr p = GlobalLock(h);
                if (p == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(p); }
                finally { GlobalUnlock(h); }
            }
            finally { CloseClipboard(); }
        }

        public static bool SetClipText(string text)
        {
            if (!OpenClipboard(IntPtr.Zero)) return false;
            try
            {
                if (!EmptyClipboard()) return false;
                byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
                IntPtr h = GlobalAlloc(0x0002, (UIntPtr)bytes.Length);   // GMEM_MOVEABLE
                if (h == IntPtr.Zero) return false;
                IntPtr p = GlobalLock(h);
                if (p == IntPtr.Zero) return false;
                try { Marshal.Copy(bytes, 0, p, bytes.Length); }
                finally { GlobalUnlock(h); }
                return SetClipboardData(CF_UNICODETEXT, h) != IntPtr.Zero;
            }
            finally { CloseClipboard(); }
        }
    }
}
'@ -ErrorAction Stop
    }
}
