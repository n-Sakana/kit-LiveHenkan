// WPF's text formatter supplies word/Japanese line breaks and character positions.
// Only the pending web composition is drawn; the application's document is untouched.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace Henkan {
 sealed class CompositionRun : TextRunProperties {
  readonly Typeface face;readonly double size;readonly Brush ink;
  public CompositionRun(Typeface f,double s,Brush b){face=f;size=s;ink=b;}
  public override Typeface Typeface{get{return face;}}
  public override double FontRenderingEmSize{get{return size;}}
  public override double FontHintingEmSize{get{return size;}}
  public override Brush ForegroundBrush{get{return ink;}}
  public override Brush BackgroundBrush{get{return null;}}
  public override CultureInfo CultureInfo{get{return CultureInfo.GetCultureInfo("ja-JP");}}
  public override TextDecorationCollection TextDecorations{get{return null;}}
  public override TextEffectCollection TextEffects{get{return null;}}
 }
 sealed class CompositionSource : TextSource {
  readonly string text;readonly TextRunProperties run;
  public CompositionSource(string t,TextRunProperties r){text=t;run=r;}
  public override TextRun GetTextRun(int at){return at<text.Length?(TextRun)new TextCharacters(text,at,text.Length-at,run):new TextEndOfParagraph(1);}
  public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int at){return new TextSpan<CultureSpecificCharacterBufferRange>(at,new CultureSpecificCharacterBufferRange(run.CultureInfo,new CharacterBufferRange(text,0,at)));}
  public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int at){return at;}
 }
 sealed class CompositionParagraph : TextParagraphProperties {
  readonly TextRunProperties run;readonly double height;
  public CompositionParagraph(TextRunProperties r,double h){run=r;height=h;}
  public override FlowDirection FlowDirection{get{return FlowDirection.LeftToRight;}}
  public override TextAlignment TextAlignment{get{return TextAlignment.Left;}}
  public override double LineHeight{get{return height;}}
  public override bool FirstLineInParagraph{get{return false;}}
  public override TextRunProperties DefaultTextRunProperties{get{return run;}}
  public override TextWrapping TextWrapping{get{return TextWrapping.Wrap;}}
  public override TextMarkerProperties TextMarkerProperties{get{return null;}}
  public override double Indent{get{return 0;}}
 }
 sealed class WrappedComposition : FrameworkElement,IDisposable {
  sealed class Row {public TextLine Line;public int Start;public double X,Y;}
  readonly List<Row> rows=new List<Row>();
  TextFormatter formatter;
  Brush back,ink,accent;double scroll;int focusStart,focusLength,textLength;
  public int Lines{get{return rows.Count;}}
  public double Scroll{get{return scroll;}}
  public void Clear(){foreach(Row row in rows)row.Line.Dispose();rows.Clear();}
  public void Dispose(){Clear();if(formatter!=null)formatter.Dispose();}
  public void Set(string value,Typeface face,double size,double lineHeight,double width,double firstOffset,double height,Brush foreground,Brush background,Brush underline,int start,int length,double glyphLineHeight=0){
   Clear();back=background;ink=foreground;accent=underline;focusStart=start;focusLength=length;textLength=value.Length;
   if(formatter==null)formatter=TextFormatter.Create(TextFormattingMode.Ideal);
   Width=width;Height=Math.Max(1,height);Clip=new RectangleGeometry(new Rect(0,0,Width,Height));
   CompositionRun run=new CompositionRun(face,size,ink);CompositionSource source=new CompositionSource(value,run);
   // The caret is already below the browser's paragraph leading. Keep the
   // single-line glyph baseline; CSS line spacing only advances following rows.
   CompositionParagraph paragraph=new CompositionParagraph(run,glyphLineHeight>0?glyphLineHeight:lineHeight);
   double y=0;int at=0;
   {
    TextLineBreak previous=null;
    try{
     while(at<value.Length){
      double x=rows.Count==0?firstOffset:0;
      // If there is no room for a glyph at the caret, start on the next line.
      if(rows.Count==0&&width-x<size){y+=lineHeight;x=0;}
      TextLine line=formatter.FormatLine(source,at,Math.Max(1,width-x),paragraph,previous);
      if(previous!=null)previous.Dispose();previous=line.GetTextLineBreak();
      rows.Add(new Row{Line=line,Start=at,X=x,Y=y});
      at+=line.Length;y+=Math.Max(lineHeight,line.Height);
     }
    }finally{if(previous!=null)previous.Dispose();}
   }
   double target=y;
   if(start>=0){foreach(Row row in rows)if(start>=row.Start&&start<row.Start+row.Line.Length){target=row.Y+Math.Max(lineHeight,row.Line.Height);break;}}
   scroll=Math.Max(0,target-Height);InvalidateVisual();
  }
  public Point Position(int index){
   if(rows.Count==0)return new Point();
   Row found=rows[rows.Count-1];foreach(Row row in rows)if(index<row.Start+row.Line.Length){found=row;break;}
   int at=Math.Max(found.Start,Math.Min(index,Math.Min(textLength,found.Start+found.Line.Length)));
   return new Point(found.X+found.Line.GetDistanceFromCharacterHit(new CharacterHit(at,0)),found.Y-scroll);
  }
  protected override void OnRender(DrawingContext dc){
   base.OnRender(dc);
   // A scrolled composition owns this viewport, so old letters cannot show
   // through the new lines. Before scrolling, leave the committed prefix visible.
   if(scroll>0)dc.DrawRectangle(back,null,new Rect(0,0,Width,Height));
   foreach(Row row in rows){
    double y=row.Y-scroll;if(y+row.Line.Height<0||y>=Height)continue;
    double width=Math.Min(Width-row.X,row.Line.WidthIncludingTrailingWhitespace);
    dc.DrawRectangle(back,null,new Rect(row.X,y,Math.Max(1,width+1),row.Line.Height+2));
    row.Line.Draw(dc,new Point(row.X,y),InvertAxes.None);
    double bottom=y+row.Line.Height;
    dc.DrawLine(new Pen(accent,1),new Point(row.X,bottom),new Point(row.X+width,bottom));
    int from=Math.Max(focusStart,row.Start),to=Math.Min(focusStart+focusLength,row.Start+row.Line.Length);
    if(focusStart>=0&&to>from)foreach(TextBounds bounds in row.Line.GetTextBounds(from,to-from)){
     Rect r=bounds.Rectangle;r.Offset(row.X,y);dc.DrawLine(new Pen(accent,2),new Point(r.Left,r.Bottom),new Point(r.Right,r.Bottom));
    }
   }
   if(focusStart<0&&rows.Count>0){Point p=Position(textLength);dc.DrawLine(new Pen(ink,1),p,new Point(p.X,Math.Min(Height,p.Y+rows[rows.Count-1].Line.Height)));}
  }
 }
}
