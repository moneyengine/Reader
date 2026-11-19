using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LightReader;

public partial class NativeReaderWindow
{
    readonly TextBlock voiceTileTitle=new(){FontFamily=StitchAssets.Heading,FontSize=13,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
    readonly TextBlock voiceTileDescription=new(){FontFamily=StitchAssets.Heading,FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis};
    void BuildStitchVoiceTile()
    {
        Unparent(Engine);Engine.Width=double.NaN;Engine.Height=30;Engine.MinHeight=28;
        Engine.Visibility=Visibility.Collapsed;Voice.ProviderElement=null;
        var tile=new Grid();tile.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(40)});tile.ColumnDefinitions.Add(new ColumnDefinition());tile.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(18)});
        var symbol=StitchAssets.Icon("neurology",20);symbol.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");var mark=new Border{Child=symbol,Width=32,Height=32,CornerRadius=new CornerRadius(4)};mark.SetResourceReference(Border.BackgroundProperty,"LensBrush");tile.Children.Add(mark);
        var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,4,0)};labels.Children.Add(voiceTileTitle);voiceTileDescription.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");labels.Children.Add(voiceTileDescription);Grid.SetColumn(labels,1);tile.Children.Add(labels);
        var arrow=StitchAssets.Icon("expand_more",14);Grid.SetColumn(arrow,2);tile.Children.Add(arrow);
        Voice.SelectionContent=tile;Voice.Style=(Style)FindResource("StitchVoicePicker");Voice.Height=42;Voice.MinHeight=40;Voice.Margin=new Thickness(0);
        RefreshVoiceTile();
    }
    void RefreshVoiceTile()
    {
        var voice=Voice.SelectedItem as VoiceChoice;
        var provider="Edge";
        var shortName=voice?.Name.Split(" · ",StringSplitOptions.None)[0]??"选择音色";
        var metadata=(onlineVoices??EdgeTtsClient.Catalogue.ToList()).FirstOrDefault(x=>x.Id==voice?.Id);
        voiceTileTitle.Text=$"{provider} · {shortName}"+(metadata is null?"":$" ({metadata.Locale})");
        voiceTileDescription.Text=$"{(metadata?.Gender=="Female"?"女声":metadata?.Gender=="Male"?"男声":"自然语音")} · 微软在线神经语音";
        Voice.ToolTip=(voice?.Name??"请选择音色")+"\n"+voiceTileDescription.Text;
    }
}
