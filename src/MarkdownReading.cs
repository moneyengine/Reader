using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace LightReader;

public static class MarkdownReading
{
    public static bool IsMarkdown(string text)
    {
        var document=Markdown.Parse(text,Pipeline);
        bool Styled(ContainerInline inline)=>inline.Any(x=>x is EmphasisInline or CodeInline or LinkInline||x is ContainerInline nested&&Styled(nested));
        bool Blocks(ContainerBlock blocks)=>blocks.Any(x=>x is HeadingBlock or Markdig.Syntax.ListBlock or QuoteBlock or CodeBlock or ThematicBreakBlock||x is LeafBlock{Inline:{} inline}&&Styled(inline)||x is ContainerBlock nested&&Blocks(nested));
        return Blocks(document);
    }
    static readonly MarkdownPipeline Pipeline=new MarkdownPipelineBuilder().UseAdvancedExtensions().UsePreciseSourceLocation().Build();
    public static FlowDocument Render(string text,double size,Brush ink,Action<int> jump,List<(int Start,int Length,Run Run)> runs)
    {
        var doc=new FlowDocument{FontSize=size,Foreground=ink,PagePadding=new Thickness(0),LineHeight=size*1.9};
        void AddText(InlineCollection target,string value,int start){
            foreach(var token in ReaderText.Tokens(value)){
                var run=new Run(token.Text);var index=Math.Clamp(start+token.Start,0,text.Length);
                run.MouseLeftButtonDown+=(_,e)=>{e.Handled=true;jump(index);};
                target.Add(run);runs.Add((index,token.Text.Length,run));
            }
        }
        void Inlines(ContainerInline? source,InlineCollection target){
            if(source is null)return;
            foreach(var item in source){
                switch(item){
                    case LiteralInline literal:AddText(target,literal.Content.ToString(),literal.Span.Start);break;
                    case CodeInline code:var span=new Span{FontFamily=new FontFamily("Consolas"),Background=new SolidColorBrush(Color.FromArgb(25,120,120,120))};AddText(span.Inlines,code.Content,code.Span.Start+code.DelimiterCount);target.Add(span);break;
                    case LineBreakInline:target.Add(new LineBreak());break;
                    case EmphasisInline emphasis:var styled=new Span();if(emphasis.DelimiterChar=='~')styled.TextDecorations=TextDecorations.Strikethrough;else if(emphasis.DelimiterCount>=2)styled.FontWeight=FontWeights.Bold;else styled.FontStyle=FontStyles.Italic;Inlines(emphasis,styled.Inlines);target.Add(styled);break;
                    case LinkInline link:var label=new Span();Inlines(link,label.Inlines);target.Add(label);break;
                    case ContainerInline container:Inlines(container,target);break;
                }
            }
        }
        void Blocks(ContainerBlock source,BlockCollection target){
            foreach(var block in source){
                if(block is Markdig.Syntax.ListBlock list){
                    var rendered=new System.Windows.Documents.List{MarkerStyle=list.IsOrdered?TextMarkerStyle.Decimal:TextMarkerStyle.Disc,Margin=new Thickness(0,8,0,12)};
                    foreach(var child in list.OfType<ListItemBlock>()){var li=new ListItem();Blocks(child,li.Blocks);rendered.ListItems.Add(li);}target.Add(rendered);
                }else if(block is QuoteBlock quote){var section=new Section{BorderBrush=Brushes.Gray,BorderThickness=new Thickness(3,0,0,0),Padding=new Thickness(12,0,0,0)};Blocks(quote,section.Blocks);target.Add(section);
                }else if(block is CodeBlock code){var p=new Paragraph{FontFamily=new FontFamily("Consolas"),Margin=new Thickness(0,8,0,12)};foreach(var line in code.Lines.Lines.Take(code.Lines.Count)){AddText(p.Inlines,line.Slice.ToString(),line.Slice.Start);p.Inlines.Add(new LineBreak());}target.Add(p);
                }else if(block is LeafBlock leaf){var p=new Paragraph{Margin=new Thickness(0,0,0,12)};if(block is HeadingBlock h){p.FontSize=size+(7-h.Level)*2;p.FontWeight=FontWeights.Bold;}Inlines(leaf.Inline,p.Inlines);target.Add(p);
                }else if(block is ContainerBlock container)Blocks(container,target);
            }
        }
        Blocks(Markdown.Parse(text,Pipeline),doc.Blocks);runs.Sort((a,b)=>a.Start.CompareTo(b.Start));return doc;
    }
}
