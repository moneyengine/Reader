using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace LightReader;

public sealed partial class NativeHistoryWindow
{
    readonly Dictionary<string,Button> sourceButtons=new();
    readonly TextBlock historyCount=new(){FontSize=12};
    readonly WrapPanel historyDataActions=new();
    readonly TextBlock historyStorage=new(){FontSize=12,VerticalAlignment=VerticalAlignment.Center};
    readonly FlowDocumentScrollViewer historyMarkdown=new(){IsToolBarVisible=false,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden};
    readonly TextBlock historyNoImage=new(){Text="此记录没有关联原图。",FontSize=13,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    bool markdownPreview;
    ReadingPreferences previewPreferences=new();
    Grid historyMasterDetail=null!;
    public void SetReadingPreferences(ReadingPreferences preferences){previewPreferences=preferences;RefreshMarkdownPreview();}
    public void SetDataActions(Action import,Action export){var clear=historyDataActions.Children.OfType<Button>().FirstOrDefault(x=>x.ToolTip?.ToString()=="清空历史");historyDataActions.Children.Clear();historyDataActions.Children.Add(HistoryAction("导入数据 (JSON)","file_upload",import));historyDataActions.Children.Add(HistoryAction("导出明文备份","file_download",export));if(clear is not null)historyDataActions.Children.Add(clear);}
    static void Detach(FrameworkElement element){if(element.Parent is Panel panel)panel.Children.Remove(element);else if(element.Parent is Decorator border)border.Child=null;}
    Button HistoryAction(string title,string icon,Action action){
        var button=new Button{ToolTip=title,MinHeight=30,Padding=new Thickness(8,4,8,4)};
        var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(StitchAssets.Icon(icon,16));row.Children.Add(new TextBlock{Text=title,Margin=new Thickness(5,0,0,0),VerticalAlignment=VerticalAlignment.Center});button.Content=row;
        button.Click+=(_,_)=>{try{action();}catch(Exception ex){info.Text=ex.Message;}};return button;
    }
    Border HistorySurface(UIElement child){var border=new Border{Child=child,CornerRadius=new CornerRadius(8),Padding=new Thickness(14),Margin=new Thickness(0,0,0,12)};border.SetResourceReference(Border.BackgroundProperty,"CardBrush");return border;}
    void BuildStitchHistory(Action<ReaderHistory> onDelete,Action onClear,Func<ReaderHistory,Task> onRecognize)
    {
        foreach(var element in new FrameworkElement[]{records,search,info,text,image,imageInfo,selectedTitle,load,read})Detach(element);
        var root=new Grid{Margin=new Thickness(16)};
        historyMasterDetail=root;
        root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(215)});root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(290)});root.ColumnDefinitions.Add(new ColumnDefinition());
        var filterPane=new DockPanel{Margin=new Thickness(0,0,12,0)};root.Children.Add(filterPane);
        var filters=new StackPanel();DockPanel.SetDock(filters,Dock.Top);filterPane.Children.Add(filters);
        search.Tag="搜索历史文字 / 来源…";search.MinHeight=34;search.Padding=new Thickness(8,6,8,6);filters.Children.Add(search);
        foreach(var choice in new[]{("all","inventory_2","全部记录"),("text","description","纯文本"),("image","image","包含截图")}){
            var mode=choice.Item1;var button=HistoryAction(choice.Item3,choice.Item2,()=>{sourceFilter=mode;Filter();RefreshHistoryCounts();});button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Margin=new Thickness(0,2,0,2);filters.Children.Add(button);sourceButtons.Add(mode,button);
        }
        var protection=new StackPanel{Margin=new Thickness(0,18,0,0)};
        protection.Children.Add(new TextBlock{Text="Windows DPAPI",FontSize=12,FontWeight=FontWeights.SemiBold});protection.Children.Add(new TextBlock{Text="最近 30 条 · 本机加密保存",FontSize=11,Margin=new Thickness(0,6,0,14),TextWrapping=TextWrapping.Wrap});
        protection.Children.Add(HistoryAction("导出原图","file_download",ExportImage));
        protection.Children.Add(HistoryAction("清空历史","delete_sweep",()=>{if(MessageBox.Show(Owner??this,"清空全部历史及不再引用的截图？","声阅",MessageBoxButton.YesNo)==MessageBoxResult.Yes)onClear();}));
        filters.Children.Add(HistorySurface(protection));
        var listPane=new DockPanel{Margin=new Thickness(0,0,14,0)};Grid.SetColumn(listPane,1);root.Children.Add(listPane);
        var listHeading=new DockPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(listHeading,Dock.Top);listPane.Children.Add(listHeading);DockPanel.SetDock(historyCount,Dock.Right);listHeading.Children.Add(historyCount);listHeading.Children.Add(new TextBlock{Text="归档记录列表",FontFamily=StitchAssets.Heading,FontSize=15,FontWeight=FontWeights.SemiBold});
        records.HorizontalContentAlignment=HorizontalAlignment.Stretch;records.BorderThickness=new Thickness(0);listPane.Children.Add(records);
        var card=new FrameworkElementFactory(typeof(StackPanel));card.SetValue(FrameworkElement.MarginProperty,new Thickness(2,6,2,6));
        var meta=new FrameworkElementFactory(typeof(TextBlock));meta.SetBinding(TextBlock.TextProperty,new Binding(nameof(ReaderHistory.Caption)));meta.SetValue(TextBlock.FontSizeProperty,10.0);meta.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);meta.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,0,7));card.AppendChild(meta);
        var summary=new FrameworkElementFactory(typeof(TextBlock));summary.SetBinding(TextBlock.TextProperty,new Binding(nameof(ReaderHistory.Preview)));summary.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);summary.SetValue(TextBlock.FontSizeProperty,14.0);summary.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);summary.SetValue(TextBlock.MaxHeightProperty,64.0);summary.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,0,10));card.AppendChild(summary);
        var thumbnail=new FrameworkElementFactory(typeof(Image));thumbnail.SetBinding(Image.SourceProperty,new Binding(nameof(ReaderHistory.ImageId)){Converter=new ThumbnailConverter(images)});thumbnail.SetValue(FrameworkElement.HeightProperty,72.0);thumbnail.SetValue(Image.StretchProperty,Stretch.Uniform);thumbnail.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Left);card.AppendChild(thumbnail);
        var actions=new FrameworkElementFactory(typeof(StackPanel));actions.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);actions.SetValue(FrameworkElement.MarginProperty,new Thickness(0,8,0,0));
        foreach(var action in new[]{("载入",false),("朗读",true)}){
            var speak=action.Item2;var b=new FrameworkElementFactory(typeof(Button));b.SetValue(ContentControl.ContentProperty,action.Item1);b.SetValue(Control.PaddingProperty,new Thickness(8,3,8,3));b.SetValue(FrameworkElement.MinHeightProperty,28.0);
            b.AddHandler(Button.ClickEvent,new RoutedEventHandler((sender,e)=>{if(sender is FrameworkElement{DataContext:ReaderHistory item}){e.Handled=true;Leave();onLoad(item,speak);}}));actions.AppendChild(b);
        }
        var remove=new FrameworkElementFactory(typeof(Button));remove.SetValue(ContentControl.ContentProperty,"×");remove.SetValue(FrameworkElement.ToolTipProperty,"删除本记录");remove.SetValue(Control.PaddingProperty,new Thickness(8,3,8,3));remove.SetValue(FrameworkElement.MinHeightProperty,28.0);remove.AddHandler(Button.ClickEvent,new RoutedEventHandler((sender,e)=>{if(sender is FrameworkElement{DataContext:ReaderHistory item}){e.Handled=true;onDelete(item);}}));actions.AppendChild(remove);card.AppendChild(actions);
        records.ItemTemplate=new DataTemplate{VisualTree=card};
        var detail=new DockPanel();Grid.SetColumn(detail,2);root.Children.Add(detail);
        var titleArea=new StackPanel{Margin=new Thickness(0,0,0,14)};DockPanel.SetDock(titleArea,Dock.Top);detail.Children.Add(titleArea);titleArea.Children.Add(info);selectedTitle.FontFamily=StitchAssets.Heading;selectedTitle.FontSize=22;titleArea.Children.Add(selectedTitle);
        var primaryActions=new WrapPanel();primaryActions.Children.Add(read);primaryActions.Children.Add(load);primaryActions.Children.Add(HistoryAction("复制全文","content_copy",()=>{if(selected is not null)Clipboard.SetText(selected.Text);}));primaryActions.Children.Add(HistoryAction("重新识别","sync",()=>{if(selected is not null){var item=selected;Leave();_=onRecognize(item);}}));titleArea.Children.Add(primaryActions);
        var preview=new Grid();preview.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1.2,GridUnitType.Star)});preview.RowDefinitions.Add(new RowDefinition());detail.Children.Add(preview);
        var textPanel=new DockPanel();var textHeader=new TextBlock{Text="完整文本内容",FontSize=13,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(textHeader,Dock.Top);textPanel.Children.Add(textHeader);textPanel.Children.Add(text);text.FontSize=15;preview.Children.Add(HistorySurface(textPanel));
        textPanel.Children.Remove(textHeader);var textToolbar=new DockPanel{Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(textToolbar,Dock.Top);textPanel.Children.Insert(0,textToolbar);
        var format=HistoryAction("Markdown","article",()=>{markdownPreview=!markdownPreview;RefreshMarkdownPreview();});DockPanel.SetDock(format,Dock.Right);textToolbar.Children.Add(format);textToolbar.Children.Add(textHeader);
        Detach(text);var textViews=new Grid();textViews.Children.Add(text);textViews.Children.Add(historyMarkdown);textPanel.Children.Add(textViews);text.VerticalScrollBarVisibility=ScrollBarVisibility.Hidden;
        var picture=new DockPanel();var imageHeader=new TextBlock{Text="原始截图对照",FontSize=13,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(imageHeader,Dock.Top);picture.Children.Add(imageHeader);DockPanel.SetDock(imageInfo,Dock.Bottom);picture.Children.Add(imageInfo);picture.Children.Add(image);var imageCard=HistorySurface(picture);Grid.SetRow(imageCard,1);preview.Children.Add(imageCard);
        Detach(image);var pictureViews=new Grid();pictureViews.Children.Add(image);pictureViews.Children.Add(historyNoImage);picture.Children.Add(pictureViews);
        var imageActions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(imageActions,Dock.Top);picture.Children.Insert(1,imageActions);imageActions.Children.Add(HistoryAction("导出原图","download",ExportImage));
        root.Children.Clear();root.ColumnDefinitions.Clear();root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition());root.ColumnDefinitions.Add(new ColumnDefinition());root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(20)});root.ColumnDefinitions.Add(new ColumnDefinition());
        listPane.Margin=new Thickness(0);Grid.SetColumn(listPane,0);Grid.SetRowSpan(listPane,2);root.Children.Add(listPane);Grid.SetColumn(detail,2);Grid.SetRowSpan(detail,2);root.Children.Add(detail);
        var page=new DockPanel();var toolbar=new StackPanel{MaxWidth=1720,Margin=new Thickness(16,12,16,12)};var upper=new WrapPanel();toolbar.Children.Add(upper);Detach(search);search.Tag="搜索历史文本、标题或日期关键字…";search.Width=380;search.MaxWidth=576;search.Height=36;search.Margin=new Thickness(0,0,12,0);upper.Children.Add(search);
        var sourceSegments=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};foreach(var b in sourceButtons.Values){Detach(b);sourceSegments.Children.Add(b);}upper.Children.Add(sourceSegments);
        var safe=new TextBlock{Text="Windows DPAPI 本机加密保护",FontSize=11,Margin=new Thickness(16,0,0,0),VerticalAlignment=VerticalAlignment.Center};upper.Children.Add(safe);
        var lower=new DockPanel{Margin=new Thickness(0,8,0,0)};toolbar.Children.Add(lower);DockPanel.SetDock(historyDataActions,Dock.Right);lower.Children.Add(historyDataActions);lower.Children.Add(historyStorage);
        historyDataActions.Children.Add(HistoryAction("清空历史","delete_sweep",()=>{if(MessageBox.Show(Owner??this,"清空全部历史及不再引用的截图？","声阅",MessageBoxButton.YesNo)==MessageBoxResult.Yes)onClear();}));
        var bar=new Border{Child=toolbar};bar.SetResourceReference(Border.BackgroundProperty,"GlassBrush");DockPanel.SetDock(bar,Dock.Top);page.Children.Add(bar);page.Children.Add(root);
        page.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.F&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control){search.Focus();e.Handled=true;}};
        Page=page;Content=new Border{Child=page};root.HorizontalAlignment=HorizontalAlignment.Center;
        void UpdateMasterColumns(){var narrow=page.ActualWidth<1024;var width=root.ActualWidth>0?root.ActualWidth:root.Width;root.ColumnDefinitions[0].Width=narrow?new GridLength(1,GridUnitType.Star):new GridLength(Math.Max(250,(width-40)/3));root.ColumnDefinitions[1].Width=new GridLength(narrow?0:20);root.ColumnDefinitions[2].Width=narrow?new GridLength(0):new GridLength(1,GridUnitType.Star);Grid.SetRow(listPane,0);Grid.SetRowSpan(listPane,narrow?1:2);Grid.SetRow(detail,narrow?1:0);Grid.SetRowSpan(detail,narrow?1:2);Grid.SetColumn(detail,narrow?0:2);}
        page.SizeChanged+=(_,_)=>{root.Width=Math.Max(0,Math.Min(1720,page.ActualWidth)-32);UpdateMasterColumns();search.Width=Math.Min(576,Math.Max(320,page.ActualWidth*.35));};root.SizeChanged+=(_,_)=>UpdateMasterColumns();RefreshHistoryCounts();RefreshMarkdownPreview();
    }
    void RefreshHistoryCounts(){
        historyCount.Text=$"{snapshot.Count} 条目";
        foreach(var item in sourceButtons){item.Value.SetResourceReference(Control.BackgroundProperty,item.Key==sourceFilter?"LensBrush":"CardBrush");item.Value.SetResourceReference(Control.ForegroundProperty,item.Key==sourceFilter?"AccentBrush":"MutedBrush");if(item.Value.Content is StackPanel row&&row.Children.OfType<TextBlock>().LastOrDefault() is {} label){var count=item.Key=="all"?snapshot.Count:snapshot.Count(x=>item.Key=="image"?x.ImageId is not null:x.ImageId is null);label.Text=(item.Key=="all"?"全部记录":item.Key=="image"?"包含截图":"纯文本")+$" ({count})";}}
        var bytes=images.StoredBytes;historyStorage.Text=bytes.HasValue?$"原图存储 {bytes.Value/1048576.0:0.0} MB · 自动保存最近 30 条":"存储统计暂不可用 · 自动保存最近 30 条";
    }
    void RefreshMarkdownPreview(){historyMarkdown.Visibility=markdownPreview?Visibility.Visible:Visibility.Collapsed;text.Visibility=markdownPreview?Visibility.Collapsed:Visibility.Visible;if(markdownPreview){var runs=new List<(int Start,int Length,System.Windows.Documents.Run Run)>();historyMarkdown.Document=MarkdownReading.Render(selected?.Text??"",15,(Brush)FindResource("InkBrush"),_=>{},runs,previewPreferences);}historyNoImage.Visibility=image.Source is null?Visibility.Visible:Visibility.Collapsed;}
}
