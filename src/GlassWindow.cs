using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;

namespace LightReader;

/// <summary>Native window material. No bitmap UI or per-frame software blur.</summary>
public static class GlassWindow
{
    public static void LoadResources(Window window) => window.Resources.MergedDictionaries.Add(
        new ResourceDictionary { Source = new Uri("/LightReader;component/GlassTheme.xaml", UriKind.Relative) });

    public static void Attach(Window window, Func<(bool Dark, bool Reduced)> preference)
    {
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome {
            CaptionHeight = 42, ResizeBorderThickness = new Thickness(7),
            GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(22), UseAeroCaptionButtons = false
        });
        void Update() { var p = preference(); Apply(window, p.Dark, p.Reduced); }
        PropertyChangedEventHandler changed = (_, _) => window.Dispatcher.BeginInvoke((Action)Update);
        HwndSource? nativeSource=null;
        window.SourceInitialized += (_, _) => {nativeSource=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);nativeSource?.AddHook(WorkAreaHook);Update();};
        SystemParameters.StaticPropertyChanged += changed;
        window.Closed += (_, _) => {SystemParameters.StaticPropertyChanged -= changed;nativeSource?.RemoveHook(WorkAreaHook);};
        Update();
    }

    public static bool Apply(Window window, bool dark, bool reduced)
    {
        var handle = new WindowInteropHelper(window).Handle;
        bool systemTransparency = true;
        try { systemTransparency = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) != 0; } catch { }
        bool acrylic = !reduced && systemTransparency && !SystemParameters.HighContrast &&
            (RenderCapability.Tier >> 16) > 0 && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
        if (handle != IntPtr.Zero) {
            int mode = acrylic ? 3 : 1, corner = 2, useDark = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, 20, ref useDark, sizeof(int));
            DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
            if (DwmSetWindowAttribute(handle, 38, ref mode, sizeof(int)) < 0) acrylic = false;
            var margins = new Margins { Left = acrylic ? -1 : 0 };
            DwmExtendFrameIntoClientArea(handle, ref margins);
            var source = HwndSource.FromHwnd(handle);
            if (source?.CompositionTarget is not null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }
        window.Background = Brushes.Transparent;
        void Brush(string key, string color) => window.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        Brush("PageBrush", dark ? "#172238" : "#E3ECFC");
        Brush("CardBrush", dark ? "#F4212D43" : "#F9FDFDFF");
        Brush("InkBrush", dark ? "#EEF3FF" : "#182438");
        Brush("MutedBrush", dark ? "#B2C0D8" : "#526178");
        Brush("LineBrush", dark ? "#557B94B9" : "#C8FFFFFF");
        Brush("AccentBrush", dark ? "#82AEFF" : "#1762E8");
        Brush("SelectionBrush", dark ? "#334D77" : "#D7E7FF");
        Brush("TrackBrush", dark ? "#66798FAC" : "#55869AB5");
        LinearGradientBrush Gradient(params string[] colors) {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            for (int i = 0; i < colors.Length; i++) b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(colors[i]), (double)i / (colors.Length - 1)));
            return b;
        }
        window.Resources["ShellBrush"] = dark
            ? Gradient(acrylic ? "#B52B4263" : "#243B59", acrylic ? "#AE30334F" : "#2E304C", acrylic ? "#C3353047" : "#373047")
            : Gradient(acrylic ? "#B5CFE6FF" : "#D7E7FD", acrylic ? "#85E0DCF7" : "#E3DEF5", acrylic ? "#BFEFE7F5" : "#EDE5F2");
        window.Resources["GlassBrush"] = dark ? Gradient("#B7445874", "#7C26364E", "#A63C4962") : Gradient("#D8FFFFFF", "#60EEF4FF", "#A8FFFFFF");
        window.Resources["LensBrush"] = dark ? Gradient("#405E86", "#344B70", "#466084") : Gradient("#C6E5FF", "#EDF4FF", "#BDD5FC");
        window.Resources["PrimaryBrush"]=Gradient("#3485FF","#135AE1","#2675F4");
        window.Resources["PrimaryTextBrush"]=Brushes.White;
        if (reduced || !systemTransparency) {
            Brush("GlassBrush", dark ? "#35435B" : "#F0F4FD");
            Brush("CardBrush", dark ? "#212D43" : "#FDFDFF");
            Brush("LineBrush", dark ? "#60738C" : "#C3CFDF");
        }
        if (SystemParameters.HighContrast) {
            window.Resources["ShellBrush"] = window.Resources["GlassBrush"] = window.Resources["CardBrush"] = SystemColors.WindowBrush;
            window.Resources["InkBrush"] = window.Resources["MutedBrush"] = SystemColors.WindowTextBrush;
            window.Resources["LineBrush"] = SystemColors.WindowTextBrush;
            window.Resources["AccentBrush"] = window.Resources["PrimaryBrush"] = SystemColors.HighlightBrush;
            window.Resources["LensBrush"] = window.Resources["SelectionBrush"] = SystemColors.ControlBrush;
            window.Resources["PrimaryTextBrush"]=SystemColors.HighlightTextBrush;
        }
        // Stitch Fluent Clarity palette: stable reading surface and restrained blue controls.
        if(!SystemParameters.HighContrast){
            Brush("PageBrush",dark?"#202020":"#F8F9FF");
            Brush("ShellBrush",dark?"#202020":"#F8F9FF");
            Brush("GlassBrush",dark?"#292929":"#EFF4FF");
            Brush("CardBrush",dark?"#242424":"#FFFFFF");
            Brush("InkBrush",dark?"#F1F1F1":"#0B1C30");
            Brush("MutedBrush",dark?"#B8BCC5":"#434655");
            Brush("LineBrush",dark?"#414752":"#C3C6D7");
            Brush("AccentBrush",dark?"#A6C8FF":"#004AC6");
            Brush("PrimaryBrush",dark?"#A6C8FF":"#2563EB");
            Brush("PrimaryTextBrush",dark?"#001C3B":"#FFFFFF");
            Brush("LensBrush",dark?"#263D54":"#E5EEFF");
            Brush("SelectionBrush",dark?"#263D54":"#D5E3FF");
        }
        window.Background=acrylic?Brushes.Transparent:(Brush)window.FindResource("PageBrush");
        Brush("ReaderInlineCodeBrush",dark?"#383838":"#EDEDED");
        Brush("ReaderCodeBackgroundBrush",dark?"#2C2C2C":"#F4F4F4");
        Brush("ReaderCodeBorderBrush",dark?"#414141":"#E4E4E4");
        Brush("ReaderCodeInkBrush",dark?"#E8E8E8":"#252525");
        Brush("ReaderLinkBrush",dark?"#8EC6FF":"#006BD6");
        Brush("ReaderKeywordBrush",dark?"#F092C5":"#A82476");
        Brush("ReaderTypeBrush",dark?"#BFA9FF":"#6741D9");
        Brush("ReaderStringBrush",dark?"#8DD5A9":"#187448");
        Brush("ReaderNumberBrush",dark?"#FFB580":"#B44C10");
        Brush("ReaderCommentBrush",dark?"#B4B4B4":"#626262");
        if(SystemParameters.HighContrast){
            window.Resources["ReaderInlineCodeBrush"]=window.Resources["ReaderCodeBackgroundBrush"]=SystemColors.WindowBrush;
            window.Resources["ReaderCodeBorderBrush"]=SystemColors.WindowTextBrush;
            foreach(var key in new[]{"ReaderCodeInkBrush","ReaderKeywordBrush","ReaderTypeBrush","ReaderStringBrush","ReaderNumberBrush","ReaderCommentBrush"})window.Resources[key]=SystemColors.WindowTextBrush;
            window.Resources["ReaderLinkBrush"]=SystemColors.HotTrackBrush;
        }
        return acrylic;
    }

    public static void Decorate(Window window, UIElement content, bool dark, bool reduced)
    {
        LoadResources(window);window.SetResourceReference(Control.ForegroundProperty,"InkBrush");Attach(window,()=> (dark,reduced));
        var frame=new Border{CornerRadius=new CornerRadius(22),BorderThickness=new Thickness(1),Padding=new Thickness(5)};
        frame.SetResourceReference(Border.BackgroundProperty,"ShellBrush");frame.SetResourceReference(Border.BorderBrushProperty,"LineBrush");
        var dock=new DockPanel();var title=TitleBar(window,window.Title);DockPanel.SetDock(title,Dock.Top);dock.Children.Add(title);dock.Children.Add(content);frame.Child=dock;window.Content=frame;
        window.StateChanged+=(_,_)=>frame.Margin=window.WindowState==WindowState.Maximized?new Thickness(7):new Thickness(0);
    }

    public static FrameworkElement TitleBar(Window window, string title)
    {
        var dock = new DockPanel { Height = 42, Margin = new Thickness(14, 0, 8, 0), LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right); dock.Children.Add(buttons);
        void Add(string glyph, string name, Action action) {
            var b = new Button { Content = glyph, Style = (Style)window.FindResource("CaptionButton"), ToolTip = name };
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            WindowChrome.SetIsHitTestVisibleInChrome(b, true); b.Click += (_, _) => action(); buttons.Children.Add(b);
        }
        Add("\uE921", "最小化", () => SystemCommands.MinimizeWindow(window));
        Add("\uE922", "最大化 / 还原", () => { if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window); else SystemCommands.MaximizeWindow(window); });
        Add("\uE8BB", "关闭", window.Close);
        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new Image { Source = (ImageSource)window.FindResource("AppMark"), Width = 30, Height = 24, Margin = new Thickness(0, 0, 8, 0) });
        label.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        dock.Children.Add(label); return dock;
    }

    [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
    static IntPtr WorkAreaHook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message!=0x24)return IntPtr.Zero; // WM_GETMINMAXINFO uses physical monitor pixels, not WPF DIP.
        var monitor=MonitorFromWindow(hwnd,2);var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(monitor,ref info))return IntPtr.Zero;
        var limits=Marshal.PtrToStructure<MinMaxInfo>(lParam);
        limits.MaxPosition=new NativePoint{X=info.Work.Left-info.Monitor.Left,Y=info.Work.Top-info.Monitor.Top};
        limits.MaxSize=new NativePoint{X=info.Work.Right-info.Work.Left,Y=info.Work.Bottom-info.Work.Top};
        Marshal.StructureToPtr(limits,lParam,false);handled=true;return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] struct NativePoint{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public uint Flags;}
    [StructLayout(LayoutKind.Sequential)] struct MinMaxInfo{public NativePoint Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
