using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace LightReader;

public static class MarkdownReading
{
    public static readonly DependencyProperty ForegroundKeyProperty=DependencyProperty.RegisterAttached("ForegroundKey",typeof(string),typeof(MarkdownReading),new PropertyMetadata(null));
    public static Brush BaseInk(Run run,Brush fallback)=>run.GetValue(ForegroundKeyProperty) is string key&&run.TryFindResource(key) is Brush brush?brush:fallback;
    public static double LineHeight(double size)=>size*1.5;
    static double HeadingSize(double size,int level)=>size*(level==1?1.6:level==2?1.3:1.1);
    public static void Resize(FlowDocument document,double size)
    {
        document.FontSize=size;document.LineHeight=LineHeight(size);
        void Inlines(InlineCollection inlines){foreach(var inline in inlines.OfType<Span>()){if(inline.Tag as string=="inline-code")inline.FontSize=size*.92;Inlines(inline.Inlines);}}
        void Blocks(BlockCollection blocks){foreach(var block in blocks){
            if(block is Paragraph paragraph){
                if(paragraph.Tag is int level)paragraph.FontSize=HeadingSize(size,level);
                if(paragraph.Tag as string=="code"){paragraph.FontSize=Math.Max(12,size*.85);paragraph.LineHeight=paragraph.FontSize*1.5;}
                Inlines(paragraph.Inlines);
            }else if(block is Section section)Blocks(section.Blocks);
            else if(block is System.Windows.Documents.List list){foreach(var item in list.ListItems)Blocks(item.Blocks);}
            else if(block is Table table){foreach(var group in table.RowGroups)foreach(var row in group.Rows)foreach(var cell in row.Cells)Blocks(cell.Blocks);}
        }}
        Blocks(document.Blocks);
    }
    public static bool IsMarkdown(string text)
    {
        var document=Markdown.Parse(text,Pipeline);
        bool Styled(ContainerInline inline)=>inline.Any(x=>x is EmphasisInline or CodeInline or LinkInline||x is ContainerInline nested&&Styled(nested));
        bool Blocks(ContainerBlock blocks)=>blocks.Any(x=>x is HeadingBlock or Markdig.Syntax.ListBlock or QuoteBlock or CodeBlock or ThematicBreakBlock or Markdig.Extensions.Tables.Table||x is LeafBlock{Inline:{} inline}&&Styled(inline)||x is ContainerBlock nested&&Blocks(nested));
        return Blocks(document);
    }
    static readonly MarkdownPipeline Pipeline=new MarkdownPipelineBuilder().UseAdvancedExtensions().UsePreciseSourceLocation().Build();
    public static int CountProgrammingBlocks(string text){
        int Count(ContainerBlock parent)=>parent.Sum(block=>block is CodeBlock code?(code is FencedCodeBlock fence&&fence.Info?.Trim().Equals("text",StringComparison.OrdinalIgnoreCase)==true?0:1):block is ContainerBlock nested?Count(nested):0);
        return Count(Markdown.Parse(text,Pipeline));
    }
    public static FlowDocument Render(string text,double size,Brush ink,Action<int> jump,List<(int Start,int Length,Run Run)> runs,ReadingPreferences? preferences=null,Action<Uri>? navigate=null,Action<string>? copy=null)
    {
        preferences??=new();
        var doc=new FlowDocument{FontSize=size,Foreground=ink,PagePadding=new Thickness(0),LineHeight=LineHeight(size)};
        void AddText(InlineCollection target,string value,int start,bool programming=false,string? colorKey=null,string[]? syntax=null,int syntaxOffset=0){
            // Pipe-table literals may report the cell's leading whitespace as Span.Start.
            if(start>=0&&start<=text.Length&&(start+value.Length>text.Length||!text.AsSpan(start,value.Length).SequenceEqual(value.AsSpan()))){
                var exact=text.IndexOf(value,start,StringComparison.Ordinal);if(exact>=0)start=exact;
            }
            foreach(var token in ReaderText.Tokens(value)){
                var run=new Run(token.Text);var index=Math.Clamp(start+token.Start,0,text.Length);
                if(programming)run.Tag="programming";
                var key=syntax is not null&&syntaxOffset+token.Start<syntax.Length?syntax[syntaxOffset+token.Start]:colorKey;
                if(key is not null){run.SetValue(ForegroundKeyProperty,key);run.SetResourceReference(TextElement.ForegroundProperty,key);}
                if(key=="ReaderCommentBrush")run.FontStyle=FontStyles.Italic;
                target.Add(run);runs.Add((index,token.Text.Length,run));
            }
        }
        void Inlines(ContainerInline? source,InlineCollection target){
            if(source is null)return;
            foreach(var item in source){
                switch(item){
                    case LiteralInline literal:AddText(target,literal.Content.ToString(),literal.Span.Start);break;
                    case CodeInline code:var span=new Span{FontFamily=StitchAssets.Code,FontSize=size*.92,Tag="inline-code"};span.SetResourceReference(TextElement.BackgroundProperty,"ReaderInlineCodeBrush");AddText(span.Inlines,code.Content,code.Span.Start+code.DelimiterCount,colorKey:"ReaderCodeInkBrush");target.Add(span);break;
                    case LineBreakInline:target.Add(new LineBreak());break;
                    case EmphasisInline emphasis:var styled=new Span();if(emphasis.DelimiterChar=='~')styled.TextDecorations=TextDecorations.Strikethrough;else if(emphasis.DelimiterCount>=2)styled.FontWeight=FontWeights.Bold;else styled.FontStyle=FontStyles.Italic;Inlines(emphasis,styled.Inlines);target.Add(styled);break;
                    case LinkInline link:
                        if(!link.IsImage&&Uri.TryCreate(link.Url,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https" or "mailto"){
                            var label=new Hyperlink{NavigateUri=uri,TextDecorations=null,ToolTip=uri.ToString()};
                            var first=runs.Count;Inlines(link,label.Inlines);
                            foreach(var entry in runs.Skip(first)){entry.Run.SetValue(ForegroundKeyProperty,"ReaderLinkBrush");entry.Run.SetResourceReference(TextElement.ForegroundProperty,"ReaderLinkBrush");}
                            label.RequestNavigate+=(_,e)=>{navigate?.Invoke(e.Uri);e.Handled=true;};
                            Point? down=null;
                            label.PreviewMouseLeftButtonDown+=(_,e)=>down=e.GetPosition(null);
                            label.PreviewMouseLeftButtonUp+=(_,e)=>{if(down is {} point&&(e.GetPosition(null)-point).Length<4){e.Handled=true;navigate?.Invoke(uri);}down=null;};
                            target.Add(label);
                        }else{var label=new Span();Inlines(link,label.Inlines);target.Add(label);}break;
                    case ContainerInline container:Inlines(container,target);break;
                }
            }
        }
        void Blocks(ContainerBlock source,BlockCollection target){
            foreach(var block in source){
                if(block is Markdig.Extensions.Tables.Table table){
                    var rendered=new System.Windows.Documents.Table{CellSpacing=0,Margin=new Thickness(0,8,0,24)};
                    var rows=table.OfType<Markdig.Extensions.Tables.TableRow>().ToList();
                    var count=rows.Select(row=>row.OfType<Markdig.Extensions.Tables.TableCell>().Sum(cell=>Math.Max(1,cell.ColumnSpan))).DefaultIfEmpty(0).Max();
                    for(int i=0;i<count;i++)rendered.Columns.Add(new TableColumn{Width=new GridLength(1,GridUnitType.Star)});
                    var group=new TableRowGroup();rendered.RowGroups.Add(group);
                    foreach(var row in rows){
                        var output=new System.Windows.Documents.TableRow();group.Rows.Add(output);int column=0;
                        foreach(var cell in row.OfType<Markdig.Extensions.Tables.TableCell>()){
                            var outputCell=new System.Windows.Documents.TableCell{ColumnSpan=Math.Max(1,cell.ColumnSpan),Padding=new Thickness(8),BorderBrush=Brushes.Gray,BorderThickness=new Thickness(.5)};
                            if(row.IsHeader){outputCell.FontWeight=FontWeights.Bold;outputCell.SetResourceReference(TextElement.BackgroundProperty,"GlassBrush");}
                            var definition=column<table.ColumnDefinitions.Count?table.ColumnDefinitions[column]:null;
                            outputCell.TextAlignment=definition?.Alignment switch{Markdig.Extensions.Tables.TableColumnAlign.Center=>TextAlignment.Center,Markdig.Extensions.Tables.TableColumnAlign.Right=>TextAlignment.Right,_=>TextAlignment.Left};
                            Blocks(cell,outputCell.Blocks);
                            foreach(var paragraph in outputCell.Blocks.OfType<Paragraph>())paragraph.Margin=new Thickness(0);
                            output.Cells.Add(outputCell);column+=outputCell.ColumnSpan;
                        }
                    }
                    target.Add(rendered);
                }else if(block is Markdig.Syntax.ListBlock list){
                    var rendered=new System.Windows.Documents.List{MarkerStyle=list.IsOrdered?TextMarkerStyle.Decimal:TextMarkerStyle.Disc,Margin=new Thickness(0,0,0,14),Padding=new Thickness(22,0,0,0)};
                    foreach(var child in list.OfType<ListItemBlock>()){
                        var li=new ListItem{Margin=new Thickness(0,2,0,2)};Blocks(child,li.Blocks);
                        foreach(var paragraph in li.Blocks.OfType<Paragraph>())paragraph.Margin=new Thickness(0,0,0,list.IsLoose?8:0);
                        rendered.ListItems.Add(li);
                    }target.Add(rendered);
                }else if(block is QuoteBlock quote){var section=new Section{BorderBrush=Brushes.Gray,BorderThickness=new Thickness(3,0,0,0),Padding=new Thickness(12,0,0,0)};Blocks(quote,section.Blocks);target.Add(section);
                }else if(block is CodeBlock code){
                    bool programming=code is not FencedCodeBlock{Info:{} fenceInfo}||!fenceInfo.Equals("text",StringComparison.OrdinalIgnoreCase);
                    var frame=new Section{Margin=new Thickness(0,4,0,18),BorderThickness=new Thickness(1),Padding=new Thickness(16,12,16,14)};
                    frame.SetResourceReference(TextElement.BackgroundProperty,"ReaderCodeBackgroundBrush");frame.SetResourceReference(Section.BorderBrushProperty,"ReaderCodeBorderBrush");
                    var info=code is FencedCodeBlock fence&&!string.IsNullOrWhiteSpace(fence.Info)?fence.Info.Trim():"code";
                    var language=info.ToLowerInvariant() switch{"cpp" or "c++"=>"C++","cs" or "csharp" or "c#"=>"C#","js"=>"JavaScript","py"=>"Python",_=>info.ToUpperInvariant()};
                    var status=programming&&preferences.SkipCode?"已跳过代码块朗读":programming?"参与朗读":"流程文本 · 正常朗读";
                    var heading=new Paragraph{FontFamily=StitchAssets.Body,FontSize=11,LineHeight=20,Margin=new Thickness(0,0,0,10)};
                    heading.SetResourceReference(TextElement.ForegroundProperty,"MutedBrush");
                    heading.Inlines.Add(new Run(language+"  ·  "+status){Tag="code-label"});
                    var lines=code.Lines.Lines.Take(code.Lines.Count).ToList();var codeText=string.Join("\n",lines.Select(x=>x.Slice.ToString()));
                    var copyButton=new Button{Width=28,Height=24,MinHeight=24,Padding=new Thickness(3),Margin=new Thickness(0),ToolTip="复制代码",Focusable=false,Background=Brushes.Transparent};
                    StitchAssets.SetIcon(copyButton,"content_copy",15);System.Windows.Automation.AutomationProperties.SetName(copyButton,"复制代码");
                    copyButton.Click+=(_,e)=>{copy?.Invoke(codeText);e.Handled=true;};
                    var floating=new Floater{HorizontalAlignment=HorizontalAlignment.Right,Width=30,Margin=new Thickness(0),Padding=new Thickness(0)};floating.Blocks.Add(new BlockUIContainer(copyButton){Margin=new Thickness(0)});heading.Inlines.InsertBefore(heading.Inlines.FirstInline,floating);frame.Blocks.Add(heading);
                    var codeSize=Math.Max(12,size*.85);
                    var p=new Paragraph{FontFamily=StitchAssets.Code,FontSize=codeSize,LineHeight=codeSize*1.5,Margin=new Thickness(0),Tag="code"};p.SetResourceReference(TextElement.ForegroundProperty,"ReaderCodeInkBrush");
                    var syntax=programming?ReadingSyntax.Colors(codeText,info):null;int offset=0;
                    for(int i=0;i<lines.Count;i++){var line=lines[i];var value=line.Slice.ToString();AddText(p.Inlines,value,line.Slice.Start,programming,"ReaderCodeInkBrush",syntax,offset);offset+=value.Length+1;if(i+1<lines.Count)p.Inlines.Add(new LineBreak());}
                    frame.Blocks.Add(p);target.Add(frame);
                }else if(block is LeafBlock leaf){var p=new Paragraph{Margin=new Thickness(0,0,0,14)};if(block is HeadingBlock h){p.Tag=h.Level;p.FontFamily=StitchAssets.Heading;p.FontSize=HeadingSize(size,h.Level);p.FontWeight=FontWeights.Bold;p.Margin=new Thickness(0,target.Count>0?12:0,0,6);}Inlines(leaf.Inline,p.Inlines);target.Add(p);
                }else if(block is ContainerBlock container)Blocks(container,target);
            }
        }
        Blocks(Markdown.Parse(text,Pipeline),doc.Blocks);runs.Sort((a,b)=>a.Start.CompareTo(b.Start));return doc;
    }
}
