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
                    else if (command.Kind == "text")
                    {
                        var bytes = Encoding.UTF8.GetBytes(command.Text ?? string.Empty);
                        XDrawString(_display, window.Window, window.Gc, command.X, command.Y, bytes, bytes.Length);
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
                    return new VmUiEvent("closed", string.Empty, 0, 0);
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
                        return new VmUiEvent("closed", string.Empty, 0, 0);
                    }

                    if (evt.Type == ClientMessage && evt.Client.Window == window.Window)
                    {
                        if (evt.Client.Data0 == _wmDeleteWindow)
                        {
                            TryCloseWindow(handle);
                            return new VmUiEvent("closed", string.Empty, 0, 0);
                        }
                    }

                    if (evt.Type == KeyPress && evt.Key.Window == window.Window)
                    {
                        return new VmUiEvent("key", string.Empty, evt.Key.X, evt.Key.Y);
                    }

                    if (evt.Type == ButtonPress && evt.Button.Window == window.Window)
                    {
                        return new VmUiEvent("click", string.Empty, evt.Button.X, evt.Button.Y);
                    }
                }

                return new VmUiEvent("none", string.Empty, 0, 0);
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
        private static extern int XDrawString(IntPtr display, IntPtr drawable, IntPtr gc, int x, int y, byte[] text, int length);

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
    }
}
