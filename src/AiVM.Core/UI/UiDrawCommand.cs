namespace AiVM.Core;

public partial class DefaultSyscallHost
{
    private sealed class UiDrawCommand
    {
        private UiDrawCommand(string kind, int x, int y, int width, int height, string color, string text, int size)
        {
            Kind = kind;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Color = string.IsNullOrWhiteSpace(color) ? "#ffffff" : color;
            Text = text;
            Size = size;
        }

        public string Kind { get; }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public string Color { get; }
        public string Text { get; }
        public int Size { get; }

        public static UiDrawCommand Rect(int x, int y, int width, int height, string color)
        {
            return new UiDrawCommand("rect", x, y, Math.Max(0, width), Math.Max(0, height), color, string.Empty, 0);
        }

        public static UiDrawCommand FromText(int x, int y, string text, string color, int size)
        {
            return new UiDrawCommand("text", x, y, 0, 0, color, text, size);
        }
    }
}
