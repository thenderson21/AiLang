using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace AiVM.Core;

public partial class DefaultSyscallHost
{
    private sealed class LinuxX11UiBackend
    {
        private const int Expose = 12;
        private const int KeyPress = 2;
        private const int ButtonPress = 4;
        private const int ClientMessage = 33;
        private const int DestroyNotify = 17;

        private const long KeyPressMask = 1L << 0;
        private const long ButtonPressMask = 1L << 2;
        private const long ExposureMask = 1L << 15;
        private const long StructureNotifyMask = 1L << 17;
        private const uint ShiftMask = 1;
        private const uint ControlMask = 4;
        private const uint Mod1Mask = 8;   // Alt
        private const uint Mod4Mask = 64;  // Meta/Super
        private const ulong XkBackSpace = 0xFF08;
        private const ulong XkTab = 0xFF09;
        private const ulong XkReturn = 0xFF0D;
        private const ulong XkEscape = 0xFF1B;
        private const ulong XkDelete = 0xFFFF;
        private const ulong XkLeft = 0xFF51;
        private const ulong XkUp = 0xFF52;
        private const ulong XkRight = 0xFF53;
        private const ulong XkDown = 0xFF54;
        private const ulong XkSpace = 0x0020;

        private readonly object _lock = new();
        private readonly Dictionary<int, LinuxWindowState> _windows = new();

        private readonly IntPtr _display;
        private readonly IntPtr _wmDeleteWindow;
        private readonly bool _available;

        public LinuxX11UiBackend()
        {
            try
            {
                _display = XOpenDisplay(IntPtr.Zero);
                _available = _display != IntPtr.Zero;
                if (_available)
                {
                    _wmDeleteWindow = XInternAtom(_display, "WM_DELETE_WINDOW", false);
                }
            }
            catch (DllNotFoundException)
            {
                _available = false;
                _display = IntPtr.Zero;
                _wmDeleteWindow = IntPtr.Zero;
            }
            catch (EntryPointNotFoundException)
            {
                _available = false;
                _display = IntPtr.Zero;
                _wmDeleteWindow = IntPtr.Zero;
            }
            catch (Win32Exception)
            {
                _available = false;
                _display = IntPtr.Zero;
                _wmDeleteWindow = IntPtr.Zero;
            }
        }

        public bool TryCreateWindow(int handle, string title, int width, int height)
        {
            if (!_available)
            {
                return false;
            }

            lock (_lock)
            {
                var screen = XDefaultScreen(_display);
                var root = XRootWindow(_display, screen);
                var white = XWhitePixel(_display, screen);
                var black = XBlackPixel(_display, screen);

                var window = XCreateSimpleWindow(_display, root, 0, 0, (uint)width, (uint)height, 1, black, white);
                if (window == IntPtr.Zero)
                {
                    return false;
                }

                XStoreName(_display, window, title);
                XSelectInput(_display, window, KeyPressMask | ButtonPressMask | ExposureMask | StructureNotifyMask);

                if (_wmDeleteWindow != IntPtr.Zero)
                {
                    var protocols = new[] { _wmDeleteWindow };
                    XSetWMProtocols(_display, window, protocols, protocols.Length);
                }

                var gc = XCreateGC(_display, window, IntPtr.Zero, IntPtr.Zero);
                if (gc == IntPtr.Zero)
                {
                    XDestroyWindow(_display, window);
                    return false;
                }

                _windows[handle] = new LinuxWindowState(window, gc, width, height);
                XMapWindow(_display, window);
                XFlush(_display);
                return true;
            }
        }

        public bool TryBeginFrame(int handle)
        {
            lock (_lock)
            {
                if (_windows.TryGetValue(handle, out var window))
                {
                    window.Commands.Clear();
                    return true;
                }
            }
            return false;
        }

        public bool TryDrawRect(int handle, int x, int y, int width, int height, string color)
        {
            lock (_lock)
            {
                if (_windows.TryGetValue(handle, out var window))
                {
                    window.Commands.Add(UiDrawCommand.Rect(x, y, width, height, color));
                    return true;
                }
            }
            return false;
        }

        public bool TryDrawText(int handle, int x, int y, string text, string color, int size)
        {
            lock (_lock)
            {
                if (_windows.TryGetValue(handle, out var window))
                {
                    window.Commands.Add(UiDrawCommand.FromText(x, y, text, color, size));
                    return true;
                }
            }
            return false;
        }

        public bool TryDrawLine(int handle, int x1, int y1, int x2, int y2, string color, int strokeWidth)
        {
            lock (_lock)
            {
                if (_windows.TryGetValue(handle, out var window))
                {
                    window.Commands.Add(UiDrawCommand.Line(x1, y1, x2, y2, color, strokeWidth));
                    return true;
                }
            }
            return false;
        }

        public bool TryDrawEllipse(int handle, int x, int y, int width, int height, string color)
        {
            lock (_lock)
            {
                if (_windows.TryGetValue(handle, out var window))
                {
                    window.Commands.Add(UiDrawCommand.Ellipse(x, y, width, height, color));
                    return true;
                }
            }
            return false;
        }

        public bool TryDrawImage(int handle, int x, int y, int width, int height, string rgbaBase64)
        {
            lock (_lock)
            {
                if (_windows.TryGetValue(handle, out var window))
                {
                    window.Commands.Add(UiDrawCommand.FromImage(x, y, width, height, rgbaBase64));
                    return true;
                }
            }

            return false;
        }

        public bool TryPresent(int handle)
        {
            lock (_lock)
            {
                if (!_windows.TryGetValue(handle, out var window))
                {
                    return false;
                }

                XClearWindow(_display, window.Window);
                foreach (var command in window.Commands)
                {
                    var pixel = ParseColorPixel(command.Color);
                    XSetForeground(_display, window.Gc, pixel);
                    if (command.Kind == "rect")
                    {
                        XFillRectangle(_display, window.Window, window.Gc, command.X, command.Y, (uint)command.Width, (uint)command.Height);
                    }
                    else if (command.Kind == "ellipse")
                    {
                        XFillArc(_display, window.Window, window.Gc, command.X, command.Y, (uint)command.Width, (uint)command.Height, 0, 360 * 64);
                    }
                    else if (command.Kind == "line")
                    {
                        XDrawLine(_display, window.Window, window.Gc, command.X, command.Y, command.X2, command.Y2);
                    }
                    else if (command.Kind == "text")
                    {
                        var bytes = Encoding.UTF8.GetBytes(command.Text ?? string.Empty);
                        XDrawString(_display, window.Window, window.Gc, command.X, command.Y, bytes, bytes.Length);
                    }
                    else if (command.Kind == "image")
                    {
                        DrawRgbaImage(window, command.X, command.Y, command.Width, command.Height, command.ImageRgbaBase64);
                    }
                }

                XFlush(_display);
                return true;
            }
        }

        public VmUiEvent PollEvent(int handle)
        {
            lock (_lock)
            {
                if (!_windows.TryGetValue(handle, out var window))
                {
                    return new VmUiEvent("closed", string.Empty, -1, -1, string.Empty, string.Empty, string.Empty, false);
                }

                while (XPending(_display) > 0)
                {
                    XNextEvent(_display, out var evt);
                    if (evt.Type == Expose && evt.Expose.Window == window.Window)
                    {
                        TryPresent(handle);
                        continue;
                    }

                    if (evt.Type == DestroyNotify && evt.Destroy.Window == window.Window)
                    {
                        _windows.Remove(handle);
                        return new VmUiEvent("closed", string.Empty, -1, -1, string.Empty, string.Empty, string.Empty, false);
                    }

                    if (evt.Type == ClientMessage && evt.Client.Window == window.Window)
                    {
                        if (evt.Client.Data0 == _wmDeleteWindow)
                        {
                            TryCloseWindow(handle);
                            return new VmUiEvent("closed", string.Empty, -1, -1, string.Empty, string.Empty, string.Empty, false);
                        }
                    }

                    if (evt.Type == KeyPress && evt.Key.Window == window.Window)
                    {
                        var key = BuildKeyName(evt.Key);
                        var text = BuildPrintableText(evt.Key);
                        return new VmUiEvent(
                            "key",
                            string.Empty,
                            -1,
                            -1,
                            key,
                            text,
                            BuildModifiers(evt.Key.State),
                            false);
                    }

                    if (evt.Type == ButtonPress && evt.Button.Window == window.Window)
                    {
                        return new VmUiEvent("click", string.Empty, evt.Button.X, evt.Button.Y, string.Empty, string.Empty, string.Empty, false);
                    }
                }

                return new VmUiEvent("none", string.Empty, -1, -1, string.Empty, string.Empty, string.Empty, false);
            }
        }

        public bool TryGetWindowSize(int handle, out int width, out int height)
        {
            width = -1;
            height = -1;
            lock (_lock)
            {
                if (!_windows.TryGetValue(handle, out var window))
                {
                    return false;
                }

                if (XGetWindowAttributes(_display, window.Window, out var attrs) == 0)
                {
                    return false;
                }

                width = Math.Max(0, attrs.Width);
                height = Math.Max(0, attrs.Height);
                return true;
            }
        }

        public bool TryCloseWindow(int handle)
        {
            lock (_lock)
            {
                if (!_windows.Remove(handle, out var window))
                {
                    return false;
                }

                XFreeGC(_display, window.Gc);
                XDestroyWindow(_display, window.Window);
                XFlush(_display);
                return true;
            }
        }

        private static ulong ParseColorPixel(string color)
        {
            if (string.IsNullOrWhiteSpace(color))
            {
                return 0x00ffffff;
            }

            if (color.StartsWith("#", StringComparison.Ordinal) && color.Length == 7)
            {
                var raw = color[1..];
                if (ulong.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var pixel))
                {
                    return pixel;
                }
            }

            return color.ToLowerInvariant() switch
            {
                "black" => 0x00000000,
                "red" => 0x00ff0000,
                "green" => 0x0000ff00,
                "blue" => 0x000000ff,
                _ => 0x00ffffff
            };
        }

        private void DrawRgbaImage(LinuxWindowState window, int x, int y, int width, int height, string rgbaBase64)
        {
            if (width <= 0 || height <= 0 || string.IsNullOrWhiteSpace(rgbaBase64))
            {
                return;
            }

            byte[] rgba;
            try
            {
                rgba = Convert.FromBase64String(rgbaBase64);
            }
            catch
            {
                return;
            }

            var expectedBytes = (long)width * height * 4;
            if (expectedBytes <= 0 || rgba.Length < expectedBytes)
            {
                return;
            }

            var index = 0;
            for (var py = 0; py < height; py++)
            {
                for (var px = 0; px < width; px++)
                {
                    var r = rgba[index];
                    var g = rgba[index + 1];
                    var b = rgba[index + 2];
                    index += 4;
                    XSetForeground(_display, window.Gc, (ulong)((r << 16) | (g << 8) | b));
                    XDrawPoint(_display, window.Window, window.Gc, x + px, y + py);
                }
            }
        }

        private static string BuildModifiers(uint state)
        {
            var values = new List<string>(4);
            if ((state & Mod1Mask) != 0)
            {
                values.Add("alt");
            }
            if ((state & ControlMask) != 0)
            {
                values.Add("ctrl");
            }
            if ((state & Mod4Mask) != 0)
            {
                values.Add("meta");
            }
            if ((state & ShiftMask) != 0)
            {
                values.Add("shift");
            }

            return values.Count == 0 ? string.Empty : string.Join(',', values);
        }

        private static string BuildKeyName(XKeyEvent evt)
        {
            var keySym = LookupKeySym(evt, out var text);
            if (!string.IsNullOrEmpty(text) && text.Length == 1 && char.IsLetterOrDigit(text[0]))
            {
                return char.ToLowerInvariant(text[0]).ToString();
            }

            if (!string.IsNullOrEmpty(text) && string.Equals(text, "`", StringComparison.Ordinal))
            {
                return "`";
            }

            return keySym switch
            {
                XkBackSpace => "backspace",
                XkTab => "tab",
                XkReturn => "enter",
                XkEscape => "escape",
                XkDelete => "delete",
                XkLeft => "left",
                XkUp => "up",
                XkRight => "right",
                XkDown => "down",
                XkSpace => "space",
                _ => $"x11:{evt.Keycode}"
            };
        }

        private static string BuildPrintableText(XKeyEvent evt)
        {
            _ = LookupKeySym(evt, out var text);
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            foreach (var rune in text.EnumerateRunes())
            {
                if (Rune.IsControl(rune))
                {
                    return string.Empty;
                }
            }

            return text;
        }

        private static ulong LookupKeySym(XKeyEvent evt, out string text)
        {
            var keyEvent = evt;
            var buffer = new StringBuilder(16);
            var written = XLookupString(ref keyEvent, buffer, buffer.Capacity, out var keySym, IntPtr.Zero);
            text = written > 0 ? buffer.ToString(0, written) : string.Empty;
            return unchecked((ulong)keySym.ToInt64());
        }

        private sealed class LinuxWindowState
        {
            public LinuxWindowState(IntPtr window, IntPtr gc, int width, int height)
            {
                Window = window;
                Gc = gc;
                Width = width;
                Height = height;
                Commands = new List<UiDrawCommand>();
            }

            public IntPtr Window { get; }
            public IntPtr Gc { get; }
            public int Width { get; }
            public int Height { get; }
            public List<UiDrawCommand> Commands { get; }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct XEvent
        {
            [FieldOffset(0)] public int Type;
            [FieldOffset(0)] public XExposeEvent Expose;
            [FieldOffset(0)] public XDestroyWindowEvent Destroy;
            [FieldOffset(0)] public XKeyEvent Key;
            [FieldOffset(0)] public XButtonEvent Button;
            [FieldOffset(0)] public XClientMessageEvent Client;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XExposeEvent
        {
            public int Type;
            public IntPtr Serial;
            public int SendEvent;
            public IntPtr Display;
            public IntPtr Window;
            public int X;
            public int Y;
            public int Width;
            public int Height;
            public int Count;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XDestroyWindowEvent
        {
            public int Type;
            public IntPtr Serial;
            public int SendEvent;
            public IntPtr Display;
            public IntPtr Event;
            public IntPtr Window;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XKeyEvent
        {
            public int Type;
            public IntPtr Serial;
            public int SendEvent;
            public IntPtr Display;
            public IntPtr Window;
            public IntPtr Root;
            public IntPtr Subwindow;
            public IntPtr Time;
            public int X;
            public int Y;
            public int XRoot;
            public int YRoot;
            public uint State;
            public uint Keycode;
            public int SameScreen;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XButtonEvent
        {
            public int Type;
            public IntPtr Serial;
            public int SendEvent;
            public IntPtr Display;
            public IntPtr Window;
            public IntPtr Root;
            public IntPtr Subwindow;
            public IntPtr Time;
            public int X;
            public int Y;
            public int XRoot;
            public int YRoot;
            public uint State;
            public uint Button;
            public int SameScreen;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XClientMessageEvent
        {
            public int Type;
            public IntPtr Serial;
            public int SendEvent;
            public IntPtr Display;
            public IntPtr Window;
            public IntPtr MessageType;
            public int Format;
            public IntPtr Data0;
            public IntPtr Data1;
            public IntPtr Data2;
            public IntPtr Data3;
            public IntPtr Data4;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XWindowAttributes
        {
            public int X;
            public int Y;
            public int Width;
            public int Height;
            public int BorderWidth;
            public int Depth;
            public IntPtr Visual;
            public IntPtr Root;
            public int Class;
            public int BitGravity;
            public int WinGravity;
            public int BackingStore;
            public ulong BackingPlanes;
            public ulong BackingPixel;
            public int SaveUnder;
            public IntPtr Colormap;
            public int MapInstalled;
            public int MapState;
            public long AllEventMasks;
            public long YourEventMask;
            public long DoNotPropagateMask;
            public int OverrideRedirect;
            public IntPtr Screen;
        }

        [DllImport("libX11.so.6")]
        private static extern IntPtr XOpenDisplay(IntPtr displayName);

        [DllImport("libX11.so.6")]
        private static extern int XDefaultScreen(IntPtr display);

        [DllImport("libX11.so.6")]
        private static extern IntPtr XRootWindow(IntPtr display, int screenNumber);

        [DllImport("libX11.so.6")]
        private static extern ulong XWhitePixel(IntPtr display, int screenNumber);

        [DllImport("libX11.so.6")]
        private static extern ulong XBlackPixel(IntPtr display, int screenNumber);

        [DllImport("libX11.so.6")]
        private static extern IntPtr XCreateSimpleWindow(
            IntPtr display,
            IntPtr parent,
            int x,
            int y,
            uint width,
            uint height,
            uint borderWidth,
            ulong border,
            ulong background);

        [DllImport("libX11.so.6")]
        private static extern int XStoreName(IntPtr display, IntPtr window, string windowName);

        [DllImport("libX11.so.6")]
        private static extern int XSelectInput(IntPtr display, IntPtr window, long eventMask);

        [DllImport("libX11.so.6")]
        private static extern int XMapWindow(IntPtr display, IntPtr window);

        [DllImport("libX11.so.6")]
        private static extern IntPtr XCreateGC(IntPtr display, IntPtr drawable, IntPtr valuemask, IntPtr values);

        [DllImport("libX11.so.6")]
        private static extern int XSetForeground(IntPtr display, IntPtr gc, ulong foreground);

        [DllImport("libX11.so.6")]
        private static extern int XFillRectangle(IntPtr display, IntPtr drawable, IntPtr gc, int x, int y, uint width, uint height);

        [DllImport("libX11.so.6")]
        private static extern int XFillArc(IntPtr display, IntPtr drawable, IntPtr gc, int x, int y, uint width, uint height, int angle1, int angle2);

        [DllImport("libX11.so.6")]
        private static extern int XDrawLine(IntPtr display, IntPtr drawable, IntPtr gc, int x1, int y1, int x2, int y2);

        [DllImport("libX11.so.6")]
        private static extern int XDrawString(IntPtr display, IntPtr drawable, IntPtr gc, int x, int y, byte[] text, int length);

        [DllImport("libX11.so.6")]
        private static extern int XDrawPoint(IntPtr display, IntPtr drawable, IntPtr gc, int x, int y);

        [DllImport("libX11.so.6")]
        private static extern int XClearWindow(IntPtr display, IntPtr window);

        [DllImport("libX11.so.6")]
        private static extern int XDestroyWindow(IntPtr display, IntPtr window);

        [DllImport("libX11.so.6")]
        private static extern int XFlush(IntPtr display);

        [DllImport("libX11.so.6")]
        private static extern int XPending(IntPtr display);

        [DllImport("libX11.so.6")]
        private static extern int XNextEvent(IntPtr display, out XEvent xevent);

        [DllImport("libX11.so.6")]
        private static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

        [DllImport("libX11.so.6")]
        private static extern int XSetWMProtocols(IntPtr display, IntPtr window, IntPtr[] protocols, int count);

        [DllImport("libX11.so.6")]
        private static extern int XFreeGC(IntPtr display, IntPtr gc);

        [DllImport("libX11.so.6")]
        private static extern int XGetWindowAttributes(IntPtr display, IntPtr window, out XWindowAttributes windowAttributes);

        [DllImport("libX11.so.6")]
        private static extern int XLookupString(
            ref XKeyEvent eventStruct,
            [Out] StringBuilder bufferReturn,
            int bytesBuffer,
            out IntPtr keysymReturn,
            IntPtr statusInOut);
    }
}
