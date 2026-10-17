// Compare ink in two captures of the SAME text/location. Exclude the blue
// composition underline, retaining grayscale coverage rather than thresholding strokes.
using System;
using System.Drawing;
namespace Henkan.Tests {
 public sealed class FontInk {
  public double Coverage,CenterX,CenterY;
  public int Left=int.MaxValue,Top=int.MaxValue,Right=-1,Bottom=-1;
 }
 public static class FontPixels {
  public static FontInk Read(string path,int left,int top,int width,int height,bool dark){
   FontInk result=new FontInk();double sx=0,sy=0;
   using(Bitmap bitmap=new Bitmap(path)){
    double back=dark?41:255,ink=dark?245:36;
    for(int y=Math.Max(0,top);y<Math.Min(bitmap.Height,top+height);y++)for(int x=Math.Max(0,left);x<Math.Min(bitmap.Width,left+width);x++){
     Color c=bitmap.GetPixel(x,y);
     // ClearType edges are coloured too; reject only our known underline colours.
     if((Math.Abs(c.R-0)<8&&Math.Abs(c.G-118)<8&&Math.Abs(c.B-236)<8)||
        (Math.Abs(c.R-61)<8&&Math.Abs(c.G-154)<8&&Math.Abs(c.B-255)<8))continue;
     double coverage=Math.Max(0,Math.Min(1,((c.R+c.G+c.B)/3.0-back)/(ink-back)));
     if(coverage<.1)continue;
     result.Coverage+=coverage;sx+=coverage*x;sy+=coverage*y;
     if(coverage>.35){result.Left=Math.Min(result.Left,x);result.Top=Math.Min(result.Top,y);result.Right=Math.Max(result.Right,x);result.Bottom=Math.Max(result.Bottom,y);}
    }
   }
   if(result.Coverage>0){result.CenterX=sx/result.Coverage;result.CenterY=sy/result.Coverage;}
   return result;
  }
 }
}
