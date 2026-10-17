using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
namespace Henkan.Tests {
 public static class WrappedChecks {
  public static CheckResult[] Run(){
   List<CheckResult> checks=new List<CheckResult>();
   Action<string,bool,string> check=delegate(string name,bool ok,string detail){checks.Add(new CheckResult{Name=name,Ok=ok,Detail=detail});};
   WebTextInfo web=WebText.Measure(new Rect(164,397,382,233),new CaretInfo{Left=265,Top=475,Height=23},new[]{new Rect(180,414,278,28),new Rect(458,628,1,2),new Rect(180,444,138,28),new Rect(318,628,1,2),new Rect(180,474,85,28)},1.25);
   check("BR markers do not enlarge line spacing",web.LinePixels==30,web.LinePixels.ToString());
   check("text area excludes chat padding",web.Bounds.Left==180&&web.Bounds.Width==350,web.Bounds.ToString());
   check("bottom padding remains outside composition",web.Bounds.Bottom==614,web.Bounds.ToString());
   check("Latin letters cannot estimate Japanese em size",!DocumentFonts.FullEmJapanese('A'),"");
   WrappedComposition view=new WrappedComposition();Typeface face=new Typeface("Noto Sans JP");
   string value="これはチームスでのテストね。日本語の長い文章でも欄の中で折り返します。";
   view.Set(value,face,14,20,280,140,140,Brushes.Black,Brushes.White,Brushes.Blue,-1,0);
   check("long Japanese text wraps",view.Lines>=3,view.Lines.ToString());
   check("first line begins after committed prefix",Math.Abs(view.Position(0).X-140)<.1,view.Position(0).ToString());
   check("text fits without scrolling",view.Scroll==0,view.Scroll.ToString());
   for(int i=0;i<=value.Length;i++){Point p=view.Position(i);check("character position in field "+i,p.X>=0&&p.X<=280&&p.Y>=0&&p.Y<140,p.ToString());}
   view.Set(new string('あ',300),face,16,24,280,80,72,Brushes.Black,Brushes.White,Brushes.Blue,-1,0);
   check("overflow scrolls within field",view.Scroll>0&&view.Height==72,view.Scroll.ToString());
   Point end=view.Position(300);check("end of long text remains visible",end.Y>=0&&end.Y<72,end.ToString());
   view.Set(new string('あ',300),face,16,24,280,80,72,Brushes.Black,Brushes.White,Brushes.Blue,0,8);
   check("candidate selection brings first clause into view",view.Scroll==0,view.Scroll.ToString());
   view.Set("日本語 😀 ABCDEF 日本語\n次の行",face,16,24,120,115,120,Brushes.Black,Brushes.White,Brushes.Blue,-1,0);
   check("no glyph is forced into a too-small first line",view.Position(0).X==0&&view.Lines>1,view.Position(0).ToString());
   view.Measure(new Size(120,120));view.Arrange(new Rect(0,0,120,120));
   System.Windows.Media.Imaging.RenderTargetBitmap bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(120,120,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
   check("multiline drawing works without a window",bitmap.PixelWidth==120,"");view.Dispose();
   return checks.ToArray();
  }
 }
}
