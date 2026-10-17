// Uncommitted text written straight into the document, for editors that can take it without side effects.
// Today that is the RichEdit control (Windows 11 Notepad and RichEdit 4.1 windows) through its Text Object Model.
using System;
using System.Text;
using System.Threading;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Henkan {
 public struct LineBox { public int Left,Right,Top,Bottom; }
 // Where the uncommitted text and the stretch in focus sit on the screen, one box per line.
 public sealed class InlineGeometry {
  public List<LineBox> Text=new List<LineBox>(),Focus=new List<LineBox>(); public int LineHeight;
  public bool Ok{get{return Text.Count>0;}}
  public bool Same(InlineGeometry o){
   if(o==null||o.Text.Count!=Text.Count||o.Focus.Count!=Focus.Count||o.LineHeight!=LineHeight)return false;
   for(int i=0;i<Text.Count;i++)if(!Equal(Text[i],o.Text[i]))return false;
   for(int i=0;i<Focus.Count;i++)if(!Equal(Focus[i],o.Focus[i]))return false;
   return true;
  }
  static bool Equal(LineBox a,LineBox b){return a.Left==b.Left&&a.Right==b.Right&&a.Top==b.Top&&a.Bottom==b.Bottom;}
 }
 public sealed class InlineJob {
  public int Session; public long Serial; public bool Begin,Active; public IntPtr Window;
  public int DelayMs;       // for Begin and Direct: how long to let input that is on its way reach the application first
  public bool Direct;       // Full is finished text to put in at the caret, as typing it would
  public string Full="";    // everything this composition has put into the document so far, committed part first
  public int CommitLen;     // how much of Full is committed
  public View View;
 }

 [ComImport,Guid("00020400-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IDispatchRaw {
  [PreserveSig] int GetTypeInfoCount(out int count);
  [PreserveSig] int GetTypeInfo(int index,int lcid,out IntPtr info);
  [PreserveSig] int GetIDsOfNames(ref Guid iid,[MarshalAs(UnmanagedType.LPArray,ArraySubType=UnmanagedType.LPWStr)] string[] names,int count,int lcid,[Out,MarshalAs(UnmanagedType.LPArray)] int[] ids);
  [PreserveSig] int Invoke(int id,ref Guid iid,int lcid,short flags,ref System.Runtime.InteropServices.ComTypes.DISPPARAMS p,IntPtr result,IntPtr excep,IntPtr argErr);
 }
 // One object that lives in the other process, called through IDispatch with the member ids remembered.
 // Late binding through reflection asks for the id again on every call, and each question is one more trip into
 // the other process. Remembering the ids avoids these repeated calls.
 sealed class Remote : IDisposable {
  [DllImport("oleaut32.dll")]static extern int VariantClear(IntPtr v);
  static readonly Dictionary<string,int> ids=new Dictionary<string,int>();
  static readonly int Size=IntPtr.Size==8?24:16,ExcepSize=IntPtr.Size==8?64:32;
  static Guid none=Guid.Empty;
  const short Method=1,PropertyGet=2,PropertyPut=4;
  object target; readonly IDispatchRaw dispatch; readonly string kind;
  public Remote(object o,string kindOfObject){target=o;dispatch=(IDispatchRaw)o;kind=kindOfObject;}
  public void Dispose(){object o=target;target=null;if(o!=null&&Marshal.IsComObject(o))Marshal.ReleaseComObject(o);}
  int Id(string name){
   string key=kind+"|"+name;int id;
   lock(ids)if(ids.TryGetValue(key,out id))return id;
   int[] found=new int[1];
   Marshal.ThrowExceptionForHR(dispatch.GetIDsOfNames(ref none,new string[]{name},1,0,found));
   lock(ids)ids[key]=found[0];
   return found[0];
  }
  static IntPtr At(IntPtr p,int offset){return new IntPtr(p.ToInt64()+offset);}
  // `outs` are whole numbers the member hands back through its last arguments.
  object Invoke(string name,short flags,int[] outs,object[] args){
   int given=args==null?0:args.Length,extra=outs==null?0:outs.Length,n=given+extra;
   int bytes=Size*(n+1)+ExcepSize+8+4*extra;
   IntPtr memory=Marshal.AllocCoTaskMem(bytes);
   try{
    for(int i=0;i<bytes;i+=4)Marshal.WriteInt32(memory,i,0);
    IntPtr result=memory,first=At(memory,Size),excep=At(memory,Size*(n+1)),named=At(excep,ExcepSize),numbers=At(named,8);
    // Arguments are passed last first.
    for(int i=0;i<given;i++)Marshal.GetNativeVariantForObject(args[i],At(first,Size*(n-1-i)));
    for(int i=0;i<extra;i++){IntPtr v=At(first,Size*(extra-1-i));Marshal.WriteInt16(v,0,0x4003);Marshal.WriteIntPtr(v,8,At(numbers,4*i));}
    System.Runtime.InteropServices.ComTypes.DISPPARAMS p=new System.Runtime.InteropServices.ComTypes.DISPPARAMS();
    p.cArgs=n;p.rgvarg=n>0?first:IntPtr.Zero;
    if(flags==PropertyPut){Marshal.WriteInt32(named,-3);p.cNamedArgs=1;p.rgdispidNamedArgs=named;}
    int hr=dispatch.Invoke(Id(name),ref none,0,flags,ref p,result,excep,IntPtr.Zero);
    object value=null;
    try{
     if(hr>=0)value=Marshal.GetObjectForNativeVariant(result);
     for(int i=0;i<extra;i++)outs[i]=Marshal.ReadInt32(numbers,4*i);
    }finally{
     VariantClear(result);
     for(int i=0;i<given;i++)VariantClear(At(first,Size*(n-1-i)));
    }
    if(hr==unchecked((int)0x80020009)){
     // The member itself failed; its own error code says why.
     int code=Marshal.ReadInt32(excep,ExcepSize-8+(IntPtr.Size==8?0:4));
     for(int i=1;i<=3;i++){IntPtr text=Marshal.ReadIntPtr(excep,IntPtr.Size*i);if(text!=IntPtr.Zero)Marshal.FreeBSTR(text);}
     if(code<0)hr=code;
    }
    if(hr<0){
     if(hr==unchecked((int)0x80020003)||hr==unchecked((int)0x80020006))lock(ids)ids.Remove(kind+"|"+name);
     throw new COMException(name+" failed (0x"+hr.ToString("X8")+")",hr);
    }
    return value;
   }finally{Marshal.FreeCoTaskMem(memory);}
  }
  public object Get(string name){return Invoke(name,PropertyGet,null,null);}
  public int Number(string name){return Convert.ToInt32(Get(name));}
  public void Set(string name,object value){Invoke(name,PropertyPut,null,new object[]{value});}
  public object Call(string name,params object[] args){return Invoke(name,Method,null,args);}
  public void Call(string name,int[] outs,params object[] args){Invoke(name,Method,outs,args);}
 }

 sealed class RichEditTarget : IDisposable {
  const int UndoSuspend=-9999995,UndoResume=-9999994,TomLine=5;
  readonly IntPtr hwnd; readonly string kind; Remote doc,sel,range; int anchor,committed; string written="";
  public RichEditTarget(IntPtr window){hwnd=window;kind=Native.ClassOf(window);}
  static Guid dispatch=new Guid("00020400-0000-0000-C000-000000000046");
  public void Dispose(){
   if(range!=null)range.Dispose();if(sel!=null)sel.Dispose();if(doc!=null)doc.Dispose();
   range=null;sel=null;doc=null;
  }

  // Finds the document and where its caret or selection is. Null when found; otherwise why not.
  // Keys and clicks the application has not got to yet (a line break typed a moment ago, say) will still move the
  // caret, and calls like these overtake them. `settle` says such input was sent a moment ago; an application that
  // was slow to answer may have some waiting as well. Then the caret is read until it stays where it is.
  string Attach(bool settle,out int start,out int end){
   object d;Stopwatch asked=Stopwatch.StartNew();start=end=0;
   if(Native.AccessibleObjectFromWindow(hwnd,0xfffffff0,ref dispatch,out d)!=0||d==null)return "no text object model";
   doc=new Remote(d,kind+"|document");
   sel=new Remote(doc.Get("Selection"),kind+"|selection");
   start=sel.Number("Start");
   if(settle||asked.ElapsedMilliseconds>=12){
    for(asked.Restart();asked.ElapsedMilliseconds<600;){
     Thread.Sleep(8);
     int again=sel.Number("Start");
     if(again==start)break;
     start=again;
    }
   }
   end=sel.Number("End");
   // Reuse one range to avoid repeated object creation and release.
   range=new Remote(doc.Call("Range",start,end),kind+"|range");
   if(Convert.ToInt32(range.Call("CanEdit"))==0)return "read only";
   return null;
  }
  // Null when the document can be written to at the caret; otherwise why not.
  public string Begin(bool settle){
   int start,end;
   string why=Attach(settle,out start,out end);
   if(why!=null)return why;
   // Typing over a selection replaces it. Nothing is taken out of the document for text that may yet be cancelled,
   // so such text is shown over the document and goes in when it is confirmed (see Insert).
   if(start!=end)return "selection";
   anchor=start;
   return null;
  }
  // Finished text, put in at the caret the way typing it would: over the selection if there is one, as one step of
  // undo. A range that is not the selection is used, so nothing is shown selected in between.
  public bool Insert(string text,bool settle){
   int start,end;
   if(Attach(settle,out start,out end)!=null)return false;
   range.Set("Text",text);
   Require(range.Number("End")==start+text.Length,"the document did not take the text");
   sel.Call("SetRange",start+text.Length,start+text.Length);
   return true;
  }
  string Slice(int from,int to){
   if(to<=from)return "";
   range.Call("SetRange",from,to);
   return Convert.ToString(range.Get("Text"))??"";
  }
  // Replaces what the range spans. An edit that is not recorded leaves no step in the document's undo history.
  // Recording is switched off only for the instant of the edit itself, so nothing stays switched off if this
  // process is ended.
  void Replace(string text,bool recorded){
   if(recorded){range.Set("Text",text);return;}
   doc.Call("Undo",UndoSuspend);
   try{range.Set("Text",text);}finally{doc.Call("Undo",UndoResume);}
  }
  static void Require(bool ok,string what){if(!ok)throw new InvalidOperationException(what);}

  // Makes the document show `target` from the anchor on. The first `commitLen` characters are committed text:
  // each newly committed stretch goes in as one recorded edit, so one Undo in the application removes exactly that.
  public void Write(string target,int commitLen){
   Require(commitLen>=committed&&commitLen<=target.Length&&string.CompareOrdinal(target,0,written,0,committed)==0,"committed text changed");
   // The document is touched only while it still holds exactly what was put there.
   string tail=written.Substring(committed);
   if(tail.Length>0)Require(Slice(anchor+committed,anchor+written.Length)==tail,"the document no longer holds the text being typed");
   int want=anchor+target.Length;
   if(commitLen==committed){
    int prefix=committed,max=Math.Min(written.Length,target.Length);
    while(prefix<max&&written[prefix]==target[prefix])prefix++;
    if(prefix>committed&&prefix<written.Length&&char.IsLowSurrogate(written[prefix]))prefix--;
    int suffix=0;
    while(suffix<written.Length-prefix&&suffix<target.Length-prefix&&written[written.Length-1-suffix]==target[target.Length-1-suffix])suffix++;
    if(suffix>0&&char.IsLowSurrogate(written[written.Length-suffix]))suffix--;
    int gone=written.Length-prefix-suffix;string fresh=target.Substring(prefix,target.Length-prefix-suffix);
    if(gone==0&&fresh.Length==0)return;
    // Only the characters that differ are replaced.
    range.Call("SetRange",anchor+prefix,anchor+prefix+gone);
    Replace(fresh,false);
    Require(range.Number("End")==anchor+prefix+fresh.Length,"the document did not take the text");
   }else{
    string add=target.Substring(committed,commitLen-committed),rest=target.Substring(commitLen);
    // Take the unrecorded text out, put the committed text in as one recorded edit, and put back what is still
    // being typed. The display is held meanwhile, so none of this is seen.
    doc.Call("Freeze");
    try{
     if(tail.Length>0)Replace("",false);    // the range spans the tail since it was read
     range.Call("SetRange",anchor+committed,anchor+committed);Replace(add,true);
     if(rest.Length>0){range.Call("SetRange",anchor+commitLen,anchor+commitLen);Replace(rest,false);}
     sel.Call("SetRange",want,want);
    }finally{doc.Call("Unfreeze");}
    Require(Slice(anchor+committed,anchor+target.Length)==add+rest,"the document did not take the text");
    committed=commitLen;written=target;
    return;
   }
   written=target;
   // The caret belongs behind the text. A detached range never selects anything, so no highlight flashes.
   sel.Call("SetRange",want,want);
  }

  // A corner of what the range spans, in screen pixels. 32: its start, otherwise its end; 8: the bottom of the line.
  bool Point(int type,out int x,out int y){
   int[] at=new int[2];x=0;y=0;
   try{range.Call("GetPoint",at,type);}catch{return false;}
   x=at[0];y=at[1];
   return x!=0||y!=0;
  }
  void Boxes(int from,int to,List<LineBox> list){
   Native.Rect window;Native.GetWindowRect(hwnd,out window);
   int position=from;
   for(int guard=0;position<to&&guard<8;guard++){
    int left,top,bottom,right,endBottom,unused,stop=to;
    range.Call("SetRange",position,to);
    if(!Point(32,out left,out top)||!Point(32|8,out unused,out bottom))return;
    bool ends=Point(8,out right,out endBottom);
    if(!ends||endBottom!=bottom||right<left){
     // The stretch runs on past this line: take the line alone.
     range.Call("SetRange",position,position);
     int lineEnd=-1;
     try{range.Call("EndOf",TomLine,0);lineEnd=range.Number("Start");}catch{}
     if(lineEnd>position&&lineEnd<to){
      stop=lineEnd;
      range.Call("SetRange",position,stop);
      ends=Point(8,out right,out endBottom);
     }
     // The end of a wrapped line is reported as the start of the next one; such a line runs to the edge of the editor.
     if(!ends||endBottom!=bottom||right<left)right=window.right-12;
    }
    // Lines scrolled out of the editor get no marks: the marks are drawn over the screen, not inside the editor.
    left=Math.Max(left,window.left);right=Math.Min(right,window.right);
    if(bottom>top&&right>left&&top>=window.top&&bottom<=window.bottom)list.Add(new LineBox{Left=left,Right=right,Top=top,Bottom=bottom});
    position=stop;
   }
  }
  public InlineGeometry Locate(int focusStart,int focusLength){
   InlineGeometry g=new InlineGeometry();
   int from=anchor+committed,to=anchor+written.Length;
   if(to<=from)return g;
   try{
    Boxes(from,to,g.Text);
    if(focusStart>=0&&focusLength>0)Boxes(Math.Min(to,from+focusStart),Math.Min(to,from+focusStart+focusLength),g.Focus);
    CaretInfo caret;IntPtr focus;
    g.LineHeight=Native.SystemCaret(out caret,out focus)?caret.Height:(g.Text.Count>0?g.Text[g.Text.Count-1].Bottom-g.Text[g.Text.Count-1].Top:0);
   }catch{g.Text.Clear();g.Focus.Clear();}
   return g;
  }
 }

 // Does the writing on its own thread: calls into another process must never hold up the keyboard hook.
 public sealed class InlineWriter {
  readonly Thread thread; readonly Queue<InlineJob> queue=new Queue<InlineJob>(); readonly object gate=new object();
  readonly ManualResetEvent idle=new ManualResetEvent(true); readonly AutoResetEvent signal=new AutoResetEvent(false);
  RichEditTarget target; int session=-1; volatile bool stop; InlineJob showing; InlineGeometry shownGeometry;
  public volatile bool Busy;
  public Action<int,string> Fallback;   // nothing was written; show this composition the other way
  public Action<int,string> Broken;     // the document changed underneath; it is left exactly as it is
  public Action<string> Typed;          // finished text that could not be put in directly; send it as keys
  public Action<InlineJob> Written;     // the text of this job is in the document
  public Action<InlineJob,InlineGeometry> Rendered;   // and this is where it sits on the screen
  public double LastMilliseconds,SlowestMilliseconds,TotalMilliseconds,LocateMilliseconds; public int Writes;

  public InlineWriter(){thread=new Thread(Loop);thread.IsBackground=true;thread.Name="inline";thread.SetApartmentState(ApartmentState.STA);thread.Start();}
  // Direct writing is limited to the supported RichEdit control classes.
  public static bool Supports(IntPtr focus){
   if(focus==IntPtr.Zero)return false;
   string cls=Native.ClassOf(focus);
   return cls=="RichEditD2DPT"||cls.IndexOf("RichEdit50W",StringComparison.OrdinalIgnoreCase)>=0||cls.IndexOf("RichEdit20W",StringComparison.OrdinalIgnoreCase)>=0;
  }
  public void Post(InlineJob job){lock(gate){queue.Enqueue(job);Busy=true;idle.Reset();}signal.Set();}
  // Waits until everything posted so far is in the document.
  public bool Flush(int milliseconds){return idle.WaitOne(milliseconds);}
  public void Stop(){stop=true;signal.Set();}
  void Loop(){
   while(!stop){
    InlineJob job=null;
    lock(gate){
     if(queue.Count>0){
      job=queue.Dequeue();
      // Each job says the whole state, so a newer one for the same composition replaces an older one.
      while(!job.Begin&&!job.Direct&&queue.Count>0&&!queue.Peek().Begin&&!queue.Peek().Direct&&queue.Peek().Session==job.Session)job=queue.Dequeue();
     }else{Busy=false;idle.Set();}
    }
    if(job!=null){Run(job);continue;}
    if(signal.WaitOne(150))continue;
    Track();
   }
   Close();
  }
  void Close(){if(target!=null){target.Dispose();target=null;}showing=null;shownGeometry=null;}
  static string Reason(Exception ex){while(ex.InnerException!=null)ex=ex.InnerException;return ex.Message;}
  void Run(InlineJob job){
   if(job.Direct){
    if(job.DelayMs>0)Thread.Sleep(Math.Min(400,job.DelayMs));
    bool done=false;
    using(RichEditTarget t=new RichEditTarget(job.Window)){try{done=t.Insert(job.Full,job.DelayMs>0);}catch{}}
    if(!done)Typed(job.Full);
    return;
   }
   if(job.Begin){
    Close();session=job.Session;
    // A key or click sent just before (Enter, an arrow, a click) must have moved the caret before it is read.
    if(job.DelayMs>0)Thread.Sleep(Math.Min(400,job.DelayMs));
    RichEditTarget t=new RichEditTarget(job.Window);string why;
    try{why=t.Begin(job.DelayMs>0);}catch(Exception ex){why=Reason(ex);}
    if(why!=null){t.Dispose();Fallback(session,why);return;}
    target=t;return;
   }
   if(job.Session!=session||target==null)return;
   try{
    Stopwatch sw=Stopwatch.StartNew();
    target.Write(job.Full,job.CommitLen);
    LastMilliseconds=sw.Elapsed.TotalMilliseconds;if(LastMilliseconds>SlowestMilliseconds)SlowestMilliseconds=LastMilliseconds;
    TotalMilliseconds+=LastMilliseconds;Writes++;
    Written(job);
    sw.Restart();
    InlineGeometry g=job.Active?target.Locate(job.View!=null?job.View.FocusStart:-1,job.View!=null?job.View.FocusLength:0):null;
    LocateMilliseconds+=sw.Elapsed.TotalMilliseconds;
    showing=job.Active?job:null;shownGeometry=g;
    Rendered(job,g);
    if(!job.Active)Close();
   }catch(Exception ex){
    Close();
    Broken(session,Reason(ex));
   }
  }
  // While text is being typed the window may be scrolled or moved; the marks under the text follow.
  void Track(){
   if(showing==null||target==null)return;
   try{
    InlineGeometry g=target.Locate(showing.View!=null?showing.View.FocusStart:-1,showing.View!=null?showing.View.FocusLength:0);
    if(!g.Same(shownGeometry)){shownGeometry=g;Rendered(showing,g);}
   }catch{}
  }
 }
}
