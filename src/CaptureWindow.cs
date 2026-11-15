using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace LightReader;

public sealed class CaptureWindow : Window
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint op);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    readonly BitmapSource screen;
    readonly Canvas canvas = new();
    readonly Rectangle selection = new() { Stroke = Brushes.MediumSpringGreen, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(25, 80, 220, 170)) };
    Point start; bool dragging;
    public byte[]? Png { get; private set; }
    public static byte[]? Select()
    {
        GetCursorPos(out var cursor);
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromPoint(cursor, 2), ref info)) throw new InvalidOperationException("无法获取屏幕信息。");
        var r = info.Monitor; int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var dc = GetDC(IntPtr.Zero); var memory = CreateCompatibleDC(dc); var bitmap = CreateCompatibleBitmap(dc, w, h);
        var previous = SelectObject(memory, bitmap);
        BitmapSource image;
        try
        {
            if (!BitBlt(memory, 0, 0, w, h, dc, r.Left, r.Top, 0x40CC0020)) throw new InvalidOperationException("截图失败，请重试。");
            image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); image.Freeze();
        }
        finally { SelectObject(memory, previous); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(IntPtr.Zero, dc); }
        var window = new CaptureWindow(image);
        window.SourceInitialized += (_, _) => SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(-1), r.Left, r.Top, w, h, 0x0040);
        window.ShowDialog(); return window.Png;
    }
    CaptureWindow(BitmapSource image)
    {
        screen = image; Title = "轻读 · 框选截图"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
        Cursor = Cursors.Cross; Background = Brushes.Black;
        var grid = new Grid(); Content = grid;
        grid.Children.Add(new Image { Source = image, Stretch = Stretch.Fill });
        grid.Children.Add(canvas); canvas.Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)); canvas.Children.Add(selection);
        grid.Children.Add(new TextBlock { Text = "拖动框选文字 · Esc 取消", Foreground = Brushes.White, Background = Brushes.Black, Padding = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20), IsHitTestVisible = false });
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseLeftButtonDown += (_, e) => { start = e.GetPosition(canvas); dragging = true; CaptureMouse(); Update(start); };
        MouseMove += (_, e) => { if (dragging) Update(e.GetPosition(canvas)); };
        MouseLeftButtonUp += (_, e) => {
            if (!dragging) return; Update(e.GetPosition(canvas)); dragging = false; ReleaseMouseCapture();
            if (selection.Width < 8 || selection.Height < 8) return;
            var sx = screen.PixelWidth / canvas.ActualWidth; var sy = screen.PixelHeight / canvas.ActualHeight;
            int x = Math.Clamp((int)(Canvas.GetLeft(selection) * sx), 0, screen.PixelWidth - 1);
            int y = Math.Clamp((int)(Canvas.GetTop(selection) * sy), 0, screen.PixelHeight - 1);
            int w = Math.Clamp((int)Math.Ceiling(selection.Width * sx), 1, screen.PixelWidth - x);
            int h = Math.Clamp((int)Math.Ceiling(selection.Height * sy), 1, screen.PixelHeight - y);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(screen, new Int32Rect(x, y, w, h))));
            using var stream = new MemoryStream(); encoder.Save(stream); Png = stream.ToArray(); Close();
        };
    }
    void Update(Point p)
    {
        p.X = Math.Clamp(p.X, 0, canvas.ActualWidth); p.Y = Math.Clamp(p.Y, 0, canvas.ActualHeight);
        Canvas.SetLeft(selection, Math.Min(start.X, p.X)); Canvas.SetTop(selection, Math.Min(start.Y, p.Y));
        selection.Width = Math.Abs(p.X - start.X); selection.Height = Math.Abs(p.Y - start.Y);
    }
}
