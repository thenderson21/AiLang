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
            var eventPath = Path.Combine(Path.GetTempPath(), $"ailang-mac-ui-{handle}.evt");
            var sizePath = Path.Combine(Path.GetTempPath(), $"ailang-mac-ui-{handle}.siz");
            File.WriteAllText(commandPath, string.Empty);
            File.WriteAllText(eventPath, string.Empty);
            File.WriteAllText(sizePath, $"{Math.Max(64, width).ToString(CultureInfo.InvariantCulture)}|{Math.Max(64, height).ToString(CultureInfo.InvariantCulture)}", Encoding.UTF8);

            var info = new ProcessStartInfo("swift")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            info.ArgumentList.Add(scriptPath);
            info.ArgumentList.Add(commandPath);
            info.ArgumentList.Add(eventPath);
            info.ArgumentList.Add(sizePath);
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
                _windows[handle] = new MacWindowState(title, width, height, commandPath, eventPath, sizePath, process);
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

        public bool TryDrawLine(int handle, int x1, int y1, int x2, int y2, string color, int strokeWidth)
        {
            lock (_lock)
            {
                if (_windows.TryGetValue(handle, out var state))
                {
                    state.Commands.Add(UiDrawCommand.Line(x1, y1, x2, y2, color, strokeWidth));
                    return true;
                }
            }

            return false;
        }

        public bool TryDrawEllipse(int handle, int x, int y, int width, int height, string color)
        {
            lock (_lock)
            {
                if (_windows.TryGetValue(handle, out var state))
                {
                    state.Commands.Add(UiDrawCommand.Ellipse(x, y, width, height, color));
                    return true;
                }
            }

            return false;
        }

        public bool TryDrawImage(int handle, int x, int y, int width, int height, string rgbaBase64)
        {
            lock (_lock)
            {
                if (_windows.TryGetValue(handle, out var state))
                {
                    state.Commands.Add(UiDrawCommand.FromImage(x, y, width, height, rgbaBase64));
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
                    return new VmUiEvent("closed", string.Empty, -1, -1, string.Empty, string.Empty, string.Empty, false);
                }

                if (state.Process.HasExited)
                {
                    _windows.Remove(handle);
                    TryDeleteFile(state.CommandPath);
                    TryDeleteFile(state.EventPath);
                    TryDeleteFile(state.SizePath);
                    return new VmUiEvent("closed", string.Empty, -1, -1, string.Empty, string.Empty, string.Empty, false);
                }

                if (TryReadQueuedEvent(state.EventPath, out var queued))
                {
                    return queued;
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
                if (!_windows.TryGetValue(handle, out var state))
                {
                    return false;
                }

                if (TryReadWindowSize(state.SizePath, out var liveWidth, out var liveHeight))
                {
                    state.Width = liveWidth;
                    state.Height = liveHeight;
                }

                width = Math.Max(0, state.Width);
                height = Math.Max(0, state.Height);
                return true;
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

                TryDeleteFile(state.CommandPath);
                TryDeleteFile(state.EventPath);
                TryDeleteFile(state.SizePath);
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
                else if (command.Kind == "line")
                {
                    sb.Append("L|");
                    sb.Append(command.X.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.Y.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.X2.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.Y2.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.StrokeWidth.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.Color);
                    sb.Append('\n');
                }
                else if (command.Kind == "ellipse")
                {
                    sb.Append("E|");
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
                else if (command.Kind == "image")
                {
                    sb.Append("I|");
                    sb.Append(command.X.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.Y.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.Width.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.Height.ToString(CultureInfo.InvariantCulture));
                    sb.Append("|");
                    sb.Append(command.ImageRgbaBase64);
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        private static bool TryReadQueuedEvent(string eventPath, out VmUiEvent evt)
        {
            evt = default;
            string[] lines;
            try
            {
                if (!File.Exists(eventPath))
                {
                    return false;
                }

                lines = File.ReadAllLines(eventPath, Encoding.UTF8);
            }
            catch
            {
                return false;
            }

            if (lines.Length == 0)
            {
                return false;
            }

            var first = lines[0].Trim();
            var rest = new string[Math.Max(0, lines.Length - 1)];
            for (var i = 1; i < lines.Length; i++)
            {
                rest[i - 1] = lines[i];
            }

            try
            {
                File.WriteAllLines(eventPath, rest, Encoding.UTF8);
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(first))
            {
                return false;
            }

            if (string.Equals(first, "X", StringComparison.Ordinal))
            {
                evt = new VmUiEvent("closed", string.Empty, -1, -1, string.Empty, string.Empty, string.Empty, false);
                return true;
            }

            var parts = first.Split('|', StringSplitOptions.None);
            if (parts.Length == 0)
            {
                return false;
            }

            if (string.Equals(parts[0], "C", StringComparison.Ordinal) && parts.Length >= 4)
            {
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
                    !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
                {
                    return false;
                }

                evt = new VmUiEvent("click", string.Empty, x, y, string.Empty, string.Empty, parts[3], false);
                return true;
            }

            if (string.Equals(parts[0], "K", StringComparison.Ordinal) && parts.Length >= 5)
            {
                var key = DecodeBase64Utf8(parts[1]);
                var text = DecodeBase64Utf8(parts[2]);
                var modifiers = parts[3];
                var repeat = string.Equals(parts[4], "1", StringComparison.Ordinal);
                evt = new VmUiEvent("key", string.Empty, -1, -1, key, text, modifiers, repeat);
                return true;
            }

            return false;
        }

        private static string DecodeBase64Utf8(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            try
            {
                var bytes = Convert.FromBase64String(value);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryReadWindowSize(string sizePath, out int width, out int height)
        {
            width = -1;
            height = -1;
            try
            {
                if (!File.Exists(sizePath))
                {
                    return false;
                }

                var content = File.ReadAllText(sizePath, Encoding.UTF8).Trim();
                if (string.IsNullOrEmpty(content))
                {
                    return false;
                }

                var parts = content.Split('|', StringSplitOptions.None);
                if (parts.Length < 2)
                {
                    return false;
                }

                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedWidth) ||
                    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedHeight))
                {
                    return false;
                }

                width = Math.Max(0, parsedWidth);
                height = Math.Max(0, parsedHeight);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryDeleteFile(string path)
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

                var path = Path.Combine(Path.GetTempPath(), "ailang-macos-ui-runner-v4.swift");
                var script = @"
import AppKit
import Foundation

struct Cmd {
    var kind: String
    var x: Int
    var y: Int
    var w: Int
    var h: Int
    var x2: Int
    var y2: Int
    var stroke: Int
    var size: Int
    var color: String
    var text: String
    var image: String
}

func encodeBase64(_ value: String) -> String {
    return Data(value.utf8).base64EncodedString()
}

func appendEvent(path: String, line: String) {
    let payload = line + ""\n""
    guard let data = payload.data(using: .utf8) else { return }

    if !FileManager.default.fileExists(atPath: path) {
        FileManager.default.createFile(atPath: path, contents: Data(), attributes: nil)
    }

    do {
        let handle = try FileHandle(forWritingTo: URL(fileURLWithPath: path))
        defer { try? handle.close() }
        try handle.seekToEnd()
        try handle.write(contentsOf: data)
    } catch {
    }
}

func writeWindowSize(path: String, width: Int, height: Int) {
    let payload = ""\(max(0, width))|\(max(0, height))""
    do {
        try payload.write(toFile: path, atomically: true, encoding: .utf8)
    } catch {
    }
}

func modifierText(_ flags: NSEvent.ModifierFlags) -> String {
    var values: [String] = []
    if flags.contains(.option) { values.append(""alt"") }
    if flags.contains(.control) { values.append(""ctrl"") }
    if flags.contains(.command) { values.append(""meta"") }
    if flags.contains(.shift) { values.append(""shift"") }
    return values.joined(separator: "","")
}

func canonicalMacKeyToken(_ event: NSEvent) -> String {
    let raw = (event.charactersIgnoringModifiers ?? """").lowercased()
    if raw == ""\u{7f}"" { return ""backspace"" }
    if raw == ""\t"" { return ""tab"" }
    if raw == ""\r"" { return ""enter"" }
    if raw == "" "" { return ""space"" }
    if raw == ""\u{1b}"" { return ""escape"" }
    if raw.count == 1 {
        return raw
    }

    switch event.keyCode {
    case 51: return ""backspace""
    case 117: return ""delete""
    case 123: return ""left""
    case 124: return ""right""
    case 125: return ""down""
    case 126: return ""up""
    case 36, 76: return ""enter""
    case 48: return ""tab""
    case 53: return ""escape""
    case 49: return ""space""
    default: return ""mac:\(event.keyCode)""
    }
}

func printableText(_ value: String) -> String {
    if value.isEmpty {
        return """"
    }

    for scalar in value.unicodeScalars {
        if CharacterSet.controlCharacters.contains(scalar) {
            return """"
        }
    }

    return value
}

final class CanvasView: NSView {
    var cmds: [Cmd] = []
    let eventPath: String
    let sizePath: String

    init(frame frameRect: NSRect, eventPath: String, sizePath: String) {
        self.eventPath = eventPath
        self.sizePath = sizePath
        super.init(frame: frameRect)
    }

    required init?(coder: NSCoder) {
        return nil
    }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        NSColor(calibratedWhite: 0.12, alpha: 1.0).setFill()
        dirtyRect.fill()

        for c in cmds {
            if c.kind == ""R"" {
                parseColor(c.color).setFill()
                NSRect(x: CGFloat(c.x), y: CGFloat(c.y), width: CGFloat(c.w), height: CGFloat(c.h)).fill()
            } else if c.kind == ""E"" {
                parseColor(c.color).setFill()
                NSBezierPath(ovalIn: NSRect(x: CGFloat(c.x), y: CGFloat(c.y), width: CGFloat(c.w), height: CGFloat(c.h))).fill()
            } else if c.kind == ""L"" {
                parseColor(c.color).setStroke()
                let line = NSBezierPath()
                line.lineWidth = CGFloat(max(1, c.stroke))
                line.move(to: NSPoint(x: CGFloat(c.x), y: CGFloat(c.y)))
                line.line(to: NSPoint(x: CGFloat(c.x2), y: CGFloat(c.y2)))
                line.stroke()
            } else if c.kind == ""T"" {
                let attrs: [NSAttributedString.Key: Any] = [
                    .foregroundColor: parseColor(c.color),
                    .font: NSFont.monospacedSystemFont(ofSize: CGFloat(max(1, c.size)), weight: .regular)
                ]
                NSString(string: c.text).draw(at: NSPoint(x: CGFloat(c.x), y: CGFloat(c.y)), withAttributes: attrs)
            } else if c.kind == ""I"" {
                if c.w <= 0 || c.h <= 0 {
                    continue
                }

                guard let bytes = Data(base64Encoded: c.image) else {
                    continue
                }

                let expected = c.w * c.h * 4
                if bytes.count < expected {
                    continue
                }

                let ptr = [UInt8](bytes)
                var idx = 0
                for py in 0..<c.h {
                    for px in 0..<c.w {
                        if idx + 3 >= ptr.count {
                            break
                        }

                        let r = CGFloat(ptr[idx]) / 255.0
                        let g = CGFloat(ptr[idx + 1]) / 255.0
                        let b = CGFloat(ptr[idx + 2]) / 255.0
                        let a = CGFloat(ptr[idx + 3]) / 255.0
                        idx += 4
                        NSColor(calibratedRed: r, green: g, blue: b, alpha: a).setFill()
                        NSRect(x: CGFloat(c.x + px), y: CGFloat(c.y + py), width: 1, height: 1).fill()
                    }
                }
            }
        }
    }

    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        let x = Int(point.x.rounded())
        let y = Int(point.y.rounded())
        let mods = modifierText(event.modifierFlags)
        appendEvent(path: eventPath, line: ""C|\(x)|\(y)|\(mods)"")
    }

    override func keyDown(with event: NSEvent) {
        let keyOut = canonicalMacKeyToken(event)
        let textOut = printableText(event.characters ?? """")
        let mods = modifierText(event.modifierFlags)
        let repeatFlag = event.isARepeat ? ""1"" : ""0""
        let keyB64 = encodeBase64(keyOut)
        let textB64 = encodeBase64(textOut)
        appendEvent(path: eventPath, line: ""K|\(keyB64)|\(textB64)|\(mods)|\(repeatFlag)"")
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
            out.append(Cmd(kind: ""R"", x: Int(parts[1]) ?? 0, y: Int(parts[2]) ?? 0, w: Int(parts[3]) ?? 0, h: Int(parts[4]) ?? 0, x2: 0, y2: 0, stroke: 1, size: 0, color: parts[5], text: """", image: """"))
        } else if parts[0] == ""E"" && parts.count >= 6 {
            out.append(Cmd(kind: ""E"", x: Int(parts[1]) ?? 0, y: Int(parts[2]) ?? 0, w: Int(parts[3]) ?? 0, h: Int(parts[4]) ?? 0, x2: 0, y2: 0, stroke: 1, size: 0, color: parts[5], text: """", image: """"))
        } else if parts[0] == ""L"" && parts.count >= 7 {
            out.append(Cmd(kind: ""L"", x: Int(parts[1]) ?? 0, y: Int(parts[2]) ?? 0, w: 0, h: 0, x2: Int(parts[3]) ?? 0, y2: Int(parts[4]) ?? 0, stroke: Int(parts[5]) ?? 1, size: 0, color: parts[6], text: """", image: """"))
        } else if parts[0] == ""T"" && parts.count >= 6 {
            let decoded = Data(base64Encoded: parts[5]).flatMap { String(data: $0, encoding: .utf8) } ?? """"
            out.append(Cmd(kind: ""T"", x: Int(parts[1]) ?? 0, y: Int(parts[2]) ?? 0, w: 0, h: 0, x2: 0, y2: 0, stroke: 1, size: Int(parts[3]) ?? 14, color: parts[4], text: decoded, image: """"))
        } else if parts[0] == ""I"" && parts.count >= 6 {
            out.append(Cmd(kind: ""I"", x: Int(parts[1]) ?? 0, y: Int(parts[2]) ?? 0, w: Int(parts[3]) ?? 0, h: Int(parts[4]) ?? 0, x2: 0, y2: 0, stroke: 1, size: 0, color: ""#ffffff"", text: """", image: parts[5]))
        }
    }
    return out
}

guard CommandLine.arguments.count >= 7 else { exit(1) }
let commandPath = CommandLine.arguments[1]
let eventPath = CommandLine.arguments[2]
let sizePath = CommandLine.arguments[3]
let title = CommandLine.arguments[4]
let width = Int(CommandLine.arguments[5]) ?? 640
let height = Int(CommandLine.arguments[6]) ?? 360

let app = NSApplication.shared
app.setActivationPolicy(.regular)
let window = NSWindow(
    contentRect: NSRect(x: 120, y: 120, width: width, height: height),
    styleMask: [.titled, .closable, .miniaturizable, .resizable],
    backing: .buffered,
    defer: false)
window.title = title
let view = CanvasView(frame: NSRect(x: 0, y: 0, width: width, height: height), eventPath: eventPath, sizePath: sizePath)
window.contentView = view
window.makeKeyAndOrderFront(nil)
window.makeFirstResponder(view)
NSApp.activate(ignoringOtherApps: true)
writeWindowSize(path: sizePath, width: width, height: height)

_ = NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: window, queue: nil) { _ in
    appendEvent(path: eventPath, line: ""X"")
    NSApp.terminate(nil)
}

let timer = Timer.scheduledTimer(withTimeInterval: 0.10, repeats: true) { _ in
    view.cmds = loadCommands(path: commandPath)
    if let contentView = window.contentView {
        let bounds = contentView.bounds
        writeWindowSize(path: sizePath, width: Int(bounds.width.rounded()), height: Int(bounds.height.rounded()))
    }
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
            public MacWindowState(string title, int width, int height, string commandPath, string eventPath, string sizePath, Process process)
            {
                Title = title;
                Width = width;
                Height = height;
                CommandPath = commandPath;
                EventPath = eventPath;
                SizePath = sizePath;
                Process = process;
                Commands = new List<UiDrawCommand>();
            }

            public string Title { get; }
            public int Width { get; set; }
            public int Height { get; set; }
            public string CommandPath { get; }
            public string EventPath { get; }
            public string SizePath { get; }
            public Process Process { get; }
            public List<UiDrawCommand> Commands { get; }
        }
    }
}
