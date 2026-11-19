using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace LightReader;

public partial class NativeReaderWindow
{
    readonly TextBlock inspectorActiveCount=new(){FontSize=11,VerticalAlignment=VerticalAlignment.Center};
    readonly TextBlock inspectorCodeSummary=new(){FontSize=10,TextWrapping=TextWrapping.Wrap};
    Button inspectorExport=null!,inspectorRetry=null!;
    string? countedInspectorText;
    int inspectorCodeBlocks;
    Border InspectorCard(UIElement child){var card=new Border{Child=child,Padding=new Thickness(16),CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,0,12)};card.SetResourceReference(Border.BackgroundProperty,"CardBrush");return card;}
    Button InspectorIcon(string glyph,string title,RoutedEventHandler action){var button=StitchAction("",title,action);StitchAssets.SetIcon(button,glyph,14);button.Width=24;button.Height=24;button.MinHeight=24;button.Margin=new Thickness(1,0,1,0);button.Padding=new Thickness(0);return button;}
    void BuildStitchInspector()
    {
        Unparent(OriginalImage);Unparent(OriginalScroll);OriginalPanel.Child=null;
        var stream=new StackPanel();var photo=new StackPanel();
        var photoHeader=new DockPanel{Height=24,Margin=new Thickness(0,0,0,12)};
        var photoActions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(photoActions,Dock.Right);photoHeader.Children.Add(photoActions);
        photoActions.Children.Add(InspectorIcon("fit_screen","自适应视图",ResetZoom));photoActions.Children.Add(InspectorIcon("zoom_in","100% 原始尺寸",(_,_)=>{
            if(OriginalImage.Source is not BitmapSource bitmap)return;originalZoom=1;OriginalImage.MaxWidth=OriginalImage.MaxHeight=double.PositiveInfinity;OriginalImage.Width=bitmap.PixelWidth;OriginalScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;ZoomLabel.Content="100%";
        }));
        var icon=StitchAssets.Icon("split_scene",18);icon.Margin=new Thickness(0,0,6,0);DockPanel.SetDock(icon,Dock.Left);photoHeader.Children.Add(icon);photoHeader.Children.Add(new TextBlock{Text="原图对照检测",FontSize=13,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center});photo.Children.Add(photoHeader);
        var viewport=new Grid{Height=176};viewport.SetResourceReference(BackgroundProperty,"LensBrush");OriginalScroll.Height=176;OriginalScroll.Content=OriginalImage;viewport.Children.Add(OriginalScroll);
        stitchImagePlaceholder=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};stitchImagePlaceholder.Children.Add(StitchAssets.Icon("image_search",30));stitchImagePlaceholder.Children.Add(new TextBlock{Text="无关联原图",FontSize=12,Margin=new Thickness(0,8,0,0)});viewport.Children.Add(stitchImagePlaceholder);
        var overlay=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(8)};
        inspectorExport=StitchAction("导图","导出原图",(_,_)=>{
            if(lastOcrImage is null)return;
            try{
                using var memory=new MemoryStream(lastOcrImage);var decoder=BitmapDecoder.Create(memory,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var extension=decoder.CodecInfo.FileExtensions.Split(',')[0];
                var dialog=new SaveFileDialog{FileName="reader-original"+extension,Filter=$"原始图片|*{extension}"};
                if(dialog.ShowDialog(this)==true)File.WriteAllBytes(dialog.FileName,lastOcrImage);
            }catch(Exception ex){Status.Text=ex.Message;}
        });
        inspectorRetry=StitchAction("重新识别","重新识别原图",RetryOriginal);overlay.Children.Add(inspectorExport);overlay.Children.Add(inspectorRetry);viewport.Children.Add(overlay);photo.Children.Add(viewport);
        stitchImageDetails.Margin=new Thickness(0,12,0,0);photo.Children.Add(stitchImageDetails);stream.Children.Add(InspectorCard(photo));
        var rules=new StackPanel();var header=new DockPanel{Height=24,Margin=new Thickness(0,0,0,12)};
        var countBadge=new Border{Child=inspectorActiveCount,Padding=new Thickness(6,2,6,2),CornerRadius=new CornerRadius(4),Background=new SolidColorBrush(Color.FromRgb(207,251,246))};inspectorActiveCount.Foreground=new SolidColorBrush(Color.FromRgb(0,106,97));DockPanel.SetDock(countBadge,Dock.Right);header.Children.Add(countBadge);
        var ruleIcon=StitchAssets.Icon("tune",18);ruleIcon.Margin=new Thickness(0,0,6,0);DockPanel.SetDock(ruleIcon,Dock.Left);header.Children.Add(ruleIcon);header.Children.Add(new TextBlock{Text="朗读与发音过滤规则",FontSize=13,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center});rules.Children.Add(header);
        foreach(var item in new[]{("SkipLinks","link_off","跳过网址与文件路径","避免机械朗读长链接"),("SkipCode","code_blocks","跳过编程代码块",""),("SkipDecorations","sentiment_satisfied","跳过表情与无义字符","过滤 Markdown 装饰标点"),("KeepInlineTerms","record_voice_over","保留行内专业术语","保留正文中的技术词语")}){
            var key=item.Item1;var row=new DockPanel();
            var toggle=StitchAssets.Switch(this,key,true,async value=>{if(updatingInspectorRules)return;var resume=reading is not null&&!paused;var start=position;CancelRead();state.ReadingPreferences=state.ReadingPreferences.Set(key,value);SaveState();UpdateInspectorSummary();if(resume)await Begin(start);else if(ReadingView.Visibility==Visibility.Visible)RenderText();});inspectorRules.Add(key,toggle);DockPanel.SetDock(toggle,Dock.Right);row.Children.Add(toggle);
            var symbol=StitchAssets.Icon(item.Item2,16);symbol.Margin=new Thickness(0,0,8,0);DockPanel.SetDock(symbol,Dock.Left);row.Children.Add(symbol);
            var labels=new StackPanel();labels.Children.Add(new TextBlock{Text=item.Item3,FontSize=13,TextWrapping=TextWrapping.Wrap});
            if(key=="SkipCode")labels.Children.Add(inspectorCodeSummary);else labels.Children.Add(new TextBlock{Text=item.Item4,FontSize=10,TextWrapping=TextWrapping.Wrap});row.Children.Add(labels);
            var panel=new Border{Child=row,Padding=new Thickness(8),CornerRadius=new CornerRadius(4),Margin=new Thickness(0,0,0,8)};panel.SetResourceReference(Border.BackgroundProperty,"GlassBrush");rules.Children.Add(panel);
        }
        var note=new TextBlock{Text="过滤只作用于音频生成序列；原文排版与朗读位置保持对齐。text 流程块正常朗读。",FontSize=11,TextWrapping=TextWrapping.Wrap};var notePanel=new Border{Child=note,Padding=new Thickness(10),CornerRadius=new CornerRadius(4)};notePanel.SetResourceReference(Border.BackgroundProperty,"LensBrush");rules.Children.Add(notePanel);stream.Children.Add(InspectorCard(rules));
        OriginalPanel.Child=new ScrollViewer{Content=stream,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    }
    void UpdateInspectorSummary()
    {
        if(countedInspectorText!=Editor.Text){countedInspectorText=Editor.Text;inspectorCodeBlocks=MarkdownReading.CountProgrammingBlocks(Editor.Text);}
        inspectorActiveCount.Text=$"{inspectorRules.Values.Count(x=>x.IsChecked==true)} 项激活";
        inspectorCodeSummary.Text=state.ReadingPreferences.SkipCode?$"已静音 {inspectorCodeBlocks} 个代码块":"代码块参与朗读";
        if(inspectorExport is not null){inspectorExport.IsEnabled=lastOcrImage is not null;inspectorRetry.IsEnabled=lastOcrImage is not null&&reading is null;}
    }
}
