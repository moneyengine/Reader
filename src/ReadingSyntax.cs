using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;

namespace LightReader;

public static class ReadingSyntax
{
    public static string[] Colors(string text,string language)
    {
        var keys=Enumerable.Repeat("ReaderCodeInkBrush",text.Length).ToArray();
        var name=language.Trim().ToLowerInvariant() switch{
            "cpp" or "c++" or "c" or "hlsl" or "glsl"=>"C++",
            "cs" or "csharp" or "c#"=>"C#",
            "js" or "javascript" or "ts" or "typescript"=>"JavaScript",
            "py" or "python"=>"Python",
            "json"=>"Json","xml" or "html"=>"XML","sql"=>"SQL",_=>null};
        var definition=name is null?null:HighlightingManager.Instance.GetDefinition(name);
        if(definition is null)return keys;
        var document=new TextDocument(text);
        using var highlighter=new DocumentHighlighter(document,definition);
        foreach(var line in document.Lines){
            foreach(var section in highlighter.HighlightLine(line.LineNumber).Sections){
                var color=section.Color.Name?.ToLowerInvariant()??"";
                var key=color.Contains("comment")?"ReaderCommentBrush":color.Contains("string")||color.Contains("char")?"ReaderStringBrush":
                    color.Contains("number")||color.Contains("digit")?"ReaderNumberBrush":color.Contains("type")?"ReaderTypeBrush":
                    color.Contains("keyword")||color.Contains("preprocessor")?"ReaderKeywordBrush":"ReaderCodeInkBrush";
                for(int i=Math.Max(0,section.Offset);i<Math.Min(keys.Length,section.Offset+section.Length);i++)keys[i]=key;
            }
        }
        return keys;
    }
}
