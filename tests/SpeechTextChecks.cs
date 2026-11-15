using LightReader;

static class SpeechTextChecks
{
    public static void Run()
    {
        var text="你好，https://example.com/a?q=1。世界！ **正文** www.example.org 😀 👨‍👩‍👧 🇨🇳 1️⃣ 价格123元，速度1.25倍。";
        var filtered=SpeechText.Filter(text);
        if(filtered.Length!=text.Length)throw new Exception("Filtering changed document offsets");
        foreach(var word in new[]{"你好","世界","正文","价格123元","速度1.25倍"})
            if(filtered.IndexOf(word,StringComparison.Ordinal)!=text.IndexOf(word,StringComparison.Ordinal))throw new Exception("Text or offsets lost: "+word);
        if(filtered.Contains("example")||filtered.Contains('*')||filtered.Contains("1️⃣"))throw new Exception("Skipped content remains");
        if(SpeechText.HasWords(SpeechText.Filter("https://example.com ** 😀 👨‍👩‍👧 🇨🇳 1️⃣")))throw new Exception("Skipped-only input still speakable");
        if(SpeechText.Filter("正常中文，English 42。问号？")==""||!SpeechText.HasWords(filtered))throw new Exception("Normal text removed");
        foreach(var path in new[]{
            "F:/R6Game/Plugins/SGFramework/Source/SGCharacterDeathComponent.cpp:2098",
            @"C:\Users\test\Documents\report.txt", @"\\server\share\folder\file.txt",
            "/home/user/project/main.cpp:42", "R6Game/Source/UAGame/main.cpp:197",
            "./src/main.cs", "../src/main.cs", "~/Documents/report.txt",
            "\"C:\\Program Files\\LightReader\\LightReader.exe\""}){
            var sample="见 [文件标签]("+path+")。接着朗读123。";
            var result=SpeechText.Filter(sample);
            if(result.Length!=sample.Length||!string.IsNullOrWhiteSpace(result.Substring(sample.IndexOf(path,StringComparison.Ordinal),path.Length)))throw new Exception("Path not skipped: "+path);
            if(result.IndexOf("接着朗读123",StringComparison.Ordinal)!=sample.IndexOf("接着朗读123",StringComparison.Ordinal)||!result.Contains("文件标签"))throw new Exception("Path filtering damaged surrounding text");
        }
        var normal="速度1.25倍，日期2026年，输入/输出，Windows/SAPI。";
        if(SpeechText.Filter(normal)!=normal)throw new Exception("Ordinary slash or numbers treated as paths");
        foreach(var code in new[]{
            "```cpp\nconst float Dot = FVector2D::DotProduct(F, D);\nconst bool bVisible = Dot > 0.f;\n```",
            "~~~python\nprint('不要朗读')\n~~~",
            "    const float DistSq = D.SizeSquared();\n    return DistSq;",
            "> ```cpp\n> const int a = 1;\n> ```",
            "```cpp\nconst int incomplete = 1;"}){
            var sample="前面的正文。\n\n"+code+"\n\n";
            var result=SpeechText.Filter(sample);
            if(result.Length!=sample.Length||!result.StartsWith("前面的正文。",StringComparison.Ordinal)||!string.IsNullOrWhiteSpace(result["前面的正文。".Length..]))throw new Exception("Code not skipped: "+code);
        }
        var mixed="计算 `Dot > 0` 后继续正常朗读。";
        var clean=SpeechText.Filter(mixed);
        if(clean.IndexOf("后继续正常朗读",StringComparison.Ordinal)!=mixed.IndexOf("后继续正常朗读",StringComparison.Ordinal))throw new Exception("Code filtering changed following offsets");
        if(SpeechText.HasWords(SpeechText.Filter("```cpp\nconst int a=1;\n```")))throw new Exception("Code-only document still speakable");
        var regression="```cpp\nconst float Dot = FVector2D::DotProduct(F, D);\n```\n\n`CosHalfAngleSq` 在配置加载时预计算，`Dot > 0` 必须保留。";
        var preserved=SpeechText.Filter(regression);
        foreach(var word in new[]{"CosHalfAngleSq","Dot","在配置加载时预计算","必须保留"}){
            var expected=regression.LastIndexOf(word,StringComparison.Ordinal);
            if(preserved.Substring(expected,word.Length)!=word)throw new Exception("Inline prose after code block lost: "+word);
        }
        if(preserved.Contains("FVector2D")||preserved.Contains('`'))throw new Exception("Code block or inline delimiters still read");
        if(!SpeechText.Filter("``value ` nested``").Contains("value"))throw new Exception("Double-backtick inline content lost");
        Console.WriteLine("PASS code blocks skipped; inline terms after block retained, including CosHalfAngleSq; original offsets preserved");
        Console.WriteLine("PASS file paths: drive, UNC, Unix, relative, quoted spaces and line numbers; labels and surrounding prose preserved");
        Console.WriteLine("PASS speech filtering: URLs, decoration, emoji/ZWJ/flags/keycaps; original offsets, punctuation and numbers preserved");
    }
}
