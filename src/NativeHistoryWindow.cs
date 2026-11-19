using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace LightReader;

public sealed partial class NativeHistoryWindow : Window
{
    public FrameworkElement Page {get;private set;}=null!;
    public Action? ReturnToReader {get;set;}
    void Leave(){if(ReturnToReader is not null)ReturnToReader();else Close();}
    readonly ListBox records = new();
    readonly TextBox search = new() { Tag="搜索历史文字", Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 10) };
    readonly TextBox text = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), FontSize = 16 };
    readonly Image image = new() { Stretch = Stretch.Uniform, VerticalAlignment=VerticalAlignment.Top };
    readonly TextBlock info = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 12) };
    readonly TextBlock selectedTitle=new(){FontSize=20,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
    readonly TextBlock imageInfo = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 7, 0, 0) };
    readonly TextBlock empty = new() { Text = "暂无历史记录\n输入文字或截图识字后会自动保存。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10), LineHeight = 26 };
    readonly Button load, read, retry, export;
    readonly HistoryImages images;
    readonly Action<ReaderHistory, bool> onLoad;
    readonly List<MenuItem> selectionActions = new();
    List<ReaderHistory> snapshot = new();
    ReaderHistory? selected;
    string sourceFilter="all";
    byte[]? selectedImage;
    bool dark, reduced;

    public NativeHistoryWindow(HistoryImages images, Action<ReaderHistory, bool> onLoad, Action<ReaderHistory> onDelete, Action onClear, Func<ReaderHistory, Task> onRecognize)
    {
        this.images = images; this.onLoad = onLoad;
        Title = "轻读 · 文字与截图历史"; Width = 1120; Height = 740; MinWidth = 900; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = StitchAssets.Body; FontSize = 13; UseLayoutRounding = true;
        GlassWindow.LoadResources(this); SetResourceReference(ForegroundProperty, "InkBrush");
        GlassWindow.Attach(this, () => (dark, reduced));
        var frame = new Border { CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1), Padding = new Thickness(5) };
        frame.SetResourceReference(Border.BackgroundProperty, "ShellBrush"); frame.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); Content = frame;
        StateChanged += (_, _) => frame.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        var root = new DockPanel(); frame.Child = root;
        var title = GlassWindow.TitleBar(this, "轻读 · 历史记录"); DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var content = new DockPanel { Margin = new Thickness(18, 8, 18, 16) }; root.Children.Add(content);
        Page=content;
        var filters=new WrapPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(filters,Dock.Top);content.Children.Add(filters);
        foreach(var choice in new[]{("全部记录","all"),("纯文本","text"),("包含截图","image")}){
            var mode=choice.Item2;var button=new Button{Content=choice.Item1,MinHeight=30};button.Click+=(_,_)=>{sourceFilter=mode;Filter();};filters.Children.Add(button);
        }
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock { Text = "历史记录", FontSize = 24, FontWeight = FontWeights.SemiBold });
        var count = new TextBlock { Text = "最近 30 条 · 原文与截图", Margin = new Thickness(14, 9, 0, 0) }; count.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); header.Children.Add(count);
        DockPanel.SetDock(header, Dock.Top); content.Children.Add(header);
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); content.Children.Add(footer);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        var menu = new ContextMenu();
        var more = new Button { Content = "更多  ⌄", ContextMenu = menu };
        more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; }; actions.Children.Add(more);
        Button Action(string caption, Action callback, bool primary = false) {
            var b = new Button { Content = caption }; if (primary) b.Style = (Style)FindResource("PrimaryButton");
            b.Click += (_, _) => { try { callback(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "历史记录"); } }; actions.Children.Add(b); return b;
        }
        MenuItem Menu(string caption, Action callback) {
            var item = new MenuItem { Header = caption }; item.Click += (_, _) => { try { callback(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "历史记录"); } }; menu.Items.Add(item); return item;
        }
        retry = new Button(); // Kept as an enablement state for the asynchronous menu action.
        var retryItem = Menu("原图重新识字", () => { });
        retryItem.Click += async (_, _) => { if (selected is not null) { var item = selected; Leave(); await onRecognize(item); } };
        retry.IsEnabledChanged += (_, _) => retryItem.IsEnabled = retry.IsEnabled;
        export = new Button(); var exportItem = Menu("导出原始图片…", ExportImage); export.IsEnabledChanged += (_, _) => exportItem.IsEnabled = export.IsEnabled;
        selectionActions.Add(Menu("复制完整文本", () => { if (selected is not null) Clipboard.SetText(selected.Text); }));
        menu.Items.Add(new Separator());
        selectionActions.Add(Menu("删除选中", () => { if (selected is not null) onDelete(selected); }));
        Menu("清空历史", () => { if (MessageBox.Show(this, "清空全部历史及不再引用的截图？", "轻读", MessageBoxButton.YesNo) == MessageBoxResult.Yes) onClear(); });
        load = Action("载入", () => LoadSelected(false)); read = Action("▶  载入并朗读", () => LoadSelected(true), true);
        var lockText = new TextBlock { Text = "\uE72E", FontFamily = new FontFamily("Segoe MDL2 Assets"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
        var saved = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; saved.Children.Add(lockText);
        var hint = new TextBlock { Text = "原文与图片已在本机加密保存", FontSize = 11, VerticalAlignment = VerticalAlignment.Center }; hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); saved.Children.Add(hint); footer.Children.Add(saved);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); content.Children.Add(grid);
        var left = new DockPanel { Margin = new Thickness(0, 0, 14, 0) }; grid.Children.Add(left);
        DockPanel.SetDock(search, Dock.Top); left.Children.Add(search); search.ToolTip = "搜索历史文字、来源或识别错误";
        System.Windows.Automation.AutomationProperties.SetName(search, "搜索历史文字");
        var overlay = new Grid(); left.Children.Add(overlay); overlay.Children.Add(records); overlay.Children.Add(empty);
        var row = new FrameworkElementFactory(typeof(DockPanel));
        var thumb = new FrameworkElementFactory(typeof(Border));thumb.SetValue(DockPanel.DockProperty,Dock.Left);thumb.SetValue(FrameworkElement.WidthProperty,46.0);thumb.SetValue(FrameworkElement.HeightProperty,54.0);thumb.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,10,0));thumb.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));thumb.SetValue(Border.BackgroundProperty,Brushes.WhiteSmoke);
        var thumbGrid=new FrameworkElementFactory(typeof(Grid));var page=new FrameworkElementFactory(typeof(TextBlock));page.SetValue(TextBlock.TextProperty,"▤");page.SetValue(TextBlock.FontSizeProperty,25.0);page.SetValue(TextBlock.ForegroundProperty,Brushes.SlateGray);page.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);page.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);thumbGrid.AppendChild(page);
        var thumbImage=new FrameworkElementFactory(typeof(Image));thumbImage.SetValue(FrameworkElement.MarginProperty,new Thickness(3));thumbImage.SetBinding(Image.SourceProperty,new Binding("ImageId"){Converter=new ThumbnailConverter(images)});thumbGrid.AppendChild(thumbImage);thumb.AppendChild(thumbGrid);row.AppendChild(thumb);
        var caption=new FrameworkElementFactory(typeof(StackPanel));caption.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);var preview=new FrameworkElementFactory(typeof(TextBlock));preview.SetBinding(TextBlock.TextProperty,new Binding("Preview"));preview.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);preview.SetValue(TextBlock.LineHeightProperty,20.0);preview.SetValue(TextBlock.LineStackingStrategyProperty,LineStackingStrategy.BlockLineHeight);preview.SetValue(FrameworkElement.MaxHeightProperty,40.0);preview.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);caption.AppendChild(preview);
        var when=new FrameworkElementFactory(typeof(TextBlock));when.SetBinding(TextBlock.TextProperty,new Binding("Caption"));when.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);when.SetValue(TextBlock.FontSizeProperty,10.0);when.SetValue(UIElement.OpacityProperty,.75);when.SetValue(FrameworkElement.MarginProperty,new Thickness(0,5,0,0));caption.AppendChild(when);row.AppendChild(caption);records.ItemTemplate=new DataTemplate{VisualTree=row};
        var right = new DockPanel(); Grid.SetColumn(right, 1); grid.Children.Add(right);DockPanel.SetDock(selectedTitle,Dock.Top);right.Children.Add(selectedTitle); DockPanel.SetDock(info, Dock.Top); right.Children.Add(info);
        var detail = new Grid(); detail.ColumnDefinitions.Add(new ColumnDefinition()); detail.ColumnDefinitions.Add(new ColumnDefinition()); detail.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1.2,GridUnitType.Star)});detail.RowDefinitions.Add(new RowDefinition());right.Children.Add(detail);
        Border Card(string heading, UIElement child) {
            var card = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(17) }; card.SetResourceReference(Border.BackgroundProperty, "CardBrush"); card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            var pane = new DockPanel(); card.Child = pane; var label = new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) }; DockPanel.SetDock(label, Dock.Top); pane.Children.Add(label); pane.Children.Add(child); return card;
        }
        var textCard = Card("完整文本", text); textCard.Margin = new Thickness(0, 0, 0, 8);Grid.SetColumnSpan(textCard,2);detail.Children.Add(textCard);
        var picture = new DockPanel(); DockPanel.SetDock(imageInfo, Dock.Bottom); picture.Children.Add(imageInfo); picture.Children.Add(image);
        var imageCard = Card("原始截图", picture);Grid.SetColumnSpan(imageCard,2);Grid.SetRow(imageCard,1);detail.Children.Add(imageCard);
        search.TextChanged += (_, _) => Filter(); records.SelectionChanged += (_, _) => ShowSelected();
        Closed += (_, _) => { image.Source = null; selectedImage = null; };
        Refresh(Array.Empty<ReaderHistory>(), true);
        try{BuildStitchHistory(onDelete,onClear,onRecognize);}catch{Close();throw;}
    }
    public void SetAppearance(bool isDark, bool isReduced) { dark = isDark; reduced = isReduced; GlassWindow.Apply(this, dark, reduced); }
    public void Refresh(IEnumerable<ReaderHistory> entries, bool force = false) {
        var updated = entries.ToList(); if (!force && snapshot.SequenceEqual(updated)) return; snapshot = updated; Filter();
    }
    void Filter() {
        var id = selected?.Id; var q = search.Text.Trim();
        var filtered = snapshot.Where(x=>sourceFilter=="all"||sourceFilter=="text"&&x.ImageId is null||sourceFilter=="image"&&x.ImageId is not null).Where(x => x.Text.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Source.Contains(q, StringComparison.OrdinalIgnoreCase) || (x.Note ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)||x.Time.LocalDateTime.ToString("yyyy-MM-dd HH:mm").Contains(q,StringComparison.OrdinalIgnoreCase)).ToList();
        records.ItemsSource = filtered; records.SelectedItem = filtered.FirstOrDefault(x => x.Id == id) ?? filtered.FirstOrDefault();
        empty.Text = q.Length == 0 ? "暂无历史记录\n输入文字或截图识字后会自动保存。" : "没有匹配的记录\n请换个关键词。";
        empty.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed; ShowSelected();RefreshHistoryCounts();
    }
    void ShowSelected() {
        selected = records.SelectedItem as ReaderHistory; selectedImage = null; image.Source = null;
        selectedTitle.Text=selected is null?"选择归档记录":selected.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim('#','*',' ','`')??"图片记录";
        info.Text = selected?.Caption ?? "选择左侧记录可查看完整文字和原始截图。"; text.Text = selected?.Text ?? "";
        if (selected?.Note is string note) info.Text += "\n" + note;
        imageInfo.Text = selected?.ImageId is null ? "此记录没有原图。旧版未保存的截图无法补回。" : "正在读取原图…";
        try {
            selectedImage = images.Load(selected?.ImageId);
            if (selectedImage is not null) {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 1600; bitmap.StreamSource = new MemoryStream(selectedImage); bitmap.EndInit(); bitmap.Freeze(); image.Source = bitmap;
                imageInfo.Text = "本机原图 · 查看不会上传图片";
            } else if (selected?.ImageId is not null) imageInfo.Text = "原图文件不存在，已保存的文字仍可查看。";
        } catch (Exception ex) { imageInfo.Text = "原图读取失败：" + ex.Message; }
        load.IsEnabled = selected is not null; read.IsEnabled = !string.IsNullOrWhiteSpace(selected?.Text);
        retry.IsEnabled = export.IsEnabled = selectedImage is not null;
        foreach (var action in selectionActions) action.IsEnabled = selected is not null;
        RefreshMarkdownPreview();
    }
    void LoadSelected(bool speak) { if (selected is null) return; var item = selected; Leave(); onLoad(item, speak); }
    void ExportImage() {
        if (selectedImage is null) return;
        using var memory=new MemoryStream(selectedImage);var decoder=BitmapDecoder.Create(memory,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var ext=decoder.CodecInfo.FileExtensions.Split(',')[0];
        var dialog = new SaveFileDialog { FileName = "历史原图" + ext, Filter = "图片文件|*" + ext }; if (dialog.ShowDialog(Owner??this) == true) File.WriteAllBytes(dialog.FileName, selectedImage);
    }
    sealed class ThumbnailConverter(HistoryImages images) : IValueConverter {
        readonly Dictionary<string, ImageSource> cache = new();
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is not string id) return null; if (cache.TryGetValue(id, out var cached)) return cached;
            try { var bytes = images.Load(id); if (bytes is null) return null; var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 72; bitmap.StreamSource = new MemoryStream(bytes); bitmap.EndInit(); bitmap.Freeze(); cache[id] = bitmap; return bitmap; } catch { return null; }
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
