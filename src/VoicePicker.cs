using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LightReader;

public sealed class VoiceCategory : INotifyPropertyChanged
{
    public string Label { get; }
    public string Path { get; }
    public VoiceChoice? Voice { get; }
    public List<VoiceCategory> Children { get; } = new();
    public bool Selectable => Voice?.Compatible ?? true;
    public int Count => Voice is null ? Children.Sum(x=>x.Count) : 1;
    bool expanded,current;
    public bool IsExpanded {get=>expanded;set{expanded=value;Changed();}}
    public bool IsCurrent {get=>current;set{current=value;Changed();Changed(nameof(Header));}}
    public string Header => Voice is null?$"{Label}  ({Count})":Label+(IsCurrent?"  ✓":"");
    public string ToolTip => Voice is null?Path:Path+"\n"+Voice.Id+(Voice.Compatible?"":"\n此音色属于其他接口，当前不可选");
    public VoiceCategory(string label,string path,VoiceChoice? voice=null){Label=label;Path=path;Voice=voice;}
    public IEnumerable<VoiceCategory> Descendants(){yield return this;foreach(var node in Children)foreach(var child in node.Descendants())yield return child;}
    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}

public static class VoiceCategories
{
    public sealed record Entry(VoiceChoice Voice,string[] Path,string SearchText);
    static readonly Dictionary<string,string> Languages=new(){["zh"]="中文",["en"]="英语",["ja"]="日语",["ko"]="韩语",["fr"]="法语",["de"]="德语",["es"]="西班牙语",["pt"]="葡萄牙语",["ru"]="俄语",["ar"]="阿拉伯语",["it"]="意大利语",["hi"]="印地语",["th"]="泰语",["vi"]="越南语",["id"]="印度尼西亚语",["ms"]="马来语",["nl"]="荷兰语",["tr"]="土耳其语",["pl"]="波兰语",["uk"]="乌克兰语",["sv"]="瑞典语"};
    static string Language(string locale){var lang=locale.Split('-')[0];if(Languages.TryGetValue(lang,out var name))return name;try{return CultureInfo.GetCultureInfo(lang).NativeName;}catch(CultureNotFoundException){return lang;}}
    static string Region(string locale)=>locale switch{
        "zh-CN"=>"普通话（中国大陆）","zh-HK"=>"粤语（中国香港）","zh-TW"=>"国语（中国台湾）",
        "zh-CN-liaoning"=>"辽宁口音","zh-CN-shaanxi"=>"陕西口音","en-US"=>"美国","en-GB"=>"英国","en-AU"=>"澳大利亚","en-CA"=>"加拿大",
        _=>RegionName(locale)};
    static string RegionName(string locale){try{return new RegionInfo(locale).DisplayName;}catch(ArgumentException){return locale;}}
    public static List<Entry> Describe(string provider,IEnumerable<VoiceChoice> voices,IEnumerable<EdgeVoice>? edge=null){
        var edgeMap=(edge??EdgeTtsClient.Catalogue).ToDictionary(x=>x.Id);
        var cloud=VoiceCatalog.All.ToDictionary(x=>x.Id);
        return voices.Select(v=>{
            string[] path;var tags="";
            if(provider=="Edge"&&edgeMap.TryGetValue(v.Id,out var e))path=new[]{Language(e.Locale),Region(e.Locale),e.Gender=="Female"?"女声":e.Gender=="Male"?"男声":"其他声音"};
            else if(provider=="Cloud"&&cloud.TryGetValue(v.Id,out var c)){
                var category=c.Category.Split(',')[0].Trim().Replace("\\-","-");
                path=new[]{v.Compatible?"当前接口音色":"其他接口音色",string.IsNullOrWhiteSpace(category)?"未分类":category};tags=c.Category+" "+c.Section;
            }else if(provider=="Cloud")path=new[]{"自定义音色"};
            else{
                var locale=Regex.Match(v.Name,@"\b[a-z]{2,3}-[A-Z]{2}\b").Value;
                path=new[]{locale.Length==0?"其他语言":Language(locale),v.Id.StartsWith("winrt:")?"Windows 音色":"SAPI 音色"};
            }
            return new Entry(v,path,string.Join(" ",path)+" "+v.Name+" "+v.Id+" "+tags);
        }).ToList();
    }
    public static List<VoiceCategory> Build(IEnumerable<Entry> source,string query,string? selected){
        var roots=new List<VoiceCategory>();var words=query.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        foreach(var entry in source.Where(x=>words.All(w=>x.SearchText.Contains(w,StringComparison.OrdinalIgnoreCase)))){
            var level=roots;var current="";
            foreach(var label in entry.Path){current=current.Length==0?label:current+" / "+label;var node=level.FirstOrDefault(x=>x.Voice is null&&x.Label==label);if(node is null){node=new(label,current);level.Add(node);}level=node.Children;}
            level.Add(new(entry.Voice.Name,current,entry.Voice){IsCurrent=entry.Voice.Id==selected});
        }
        foreach(var group in roots.SelectMany(x=>x.Descendants()).Where(x=>x.Voice is null))group.IsExpanded=words.Length>0||group.Descendants().Any(x=>x.Voice?.Id==selected);
        return roots;
    }
}

/// <summary>Preserves ComboBox selection/API; category headers never select a different voice.</summary>
public sealed class VoicePicker : ComboBox
{
    readonly TreeView tree=new(){BorderThickness=new Thickness(0),Background=Brushes.Transparent};
    readonly TextBox search=new(){Tag="搜索音色、语言、地区或分类",Margin=new Thickness(0,7,0,9)};
    readonly TextBlock summary=new(){FontSize=11,Margin=new Thickness(3,0,0,8),TextWrapping=TextWrapping.Wrap};
    readonly TextBlock empty=new(){Text="没有匹配的音色，请换个关键词。",Margin=new Thickness(10,15,10,15)};
    List<VoiceCategories.Entry> entries=new();
    string query="";bool updating;
    Border? popupFrame;
    public IReadOnlyList<VoiceCategory> Categories {get;private set;}=Array.Empty<VoiceCategory>();
    public event Action<string>? SearchChanged;
    public VoicePicker(){
        VirtualizingPanel.SetIsVirtualizing(tree,true);
        VirtualizingPanel.SetVirtualizationMode(tree,VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(tree,true);
        ScrollViewer.SetHorizontalScrollBarVisibility(tree,ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(this,true);
        VirtualizingPanel.SetVirtualizationMode(this,VirtualizationMode.Recycling);
        tree.SetResourceReference(ForegroundProperty,"InkBrush");search.TextChanged+=(_,_)=>{if(!updating){Filter(search.Text);SearchChanged?.Invoke(search.Text);}};
        tree.SelectedItemChanged+=(_,e)=>{if(IsDropDownOpen&&e.NewValue is VoiceCategory{Voice:{} choice}&&choice.Compatible)Commit(choice);};
        tree.PreviewMouseLeftButtonUp+=(_,e)=>{
            var source=e.OriginalSource as DependencyObject;bool toggle=false;TreeViewItem? item=null;
            while(source is not null){if(source is ToggleButton)toggle=true;if(source is TreeViewItem found){item=found;break;}source=VisualTreeHelper.GetParent(source);}
            if(!toggle&&item?.DataContext is VoiceCategory{Voice:null} node){node.IsExpanded=!node.IsExpanded;e.Handled=true;}
        };
        tree.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space&&tree.SelectedItem is VoiceCategory node){if(node.Voice is null)node.IsExpanded=!node.IsExpanded;else if(node.Selectable)Commit(node.Voice);e.Handled=true;}};
        search.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Down){tree.Focus();var container=tree.ItemContainerGenerator.ContainerFromIndex(0) as TreeViewItem;container?.Focus();e.Handled=true;}};
        var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new Binding(nameof(VoiceCategory.Header)));label.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
        tree.ItemTemplate=new HierarchicalDataTemplate(typeof(VoiceCategory)){ItemsSource=new Binding(nameof(VoiceCategory.Children)),VisualTree=label};
    }
    public void SetCatalogue(string provider,IEnumerable<VoiceChoice> voices,IEnumerable<EdgeVoice>? metadata=null){entries=VoiceCategories.Describe(provider,voices,metadata);SetCurrentValue(IsTextSearchEnabledProperty,false);query="";updating=true;search.Clear();updating=false;Rebuild();}
    public void Filter(string text){if(query==text&&search.Text==text)return;query=text;updating=true;if(search.Text!=text)search.Text=text;updating=false;Rebuild();}
    void Rebuild(){Categories=VoiceCategories.Build(entries,query,(SelectedItem as VoiceChoice)?.Id);tree.ItemsSource=Categories;var count=Categories.Sum(x=>x.Count);summary.Text=$"{count} 个音色 · 展开分类后选择具体音色";empty.Visibility=count==0?Visibility.Visible:Visibility.Collapsed;}
    public void CollapseCategories(){foreach(var node in Categories.SelectMany(x=>x.Descendants()))node.IsExpanded=false;}
    void Commit(VoiceChoice choice){SelectedItem=Items.OfType<VoiceChoice>().FirstOrDefault(x=>x.Id==choice.Id);IsDropDownOpen=false;Focus();}
    protected override void OnSelectionChanged(SelectionChangedEventArgs e){base.OnSelectionChanged(e);foreach(var node in Categories.SelectMany(x=>x.Descendants()))node.IsCurrent=node.Voice?.Id==(SelectedItem as VoiceChoice)?.Id;}
    public override void OnApplyTemplate(){
        base.OnApplyTemplate();if(GetTemplateChild("PART_Popup") is not Popup popup)return;
        foreach(var control in new FrameworkElement[]{search,tree,summary,empty})if(control.Parent is Panel parent)parent.Children.Remove(control);
        var border=new Border{CornerRadius=new CornerRadius(19),BorderThickness=new Thickness(1),Padding=new Thickness(12),Margin=new Thickness(0,5,0,0)};
        border.SetResourceReference(Border.BackgroundProperty,"CardBrush");border.SetResourceReference(Border.BorderBrushProperty,"LineBrush");border.SetResourceReference(ForegroundProperty,"InkBrush");
        popupFrame=border;var dock=new DockPanel();border.Child=dock;
        var header=new DockPanel();DockPanel.SetDock(header,Dock.Top);dock.Children.Add(header);
        var collapse=new Button{Content="收起分类",FontSize=11,Padding=new Thickness(10,4,10,4),MinHeight=30};DockPanel.SetDock(collapse,Dock.Right);collapse.Click+=(_,_)=>CollapseCategories();header.Children.Add(collapse);
        header.Children.Add(new TextBlock{Text="分类选择音色",FontSize=16,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
        DockPanel.SetDock(search,Dock.Top);dock.Children.Add(search);DockPanel.SetDock(summary,Dock.Top);dock.Children.Add(summary);summary.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");
        // A bounded viewport is required for WPF to virtualize the category tree.
        var body=new Grid{MaxHeight=360};body.Children.Add(tree);body.Children.Add(empty);dock.Children.Add(body);
        tree.ItemContainerStyle=TryFindResource("VoiceTreeItem") as Style;
        popup.Child=border;
    }
    protected override void OnDropDownOpened(EventArgs e){
        base.OnDropDownOpened(e);
        if(popupFrame is not null){popupFrame.Width=Math.Min(Math.Max(ActualWidth,440),720);popupFrame.MaxHeight=Math.Min(520,SystemParameters.WorkArea.Height*.7);}
        foreach(var node in Categories.Where(x=>x.Descendants().Any(n=>n.IsCurrent)))ExpandSelected(node);
        Dispatcher.BeginInvoke(DispatcherPriority.Input,(Action)(()=>{if(IsDropDownOpen)search.Focus();}));
    }

    void ExpandSelected(VoiceCategory node){if(node.Voice is not null)return;if(node.Descendants().Any(n=>n.IsCurrent))node.IsExpanded=true;foreach(var child in node.Children)ExpandSelected(child);}
    protected override void OnPreviewKeyDown(KeyEventArgs e){
        if(IsDropDownOpen){
            if(e.Key==Key.Escape){IsDropDownOpen=false;Focus();e.Handled=true;return;}
            if(search.IsKeyboardFocusWithin||tree.IsKeyboardFocusWithin){if(e.Key==Key.Enter&&search.IsKeyboardFocusWithin){var first=Categories.SelectMany(x=>x.Descendants()).FirstOrDefault(x=>x.Voice is not null&&x.Selectable);if(query.Length>0&&first?.Voice is not null)Commit(first.Voice);e.Handled=true;}return;}
        }
        if(!IsDropDownOpen&&e.Key is Key.Down or Key.Up){IsDropDownOpen=true;e.Handled=true;return;}
        base.OnPreviewKeyDown(e);
    }
    protected override void OnKeyDown(KeyEventArgs e){if(IsDropDownOpen&&(search.IsKeyboardFocusWithin||tree.IsKeyboardFocusWithin))return;base.OnKeyDown(e);}
}
