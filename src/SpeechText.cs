using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;

namespace LightReader;

public static class SpeechText
{
    static readonly MarkdownPipeline Pipeline=new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build();
    static string WithoutCode(string text)
    {
        var characters=text.ToCharArray();
        void Blank(SourceSpan span){
            for(int i=Math.Max(0,span.Start);i<=span.End&&i<characters.Length;i++)
                if(characters[i] is not '\r' and not '\n')characters[i]=' ';
        }
        void VisitBlock(ContainerBlock container){
            foreach(var child in container){
                if(child is CodeBlock code){
                    if(code is FencedCodeBlock fence&&fence.Info?.Trim().Equals("text",StringComparison.OrdinalIgnoreCase)==true)continue;
                    Blank(code.Span);
                    foreach(var line in code.Lines.Lines.Take(code.Lines.Count))Blank(new SourceSpan(line.Slice.Start,line.Slice.End));
                    if(code is FencedCodeBlock{ClosingFencedCharCount:0})Blank(new SourceSpan(code.Span.Start,text.Length-1));
                }
                else if(child is ContainerBlock nested)VisitBlock(nested);
            }
        }
        VisitBlock(Markdown.Parse(text,Pipeline));
        return new string(characters);
    }
    const string PathPart = @"[^\s/\\<>""'`()\[\]{},，。！？；（）【】]+";
    static readonly Regex Paths = new(
        @"(?:https?://|ftp://|www\.)[^\s<>""，。！？；（）【】]+|" +
        @"[""'`](?:[A-Z]:[/\\]|\\\\|/|\.{1,2}[/\\]|~/)[^\r\n""'`]+[""'`]|" +
        @"(?:[A-Z]:[/\\]|\\\\|(?<![\p{L}\p{N}])(?:/|\.{1,2}[/\\]|~/))" + PathPart + @"(?:[/\\]" + PathPart + @")*|" +
        @"(?<![\p{L}\p{N}/\\])" + PathPart + @"(?:[/\\]" + PathPart + @"){2,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));
    static readonly Regex Skipped = new(
        @"(?:https?://|ftp://|www\.)[^\s<>""，。！？；（）【】]+|[0-9#*]\ufe0f?\u20e3|[\p{So}\p{Sk}\p{Cs}\p{Co}\p{Cf}]|[*#_~`|\\^=<>]+|[\u2300-\u23ff\u2600-\u27bf\ufe0e\ufe0f\u20e3]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    // Preserve UTF-16 offsets so speech boundaries still refer to the original document.
    public static string Filter(string text)
    {
        // Match paths before stripping backslashes and Markdown delimiters.
        var withoutPaths=Paths.Replace(WithoutCode(text),m=>new string(' ',m.Length));
        return Skipped.Replace(withoutPaths,m=>new string(' ',m.Length));
    }

    public static bool HasWords(string text)=>text.EnumerateRunes().Any(Rune.IsLetterOrDigit);
}
