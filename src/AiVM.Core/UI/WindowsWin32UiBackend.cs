using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace AiVM.Core;

public partial class DefaultSyscallHost
{
    private sealed class WindowsWin32UiBackend
    {
        private const uint WsOverlappedWindow = 0x00CF0000;
        private const uint WsVisible = 0x10000000;
        private const int CwUseDefault = unchecked((int)0x80000000);
        private const uint WmDestroy = 0x0002;
        private const uint WmClose = 0x0010;
        private const uint WmLButtonDown = 0x0201;
        private const uint WmKeyDown = 0x0100;
        private const uint PmRemove = 0x0001;
        private const int ColorWindow = 5;
        private const int SwShow = 5;

        private static readonly object StaticLock = new();
        private static readonly Dictionary<IntPtr, WindowsWindowState> GlobalWindows = new();
        private static bool _classRegistered;
        private static readonly WndProcDelegate WndProcRef = WindowProc;
        private static readonly string WindowClassName = "AiLangUiWindowClass";

        private readonly object _lock = new();
        private readonly Dictionary<int, WindowsWindowState> _windows = new();

        public bool TryCreateWindow(int handle, string title, int width, int height)
        {
            EnsureClassRegistered();
            if (!_classRegistered)
            {
                return false;
            }

            var hwnd = CreateWindowEx(
                0,
                WindowClassName,
                title,
                WsOverlappedWindow | WsVisible,
                CwUseDefault,
                CwUseDefault,
                Math.Max(64, width),
                Math.Max(64, height),
                IntPtr.Zero,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            var state = new WindowsWindowState(hwnd);
            lock (_lock)
            {
                _windows[handle] = state;
            }

            lock (StaticLock)
            {
                GlobalWindows[hwnd] = state;
            }

            ShowWindow(hwnd, SwShow);
            UpdateWindow(hwnd);
            return true;
        }

        public bool TryBeginFrame(int handle)
        {
            lock (_lock)
            {
                if (_windows.TryGetValue(handle, out var state))
                {
                    state.Commands.Clear();
                    return true;
                }
            }

            return false;
        }

        public bool TryDrawRect(int handle, int x, int y, int width, int height, string color)
        {
            lock (_lock)
            {
                if (_windows.TryGetValue(handle, out var state))
                {
                    state.Commands.Add(UiDrawCommand.Rect(x, y, width, height, color));
                    return true;
                }
            }

            return false;
        }

        public bool TryDrawText(int handle, int x, int y, string text, string color, int size)
        {
            lock (_lock)
            {
                if (_windows.TryGetValue(handle, out var state))
                {
                    state.Commands.Add(UiDrawCommand.FromText(x, y, text, color, size));
                    return true;
                }
            }

            return false;
        }

        public bool TryPresent(int handle)
        {
            PumpMessages();
            lock (_lock)
            {
                if (!_windows.TryGetValue(handle, out var state))
                {
                    return false;
                }

                var hdc = GetDC(state.Hwnd);
                if (hdc == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    GetClientRect(state.Hwnd, out var clientRect);
                    var bgBrush = CreateSolidBrush(ToColorRef("#1e1e1e"));
                    try
                    {
                        FillRect(hdc, ref clientRect, bgBrush);
                    }
                    finally
                    {
                        DeleteObject(bgBrush);
                    }

                    foreach (var command in state.Commands)
                    {
                        if (command.Kind == "rect")
                        {
                            var rect = new RECT
                            {
                                Left = command.X,
                                Top = command.Y,
                                Right = command.X + command.Width,
                                Bottom = command.Y + command.Height
                            };
                            var brush = CreateSolidBrush(ToColorRef(command.Color));
                            try
                            {
                                FillRect(hdc, ref rect, brush);
                            }
                            finally
                            {
                                DeleteObject(brush);
                            }
                        }
                        else if (command.Kind == "text")
                        {
                            SetTextColor(hdc, ToColorRef(command.Color));
                            SetBkMode(hdc, 1);
                            _ = command.Size;
                            TextOut(hdc, command.X, command.Y, command.Text ?? string.Empty, (command.Text ?? string.Empty).Length);
                        }
                    }
                }
                finally
                {
                    ReleaseDC(state.Hwnd, hdc);
                }

                return true;
            }
        }

        public VmUiEvent PollEvent(int handle)
        {
            PumpMessages();
            lock (_lock)
            {
                if (!_windows.TryGetValue(handle, out var state))
                {
                    return new VmUiEvent("closed", string.Empty, 0, 0);
                }

                if (state.Events.Count > 0)
                {
                    return state.Events.Dequeue();
                }

                return new VmUiEvent("none", string.Empty, 0, 0);
            }
        }

        public bool TryCloseWindow(int handle)
        {
            WindowsWindowState? state;
            lock (_lock)
            {
                if (!_windows.Remove(handle, out state))
                {
                    return false;
                }
            }

            if (state is not null)
            {
                lock (StaticLock)
                {
                    GlobalWindows.Remove(state.Hwnd);
                }

                DestroyWindow(state.Hwnd);
                state.Events.Enqueue(new VmUiEvent("closed", string.Empty, 0, 0));
            }

            return true;
        }

        private static void EnsureClassRegistered()
        {
            if (_classRegistered)
            {
                return;
            }

            lock (StaticLock)
            {
                if (_classRegistered)
                {
                    return;
                }

                var hInstance = GetModuleHandle(null);
                var wc = new WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                    style = 0,
                    lpfnWndProc = WndProcRef,
                    cbClsExtra = 0,
                    cbWndExtra = 0,
                    hInstance = hInstance,
                    hIcon = IntPtr.Zero,
                    hCursor = IntPtr.Zero,
                    hbrBackground = (IntPtr)(ColorWindow + 1),
                    lpszMenuName = null,
                    lpszClassName = WindowClassName,
                    hIconSm = IntPtr.Zero
                };

                _classRegistered = RegisterClassEx(ref wc) != 0;
            }
        }

        private static void PumpMessages()
        {
            while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PmRemove))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            WindowsWindowState? state;
            lock (StaticLock)
            {
                GlobalWindows.TryGetValue(hwnd, out state);
            }

            if (state is not null)
            {
                if (msg == WmLButtonDown)
                {
                    var x = unchecked((short)(long)lParam);
                    var y = unchecked((short)((long)lParam >> 16));
                    state.Events.Enqueue(new VmUiEvent("click", string.Empty, x, y));
                }
                else if (msg == WmKeyDown)
                {
                    state.Events.Enqueue(new VmUiEvent("key", string.Empty, 0, 0));
                }
                else if (msg == WmClose || msg == WmDestroy)
                {
                    state.Events.Enqueue(new VmUiEvent("closed", string.Empty, 0, 0));
                    lock (StaticLock)
                    {
                        GlobalWindows.Remove(hwnd);
                    }
                }
            }

            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private static uint ToColorRef(string color)
        {
            if (string.IsNullOrWhiteSpace(color))
            {
                return 0x00ffffff;
            }

            if (color.StartsWith("#", StringComparison.Ordinal) && color.Length == 7)
            {
                var r = ParseHexByte(color, 1);
                var g = ParseHexByte(color, 3);
                var b = ParseHexByte(color, 5);
                return (uint)(b | (g << 8) | (r << 16));
            }

            return color.ToLowerInvariant() switch
            {
                "black" => 0x00000000,
                "red" => 0x000000ff,
                "green" => 0x0000ff00,
                "blue" => 0x00ff0000,
                _ => 0x00ffffff
            };
        }

        private static int ParseHexByte(string value, int start)
        {
            return int.Parse(value.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private sealed class WindowsWindowState
        {
            public WindowsWindowState(IntPtr hwnd)
            {
                Hwnd = hwnd;
                Commands = new List<UiDrawCommand>();
                Events = new Queue<VmUiEvent>();
            }

            public IntPtr Hwnd { get; }
            public List<UiDrawCommand> Commands { get; }
            public Queue<VmUiEvent> Events { get; }
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage([In] ref MSG lpmsg);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int FillRect(IntPtr hDC, [In] ref RECT lprc, IntPtr hbr);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(uint colorRef);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern uint SetTextColor(IntPtr hdc, uint color);

        [DllImport("gdi32.dll")]
        private static extern int SetBkMode(IntPtr hdc, int mode);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern bool TextOut(IntPtr hdc, int x, int y, string lpString, int c);
    }
}
