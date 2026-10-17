// Read-only geometry of the focused Chromium editor. Queried on the probe
// thread once per composition (and after a commit), never on the key hook.
using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Henkan {
 public sealed class WebTextInfo {
  public Rect Bounds;
  public double LinePixels,FontPixels;
 }
 static class WebText {
  public static WebTextInfo Measure(Rect bounds,CaretInfo caret,Rect[] rects,double scale){
   double left=caret.Left,line=0,lastTop=double.NaN;
   foreach(Rect r in rects){
    // Chromium publishes <br> markers as tiny rectangles at the bottom of
    // the editor. They are not text lines and must not determine line spacing.
    if(r.IsEmpty||r.Height<Math.Max(4,caret.Height*.5)||r.Left<bounds.Left||r.Right>bounds.Right+2)continue;
    left=Math.Min(left,r.Left);
    if(!double.IsNaN(lastTop)&&r.Top>lastTop+2){double distance=r.Top-lastTop;if(distance>=caret.Height*.8&&(line==0||distance<line))line=distance;}
    lastTop=r.Top;
   }
   double inset=Math.Max(0,Math.Min(left-bounds.Left,24*scale)),right=bounds.Right-inset;
   return right<=left+8?null:new WebTextInfo{Bounds=new Rect(left,bounds.Top,right-left,Math.Max(1,bounds.Height-inset)),LinePixels=line};
  }
  public static void Read(AutomationElement element,TextPatternRange selection,CaretInfo caret,StyleInfo style){
   if(element==null||selection==null||style==null||!style.Web||!caret.Found)return;
   try{
    Rect bounds=element.Current.BoundingRectangle;
    if(bounds.IsEmpty||!CaretGeometry.InBounds(caret,bounds))return;
    object pattern;if(!element.TryGetCurrentPattern(TextPattern.Pattern,out pattern))return;
    TextPatternRange document=((TextPattern)pattern).DocumentRange;
    Rect[] rects=document.GetBoundingRectangles();
    // UIA exposes the editor border box, not CSS padding. The text's left
    // inset also bounds the right inset in the usual symmetric chat editors.
    WebTextInfo info=Measure(bounds,caret,rects,Native.ScaleAt(caret.Left,caret.Top));if(info==null)return;
    // UIA rounds CSS font sizes to half points and can omit the visual zoom.
    // A run of full-em Japanese glyphs gives the actual rendered size. Do not use
    // caret height: it can include leading, or even an entire empty editor.
    if(!DocumentFonts.HasJapanese(style.FontName)){
     TextPatternRange before=selection.Clone();before.MoveEndpointByRange(TextPatternRangeEndpoint.End,before,TextPatternRangeEndpoint.Start);
     before.MoveEndpointByUnit(TextPatternRangeEndpoint.Start,TextUnit.Character,-16);
     string value=before.GetText(32);int end=value.Length;
     while(end>0){
      while(end>0&&!DocumentFonts.FullEmJapanese(value[end-1]))end--;
      int start=end;while(start>0&&DocumentFonts.FullEmJapanese(value[start-1]))start--;
      if(end-start>=2){
       TextPatternRange sample=before.Clone();sample.MoveEndpointByUnit(TextPatternRangeEndpoint.End,TextUnit.Character,end-value.Length);sample.MoveEndpointByUnit(TextPatternRangeEndpoint.Start,TextUnit.Character,start);
       Rect[] glyphs=sample.GetBoundingRectangles();
       object name=sample.GetAttributeValue(TextPattern.FontNameAttribute),points=sample.GetAttributeValue(TextPattern.FontSizeAttribute);
       if(name is string&&(string)name==style.FontName&&points is double&&Math.Abs((double)points-style.SizePoints)<.01&&sample.GetText(32)==value.Substring(start,end-start)&&glyphs.Length==1&&glyphs[0].Width>0){
        double scale=Native.ScaleAt(caret.Left,caret.Top),pixels=glyphs[0].Width/(end-start),expected=style.SizePoints*96/72*scale;
        // Integer UIA rectangles have rounding noise. Preserve the already
        // published size unless the observed visual scale differs materially.
        if(pixels>=expected*.5&&pixels<=expected*2&&Math.Abs(pixels-expected)>.5*scale)info.FontPixels=pixels;
       }
       break;
      }
      end=start;
     }
    }
    style.WebText=info;
   }catch{} // Geometry is optional; retain the ordinary document style.
  }
 }
}
