using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LightReader;

public sealed class Settings
{
    public string ArkKey { get; set; } = "";
    public string SpeechKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string OcrProvider { get; set; } = "Local";
    public string OcrLanguage { get; set; } = "auto";
    public string SpeechProvider { get; set; } = "Windows";
    public string WindowsVoice { get; set; } = "";
    public string EdgeVoice { get; set; } = "zh-CN-XiaoxiaoNeural";
    public string EdgePython { get; set; } = "";
    public string AudioVoice { get; set; } = "zh_female_vv_uranus_bigtts";
    public string AudioVoices { get; set; } = "Vivi 2.0|zh_female_vv_uranus_bigtts\n小何 2.0|zh_female_xiaohe_uranus_bigtts\n云舟 2.0|zh_male_m191_uranus_bigtts\n小天 2.0|zh_male_taocheng_uranus_bigtts";
    public string Voice { get; set; } = "zh_female_vv_jupiter_bigtts";
    public string Voices { get; set; } = "Vivi|zh_female_vv_jupiter_bigtts\n小何|zh_female_xiaohe_jupiter_bigtts\n云舟|zh_male_yunzhou_jupiter_bigtts\n小天|zh_male_xiaotian_jupiter_bigtts";
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightReader", "settings.dat");
    public static Settings Load() => !File.Exists(FilePath) ? new() : JsonSerializer.Deserialize<Settings>(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser)) ?? new();
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath + ".tmp", bytes);
        File.Move(FilePath + ".tmp", FilePath, true);
    }
    public Settings Copy() => (Settings)MemberwiseClone();
}

public record VoiceChoice(string Name, string Id, bool Compatible = true)
{
    public override string ToString() => Name;
    public static List<VoiceChoice> Parse(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim().Split('|', 2)).Where(x => x.Length == 2 && x.All(y => !string.IsNullOrWhiteSpace(y)))
        .Select(x => new VoiceChoice(x[0].Trim(), x[1].Trim())).DistinctBy(x => x.Id).ToList();
}

