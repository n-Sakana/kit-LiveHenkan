// Win32 declarations and small helpers shared by the host, the overlay and the tests.
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LiveHenkan {
 public struct CaretInfo {
  public bool Found; public int Left,Top,Height; public IntPtr Window; public string Source;
  public bool Same(CaretInfo o){return Found==o.Found&&Left==o.Left&&Top==o.Top&&Height==o.Height;}
 }

 // What the application says about the text at the caret. Colours are 0xRRGGBB, -1 when unknown.
 public sealed class StyleInfo {
  public string FontName; public double SizePoints; public int Ink=-1,Back=-1; public bool Bold;
 }

 public static class Native {
  // Tags on injected input: our own output is never read back, test input is read only in test mode.
  public const long OwnMark=0x4C48454E,TestMark=0x4C485454;
  public delegate IntPtr HookProc(int code,IntPtr wParam,IntPtr lParam);
  public delegate void WinEventProc(IntPtr hook,uint evt,IntPtr hwnd,int idObject,int idChild,uint thread,uint time);
  [StructLayout(LayoutKind.Sequential)] public struct KeyData {public uint vk,scan,flags,time;public UIntPtr extra;}
  [StructLayout(LayoutKind.Sequential)] public struct MouseData {public int x,y;public uint mouseData,flags,time;public UIntPtr extra;}
  [StructLayout(LayoutKind.Sequential)] public struct KeyInput {public ushort vk,scan;public uint flags,time;public UIntPtr extra;}
  [StructLayout(LayoutKind.Sequential)] public struct MouseInput {public int dx,dy;public uint mouseData,flags,time;public UIntPtr extra;}
  [StructLayout(LayoutKind.Explicit,Size=32)] public struct InputUnion {[FieldOffset(0)]public KeyInput key;[FieldOffset(0)]public MouseInput mouse;}
  [StructLayout(LayoutKind.Sequential)] public struct Input {public uint type;public InputUnion data;}
  [StructLayout(LayoutKind.Sequential)] public struct Rect {public int left,top,right,bottom;}
  [StructLayout(LayoutKind.Sequential)] public struct Point {public int x,y;}
  [StructLayout(LayoutKind.Sequential)] public struct GuiInfo {public int size,flags;public IntPtr active,focus,capture,menu,move,caret;public Rect rect;}
  [StructLayout(LayoutKind.Sequential)] public struct LastInput {public uint size,time;}
  [StructLayout(LayoutKind.Sequential)] struct Throttle {public uint Version,ControlMask,StateMask;}

  [DllImport("user32.dll",SetLastError=true)]public static extern IntPtr SetWindowsHookEx(int id,HookProc proc,IntPtr module,uint thread);
  [DllImport("user32.dll")]public static extern bool UnhookWindowsHookEx(IntPtr hook);
  [DllImport("user32.dll")]public static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wParam,IntPtr lParam);
  [DllImport("user32.dll")]public static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEventProc proc,uint pid,uint thread,uint flags);
  [DllImport("user32.dll")]public static extern bool UnhookWinEvent(IntPtr hook);
  [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [DllImport("user32.dll")]public static extern bool GetGUIThreadInfo(uint thread,ref GuiInfo info);
  [DllImport("user32.dll")]public static extern short GetAsyncKeyState(int vk);
  [DllImport("user32.dll",SetLastError=true)]public static extern uint SendInput(uint count,Input[] inputs,int size);
  [DllImport("user32.dll")]public static extern bool PostThreadMessage(uint thread,uint msg,IntPtr wParam,IntPtr lParam);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetClassName(IntPtr hwnd,StringBuilder text,int max);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int max);
  [DllImport("user32.dll")]public static extern bool ClientToScreen(IntPtr hwnd,ref Point p);
  [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr hwnd,out Rect r);
  [DllImport("user32.dll")]public static extern bool IsWindow(IntPtr hwnd);
  [DllImport("user32.dll")]public static extern IntPtr GetKeyboardLayout(uint thread);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int ToUnicodeEx(uint vk,uint scan,byte[] state,StringBuilder buffer,int size,uint flags,IntPtr layout);
  [DllImport("user32.dll")]public static extern bool GetLastInputInfo(ref LastInput info);
  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]public static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]public static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
  [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
  [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr hwnd,int command);
  [DllImport("user32.dll")]public static extern uint GetDpiForWindow(IntPtr hwnd);
  [DllImport("user32.dll")]public static extern IntPtr MonitorFromPoint(Point p,uint flags);
  [DllImport("shcore.dll")]public static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);
  [DllImport("user32.dll")]public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")]public static extern IntPtr GetDC(IntPtr hwnd);
  [DllImport("user32.dll")]public static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
  [DllImport("gdi32.dll")]public static extern uint GetPixel(IntPtr dc,int x,int y);
  [DllImport("user32.dll")]public static extern bool DestroyIcon(IntPtr icon);
  [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW")]public static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint msg,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out UIntPtr result);
  [DllImport("imm32.dll")]public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hwnd);
  [DllImport("kernel32.dll")]public static extern uint GetCurrentThreadId();
  [DllImport("kernel32.dll")]public static extern IntPtr GetCurrentProcess();
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr GetModuleHandle(string name);
  [DllImport("kernel32.dll")]static extern bool SetPriorityClass(IntPtr process,uint cls);
  [DllImport("kernel32.dll")]static extern bool SetProcessInformation(IntPtr process,int cls,ref Throttle info,int size);
  [DllImport("oleacc.dll")]public static extern int AccessibleObjectFromWindow(IntPtr hwnd,uint objectId,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object result);

  // A hidden helper started from Explorer can be scheduled as background work. Typing must not wait for that.
  public static void PrepareProcess(){
   try{SetPriorityClass(GetCurrentProcess(),0x20);}catch{}
   try{Throttle t=new Throttle{Version=1,ControlMask=1,StateMask=0};SetProcessInformation(GetCurrentProcess(),4,ref t,12);}catch{}
   try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch{}
  }
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetProcessWorkingSetSizeEx(IntPtr process,IntPtr min,IntPtr max,uint flags);
  // Keeps the memory in use now from being paged out. When Windows is short of memory it empties the working set of
  // a process that looks idle; the next keystroke then waits on the disk, the keyboard hook misses its time limit and the
  // key reaches the application unconverted. Returns 0, or the Win32 error.
  public static int HoldWorkingSet(long extraBytes){
   long now=Environment.WorkingSet,min=now+extraBytes;
   return SetProcessWorkingSetSizeEx(GetCurrentProcess(),new IntPtr(min),new IntPtr(min+512L*1024*1024),0x1|0x8)?0:Marshal.GetLastWin32Error();
  }
  public static bool Down(int vk){return (GetAsyncKeyState(vk)&0x8000)!=0;}
  public static string ClassOf(IntPtr hwnd){StringBuilder s=new StringBuilder(256);GetClassName(hwnd,s,256);return s.ToString();}
  public static uint ThreadOf(IntPtr hwnd){uint pid;return GetWindowThreadProcessId(hwnd,out pid);}
  public static uint ProcessOf(IntPtr hwnd){uint pid;GetWindowThreadProcessId(hwnd,out pid);return pid;}
  public static IntPtr FocusWindow(){
   IntPtr fg=GetForegroundWindow();GuiInfo g=new GuiInfo();g.size=Marshal.SizeOf(typeof(GuiInfo));
   if(fg!=IntPtr.Zero&&GetGUIThreadInfo(ThreadOf(fg),ref g)&&g.focus!=IntPtr.Zero)return g.focus;
   return fg;
  }

  static Input KeyOf(int vk,int scan,uint flags,long tag){return new Input{type=1,data=new InputUnion{key=new KeyInput{vk=(ushort)vk,scan=(ushort)scan,flags=flags,extra=new UIntPtr((ulong)tag)}}};}
  static void Send(List<Input> list){
   if(list.Count==0)return;
   Input[] a=list.ToArray();uint n=SendInput((uint)a.Length,a,Marshal.SizeOf(typeof(Input)));
   if(n!=a.Length)throw new InvalidOperationException("SendInput "+n+"/"+a.Length+" Win32="+Marshal.GetLastWin32Error());
  }
  // The whole string goes into the input queue in one call, so nothing typed can land in the middle of it.
  // An open system IME takes typed kana as kana-key input and rewrites it in its own mode (katakana arrived as
  // hiragana in Word). The IME-off key in front of the text closes it first, in the same queue.
  public static void SendText(string text,long tag){
   List<Input> a=new List<Input>(text.Length*2+6);
   // Text that arrives while Ctrl is held is taken for shortcuts. Ctrl is let go for the text and pressed again after it.
   bool left=Down(0xA2),right=Down(0xA3);
   if(left)a.Add(KeyOf(0xA2,0,2,tag));if(right)a.Add(KeyOf(0xA3,0,2|1,tag));
   a.Add(KeyOf(0x1A,0,0,tag));a.Add(KeyOf(0x1A,0,2,tag));
   foreach(char c in text){a.Add(KeyOf(0,c,4,tag));a.Add(KeyOf(0,c,6,tag));}
   if(left)a.Add(KeyOf(0xA2,0,0,tag));if(right)a.Add(KeyOf(0xA3,0,1,tag));
   Send(a);
  }
  public static void SendKeyDown(int vk,long tag){Send(new List<Input>{KeyOf(vk,0,(vk==0xA5||vk==0x5B||vk==0x5C)?1u:0u,tag)});}
  public static void SendKeyUp(int vk,long tag){Send(new List<Input>{KeyOf(vk,0,((vk==0xA5||vk==0x5B||vk==0x5C)?1u:0u)|2u,tag)});}
  public static void SendKey(int vk,bool shift,bool ctrl,bool alt,long tag){
   List<Input> a=new List<Input>();
   bool addShift=shift&&!Down(16),addCtrl=ctrl&&!Down(17),addAlt=alt&&!Down(18);
   if(addCtrl)a.Add(KeyOf(17,0,0,tag));if(addAlt)a.Add(KeyOf(18,0,0,tag));if(addShift)a.Add(KeyOf(16,0,0,tag));
   a.Add(KeyOf(vk,0,0,tag));a.Add(KeyOf(vk,0,2,tag));
   if(addShift)a.Add(KeyOf(16,0,2,tag));if(addAlt)a.Add(KeyOf(18,0,2,tag));if(addCtrl)a.Add(KeyOf(17,0,2,tag));
   Send(a);
  }
  public static void SendMouseDown(uint flag,uint data,long tag){
   Send(new List<Input>{new Input{type=0,data=new InputUnion{mouse=new MouseInput{flags=flag,mouseData=data,extra=new UIntPtr((ulong)tag)}}}});
  }

  // What the key types on the layout of the foreground window; '\0' for keys that type nothing.
  public static char Translate(int vk,int scan,bool shift){
   if(vk>=0x41&&vk<=0x5A)return (char)(shift?vk:vk+32);
   if(vk<0x30&&vk!=0x20)return '\0';
   if(vk>=0x70&&vk<=0x87)return '\0';
   byte[] state=new byte[256];if(shift)state[16]=0x80;
   StringBuilder b=new StringBuilder(8);
   int n=ToUnicodeEx((uint)vk,(uint)scan,state,b,8,4,GetKeyboardLayout(ThreadOf(GetForegroundWindow())));
   if(n==1&&b[0]>0x20&&b[0]<0x7F)return b[0];
   return '\0';
  }

  // The system caret, for applications that keep one. Coordinates are screen pixels.
  public static bool SystemCaret(out CaretInfo caret,out IntPtr focus){
   caret=new CaretInfo();focus=IntPtr.Zero;
   IntPtr fg=GetForegroundWindow();if(fg==IntPtr.Zero)return false;
   GuiInfo g=new GuiInfo();g.size=Marshal.SizeOf(typeof(GuiInfo));
   if(!GetGUIThreadInfo(ThreadOf(fg),ref g)){focus=fg;return false;}
   focus=g.focus!=IntPtr.Zero?g.focus:fg;
   if(g.caret==IntPtr.Zero||g.rect.bottom<=g.rect.top)return false;
   Point p=new Point{x=g.rect.left,y=g.rect.top};
   if(!ClientToScreen(g.caret,ref p))return false;
   caret=new CaretInfo{Found=true,Left=p.x,Top=p.y,Height=g.rect.bottom-g.rect.top,Window=g.caret,Source="system"};
   return true;
  }
  static Guid accessibleId=new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
  // The caret that browsers and Office publish for magnifiers and input methods.
  public static bool AccessibleCaret(IntPtr focus,out CaretInfo caret){
   caret=new CaretInfo();
   if(focus==IntPtr.Zero)return false;
   object o=null;
   try{
    if(AccessibleObjectFromWindow(focus,0xFFFFFFF8,ref accessibleId,out o)!=0||o==null)return false;
    Accessibility.IAccessible acc=(Accessibility.IAccessible)o;int l,t,w,h;
    acc.accLocation(out l,out t,out w,out h,0);
    if(h<=0||(l==0&&t==0&&w==0))return false;
    caret=new CaretInfo{Found=true,Left=l,Top=t,Height=h,Window=focus,Source="accessible"};
    return true;
   }catch{return false;}
   finally{if(o!=null&&Marshal.IsComObject(o))Marshal.ReleaseComObject(o);}
  }
  public static double ScaleAt(int x,int y){
   try{uint dx,dy;if(GetDpiForMonitor(MonitorFromPoint(new Point{x=x,y=y},2),0,out dx,out dy)==0&&dx>0)return dx/96.0;}catch{}
   return 1.0;
  }
  public static uint ScreenPixel(int x,int y){
   IntPtr dc=GetDC(IntPtr.Zero);
   try{return GetPixel(dc,x,y);}finally{ReleaseDC(IntPtr.Zero,dc);}
  }
  // Closes the system IME on the focused window so that alphanumeric mode really is direct input.
  public static void CloseSystemIme(IntPtr focus){
   try{
    IntPtr ime=ImmGetDefaultIMEWnd(focus);UIntPtr r;
    if(ime!=IntPtr.Zero)SendMessageTimeout(ime,0x283,new IntPtr(6),IntPtr.Zero,2,120,out r);
   }catch{}
  }
 }
}
