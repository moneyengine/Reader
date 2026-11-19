using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace LightReader;

public partial class NativeReaderWindow
{
    readonly Grid stitchFooter=new();
    readonly Dictionary<string,CheckBox> inspectorRules=new();
    bool updatingInspectorRules;
    readonly Grid stitchInputToolbar=new();
    readonly TextBlock stitchFontIndicator=new(){FontSize=11,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,4,0)};
    StackPanel stitchModeControls=null!;
    Button stitchReadMode=null!;
    readonly Dictionary<double,Button> stitchRateButtons=new();
    readonly List<System.Windows.Controls.Primitives.Popup> tuningPopups=new();
    StackPanel stitchImagePlaceholder=null!;
    readonly TextBlock stitchImageDetails=new(){FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    static void Unparent(FrameworkElement element){if(element.Parent is Panel panel)panel.Children.Remove(element);else if(element.Parent is Decorator decorator)decorator.Child=null;else if(element.Parent is ContentControl content)content.Content=null;}
    Button StitchAction(string text,string tip,RoutedEventHandler action){
        var b=new Button{Content=text,ToolTip=tip,MinHeight=28,Padding=new Thickness(6,3,6,3),Margin=new Thickness(2,0,2,0),Style=(Style)FindResource("QuietButton")};
        var symbol=tip switch{"阅读模式"=>"auto_stories","原图对照与过滤"=>"view_sidebar","填入示例"=>"description","清空工作区"=>"delete_sweep","后退一句"=>"skip_previous","前进一句"=>"skip_next","音调与音量"=>"graphic_eq",_=>null};
        if(symbol is not null){
            if(tip is "阅读模式" or "原图对照与过滤"){var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(StitchAssets.Icon(symbol,16));row.Children.Add(new TextBlock{Text=text,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,0,0)});b.Content=row;}
            else StitchAssets.SetIcon(b,symbol,18);
        }
        System.Windows.Automation.AutomationProperties.SetName(b,tip);b.Click+=action;return b;
    }
    void ApplyExportedStitchLayout()
    {
        Title="轻读 · Fluent Acoustic Reading";
        FontFamily=StitchAssets.Body;
        Editor.FontFamily=StitchAssets.Body;
        var shell=(Grid)WindowFrame.Child;var titlePanel=shell.Children.OfType<DockPanel>().First();
        titlePanel.Margin=new Thickness(16,0,16,0);readerPage.Margin=new Thickness(0);
        shell.RowDefinitions[0].Height=new GridLength(40);shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        WindowFrame.Padding=new Thickness(0);WindowFrame.BorderThickness=new Thickness(1);WindowFrame.CornerRadius=new CornerRadius(8);
        var mark=titlePanel.Children.OfType<StackPanel>().Last();titlePanel.Children.Remove(mark);titlePanel.Children.Insert(0,mark);DockPanel.SetDock(mark,Dock.Left);
        MainMenuButton.Visibility=Visibility.Collapsed;
        var oldMark=mark.Children.OfType<Image>().FirstOrDefault();if(oldMark is not null)mark.Children.Remove(oldMark);
        var brandIcon=StitchAssets.Icon("record_voice_over",15);brandIcon.Foreground=Brushes.White;
        var brandBadge=new Border{Child=brandIcon,Width=24,Height=24,CornerRadius=new CornerRadius(4)};brandBadge.SetResourceReference(Border.BackgroundProperty,"PrimaryBrush");mark.Children.Insert(0,brandBadge);
        var brand=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,20,0)};brand.Children.Add(new TextBlock{Text="声阅",FontFamily=StitchAssets.Heading,FontSize=13,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center});var secondaryBrand=new TextBlock{Text="ReadEase",FontFamily=StitchAssets.Heading,FontSize=11,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,0,0)};secondaryBrand.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");brand.Children.Add(secondaryBrand);mark.Children.Insert(1,brand);
        var navigation=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left};
        Unparent(HistoryNavButton);HistoryNavButton.Visibility=Visibility.Collapsed;
        foreach(var b in new[]{ReadNavButton,settingsNav}){if(b is null)continue;Unparent(b);b.Height=24;b.MinHeight=24;b.Width=80;b.Margin=new Thickness(2,0,2,0);b.Padding=new Thickness(6,2,6,2);b.Content=b==ReadNavButton?"朗读阅读":"偏好设置";WindowChrome.SetIsHitTestVisibleInChrome(b,true);navigation.Children.Add(b);}
        var navFrame=new Border{Child=navigation,CornerRadius=new CornerRadius(4),Padding=new Thickness(2),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left};navFrame.SetResourceReference(Border.BackgroundProperty,"GlassBrush");titlePanel.Children.Add(navFrame);WindowChrome.SetIsHitTestVisibleInChrome(navigation,true);WindowChrome.SetIsHitTestVisibleInChrome(navFrame,true);
        Unparent(PinButton);Unparent(CompactButton);var captions=titlePanel.Children.OfType<StackPanel>().First(x=>x!=mark);
        captions.Children.Insert(0,CompactButton);captions.Children.Insert(0,PinButton);CompactButton.Content="\uE8A7";CompactButton.FontFamily=new FontFamily("Segoe MDL2 Assets");CompactButton.Width=34;CompactButton.Padding=new Thickness(0);CompactButton.Style=(Style)FindResource("QuietButton");
        StitchAssets.SetIcon(CompactButton,"picture_in_picture_alt");StitchAssets.SetIcon(PinButton,"push_pin");
        foreach(var b in captions.Children.OfType<Button>()){
            b.Width=32;b.Height=32;b.MinHeight=28;b.Margin=new Thickness(0);b.Padding=new Thickness(0);
            if(b.ToolTip?.ToString()=="最小化")StitchAssets.SetIcon(b,"minimize");
            if(b==MaximizeCaptionButton)StitchAssets.SetIcon(b,"crop_square");
            if(b.ToolTip?.ToString()=="关闭")StitchAssets.SetIcon(b,"close");
        }
        PinButton.Width=CompactButton.Width=28;PinButton.Height=CompactButton.Height=28;
        var appearance=StitchAction("","切换外观模式",ToggleTheme);appearance.Width=28;appearance.Height=28;appearance.Padding=new Thickness(0);StitchAssets.SetIcon(appearance,"light_mode");captions.Children.Insert(2,appearance);
        var userIcon=StitchAssets.Icon("person",18);userIcon.Foreground=Brushes.White;var userBadge=new Border{Child=userIcon,Width=32,Height=32,CornerRadius=new CornerRadius(12),Margin=new Thickness(10,0,10,0),ToolTip="本机用户"};userBadge.SetResourceReference(Border.BackgroundProperty,"PrimaryBrush");captions.Children.Insert(3,userBadge);
        bottomNavigation.Visibility=Visibility.Collapsed;
        var input=new WrapPanel{Margin=new Thickness(0)};
        Unparent(PasteButton);PasteButton.Visibility=Visibility.Collapsed;Unparent(OcrEngineChoice);OcrEngineChoice.Visibility=Visibility.Collapsed;
        foreach(var c in new FrameworkElement[]{CaptureButton,ImageOcrButton}){Unparent(c);input.Children.Add(c);c.Margin=new Thickness(2,0,2,0);c.Height=32;c.MinHeight=28;}
        PasteButton.Style=(Style)FindResource("PrimaryButton");PasteButton.Padding=CaptureButton.Padding=ImageOcrButton.Padding=new Thickness(8,3,8,3);OcrEngineChoice.Width=205;
        Header.Children.Clear();Header.Margin=new Thickness(0);Header.MinHeight=40;Header.SetResourceReference(BackgroundProperty,"GlassBrush");
        foreach(var pair in new[]{(PasteButton,"content_paste","粘贴文本 / 图片"),(CaptureButton,"crop_free","屏幕截屏识字"),(ImageOcrButton,"add_photo_alternate","打开图片")}){
            var content=new StackPanel{Orientation=Orientation.Horizontal};content.Children.Add(StitchAssets.Icon(pair.Item2,16));content.Children.Add(new TextBlock{Text=pair.Item3,Margin=new Thickness(5,0,0,0),VerticalAlignment=VerticalAlignment.Center});pair.Item1.Content=content;
        }
        Unparent(OcrBar);readerPage.RowDefinitions[1].Height=new GridLength(0);
        var modes=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};stitchModeControls=modes;
        Unparent(EditButton);EditButton.Visibility=Visibility.Collapsed;
        stitchReadMode=StitchAction("阅读模式","阅读模式",PreviewReading);stitchReadMode.Visibility=Visibility.Collapsed;
        modes.Children.Add(StitchAction("A−","减小阅读字号",Smaller));modes.Children.Add(stitchFontIndicator);modes.Children.Add(StitchAction("A+","增大阅读字号",Larger));modes.Children.Add(StitchAction("原图对照与过滤","原图对照与过滤",(_,_)=>{originalRequested=!originalRequested;AdaptPanels();}));modes.Children.Add(StitchAction("\uE74D","清空工作区",Clear));
        stitchInputToolbar.Margin=new Thickness(14,4,14,4);stitchInputToolbar.ColumnDefinitions.Add(new ColumnDefinition());stitchInputToolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});stitchInputToolbar.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});stitchInputToolbar.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});stitchInputToolbar.Children.Add(input);Grid.SetColumn(modes,1);stitchInputToolbar.Children.Add(modes);Header.Children.Add(stitchInputToolbar);
        var textBorder=(Border)ReadingArea.Children[0];var textGrid=(Grid)textBorder.Child;var heading=textGrid.Children.OfType<DockPanel>().First();textGrid.Children.Remove(heading);
        Unparent(TextCount);TextCount.FontSize=11;TextCount.VerticalAlignment=VerticalAlignment.Center;
        Unparent(SourceLabel);SourceLabel.Margin=new Thickness(0);SourceLabel.FontSize=11;
        var metadata=new DockPanel();DockPanel.SetDock(TextCount,Dock.Right);metadata.Children.Add(TextCount);metadata.Children.Add(SourceLabel);
        var metadataFrame=new Border{Child=metadata,Padding=new Thickness(8,6,8,6),CornerRadius=new CornerRadius(4),Margin=new Thickness(0,0,0,24)};metadataFrame.SetResourceReference(Border.BackgroundProperty,"GlassBrush");textGrid.Children.Add(metadataFrame);
        textGrid.Margin=new Thickness(48,32,48,32);
        textBorder.BorderThickness=new Thickness(0);textBorder.CornerRadius=new CornerRadius(8);textBorder.MaxWidth=820;textBorder.HorizontalAlignment=HorizontalAlignment.Center;ReadingArea.Margin=new Thickness(16,20,16,12);ReadingArea.HorizontalAlignment=HorizontalAlignment.Center;
        if(!SystemParameters.HighContrast)textBorder.Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Color.FromRgb(11,28,48),Opacity=.04,BlurRadius=12,ShadowDepth=2,Direction=270};
        ReadingView.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;Editor.VerticalScrollBarVisibility=ScrollBarVisibility.Hidden;OriginalScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Hidden;
        OriginalPanel.CornerRadius=new CornerRadius(8);OriginalPanel.Padding=new Thickness(0);OriginalPanel.Margin=new Thickness(20,0,0,0);OriginalPanel.Background=Brushes.Transparent;OriginalPanel.BorderThickness=new Thickness(0);
        BuildStitchInspector();
        Unparent(PlayerDock);Grid.SetRow(PlayerDock,2);shell.Children.Add(PlayerDock);PlayerDock.CornerRadius=new CornerRadius(0);PlayerDock.BorderThickness=new Thickness(0,1,0,0);PlayerDock.Padding=new Thickness(10,5,10,5);PlayerDock.Margin=new Thickness(0);PlayerDock.Child=null;PlayerDock.Child=stitchFooter;
        stitchFooter.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(250)});stitchFooter.ColumnDefinitions.Add(new ColumnDefinition());stitchFooter.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});
        var voices=new StackPanel();stitchFooter.Children.Add(voices);Unparent(Voice);voices.Children.Add(Voice);Voice.Width=240;Engine.FontSize=13;BuildStitchVoiceTile();
        Unparent(PlaybackControls);Unparent(ProgressPanel);var center=new StackPanel();Grid.SetColumn(center,1);stitchFooter.Children.Add(center);
        Unparent(RateChoice);Unparent(RestartButton);Unparent(CaretButton);PlaybackControls.Children.Insert(0,RestartButton);PlaybackControls.Children.Insert(1,CaretButton);
        PlaybackControls.Children.Insert(2,StitchAction("◀","后退一句",async(_,_)=>{var range=SentenceFocus.Range(Editor.Text,position);var previous=SentenceFocus.Range(Editor.Text,Math.Max(0,range.Start-1));CancelRead();await Begin(previous.Start);}));
        PlaybackControls.Children.Add(StitchAction("▶","前进一句",async(_,_)=>{var range=SentenceFocus.Range(Editor.Text,position);if(range.End<Editor.Text.Length){CancelRead();await Begin(range.End);}}));
        center.Children.Add(PlaybackControls);center.Children.Add(ProgressPanel);PlaybackControls.HorizontalAlignment=HorizontalAlignment.Center;RestartButton.MinHeight=CaretButton.MinHeight=28;RestartButton.FontSize=CaretButton.FontSize=11;RestartButton.Padding=CaretButton.Padding=new Thickness(5,2,5,2);CaretButton.Height=28;
        PlayButton.Height=36;PlayButton.MinHeight=32;PlayButton.MinWidth=76;StopButton.Content="■";StopButton.ToolTip="停止";StopButton.MinHeight=28;StopButton.Padding=new Thickness(6,2,6,2);ProgressPanel.Margin=new Thickness(10,2,10,0);
        var right=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(right,2);stitchFooter.Children.Add(right);var presets=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};right.Children.Add(presets);
        foreach(var rate in new[]{.8,1,1.25,1.5,2}){var value=rate;var rateButton=StitchAction(rate.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"x","语速",(_,_)=>Rate.Value=value);rateButton.FontSize=11;rateButton.MinHeight=22;rateButton.Padding=new Thickness(4,2,4,2);rateButton.Margin=new Thickness(1);stitchRateButtons.Add(rate,rateButton);presets.Children.Add(rateButton);}
        Unparent(RateChoice);right.Children.Add(RateChoice);RateChoice.Width=65;RateChoice.Height=25;RateChoice.MinHeight=25;
        var pitchButton=StitchAction("","音调调节",(_,_)=>{});StitchAssets.SetIcon(pitchButton,"graphic_eq",17);right.Children.Add(pitchButton);BuildTuningPopup(pitchButton,Pitch,PitchLabel);
        var volumeButton=StitchAction("","音量调节",(_,_)=>{});StitchAssets.SetIcon(volumeButton,"volume_up",17);right.Children.Add(volumeButton);BuildTuningPopup(volumeButton,Volume,VolumeLabel);
        VoicePanel.Visibility=Visibility.Collapsed;Unparent(VoiceSearch);settingsActions.Children.Add(VoiceSearch);VoiceSearch.Width=240;Unparent(Status);voices.Children.Add(Status);Status.MaxWidth=245;Status.FontSize=10;StatusPanel.Visibility=Visibility.Collapsed;
        Unparent(Seek);Unparent(PositionLabel);Unparent(PlayerState);ProgressPanel.Children.Clear();ProgressPanel.RowDefinitions.Clear();ProgressPanel.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});ProgressPanel.ColumnDefinitions.Add(new ColumnDefinition());ProgressPanel.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        PlayerState.FontSize=11;PlayerState.Margin=new Thickness(0,0,6,0);ProgressPanel.Children.Add(PlayerState);Seek.MinHeight=16;Seek.Height=16;Grid.SetColumn(Seek,1);Grid.SetRow(Seek,0);ProgressPanel.Children.Add(Seek);PositionLabel.VerticalAlignment=VerticalAlignment.Center;PositionLabel.Margin=new Thickness(6,0,0,0);Grid.SetColumn(PositionLabel,2);ProgressPanel.Children.Add(PositionLabel);
        center.MaxWidth=680;center.HorizontalAlignment=HorizontalAlignment.Center;center.VerticalAlignment=VerticalAlignment.Center;
        Voice.Height=42;Voice.MinHeight=40;
        PlayerDock.Height=72;PlayerDock.MinHeight=72;
        Closed+=(_,_)=>{foreach(var popup in tuningPopups)popup.IsOpen=false;};
        SizeChanged+=(_,_)=>LayoutStitchFooter();readerPage.SizeChanged+=(_,_)=>LayoutStitchFooter();LayoutStitchFooter();
    }
    void BuildTuningPopup(Button button,Slider slider,TextBlock label){
        Unparent(slider);Unparent(label);var values=new StackPanel{Width=200};values.Children.Add(label);values.Children.Add(slider);
        var border=new Border{Child=values,CornerRadius=new CornerRadius(8),Padding=new Thickness(14),BorderThickness=new Thickness(1)};border.SetResourceReference(Border.BackgroundProperty,"CardBrush");border.SetResourceReference(Border.BorderBrushProperty,"LineBrush");
        var popup=new System.Windows.Controls.Primitives.Popup{Child=border,PlacementTarget=button,Placement=System.Windows.Controls.Primitives.PlacementMode.Top,StaysOpen=false,AllowsTransparency=true};((Grid)WindowFrame.Child).Children.Add(popup);button.Click+=(_,_)=>popup.IsOpen=!popup.IsOpen;tuningPopups.Add(popup);
    }
    void RefreshStitchRates(){foreach(var item in stitchRateButtons)item.Value.SetResourceReference(Control.BackgroundProperty,Math.Abs(item.Key-Rate.Value)<.001?"CardBrush":"LensBrush");if(stitchFooter.ColumnDefinitions.Count==3)LayoutStitchFooter();}
    void RefreshInspectorRules(){updatingInspectorRules=true;try{foreach(var item in inspectorRules)item.Value.IsChecked=state.ReadingPreferences.Enabled(item.Key);}finally{updatingInspectorRules=false;}UpdateInspectorSummary();}
    void LayoutStitchFooter(){
        ReadingArea.Width=Math.Max(0,Math.Min(1440,readerPage.ActualWidth>0?readerPage.ActualWidth:ActualWidth)-32);
        bool toolbarWrap=ActualWidth<1150;
        if(stitchModeControls is not null){Grid.SetRow(stitchModeControls,toolbarWrap?1:0);Grid.SetColumn(stitchModeControls,toolbarWrap?0:1);Grid.SetColumnSpan(stitchModeControls,toolbarWrap?2:1);stitchModeControls.HorizontalAlignment=toolbarWrap?HorizontalAlignment.Left:HorizontalAlignment.Right;}
        bool narrow=ActualWidth<1120||compact;
        stitchFooter.ColumnDefinitions[0].Width=new GridLength(compact?0:narrow?180:250);
        stitchFooter.ColumnDefinitions[2].Width=new GridLength(compact?0:narrow?140:280);
        if(stitchFooter.Children.Count>1&&stitchFooter.Children[1] is FrameworkElement center)center.Width=Math.Max(0,Math.Min(680,(stitchFooter.ActualWidth>0?stitchFooter.ActualWidth:ActualWidth-20)-stitchFooter.ColumnDefinitions[0].Width.Value-stitchFooter.ColumnDefinitions[2].Width.Value));
        if(stitchFooter.Children.Count>2){
            stitchFooter.Children[0].Visibility=stitchFooter.Children[2].Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            if(stitchFooter.Children[2] is StackPanel right&&right.Children[0] is Panel rates)rates.Visibility=narrow||customRateItem is not null?Visibility.Collapsed:Visibility.Visible;
        }
        RateChoice.Visibility=narrow||customRateItem is not null?Visibility.Visible:Visibility.Collapsed;
        Voice.Width=narrow?170:240;
    }
}
