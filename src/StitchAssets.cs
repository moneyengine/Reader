using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LightReader;

public static class StitchAssets
{
    static readonly Uri Root=new("pack://application:,,,/LightReader;component/");
    public static FontFamily Body {get;}=new(Root,"./Assets/Fonts/#Source Sans 3");
    public static FontFamily Heading {get;}=new(Root,"./Assets/Fonts/#Plus Jakarta Sans");
    public static FontFamily Code {get;}=new("Cascadia Mono, Consolas, Microsoft YaHei UI");
    public static FontFamily Symbols {get;}=new(Root,"./Assets/Fonts/#Material Symbols Outlined");
    static readonly Dictionary<string,int> Glyphs=LoadGlyphs();
    static Dictionary<string,int> LoadGlyphs(){
        using var stream=typeof(StitchAssets).Assembly.GetManifestResourceStream("LightReader.Assets.Fonts.MaterialSymbolsOutlined.codepoints")!;
        using var reader=new StreamReader(stream);var map=new Dictionary<string,int>();
        while(reader.ReadLine() is {} line){var pair=line.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(pair.Length==2)map[pair[0]]=Convert.ToInt32(pair[1],16);}
        return map;
    }
    public static TextBlock Icon(string name,double size=18){return new TextBlock{Text=char.ConvertFromUtf32(Glyphs[name]),FontFamily=Symbols,FontSize=size,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};}
    public static void SetIcon(Button button,string name,double size=18){button.Content=Icon(name,size);}
    public static CheckBox Switch(Window owner,string key,bool value,Action<bool> changed){
        var label=key switch{"SkipCode"=>"跳过编程代码块","SkipLinks"=>"跳过网址与文件路径","SkipDecorations"=>"跳过表情与装饰字符","KeepInlineTerms"=>"保留行内专业术语","FocusSentences"=>"句子自动聚焦","ReducedTransparency"=>"减少透明度",_=>key};
        var control=new CheckBox{Name=key,IsChecked=value,Style=(Style)owner.FindResource("StitchSwitch"),VerticalAlignment=VerticalAlignment.Center,ToolTip=label};
        System.Windows.Automation.AutomationProperties.SetName(control,label);
        control.Checked+=(_,_)=>changed(true);control.Unchecked+=(_,_)=>changed(false);return control;
    }
}
