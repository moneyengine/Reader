using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LightReader;

public sealed partial class SettingsWindow : Window
{
    public FrameworkElement Page {get;private set;}=null!;
    public Action? Saved {get;set;}
    public Settings Result {get;private set;}
    public ReadingPreferences ResultPreferences {get;private set;}
    readonly ComboBox ocrLanguage=new();
    readonly TextBox edgePython=new();
    readonly TextBlock edgeStatus=new(){TextWrapping=TextWrapping.Wrap,FontSize=12},validationStatus=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Firebrick};
    readonly Button checkEdge,save;
    readonly CancellationTokenSource lifetime=new();

    public SettingsWindow(Settings current,bool dark=false,bool reduced=false,ReadingPreferences? preferences=null)
    {
        Result=current.Copy();ResultPreferences=preferences??new();Title="声阅 · 偏好设置";Width=1100;Height=800;FontFamily=StitchAssets.Body;FontSize=13;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        GlassWindow.LoadResources(this);GlassWindow.Attach(this,()=> (dark,reduced));
        var languages=new List<OcrLanguage>{new("优先自动检测（中 / 英混合）","auto")};
        try{languages.AddRange(LocalOcr.Languages());}catch{ }
        ocrLanguage.ItemsSource=languages;ocrLanguage.SelectedItem=languages.FirstOrDefault(x=>x.Tag==current.OcrLanguage)??languages[0];
        edgePython.Text=current.EdgePython;edgePython.Tag="留空自动检测 Python 环境";
        edgeStatus.Text="Python 3.10+ · edge-tts 7.3.1+";
        checkEdge=ActionButton("一键检测环境","terminal",CheckEdge);
        save=new Button{Content="应用并同步引擎",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right,Padding=new Thickness(14,7,14,7),Style=(Style)FindResource("PrimaryButton")};save.Click+=Save;
        try{BuildPage();Content=new Border{Child=Page};}catch{Close();throw;}
        Closed+=(_,_)=>lifetime.Cancel();
    }
    void OpenExternal(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception ex){validationStatus.Text=ex.Message;}}
    async void CheckEdge(object sender,RoutedEventArgs e)
    {
        checkEdge.IsEnabled=false;edgeStatus.Text="正在检测…";
        try{edgeStatus.Text=await new EdgeTtsClient().CheckEnvironment(edgePython.Text,lifetime.Token);}
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(Exception ex){edgeStatus.Text=ex.Message;}
        finally{checkEdge.IsEnabled=true;}
    }
    void Save(object sender,RoutedEventArgs e)
    {
        Result.OcrProvider="Local";Result.SpeechProvider="Edge";Result.OcrLanguage=(ocrLanguage.SelectedItem as OcrLanguage)?.Tag??"auto";Result.EdgePython=edgePython.Text.Trim();
        try{Result.Save();if(Saved is not null)Saved();else DialogResult=true;}
        catch(Exception ex){validationStatus.Text="保存失败："+ex.Message;}
    }
}
