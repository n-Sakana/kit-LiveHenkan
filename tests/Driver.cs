// Test-side helpers: tagged key input, window handling, reading text back and screen capture.
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Henkan.Tests {
 public static class Driver {
  [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")]static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")]static extern bool AttachThreadInput(uint a,uint b,bool attach);
  [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc proc,IntPtr p);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")]static extern short VkKeyScanEx(char c,IntPtr layout);
  [DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
  [DllImport("user32.dll")]static extern bool MoveWindow(IntPtr h,int x,int y,int w,int ht,bool repaint);
  [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW",CharSet=CharSet.Unicode)]static extern IntPtr SendText(IntPtr h,uint msg,IntPtr w,StringBuilder text,uint flags,uint timeout,out UIntPtr result);
  delegate bool EnumProc(IntPtr h,IntPtr p);

  public static void Dpi(){Native.SetProcessDpiAwarenessContext(new IntPtr(-4));}
  public static long[] Windows(int pid){
   List<long> list=new List<long>();
   EnumWindows(delegate(IntPtr h,IntPtr p){if(Native.ProcessOf(h)==pid&&IsWindowVisible(h))list.Add(h.ToInt64());return true;},IntPtr.Zero);
   return list.ToArray();
  }
  // The resident's word or settings window: a visible top-level window that is not one of its tool windows.
  public static long Panel(int pid){
   long found=0;
   EnumWindows(delegate(IntPtr h,IntPtr p){
    if(Native.ProcessOf(h)==pid&&IsWindowVisible(h)&&(Native.GetWindowLongPtr(h,-20).ToInt64()&0x80)==0&&Native.ClassOf(h).StartsWith("HwndWrapper",StringComparison.Ordinal)){found=h.ToInt64();return false;}
    return true;
   },IntPtr.Zero);
   return found;
  }
  public static string Title(long hwnd){StringBuilder b=new StringBuilder(256);Native.GetWindowText(new IntPtr(hwnd),b,256);return b.ToString();}
  public static void Click(int x,int y){
   EnsureTarget();
   SetCursorPos(x,y);Thread.Sleep(60);
   Native.SendMouseDown(0x2,0,Native.TestMark);Thread.Sleep(40);Native.SendMouseDown(0x4,0,Native.TestMark);
  }
  public static bool Activate(long hwnd){
   IntPtr h=new IntPtr(hwnd);uint me=Native.GetCurrentThreadId(),fg=Native.ThreadOf(Native.GetForegroundWindow()),dest=Native.ThreadOf(h);bool a=false,b=false;
   try{
    if(fg!=me)a=AttachThreadInput(me,fg,true);
    if(dest!=me&&dest!=fg)b=AttachThreadInput(me,dest,true);
    Native.ShowWindow(h,9);BringWindowToTop(h);SetForegroundWindow(h);
    return Native.GetForegroundWindow()==h;
   }finally{if(b)AttachThreadInput(me,dest,false);if(a)AttachThreadInput(me,fg,false);}
  }
  public static long Foreground(){return Native.GetForegroundWindow().ToInt64();}
  public static void Move(long hwnd,int x,int y,int w,int h){MoveWindow(new IntPtr(hwnd),x,y,w,h,true);}
  public static void Close(long hwnd){PostMessage(new IntPtr(hwnd),0x10,IntPtr.Zero,IntPtr.Zero);}
  public static void Park(int x,int y){SetCursorPos(x,y);}

  // Keys go through the real low-level hook; the tag lets the resident accept them in test mode.
  public static int KeysSent;   // key presses sent with the test tag; the resident must have seen as many
  public static long TargetWindow;
  static void EnsureTarget(){if(TargetWindow!=0&&Foreground()!=TargetWindow)throw new InvalidOperationException("The test window is no longer in front; input stopped.");}
  public static void Key(int vk){EnsureTarget();KeysSent++;Native.SendKey(vk,false,false,false,Native.TestMark);}
  // Modifiers go down and up as separate events with a pause, the way a hand does it. Sent as one batch, the release
  // of the modifier would be queued ahead of anything the resident sends in answer.
  public static void Key(int vk,bool shift,bool ctrl,bool alt){
   EnsureTarget();
   if(ctrl){Native.SendKeyDown(0xA2,Native.TestMark);Thread.Sleep(15);}
   if(alt){Native.SendKeyDown(0xA4,Native.TestMark);Thread.Sleep(15);}
   if(shift){Native.SendKeyDown(0xA0,Native.TestMark);Thread.Sleep(15);}
   KeysSent+=1+(ctrl?1:0)+(alt?1:0)+(shift?1:0);
   Native.SendKeyDown(vk,Native.TestMark);Thread.Sleep(12);Native.SendKeyUp(vk,Native.TestMark);Thread.Sleep(25);
   if(shift){Native.SendKeyUp(0xA0,Native.TestMark);Thread.Sleep(10);}
   if(alt){Native.SendKeyUp(0xA4,Native.TestMark);Thread.Sleep(10);}
   if(ctrl){Native.SendKeyUp(0xA2,Native.TestMark);Thread.Sleep(10);}
  }
  public static void OwnKey(int vk,bool shift,bool ctrl){EnsureTarget();Native.SendKey(vk,shift,ctrl,false,Native.OwnMark);}
  public static void Char(char c){
   if(c=='\n'){Key(13);return;}if(c=='\b'){Key(8);return;}if(c=='\u001b'){Key(27);return;}if(c==' '){Key(32);return;}
   short code=VkKeyScanEx(c,Native.GetKeyboardLayout(Native.ThreadOf(Native.GetForegroundWindow())));
   if(code==-1)throw new ArgumentException("No key for "+c);
   Key(code&0xFF,(code&0x100)!=0,false,false);
  }
  public static void Type(string text,int intervalMs){foreach(char c in text){Char(c);if(intervalMs>0)Thread.Sleep(intervalMs);}}

  public static AutomationElement FindClass(long hwnd,string cls){
   return AutomationElement.FromHandle(new IntPtr(hwnd)).FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.ClassNameProperty,cls));
  }
  public static AutomationElement FindId(long hwnd,string id){
   return AutomationElement.FromHandle(new IntPtr(hwnd)).FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,id));
  }
  public static string Read(AutomationElement e){
   object p;
   if(e.TryGetCurrentPattern(ValuePattern.Pattern,out p))return ((ValuePattern)p).Current.Value;
   if(e.TryGetCurrentPattern(TextPattern.Pattern,out p))return ((TextPattern)p).DocumentRange.GetText(-1);
   throw new InvalidOperationException("No value or text pattern");
  }
  public static string WindowText(long hwnd){
   IntPtr h=new IntPtr(hwnd);UIntPtr r;StringBuilder s=new StringBuilder(70000);
   if(SendText(h,13,new IntPtr(s.Capacity),s,2,800,out r)==IntPtr.Zero)throw new TimeoutException("WM_GETTEXT");
   return s.ToString();
  }
  public static int[] Rect(long hwnd){Native.Rect r;Native.GetWindowRect(new IntPtr(hwnd),out r);return new int[]{r.left,r.top,r.right,r.bottom};}
  public static void Capture(int left,int top,int width,int height,string path){
   using(Bitmap b=new Bitmap(width,height)){
    using(Graphics g=Graphics.FromImage(b))g.CopyFromScreen(left,top,0,0,b.Size);
    Directory.CreateDirectory(Path.GetDirectoryName(path));b.Save(path,ImageFormat.Png);
   }
  }
  // Enlarged crop, so small text can be judged by eye.
  public static void CaptureZoom(int left,int top,int width,int height,int factor,string path){
   using(Bitmap b=new Bitmap(width,height))using(Bitmap z=new Bitmap(width*factor,height*factor)){
    using(Graphics g=Graphics.FromImage(b))g.CopyFromScreen(left,top,0,0,b.Size);
    using(Graphics g=Graphics.FromImage(z)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;g.DrawImage(b,0,0,z.Width,z.Height);}
    Directory.CreateDirectory(Path.GetDirectoryName(path));z.Save(path,ImageFormat.Png);
   }
  }
  // ---- watching the screen while a test types ---------------------------
  // Frame after frame of one part of the screen, counting what must never be seen there: a selection highlight,
  // and text that is gone for an instant and back in the next frame.
  static Thread watcher; static volatile bool watching; static int[] watchCounts=new int[4];
  public static void StartWatch(int left,int top,int width,int height){
   StopWatch();
   watching=true;int[] counts=new int[4];watchCounts=counts;
   watcher=new Thread(delegate(){
    List<int> ink=new List<int>();byte[] px=new byte[width*height*4];
    using(Bitmap b=new Bitmap(width,height,PixelFormat.Format32bppArgb))using(Graphics g=Graphics.FromImage(b)){
     int ground=-1;
     while(watching){
      g.CopyFromScreen(left,top,0,0,b.Size);
      BitmapData d=b.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
      Marshal.Copy(d.Scan0,px,0,px.Length);b.UnlockBits(d);
      // The far end of the strip is bare page: that is the ground the text stands on.
      if(ground<0){int e=px.Length-4;ground=(px[e+2]*299+px[e+1]*587+px[e]*114)/1000;}
      int inked=0,blue=0;
      for(int i=0;i<px.Length;i+=4){
       int bl=px[i],gr=px[i+1],rd=px[i+2];
       if(bl-rd>48&&bl>110)blue++;
       else if(Math.Abs((rd*299+gr*587+bl*114)/1000-ground)>80)inked++;
      }
      ink.Add(inked);counts[0]++;
      if(blue*12>width*height)counts[1]++;
      if(blue>counts[3])counts[3]=blue;
     }
    }
    for(int i=1;i+1<ink.Count;i++){int around=Math.Min(ink[i-1],ink[i+1]);if(around>40&&ink[i]*2<around)counts[2]++;}
   });
   watcher.IsBackground=true;watcher.Start();
  }
  // frames seen, frames with a highlight, frames in which the text was gone for an instant, most highlighted pixels in one frame
  public static int[] StopWatch(){
   watching=false;if(watcher!=null){watcher.Join(3000);watcher=null;}
   return watchCounts;
  }
  public static string Style(){
   IntPtr window;StyleInfo st=Probe.FindStyle(out window);
   return st==null?"none":st.FontName+" "+st.SizePoints+"pt ink="+st.Ink.ToString("X6")+" back="+st.Back.ToString("X6")+(st.Bold?" bold":"");
  }
  public static string Caret(){
   CaretInfo c=Probe.Find(Native.FocusWindow());
   return c.Found?c.Source+":"+c.Left+","+c.Top+","+c.Height:"none";
  }
 }
}
