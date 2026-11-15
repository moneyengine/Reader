using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace LightReader;

public sealed record CatalogVoice(string Name, string Id, string Category, string Section, bool Compatible);

public static class VoiceCatalog
{
    public static IReadOnlyList<CatalogVoice> All { get; } = Load();
    static List<CatalogVoice> Load()
    {
        using var stream = typeof(VoiceCatalog).Assembly.GetManifestResourceStream("LightReader.Data.voices.json")
            ?? throw new InvalidOperationException("内置音色列表缺失。");
        return JsonSerializer.Deserialize<List<CatalogVoice>>(stream)!;
    }
    public static List<VoiceChoice> Choices(string custom = "") => All.Select(x => new VoiceChoice(
            x.Name + (x.Compatible ? "" : "（其他接口）"), x.Id, x.Compatible))
        .Concat(VoiceChoice.Parse(custom)).DistinctBy(x => x.Id)
        .OrderByDescending(x => x.Compatible).ThenBy(x => x.Name).ToList();
    public static void Configure(ComboBox combo)
    {
        combo.MaxDropDownHeight = 420;
        combo.IsTextSearchEnabled = true;
        combo.SetValue(TextSearch.TextPathProperty, nameof(VoiceChoice.Name));
        var style = new Style(typeof(ComboBoxItem),combo.TryFindResource(typeof(ComboBoxItem)) as Style);
        style.Setters.Add(new Setter(UIElement.IsEnabledProperty, new Binding(nameof(VoiceChoice.Compatible))));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(nameof(VoiceChoice.Id))));
        combo.ItemContainerStyle = style;
        combo.ToolTip = $"官方音色 {All.Count} 个，其中 {All.Count(x => x.Compatible)} 个属于 TTS 2.0。灰色项适用其他接口。可键入名称前缀定位。";
    }
}
