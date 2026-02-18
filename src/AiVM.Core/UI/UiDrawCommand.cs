using System.Globalization;

namespace AiVM.Core;

public partial class DefaultSyscallHost
{
    private sealed class UiDrawCommand
    {
        private UiDrawCommand(
            string kind,
            int x,
            int y,
            int width,
            int height,
            string color,
            string text,
            int size,
            int x2,
            int y2,
            string path,
            int strokeWidth,
            string imageRgbaBase64)
        {
            Kind = kind;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Color = string.IsNullOrWhiteSpace(color) ? "#ffffff" : color;
            Text = text;
            Size = size;
            X2 = x2;
            Y2 = y2;
            Path = path;
            StrokeWidth = Math.Max(1, strokeWidth);
            ImageRgbaBase64 = imageRgbaBase64 ?? string.Empty;
        }

        public string Kind { get; }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public string Color { get; }
        public string Text { get; }
        public int Size { get; }
        public int X2 { get; }
        public int Y2 { get; }
        public string Path { get; }
        public int StrokeWidth { get; }
        public string ImageRgbaBase64 { get; }

        public static UiDrawCommand Rect(int x, int y, int width, int height, string color)
        {
            return new UiDrawCommand("rect", x, y, Math.Max(0, width), Math.Max(0, height), color, string.Empty, 0, 0, 0, string.Empty, 1, string.Empty);
        }

        public static UiDrawCommand FromText(int x, int y, string text, string color, int size)
        {
            return new UiDrawCommand("text", x, y, 0, 0, color, text, size, 0, 0, string.Empty, 1, string.Empty);
        }

        public static UiDrawCommand Line(int x1, int y1, int x2, int y2, string color, int strokeWidth)
        {
            return new UiDrawCommand("line", x1, y1, 0, 0, color, string.Empty, 0, x2, y2, string.Empty, strokeWidth, string.Empty);
        }

        public static UiDrawCommand Ellipse(int x, int y, int width, int height, string color)
        {
            return new UiDrawCommand("ellipse", x, y, Math.Max(0, width), Math.Max(0, height), color, string.Empty, 0, 0, 0, string.Empty, 1, string.Empty);
        }

        public static UiDrawCommand FromPath(string path, string color, int strokeWidth)
        {
            return new UiDrawCommand("path", 0, 0, 0, 0, color, string.Empty, 0, 0, 0, path ?? string.Empty, strokeWidth, string.Empty);
        }

        public static UiDrawCommand FromImage(int x, int y, int width, int height, string rgbaBase64)
        {
            return new UiDrawCommand("image", x, y, Math.Max(0, width), Math.Max(0, height), "#ffffff", string.Empty, 0, 0, 0, string.Empty, 1, rgbaBase64 ?? string.Empty);
        }

        public static List<(int X, int Y)> ParsePathPoints(string path)
        {
            var points = new List<(int X, int Y)>();
            if (string.IsNullOrWhiteSpace(path))
            {
                return points;
            }

            var segments = path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var segment in segments)
            {
                var point = segment.Split(',', StringSplitOptions.TrimEntries);
                if (point.Length != 2 ||
                    !int.TryParse(point[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
                    !int.TryParse(point[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
                {
                    continue;
                }

                points.Add((x, y));
            }

            return points;
        }
    }
}
