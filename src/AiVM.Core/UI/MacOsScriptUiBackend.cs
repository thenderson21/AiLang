using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AiVM.Core;

public partial class DefaultSyscallHost
{
    private sealed class MacOsScriptUiBackend
    {
        private readonly object _lock = new();
        private readonly Dictionary<int, MacWindowState> _windows = new();
        private static readonly object ScriptLock = new();
        private static string? _scriptPath;

        public bool TryCreateWindow(int handle, string title, int width, int height)
        {
            var scriptPath = EnsureRunnerScript();
            if (scriptPath is null)
            {
                return false;
            }

            var commandPath = Path.Combine(Path.GetTempPath(), $"ailang-mac-ui-{handle}.cmd");
            File.WriteAllText(commandPath, string.Empty);

            var info = new ProcessStartInfo("swift")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            info.ArgumentList.Add(scriptPath);
            info.ArgumentList.Add(commandPath);
            info.ArgumentList.Add(title);
            info.ArgumentList.Add(Math.Max(64, width).ToString(CultureInfo.InvariantCulture));
            info.ArgumentList.Add(Math.Max(64, height).ToString(CultureInfo.InvariantCulture));

            Process? process;
            try
            {
                process = Process.Start(info);
            }
            catch
            {
                return false;
            }

            if (process is null)
            {
                return false;
            }

            lock (_lock)
            {
                _windows[handle] = new MacWindowState(title, width, height, commandPath, process);
                return true;
            }
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
            MacWindowState? state;
            lock (_lock)
            {
                if (!_windows.TryGetValue(handle, out state))
                {
                    return false;
                }
            }

            try
            {
                if (state.Process.HasExited)
                {
                    return false;
                }

                File.WriteAllText(state.CommandPath, BuildCommandPayload(state), Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public VmUiEvent PollEvent(int handle)
        {
            lock (_lock)
            {
                if (!_windows.TryGetValue(handle, out var state))
                {
                    return new VmUiEvent("closed", string.Empty, 0, 0);
                }

                if (state.Process.HasExited)
                {
                    _windows.Remove(handle);
                    TryDeleteCommandFile(state.CommandPath);
                    return new VmUiEvent("closed", string.Empty, 0, 0);
                }

                return new VmUiEvent("none", string.Empty, 0, 0);
            }
        }

        public bool TryCloseWindow(int handle)
        {
            lock (_lock)
            {
                if (!_windows.Remove(handle, out var state))
                {
                    return false;
                }

                try
                {
                    if (!state.Process.HasExited)
                    {
                        state.Process.Kill(true);
                    }
                }
                catch
                {
                }

                TryDeleteCommandFile(state.CommandPath);
                return true;
            }
        }

        private static string BuildCommandPayload(MacWindowState state)
        {
            var sb = new StringBuilder(512);
            sb.Append("S|");
            sb.Append(state.Width.ToString(CultureInfo.InvariantCulture));
            sb.Append("|");
            sb.Append(state.Height.ToString(CultureInfo.InvariantCulture));
            sb.Append('\n');

            foreach (var command in state.Commands)
            {
                if (command.Kind == "rect")
                {
                    sb.Append("R|");
                    sb.Append(command.X.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.Y.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.Width.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.Height.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.Color);
                    sb.Append('\n');
                }
                else if (command.Kind == "text")
                {
                    var textBytes = Encoding.UTF8.GetBytes(command.Text ?? string.Empty);
                    var textBase64 = Convert.ToBase64String(textBytes);
                    sb.Append("T|");
                    sb.Append(command.X.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.Y.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.Size.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.Color);
                    sb.Append("|");
                    sb.Append(textBase64);
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        private static void TryDeleteCommandFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static string? EnsureRunnerScript()
        {
            lock (ScriptLock)
            {
                if (!string.IsNullOrWhiteSpace(_scriptPath) && File.Exists(_scriptPath))
                {
                    return _scriptPath;
                }

                var path = Path.Combine(Path.GetTempPath(), "ailang-macos-ui-runner.swift");
                var script = @"
import AppKit
import Foundation

struct Cmd {
    var kind: String
    var x: Int
    var y: Int
    var w: Int
    var h: Int
    var size: Int
    var color: String
    var text: String
}

final class CanvasView: NSView {
    var cmds: [Cmd] = []

    override var isFlipped: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        NSColor(calibratedWhite: 0.12, alpha: 1.0).setFill()
        dirtyRect.fill()

        for c in cmds {
            if c.kind == ""R"" {
                parseColor(c.color).setFill()
                NSRect(x: CGFloat(c.x), y: CGFloat(c.y), width: CGFloat(c.w), height: CGFloat(c.h)).fill()
            } else if c.kind == ""T"" {
                let attrs: [NSAttributedString.Key: Any] = [
                    .foregroundColor: parseColor(c.color),
                    .font: NSFont.monospacedSystemFont(ofSize: CGFloat(max(1, c.size)), weight: .regular)
                ]
                NSString(string: c.text).draw(at: NSPoint(x: CGFloat(c.x), y: CGFloat(c.y)), withAttributes: attrs)
            }
        }
    }
}

func parseColor(_ s: String) -> NSColor {
    if s.hasPrefix(""#"") && s.count == 7 {
        let raw = String(s.dropFirst())
        if let v = Int(raw, radix: 16) {
            let r = CGFloat((v >> 16) & 0xff) / 255.0
            let g = CGFloat((v >> 8) & 0xff) / 255.0
            let b = CGFloat(v & 0xff) / 255.0
            return NSColor(calibratedRed: r, green: g, blue: b, alpha: 1.0)
        }
    }
    switch s.lowercased() {
    case ""black"": return .black
    case ""red"": return .red
    case ""green"": return .green
    case ""blue"": return .blue
    default: return .white
    }
}

func loadCommands(path: String) -> [Cmd] {
    guard let data = try? String(contentsOfFile: path, encoding: .utf8) else { return [] }
    var out: [Cmd] = []
    for line in data.split(separator: ""\n"", omittingEmptySubsequences: true) {
        let parts = line.split(separator: ""|"", omittingEmptySubsequences: false).map(String.init)
        if parts.count == 0 { continue }
        if parts[0] == ""R"" && parts.count >= 6 {
            out.append(Cmd(kind: ""R"", x: Int(parts[1]) ?? 0, y: Int(parts[2]) ?? 0, w: Int(parts[3]) ?? 0, h: Int(parts[4]) ?? 0, size: 0, color: parts[5], text: """"))
        } else if parts[0] == ""T"" && parts.count >= 6 {
            let decoded = Data(base64Encoded: parts[5]).flatMap { String(data: $0, encoding: .utf8) } ?? """"
            out.append(Cmd(kind: ""T"", x: Int(parts[1]) ?? 0, y: Int(parts[2]) ?? 0, w: 0, h: 0, size: Int(parts[3]) ?? 14, color: parts[4], text: decoded))
        }
    }
    return out
}

guard CommandLine.arguments.count >= 5 else { exit(1) }
let commandPath = CommandLine.arguments[1]
let title = CommandLine.arguments[2]
let width = Int(CommandLine.arguments[3]) ?? 640
let height = Int(CommandLine.arguments[4]) ?? 360

let app = NSApplication.shared
app.setActivationPolicy(.regular)
let window = NSWindow(
    contentRect: NSRect(x: 120, y: 120, width: width, height: height),
    styleMask: [.titled, .closable, .miniaturizable, .resizable],
    backing: .buffered,
    defer: false)
window.title = title
let view = CanvasView(frame: NSRect(x: 0, y: 0, width: width, height: height))
window.contentView = view
window.makeKeyAndOrderFront(nil)
NSApp.activate(ignoringOtherApps: true)

_ = NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: window, queue: nil) { _ in
    NSApp.terminate(nil)
}

let timer = Timer.scheduledTimer(withTimeInterval: 0.10, repeats: true) { _ in
    view.cmds = loadCommands(path: commandPath)
    view.needsDisplay = true
}
RunLoop.current.add(timer, forMode: .default)
app.run()
";

                try
                {
                    File.WriteAllText(path, script, Encoding.UTF8);
                    _scriptPath = path;
                    return path;
                }
                catch
                {
                    return null;
                }
            }
        }

        private sealed class MacWindowState
        {
            public MacWindowState(string title, int width, int height, string commandPath, Process process)
            {
                Title = title;
                Width = width;
                Height = height;
                CommandPath = commandPath;
                Process = process;
                Commands = new List<UiDrawCommand>();
            }

            public string Title { get; }
            public int Width { get; }
            public int Height { get; }
            public string CommandPath { get; }
            public Process Process { get; }
            public List<UiDrawCommand> Commands { get; }
        }
    }
}
