using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LightReader;

public sealed partial class SettingsWindow
{
    readonly WrapPanel appearanceActions=new();
    readonly WrapPanel diagnosticActions=new();
    readonly StackPanel extraVoiceControls=new();
    readonly Dictionary<string,FrameworkElement> sections=new();
    readonly Dictionary<string,Button> sectionButtons=new();
    StackPanel settingsStream=null!;
    ScrollViewer settingsScroll=null!;
    public void AddOptions(UIElement options,ReaderState? appearance=null,Action<string>? themeChanged=null,Action<bool>? transparencyChanged=null)
    {
        if(appearance is not null){
            var themes=new StackPanel();themes.Children.Add(Heading("主题模式",15));
            var buttons=new WrapPanel{Margin=new Thickness(0,10,0,18)};themes.Children.Add(buttons);
            foreach(var choice in new[]{("Light","light_mode","浅色模式"),("Dark","dark_mode","深色模式"),("System","settings_brightness","跟随系统")}){
                var mode=choice.Item1;var button=ActionButton(choice.Item3,choice.Item2,(_,_)=>{themeChanged?.Invoke(mode);foreach(var item in buttons.Children.OfType<Button>())item.SetResourceReference(Control.BackgroundProperty,(string?)item.Tag==mode?"LensBrush":"GlassBrush");});button.Tag=mode;
                if(mode==(appearance.ThemeMode??(appearance.Dark?"Dark":"Light")))button.SetResourceReference(Control.BackgroundProperty,"LensBrush");buttons.Children.Add(button);
            }
            var reduced=new DockPanel{Margin=new Thickness(0,0,0,16)};
            var switcher=StitchAssets.Switch(this,"ReducedTransparency",appearance.ReducedTransparency,value=>transparencyChanged?.Invoke(value));DockPanel.SetDock(switcher,Dock.Right);reduced.Children.Add(switcher);reduced.Children.Add(Text("减少透明度与动态模糊"));themes.Children.Add(reduced);appearanceActions.Children.Add(themes);
        }
        if(options is Panel panel){
            foreach(var child in panel.Children.Cast<UIElement>().ToList()){
                if(child is Button button){
                    var title=button.Content?.ToString()??"";
                    if(title is "深色 / 浅色" or "减少透明度")continue;
                    if(title=="API 调试")continue;
                    var symbol=title switch{"英文字典"=>"translate","API 调试"=>"bug_report","导入阅读数据…"=>"file_upload","导出阅读数据…"=>"file_download",_=>"settings"};
                    var proxy=ActionButton(title,symbol,(_,_)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                    if(title=="英文字典")appearanceActions.Children.Add(proxy);else diagnosticActions.Children.Add(proxy);
                }else if(child is TextBox source){
                    var search=new TextBox{Text=source.Text,Tag=source.Tag,ToolTip=source.ToolTip,Width=240,Margin=new Thickness(0,12,0,0)};
                    search.TextChanged+=(_,_)=>source.Text=search.Text;extraVoiceControls.Children.Add(search);
                }
            }
        }else diagnosticActions.Children.Add(options);
    }
    static TextBlock Text(string value,int size=13)=>new(){Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
    TextBlock Heading(string value,int size=20)=>new(){Text=value,FontFamily=StitchAssets.Heading,FontSize=size,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    Button ActionButton(string label,string icon,RoutedEventHandler action){
        var b=new Button{ToolTip=label,Padding=new Thickness(10,5,10,5),MinHeight=30,FontSize=13,HorizontalAlignment=HorizontalAlignment.Left};var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(StitchAssets.Icon(icon,16));row.Children.Add(new TextBlock{Text=label,Margin=new Thickness(5,0,0,0),VerticalAlignment=VerticalAlignment.Center});b.Content=row;b.Click+=action;return b;
    }
    Border Surface(UIElement content,bool tinted=false){var frame=new Border{Child=content,CornerRadius=new CornerRadius(8),Padding=new Thickness(20),Margin=new Thickness(0,0,0,12)};frame.SetResourceReference(Border.BackgroundProperty,tinted?"GlassBrush":"CardBrush");return frame;}
    StackPanel Section(string id,string title,string detail){
        var stack=new StackPanel{Margin=new Thickness(0,0,0,24)};var row=new DockPanel{Margin=new Thickness(0,0,0,12)};
        var note=Text(detail,11);note.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");DockPanel.SetDock(note,Dock.Right);row.Children.Add(note);
        var marker=new Border{Width=5,Height=18,CornerRadius=new CornerRadius(3),Margin=new Thickness(0,0,9,0)};marker.SetResourceReference(Border.BackgroundProperty,"AccentBrush");DockPanel.SetDock(marker,Dock.Left);row.Children.Add(marker);row.Children.Add(Heading(title,20));stack.Children.Add(row);sections.Add(id,stack);settingsStream.Children.Add(stack);return stack;
    }
    StackPanel EngineCard(string title,string icon,string subtitle,Button? action=null){
        var card=new StackPanel();var header=new DockPanel{Margin=new Thickness(0,0,0,14)};
        if(action is not null){DockPanel.SetDock(action,Dock.Right);header.Children.Add(action);}
        var badge=new Border{Child=StitchAssets.Icon(icon,23),Width=40,Height=40,CornerRadius=new CornerRadius(4),Margin=new Thickness(0,0,12,0)};badge.SetResourceReference(Border.BackgroundProperty,"GlassBrush");DockPanel.SetDock(badge,Dock.Left);header.Children.Add(badge);
        var names=new StackPanel();names.Children.Add(Heading(title,15));var description=Text(subtitle,13);description.Margin=new Thickness(0,3,0,0);description.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");names.Children.Add(description);header.Children.Add(names);card.Children.Add(header);return card;
    }
    static void Field(Panel parent,string label,UIElement control){var wrap=new StackPanel{Margin=new Thickness(0,0,0,12)};wrap.Children.Add(new TextBlock{Text=label,FontSize=12,Margin=new Thickness(0,0,0,5),TextWrapping=TextWrapping.Wrap});wrap.Children.Add(control);parent.Children.Add(wrap);}
    void BuildPage()
    {
        var root=new DockPanel{Margin=new Thickness(16,14,16,14)};
        var header=new DockPanel{Margin=new Thickness(0,0,0,24)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var headerIcon=new Border{Child=StitchAssets.Icon("tune",24),Width=40,Height=40,CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,12,0)};headerIcon.SetResourceReference(Border.BackgroundProperty,"GlassBrush");DockPanel.SetDock(headerIcon,Dock.Left);header.Children.Add(headerIcon);
        var headings=new StackPanel();headings.Children.Add(Heading("偏好设置与语音引擎"));var subtitle=Text("微软 Edge 语音 · 本机 OCR · 朗读过滤",13);subtitle.Margin=new Thickness(0,4,0,0);subtitle.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");headings.Children.Add(subtitle);header.Children.Add(headings);
        var content=new Grid();content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(250)});content.ColumnDefinitions.Add(new ColumnDefinition());root.Children.Add(content);
        var navigation=new StackPanel{Margin=new Thickness(0,0,20,0)};content.Children.Add(navigation);
        settingsStream=new StackPanel();settingsScroll=new ScrollViewer{Content=settingsStream,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetColumn(settingsScroll,1);content.Children.Add(settingsScroll);
        foreach(var entry in new[]{("tts","record_voice_over","语音合成 (TTS)"),("ocr","document_scanner","文字识别 (OCR)"),("filters","rule","过滤与排版"),("window","desktop_windows","界面与窗口"),("diagnostics","shield","安全与诊断")}){
            var id=entry.Item1;var b=ActionButton(entry.Item3,entry.Item2,(_,_)=>{settingsScroll.ScrollToVerticalOffset(settingsScroll.VerticalOffset+sections[id].TransformToAncestor(settingsScroll).Transform(new Point()).Y);SelectSection(id);});b.HorizontalContentAlignment=HorizontalAlignment.Left;b.HorizontalAlignment=HorizontalAlignment.Stretch;b.Margin=new Thickness(0,1,0,1);b.Style=(Style)FindResource("QuietButton");sectionButtons.Add(id,b);navigation.Children.Add(b);
        }
        var tts=Section("tts","语音合成引擎 (TTS)","Microsoft Edge");
        var edge=EngineCard("Edge 自然语音（在线高清 · 无需密钥）","cloud_sync","微软在线语音 · 正文发送至微软",checkEdge);
        Field(edge,"Python 路径",edgePython);edge.Children.Add(edgeStatus);
        var edgeChoices=new UniformGrid{Columns=3,Margin=new Thickness(0,12,0,0)};
        foreach(var entry in new[]{("zh-CN-XiaoyiNeural","晓伊 (Xiaoyi)","女声 · 温和自然"),("zh-CN-YunjianNeural","云健 (Yunjian)","男声 · 沉稳播报"),("zh-CN-XiaoxiaoNeural","晓晓 (Xiaoxiao)","女声 · 清澈活力")}){
            var id=entry.Item1;var names=new StackPanel();names.Children.Add(Text(entry.Item2,13));names.Children.Add(Text(entry.Item3,11));var radio=new RadioButton{Content=names,GroupName="edge-voice",IsChecked=Result.EdgeVoice==id,Margin=new Thickness(0,0,10,0),Padding=new Thickness(7)};radio.Checked+=(_,_)=>Result.EdgeVoice=id;edgeChoices.Children.Add(radio);
        }
        edge.Children.Add(edgeChoices);edge.Children.Add(extraVoiceControls);tts.Children.Add(Surface(edge));
        var ocr=Section("ocr","文字识别 (OCR)","Windows 本机识别");
        var native=EngineCard("Windows 本机 OCR","crop_free","截图识字 · 图片不上传");Field(native,"识别语言",ocrLanguage);native.Children.Add(ActionButton("Windows 语言设置","open_in_new",(_,_)=>OpenExternal("ms-settings:regionlanguage")));ocr.Children.Add(Surface(native));
        var filters=Section("filters","朗读过滤与排版规则","保留正文与进度映射");
        var rules=new StackPanel();foreach(var item in new[]{("SkipCode","code","跳过编程代码块","text 流程块不受影响"),("SkipLinks","link_off","跳过 URL 与文件路径","忽略冗长链接与磁盘路径"),("SkipDecorations","sentiment_satisfied","跳过表情与装饰字符","保持正文语流连贯"),("KeepInlineTerms","spellcheck","保留行内专业术语","CosHalfAngleSq / API / LLM / OCR"),("FocusSentences","center_focus_strong","朗读句子自动聚焦","非当前句透明度 35%")}){
            var key=item.Item1;var row=new DockPanel{Margin=new Thickness(0,0,0,14)};var enabled=StitchAssets.Switch(this,key,ResultPreferences.Enabled(key),value=>ResultPreferences=ResultPreferences.Set(key,value));DockPanel.SetDock(enabled,Dock.Right);row.Children.Add(enabled);var icon=StitchAssets.Icon(item.Item2,20);icon.Margin=new Thickness(0,0,12,0);DockPanel.SetDock(icon,Dock.Left);row.Children.Add(icon);var labels=new StackPanel();labels.Children.Add(Text(item.Item3));labels.Children.Add(Text(item.Item4,11));row.Children.Add(labels);rules.Children.Add(row);
        }filters.Children.Add(Surface(rules));
        var window=Section("window","窗口与外观 (UI / UX)","桌面体验");window.Children.Add(Surface(appearanceActions));
        var diagnostics=Section("diagnostics","系统诊断与数据安全","Windows 当前用户 DPAPI 加密");
        var protection=new StackPanel();protection.Children.Add(Text("当前正文与原图保存在本机，使用 Windows 当前用户加密。不记录阅读历史。导出数据为明文 JSON。"));protection.Children.Add(diagnosticActions);
        protection.Children.Add(ActionButton("导出运行播放诊断日志","bug_report",(_,_)=>{
            var dialog=new Microsoft.Win32.SaveFileDialog{FileName="playback-diagnostics.log",Filter="日志文件|*.log"};
            if(dialog.ShowDialog(Owner??this)==true){try{var path=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LightReader","playback-diagnostics.log");if(System.IO.File.Exists(path))System.IO.File.Copy(path,dialog.FileName,true);else validationStatus.Text="暂无播放诊断日志。";}catch(Exception ex){validationStatus.Text=ex.Message;}}
        }));protection.Children.Add(validationStatus);protection.Children.Add(save);diagnostics.Children.Add(Surface(protection));
        Page=root;SelectSection("tts");
        root.SizeChanged+=(_,_)=>{var narrow=root.ActualWidth<950;content.ColumnDefinitions[0].Width=new GridLength(narrow?170:250);edgeChoices.Columns=narrow?1:3;};
    }
    void SelectSection(string id){foreach(var item in sectionButtons){item.Value.SetResourceReference(Control.BackgroundProperty,item.Key==id?"LensBrush":"CardBrush");item.Value.SetResourceReference(Control.ForegroundProperty,item.Key==id?"AccentBrush":"MutedBrush");}}
}
