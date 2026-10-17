// The resident: keyboard and mouse hooks, input mode, output to the application, tray icon and commands.
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Runtime;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using WinForms=System.Windows.Forms;

namespace LiveHenkan {
 public enum InputMode { Alnum, Kana }

 // Finds the caret off the hook thread: accessibility calls into another process can be slow.
 sealed class Probe {
  sealed class Request {public IntPtr Focus;public int Session;public bool AfterCommit,CloseIme,Locate,Style,Password;public CaretInfo Before;}
  public Action<bool,IntPtr> PasswordFound;
  readonly Thread thread; readonly AutoResetEvent signal=new AutoResetEvent(false); readonly object gate=new object();
  readonly Action<CaretInfo,int,bool> deliver; readonly Action<StyleInfo,IntPtr,int> deliverStyle; Request waiting; volatile bool stop;
  public Probe(Action<CaretInfo,int,bool> sink,Action<StyleInfo,IntPtr,int> styleSink){deliver=sink;deliverStyle=styleSink;thread=new Thread(Loop);thread.IsBackground=true;thread.Name="probe";thread.SetApartmentState(ApartmentState.STA);thread.Start();}
  public void Stop(){stop=true;signal.Set();}
  void Post(Request r){
   lock(gate){
    if(waiting!=null&&waiting.Focus==r.Focus){
     if(!r.Locate){waiting.CloseIme|=r.CloseIme;waiting.Password|=r.Password;signal.Set();return;}
     r.CloseIme|=waiting.CloseIme;r.Password|=waiting.Password;
    }
    waiting=r;
   }
   signal.Set();
  }
  public void Start(IntPtr focus,int session){Post(new Request{Focus=focus,Session=session,Locate=true,Style=true,CloseIme=true});}
  public void Locate(IntPtr focus,int session){Post(new Request{Focus=focus,Session=session,Locate=true});}
  public void AfterCommit(IntPtr focus,int session,CaretInfo before){Post(new Request{Focus=focus,Session=session,Locate=true,AfterCommit=true,Before=before});}
  public void CloseIme(IntPtr focus){Post(new Request{Focus=focus,CloseIme=true});}
  public void CheckPassword(IntPtr focus){lock(gate){if(waiting!=null&&waiting.Focus==focus){waiting.Password=true;signal.Set();return;}}Post(new Request{Focus=focus,Password=true});}
  // Query focused elements on focus changes as well as typing. Web accessibility
  // providers need time to initialize before their first caret query.
  static bool IsPassword(IntPtr focus){
   if(focus!=IntPtr.Zero&&Native.ClassOf(focus)=="Edit"&&(Native.GetWindowLongPtr(focus,-16).ToInt64()&0x20)!=0)return true;
   try{AutomationElement fe=AutomationElement.FocusedElement;return fe!=null&&fe.Current.IsPassword;}catch{return false;}
  }
  bool Superseded(){lock(gate)return waiting!=null&&waiting.Locate;}
  void Loop(){
   while(!stop){
    signal.WaitOne(500);
    Request r;lock(gate){r=waiting;waiting=null;}
    if(r==null)continue;
    try{
     if(r.CloseIme)Native.CloseSystemIme(r.Focus);
     if(r.Password&&PasswordFound!=null)PasswordFound(IsPassword(r.Focus),r.Focus);
     if(r.Focus!=Native.FocusWindow())continue;
     if(r.Locate&&!r.AfterCommit){
      CaretInfo caret;StyleInfo style;FindTarget(r.Focus,out caret,out style);
      for(int retry=0;!caret.Found&&retry<8&&!Superseded();retry++){
       Thread.Sleep(50);if(r.Focus!=Native.FocusWindow())break;FindTarget(r.Focus,out caret,out style);
      }
      if(r.Focus!=Native.FocusWindow()||Superseded())continue;
      deliver(caret,r.Session,false);
      if(r.Style)deliverStyle(style,r.Focus,r.Session);
     }
     if(!r.AfterCommit)continue;
     // The application takes the text in one character at a time. Wait until its caret has moved and come to rest.
     CaretInfo last=r.Before;bool moved=false;int still=0;
     for(int i=0;i<24&&!stop&&!Superseded();i++){
      Thread.Sleep(20);
      CaretInfo c=Find(r.Focus);
      if(r.Focus!=Native.FocusWindow())break;
      if(!c.Found)continue;
      if(!c.Same(r.Before))moved=true;
      if(c.Same(last))still++;else still=0;
      last=c;
      if(moved&&still>=2){deliver(c,r.Session,true);break;}
     }
    }catch{}
   }
  }
  public static CaretInfo Find(IntPtr focusHint){
   CaretInfo c;StyleInfo style;FindTarget(focusHint,out c,out style);return c;
  }
  // A host window can contain several editors (Office's sheet and its web task pane).
  // Read position and font from the SAME focused element, never from a background document.
  static void FindTarget(IntPtr focus,out CaretInfo caret,out StyleInfo style){
   caret=new CaretInfo{Window=focus};style=null;
   AutomationElement element=null;TextPatternRange selection=null;
   System.Windows.Rect bounds=System.Windows.Rect.Empty;
   try{
    selection=Selection(out element);
    if(element!=null){
     if(!element.Current.HasKeyboardFocus)return;
     bounds=element.Current.BoundingRectangle;
    }
    if(selection!=null){
     style=RangeStyle(selection);
    }
   }catch{}
   style=OfficeStyle.Find(focus,style);
   CaretInfo native;IntPtr actual;
   if(Native.SystemCaret(out native,out actual)&&actual==focus&&
      Native.CaretBelongsToFocus(native.Window,focus)&&CaretGeometry.InBounds(native,bounds)){caret=native;return;}
   if(Native.AccessibleCaret(focus,out native)&&CaretGeometry.InBounds(native,bounds)){caret=native;return;}
   StyleInfo caretStyle;
   if(LiveHenkan.AutomationCaret.Find(focus,out caret,out caretStyle)&&CaretGeometry.InBounds(caret,bounds)){
    if(caretStyle!=null)style=caretStyle;return;
   }
   if(selection!=null&&AutomationCaret(selection,focus,out caret)&&CaretGeometry.InBounds(caret,bounds))return;
   // Never ask the Office host for a caret when a child editor has focus.
   caret=new CaretInfo{Window=focus};
  }
  static TextPatternRange Selection(out AutomationElement element){
   element=AutomationElement.FocusedElement;if(element==null)return null;
   object pattern;if(!element.TryGetCurrentPattern(TextPattern.Pattern,out pattern))return null;
   TextPatternRange[] sel=((TextPattern)pattern).GetSelection();
   return sel==null||sel.Length==0?null:sel[0];
  }
  static bool AutomationCaret(TextPatternRange sel,IntPtr window,out CaretInfo caret){
   caret=new CaretInfo();
   try{
    sel=sel.Clone();sel.MoveEndpointByRange(TextPatternRangeEndpoint.End,sel,TextPatternRangeEndpoint.Start);
    System.Windows.Rect[] rects=sel.GetBoundingRectangles();
    // Some web providers return the entire input box for an empty range. It is not a caret.
    if(rects!=null&&rects.Length>0&&CaretGeometry.IsCaretRectangle(rects[0])){caret=new CaretInfo{Found=true,Left=(int)rects[0].Left,Top=(int)rects[0].Top,Height=(int)rects[0].Height,Window=window,Source="automation"};return true;}
    // An empty range has no rectangle: measure the character before the caret, then the one after it.
    TextPatternRange before=sel.Clone();
    if(before.MoveEndpointByUnit(TextPatternRangeEndpoint.Start,TextUnit.Character,-1)!=0){
     rects=before.GetBoundingRectangles();string t=before.GetText(2);
     if(rects!=null&&rects.Length>0&&rects[rects.Length-1].Height>0&&t.IndexOf((char)13)<0&&t.IndexOf((char)10)<0){System.Windows.Rect r=rects[rects.Length-1];caret=new CaretInfo{Found=true,Left=(int)r.Right,Top=(int)r.Top,Height=(int)r.Height,Window=window,Source="automation-before"};return true;}
    }
    TextPatternRange after=sel.Clone();
    if(after.MoveEndpointByUnit(TextPatternRangeEndpoint.End,TextUnit.Character,1)!=0){
     rects=after.GetBoundingRectangles();
     if(rects!=null&&rects.Length>0&&rects[0].Height>0){caret=new CaretInfo{Found=true,Left=(int)rects[0].Left,Top=(int)rects[0].Top,Height=(int)rects[0].Height,Window=window,Source="automation-after"};return true;}
    }
    return false;
   }catch{return false;}
  }
  static int Rgb(object colorRef){
   if(!(colorRef is int))return -1;
   int v=(int)colorRef;return ((v&0xFF)<<16)|(v&0xFF00)|((v>>16)&0xFF);
  }
  // Font and colours of the text at the caret, where the application publishes them. Null otherwise.
  public static StyleInfo FindStyle(out IntPtr window){
   window=IntPtr.Zero;
   try{
    AutomationElement fe;TextPatternRange sel=Selection(out fe);if(sel==null)return null;
    window=new IntPtr(fe.Current.NativeWindowHandle);
    return RangeStyle(sel);
   }catch{return null;}
  }
  static StyleInfo RangeStyle(TextPatternRange sel){
   try{
    object name=sel.GetAttributeValue(TextPattern.FontNameAttribute),size=sel.GetAttributeValue(TextPattern.FontSizeAttribute);
    if(!(name is string)||!(size is double)||((string)name).Length==0||!CaretGeometry.ValidPoints((double)size))return null;
    StyleInfo style=new StyleInfo{FontName=(string)name,SizePoints=(double)size};
    style.Ink=Rgb(sel.GetAttributeValue(TextPattern.ForegroundColorAttribute));
    style.Back=Rgb(sel.GetAttributeValue(TextPattern.BackgroundColorAttribute));
    object weight=sel.GetAttributeValue(TextPattern.FontWeightAttribute);
    style.Bold=weight is int&&(int)weight>=600;
    return style;
   }catch{return null;}
  }
 }

 public sealed class App : IHost {
  const string MutexName="Local\\LiveHenkan.Resident";
  public static string PipeName{get{return "LiveHenkan."+Environment.UserName;}}
  readonly string root,userDir; readonly bool testMode; readonly Settings settings=new Settings();
  Jinen.Engine engine; Converter converter; Composer composer; Ui ui; Probe probe; UserDictionary dictionary; InlineWriter inline;
  // A composition is shown either in the document itself (inline) or in the window laid over the caret.
  // Keys and clicks that go to the application travel through its input queue, while writing into the document is a
  // call that overtakes that queue. `passedAt` is when such input is expected to have arrived.
  double passedAt; volatile string inlineAt;
  bool inlineSession; IntPtr inlineWindow; readonly Dictionary<int,string> inlineCommitted=new Dictionary<int,string>(); readonly uint ownPid=(uint)Process.GetCurrentProcess().Id;
  Thread hookThread; WinForms.Control invoker; WinForms.Timer awaitTimer,healthTimer; WinForms.NotifyIcon tray; IntPtr trayIcon;
  readonly ManualResetEvent ready=new ManualResetEvent(false); string failure;
  Native.HookProc keyProc,mouseProc; Native.WinEventProc eventProc; IntPtr keyHook,mouseHook,foregroundHook,focusHook;
  volatile InputMode mode=InputMode.Alnum; volatile bool secure,quitting;
  readonly HashSet<int> swallowed=new HashSet<int>(); readonly Stopwatch clock=Stopwatch.StartNew();
  bool downOnly; int keysSeen,testKeysSeen,hookWaitMaxMs; double hookMaxMs;
  int lastSession=-1,rehooks,errors; CaretInfo lastCaret; uint lastHookTick; double keyAt,lastToggleAt=-1000; bool reprobe,inResult;
  readonly HashSet<int> modeKeysDown=new HashSet<int>();
  // latency records, guarded by `stamps`
  readonly Dictionary<long,double[]> stamps=new Dictionary<long,double[]>(); readonly List<double> shownMs=new List<double>(),convertedMs=new List<double>();

  App(string rootDir,bool test,string userFolder){
   root=rootDir;testMode=test;
   string dir=string.IsNullOrEmpty(userFolder)?Path.Combine(root,"user"):userFolder;
   try{Directory.CreateDirectory(dir);File.AppendAllText(Path.Combine(dir,"log.txt"),"");}
   catch{dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"live-henkan");Directory.CreateDirectory(dir);}
   userDir=dir;
  }
  string SettingsPath{get{return Path.Combine(userDir,"settings.txt");}}
  void Log(string message){try{File.AppendAllText(Path.Combine(userDir,"log.txt"),DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+message+Environment.NewLine,Encoding.UTF8);}catch{}}

  // Returns 0 on a normal end, 2 when another instance is already resident.
  public static int Run(string root,string modelPath,string command,bool test,string userFolder){
   bool created;
   using(Mutex single=new Mutex(true,MutexName,out created)){
    if(!created)return 2;
    try{return new App(root,test,userFolder).Main(modelPath,command);}
    finally{single.ReleaseMutex();}
   }
  }
  int ThreadCount(){
   if(settings.Threads>0)return settings.Threads;
   int n=Environment.ProcessorCount;
   return n<=4?2:n<=8?3:n<=12?4:5;
  }
  int Main(string modelPath,string command){
   Native.PrepareProcess();
   try{GCSettings.LatencyMode=GCLatencyMode.SustainedLowLatency;}catch{}
   try{AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges",false);}catch{}
   Log("start pid="+Process.GetCurrentProcess().Id+(testMode?" test":""));
   // Whatever goes wrong on any thread is written down; when the process ends, Windows removes the hooks with it.
   AppDomain.CurrentDomain.UnhandledException+=delegate(object s,UnhandledExceptionEventArgs e){Log("unhandled "+e.ExceptionObject);};
   try{SettingsStore.Load(SettingsPath,settings);}catch(Exception ex){Log("settings "+ex.Message);}
   try{
    engine=new Jinen.Engine(modelPath,ThreadCount());
    dictionary=new UserDictionary(userDir);
    converter=new Converter(engine,dictionary,OnWorkerResult,OnWorkerCandidates);
    composer=new Composer(this,settings,new RomajiTable(Path.Combine(root,"app","romaji.tsv")),dictionary);
    composer.SymbolMap=MapSymbol;
    ui=new Ui();ui.Rendered=OnRendered;ui.Failed=delegate(string what){errors++;Log("display "+what);};ui.Start();
    probe=new Probe(delegate(CaretInfo c,int session,bool afterCommit){if(c.Found)lastCaret=c;ui.Caret(c,session,afterCommit);},
     delegate(StyleInfo style,IntPtr window,int session){ui.Style(style,window,session);});
    probe.PasswordFound=delegate(bool found,IntPtr window){if(Native.FocusWindow()==window)secure=found;};
    inline=new InlineWriter();inline.Fallback=OnInlineFallback;inline.Broken=OnInlineBroken;inline.Written=delegate(InlineJob job){OnRendered(job.Serial);};inline.Typed=delegate(string text){Native.SendText(text,Native.OwnMark);Passed(text.Length);};inline.Rendered=OnInlineRendered;
    hookThread=new Thread(HookLoop);hookThread.IsBackground=true;hookThread.Name="hook";hookThread.SetApartmentState(ApartmentState.STA);hookThread.Priority=ThreadPriority.AboveNormal;hookThread.Start();
    if(!ready.WaitOne(15000))throw new TimeoutException("hook thread");
    if(failure!=null)throw new InvalidOperationException(failure);
    Thread pipe=new Thread(PipeLoop);pipe.IsBackground=true;pipe.Name="pipe";pipe.Start();
    // Warm the model so the first keystroke is as quick as the rest.
    converter.Submit(new Job{Version=0,ContextLeft="",Chunks=new List<ChunkRequest>{new ChunkRequest{Id=-2,Reading="じゅんび"}}},false);
    Execute(command??"on");
    // After the first conversion everything that typing needs has been touched once.
    Thread.Sleep(1500);
    if(settings.HoldMemory){
     int held=Native.HoldWorkingSet(32L*1024*1024);
     Log("working set "+(Environment.WorkingSet>>20)+" MB "+(held==0?"held":"not held, error "+held));
    }
    hookThread.Join();
    Log("stop");
    return 0;
   }catch(Exception ex){
    Log("fatal "+ex);
    throw;
   }finally{
    quitting=true;
    try{if(inline!=null)inline.Stop();}catch{}
    try{if(probe!=null)probe.Stop();}catch{}
    try{if(ui!=null)ui.Stop();}catch{}
    try{if(converter!=null)converter.Dispose();}catch{}
    try{if(engine!=null)engine.Dispose();}catch{}
   }
  }

  // ---- hook thread -----------------------------------------------------
  void HookLoop(){
   try{
    invoker=new WinForms.Control();IntPtr unused=invoker.Handle;
    awaitTimer=new WinForms.Timer();awaitTimer.Tick+=delegate{awaitTimer.Stop();Guard(delegate{composer.OnTimeout();});};
    healthTimer=new WinForms.Timer();healthTimer.Interval=2000;healthTimer.Tick+=delegate{Health();};healthTimer.Start();
    if(testMode){
     Interlocked.Exchange(ref beatAt,Stopwatch.GetTimestamp());
     WinForms.Timer beat=new WinForms.Timer();beat.Interval=30;beat.Tick+=delegate{Interlocked.Exchange(ref beatAt,Stopwatch.GetTimestamp());};beat.Start();
     Watch();
    }
    keyProc=OnKey;mouseProc=OnMouse;eventProc=OnWinEvent;
    InstallHooks();
    foregroundHook=Native.SetWinEventHook(3,3,IntPtr.Zero,eventProc,0,0,0);
    focusHook=Native.SetWinEventHook(0x8005,0x8005,IntPtr.Zero,eventProc,0,0,0);
    CreateTray();
    ready.Set();
    WinForms.Application.Run();
   }catch(Exception ex){failure=ex.ToString();Log("hook thread error "+ex);ready.Set();}
   finally{
    RemoveHooks();
    if(foregroundHook!=IntPtr.Zero)Native.UnhookWinEvent(foregroundHook);
    if(focusHook!=IntPtr.Zero)Native.UnhookWinEvent(focusHook);
    if(tray!=null){tray.Visible=false;tray.Dispose();}
    if(trayIcon!=IntPtr.Zero)Native.DestroyIcon(trayIcon);
   }
  }
  void InstallHooks(){
   IntPtr module=Native.GetModuleHandle(null);
   keyHook=Native.SetWindowsHookEx(13,keyProc,module,0);
   if(keyHook==IntPtr.Zero)throw new InvalidOperationException("Keyboard hook "+Marshal.GetLastWin32Error());
   mouseHook=Native.SetWindowsHookEx(14,mouseProc,module,0);
   if(mouseHook==IntPtr.Zero)throw new InvalidOperationException("Mouse hook "+Marshal.GetLastWin32Error());
   lastHookTick=(uint)Environment.TickCount;
  }
  void RemoveHooks(){
   IntPtr k=Interlocked.Exchange(ref keyHook,IntPtr.Zero),m=Interlocked.Exchange(ref mouseHook,IntPtr.Zero);
   if(k!=IntPtr.Zero)Native.UnhookWindowsHookEx(k);
   if(m!=IntPtr.Zero)Native.UnhookWindowsHookEx(m);
  }
  // Windows drops a low-level hook without notice if it ever answers too slowly. Input that the hooks did not see means that happened.
  void Health(){
   if(quitting)return;
   Native.LastInput info=new Native.LastInput();info.size=(uint)Marshal.SizeOf(typeof(Native.LastInput));
   if(!Native.GetLastInputInfo(ref info))return;
   if((int)(info.time-lastHookTick)>1500){
    try{RemoveHooks();InstallHooks();modeKeysDown.Clear();swallowed.Clear();rehooks++;Log("hooks reinstalled");}
    catch(Exception ex){errors++;Log("hook recovery failed: "+ex.Message);}
   }
  }
  // Test-mode watch on the hook thread: says what it was doing whenever it stops answering for a while.
  volatile string doing; long doingSince,beatAt;
  void Watch(){
   Thread t=new Thread(delegate(){
    long reported=0,last=Stopwatch.GetTimestamp();int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);
    while(!quitting){
     Thread.Sleep(20);
     long now=Stopwatch.GetTimestamp();string what=doing;
     // This thread only sleeps and looks. If it was itself held up, every thread of the process was.
     double gap=(now-last)*1000.0/Stopwatch.Frequency;last=now;
     int n0=GC.CollectionCount(0),n1=GC.CollectionCount(1),n2=GC.CollectionCount(2);
     if(gap>150)Log("whole process paused "+gap.ToString("F0")+" ms; collections during it: gen0 +"+(n0-g0)+", gen1 +"+(n1-g1)+", gen2 +"+(n2-g2)+"; managed heap "+(GC.GetTotalMemory(false)>>20)+" MB; working set "+(Environment.WorkingSet>>20)+" MB");
     g0=n0;g1=n1;g2=n2;
     double busy=what==null?0:(now-Interlocked.Read(ref doingSince))*1000.0/Stopwatch.Frequency,silent=(now-Interlocked.Read(ref beatAt))*1000.0/Stopwatch.Frequency;
     if((busy>120||silent>200)&&now-reported>Stopwatch.Frequency){reported=now;Log("hook thread stalled: doing="+(what??"(idle)")+" busy="+busy.ToString("F0")+" ms, no heartbeat for "+silent.ToString("F0")+" ms, working set "+(Environment.WorkingSet>>20)+" MB, gc="+GC.CollectionCount(0)+"/"+GC.CollectionCount(1)+"/"+GC.CollectionCount(2));}
    }
   });
   t.IsBackground=true;t.Priority=ThreadPriority.Highest;t.Name="watch";t.Start();
  }
  void Guard(Action a){
   doing=a.Method.Name;Interlocked.Exchange(ref doingSince,Stopwatch.GetTimestamp());
   try{a();}
   catch(Exception ex){
    errors++;Log("error "+ex);
    try{composer.Abandon();}catch{}
    // A lifetime counter of recoverable failures must not silently turn input off.
   }
   finally{doing=null;}
  }
  // Width and punctuation style of what the keys type in kana mode, as set by the user.
  string MapSymbol(char c){
   if(settings.WesternPunctuation){if(c=='\u3001')return "\uFF0C";if(c=='\u3002')return "\uFF0E";}
   if(c>='0'&&c<='9')return settings.FullWidthDigits?((char)(c+0xFEE0)).ToString():null;
   return settings.FullWidthSymbols?Symbols.Map(c):null;
  }
  static bool IsModifier(int vk){return vk==16||vk==17||vk==18||vk==91||vk==92||(vk>=160&&vk<=165);}
  static int ModeKey(int vk){return vk==0xF4?0xF3:vk;}

  IntPtr OnKey(int code,IntPtr wParam,IntPtr lParam){
   if(code>=0&&!quitting){
    bool swallow=false;long began=Stopwatch.GetTimestamp();
    doing="key";Interlocked.Exchange(ref doingSince,began);Interlocked.Exchange(ref beatAt,began);
    try{
     Native.KeyData k=(Native.KeyData)Marshal.PtrToStructure(lParam,typeof(Native.KeyData));
     lastHookTick=k.time;
     // How long the event waited before this thread got to it. Past the system's limit Windows gives up on the hook.
     int waited=Environment.TickCount-(int)k.time;if(waited>hookWaitMaxMs)hookWaitMaxMs=waited;if(waited>150)Log("key waited "+waited+" ms for the hook");
     long tag=(long)k.extra.ToUInt64();bool injected=(k.flags&0x10)!=0;
     if(testMode&&tag==Native.TestMark&&(wParam.ToInt32()==0x100||wParam.ToInt32()==0x104))testKeysSeen++;
     if(tag!=Native.OwnMark&&(!injected||(testMode&&tag==Native.TestMark))){
      int msg=wParam.ToInt32(),vk=(int)k.vk;
      if(msg==0x100||msg==0x104){
       downOnly=false;
       if(KeyDown(vk,(int)k.scan)){if(!downOnly)swallowed.Add(ModeKey(vk));swallow=true;}
       else if(!IsModifier(vk))Passed(Native.Down(17)||Native.Down(18)?15:1);   // a shortcut starts something that takes longer than a letter
      }
      else{modeKeysDown.Remove(ModeKey(vk));if(swallowed.Remove(ModeKey(vk)))swallow=true;}
     }
    }catch(Exception ex){errors++;Log("key error "+ex);try{composer.Abandon();}catch{}}
    // Windows passes a key on untouched when a hook takes too long, so the slowest answer is worth knowing.
    double took=(Stopwatch.GetTimestamp()-began)*1000.0/Stopwatch.Frequency;
    keysSeen++;if(took>hookMaxMs)hookMaxMs=took;if(took>100)Log("slow key hook "+took.ToString("F0")+" ms");
    doing=null;
    if(swallow)return new IntPtr(1);
   }
   return Native.CallNextHookEx(keyHook,code,wParam,lParam);
  }
  bool KeyDown(int vk,int scan){
   bool ctrl=Native.Down(17),shift=Native.Down(16),alt=Native.Down(18),win=Native.Down(91)||Native.Down(92);
   if(vk==0x7B&&ctrl&&shift){Log("emergency stop");Quit(true);return false;}
   // A key that goes on to the application must find the last committed text already in the document.
   if(inline.Busy&&!composer.Composing&&!composer.Awaiting)inline.Flush(250);
   if(IsModifier(vk)){
    // Alt and the Windows key lead to menus and other windows. The text must be in place first, and it must not be typed
    // with the key held, so this press is dropped and sent again behind the text. Its release is left alone.
    if((vk==18||vk==164||vk==165||vk==91||vk==92)&&(composer.Composing||composer.Awaiting)){
     composer.CommitNow();Native.SendKeyDown(vk,Native.OwnMark);
     downOnly=true;return true;
    }
    return false;
   }
   if(vk==0x1C||vk==0xF2){
    if(!modeKeysDown.Add(vk))return true;
    // While a word is being typed in Latin letters, the convert key only returns to kana.
    if(mode==InputMode.Kana&&composer.Composing&&composer.LatinMode){composer.EndLatin();return true;}
    SetMode(InputMode.Kana,true,"key "+vk.ToString("X2"));return true;
   }
   if(vk==0x1D){if(modeKeysDown.Add(vk))SetMode(InputMode.Alnum,true,"key "+vk.ToString("X2"));return true;}
   if(modeKeysDown.Contains(ModeKey(vk)))return true;
   if(((vk==0xF3||vk==0xF4||vk==0x19)&&settings.ToggleWithHankaku&&!ctrl&&!alt&&!win)||(vk==0x20&&ctrl&&shift&&!alt&&!win&&settings.ToggleWithCtrlShiftSpace)){
    modeKeysDown.Add(ModeKey(vk));
    // JIS IME compatibility events can include an immediate release/press pair.
    // Debounce that pair as well as latching genuine repeated key-down events.
    double now=clock.Elapsed.TotalMilliseconds;
    if(now-lastToggleAt>60){lastToggleAt=now;SetMode(mode==InputMode.Kana?InputMode.Alnum:InputMode.Kana,true,"key "+vk.ToString("X2")+(ctrl?" Ctrl":"")+(shift?" Shift":""));}
    return true;
   }
   if(mode!=InputMode.Kana||secure)return false;
   KeyEvt e=new KeyEvt{Vk=vk,Scan=scan,Shift=shift,Ctrl=ctrl,Alt=alt,Win=win};
   if(!ctrl&&!alt&&!win)e.Char=Native.Translate(vk,scan,shift);
   else if(ctrl&&!alt&&!win)e.Base=Native.Translate(vk,scan,shift);
   keyAt=clock.Elapsed.TotalMilliseconds;
   return composer.Handle(e)==Decision.Swallow;
  }
  IntPtr OnMouse(int code,IntPtr wParam,IntPtr lParam){
   if(code>=0&&!quitting){
    int msg=wParam.ToInt32();
    if(msg==0x200)lastHookTick=(uint)Environment.TickCount;
    else{
     try{
      Native.MouseData m=(Native.MouseData)Marshal.PtrToStructure(lParam,typeof(Native.MouseData));
      lastHookTick=m.time;
      bool own=(long)m.extra.ToUInt64()==Native.OwnMark;
      if(!own&&(msg==0x201||msg==0x204||msg==0x207||msg==0x20B)){
       if(composer.Composing||composer.Awaiting){
        // The text has to arrive before the click moves the caret, so this click is dropped and sent again behind the text.
        // Send both events after the hook returns to prevent duplicate delivery.
        uint flag=msg==0x201?0x2u:msg==0x204?0x8u:msg==0x207?0x20u:0x80u,data=msg==0x20B?(m.mouseData>>16):0;
        invoker.BeginInvoke((Action)delegate{Guard(delegate{composer.CommitNow();if(inline.Busy)inline.Flush(250);Native.SendMouseDown(flag,data,Native.OwnMark);});});
        return new IntPtr(1);
       }
       if(inline.Busy)inline.Flush(250);
       composer.ResetContext();Passed(1);
      }else if(!own&&msg==0x20A&&composer.Composing)probe.Locate(Native.FocusWindow(),lastSession);
     }catch(Exception ex){errors++;Log("mouse error "+ex);}
    }
   }
   return Native.CallNextHookEx(mouseHook,code,wParam,lParam);
  }
  void OnWinEvent(IntPtr hook,uint evt,IntPtr hwnd,int idObject,int idChild,uint thread,uint time){
   if(quitting)return;
   Guard(delegate{
    if(evt==3){
     // Text shown in the overlay cannot be delivered to a window that is no longer in front, so it is dropped.
     // Text written into the document is already there: it simply becomes committed.
     if(composer.Composing||composer.Awaiting){if(inlineSession)composer.CommitNow();else composer.Abandon();}
     composer.ResetContext();
    }else if(inlineSession&&(composer.Composing||composer.Awaiting)&&Native.FocusWindow()!=inlineWindow)composer.CommitNow();
    IntPtr focus=Native.FocusWindow();
    // Some applications switch the system IME on whenever they come to the front (Word does). While this tool is on,
    // Japanese is typed through it, and alphanumeric mode must stay direct input.
    if(evt==3)probe.CloseIme(focus);
    bool edit=focus!=IntPtr.Zero&&Native.ClassOf(focus)=="Edit"&&(Native.GetWindowLongPtr(focus,-16).ToInt64()&0x20)!=0;
    if(edit&&!secure&&(composer.Composing||composer.Awaiting))composer.Abandon();
    secure=edit;
    if(!edit&&mode==InputMode.Kana)probe.CheckPassword(focus);   // other kinds of password box take a slower question
   });
  }

  // ---- mode ------------------------------------------------------------
  void SetMode(InputMode m,bool announce,string reason="command/menu"){
   if(m!=mode)Log("mode "+mode+" -> "+m+" ("+reason+")");
   if(m!=mode&&(composer.Composing||composer.Awaiting)){composer.CommitNow();if(inline.Busy)inline.Flush(250);}
   mode=m;
   UpdateTray();ui.Mode(m==InputMode.Kana);
   IntPtr focus=Native.FocusWindow();
   probe.CloseIme(focus);
   // Our own windows show the mode in their title row; the floating indicator would cover the field below the caret.
   if(announce&&Native.ProcessOf(Native.GetForegroundWindow())!=ownPid){
    CaretInfo c;IntPtr f;
    if(!Native.SystemCaret(out c,out f))c=new CaretInfo();
    ui.Flash(m==InputMode.Kana?"あ  LiveHenkan":"A  英数",c,900);
   }
  }
  // An emergency stop puts the document back as it was before the unfinished text; an ordinary stop keeps the text as typed.
  void Quit(){Quit(false);}
  void Quit(bool emergency){
   if(quitting)return;quitting=true;
   Log(emergency?"stop requested: emergency":"stop requested: command/menu");
   try{if(composer.Composing||composer.Awaiting){if(emergency)composer.Abandon();else composer.CommitNow();}}catch{}
   try{inline.Flush(800);}catch{}
   RemoveHooks();
   ui.CloseWindows();
   ui.Flash("LiveHenkan  オフ",new CaretInfo(),700);
   WinForms.Timer bye=new WinForms.Timer();bye.Interval=750;bye.Tick+=delegate{bye.Stop();WinForms.Application.ExitThread();};bye.Start();
  }
  bool started;
  void OpenWords(){
   ui.ShowWords(dictionary,delegate(bool on){try{invoker.BeginInvoke((Action)delegate{composer.DirectKana=on;});}catch{}});
  }
  void OpenSettings(){
   string footer="モデル xsmall.jinen（jinen-v2-xsmall、このパソコンの中だけで動作） ・ 計算スレッド "+engine.Threads+"\n登録した単語と設定の保存先: "+userDir;
   ui.ShowSettings(settings,dictionary,delegate{try{SettingsStore.Save(SettingsPath,settings);}catch(Exception ex){Log("settings "+ex.Message);}},footer);
  }
  void Execute(string command){
   invoker.BeginInvoke((Action)delegate{
    Guard(delegate{
     switch(command){
      case "on": SetMode(settings.StartInKana?InputMode.Kana:InputMode.Alnum,false);ui.Flash(settings.StartInKana?"LiveHenkan  オン":"LiveHenkan  オン（英数）",new CaretInfo(),1300);break;
      case "kana": SetMode(InputMode.Kana,true);break;
      case "word": if(!started){SetMode(settings.StartInKana?InputMode.Kana:InputMode.Alnum,false);}OpenWords();break;
      case "settings": if(!started){SetMode(settings.StartInKana?InputMode.Kana:InputMode.Alnum,false);}OpenSettings();break;
      case "alnum": SetMode(InputMode.Alnum,true);break;
      case "toggle": SetMode(mode==InputMode.Kana?InputMode.Alnum:InputMode.Kana,true);break;
      case "off": case "quit": Quit();break;
     }
     started=true;
    });
   });
  }

  // ---- tray ------------------------------------------------------------
  sealed class MenuColors : WinForms.ProfessionalColorTable {
   static System.Drawing.Color C(int rgb){return System.Drawing.Color.FromArgb(255,(rgb>>16)&255,(rgb>>8)&255,rgb&255);}
   public override System.Drawing.Color ToolStripDropDownBackground{get{return C(0x232427);}}
   public override System.Drawing.Color ImageMarginGradientBegin{get{return C(0x232427);}}
   public override System.Drawing.Color ImageMarginGradientMiddle{get{return C(0x232427);}}
   public override System.Drawing.Color ImageMarginGradientEnd{get{return C(0x232427);}}
   public override System.Drawing.Color MenuBorder{get{return C(0x3A3B40);}}
   public override System.Drawing.Color MenuItemBorder{get{return C(0x313236);}}
   public override System.Drawing.Color MenuItemSelected{get{return C(0x313236);}}
   public override System.Drawing.Color SeparatorDark{get{return C(0x3A3B40);}}
   public override System.Drawing.Color SeparatorLight{get{return C(0x232427);}}
  }
  // The tray menu in the same dark theme as the windows.
  sealed class MenuRenderer : WinForms.ToolStripProfessionalRenderer {
   public MenuRenderer():base(new MenuColors()){RoundedEdges=false;}
   protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e){e.TextColor=System.Drawing.Color.FromArgb(0xF2,0xF3,0xF4);base.OnRenderItemText(e);}
   protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e){
    System.Drawing.Rectangle r=e.ImageRectangle;e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    using(System.Drawing.Pen pen=new System.Drawing.Pen(System.Drawing.Color.FromArgb(0x3D,0x9A,0xFF),2f)){
     int cx=r.Left+r.Width/2,cy=r.Top+r.Height/2;
     e.Graphics.DrawLines(pen,new System.Drawing.Point[]{new System.Drawing.Point(cx-5,cy),new System.Drawing.Point(cx-1,cy+4),new System.Drawing.Point(cx+6,cy-4)});
    }
   }
  }
  void CreateTray(){
   tray=new WinForms.NotifyIcon();tray.Text="LiveHenkan";
   WinForms.ContextMenuStrip menu=new WinForms.ContextMenuStrip();
   menu.Renderer=new MenuRenderer();menu.Font=new System.Drawing.Font("Yu Gothic UI",10f);menu.ShowImageMargin=false;menu.ShowCheckMargin=true;
   menu.Items.Add("かな（LiveHenkan）",null,delegate{SetMode(InputMode.Kana,true);});
   menu.Items.Add("英数",null,delegate{SetMode(InputMode.Alnum,true);});
   menu.Items.Add(new WinForms.ToolStripSeparator());
   menu.Items.Add("単語を登録…",null,delegate{OpenWords();});
   menu.Items.Add("設定…",null,delegate{OpenSettings();});
   menu.Items.Add(new WinForms.ToolStripSeparator());
   menu.Items.Add("オフにする",null,delegate{Quit();});
   foreach(WinForms.ToolStripItem item in menu.Items)item.Padding=new WinForms.Padding(4,5,12,5);
   menu.Opening+=delegate{((WinForms.ToolStripMenuItem)menu.Items[0]).Checked=mode==InputMode.Kana;((WinForms.ToolStripMenuItem)menu.Items[1]).Checked=mode==InputMode.Alnum;};
   tray.ContextMenuStrip=menu;
   tray.MouseClick+=delegate(object s,WinForms.MouseEventArgs e){if(e.Button==WinForms.MouseButtons.Left)SetMode(mode==InputMode.Kana?InputMode.Alnum:InputMode.Kana,true);};
   UpdateTray();tray.Visible=true;
  }
  void UpdateTray(){
   if(tray==null)return;
   bool kana=mode==InputMode.Kana;
   using(System.Drawing.Bitmap bmp=new System.Drawing.Bitmap(32,32,System.Drawing.Imaging.PixelFormat.Format32bppArgb))
   using(System.Drawing.Graphics g=System.Drawing.Graphics.FromImage(bmp)){
    g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;g.Clear(System.Drawing.Color.Transparent);
    using(System.Drawing.Drawing2D.GraphicsPath path=new System.Drawing.Drawing2D.GraphicsPath()){
     int r=14;System.Drawing.Rectangle b=new System.Drawing.Rectangle(1,1,30,30);
     path.AddArc(b.X,b.Y,r,r,180,90);path.AddArc(b.Right-r,b.Y,r,r,270,90);path.AddArc(b.Right-r,b.Bottom-r,r,r,0,90);path.AddArc(b.X,b.Bottom-r,r,r,90,90);path.CloseFigure();
     using(System.Drawing.SolidBrush fill=new System.Drawing.SolidBrush(kana?System.Drawing.Color.FromArgb(0x3D,0x9A,0xFF):System.Drawing.Color.FromArgb(0x4B,0x4D,0x51)))g.FillPath(fill,path);
    }
    using(System.Drawing.Font font=new System.Drawing.Font("Yu Gothic UI",kana?19f:20f,System.Drawing.FontStyle.Bold,System.Drawing.GraphicsUnit.Pixel))
    using(System.Drawing.StringFormat center=new System.Drawing.StringFormat{Alignment=System.Drawing.StringAlignment.Center,LineAlignment=System.Drawing.StringAlignment.Center})
     g.DrawString(kana?"あ":"A",font,System.Drawing.Brushes.White,new System.Drawing.RectangleF(0,1,32,32),center);
    IntPtr icon=bmp.GetHicon();
    tray.Icon=System.Drawing.Icon.FromHandle(icon);
    if(trayIcon!=IntPtr.Zero)Native.DestroyIcon(trayIcon);
    trayIcon=icon;
   }
   tray.Text=kana?"LiveHenkan: かな":"LiveHenkan: 英数";
  }

  // ---- IHost -----------------------------------------------------------
  public void Commit(string text){
   if(inlineSession){
    // The text is in the document already; it only has to be marked as committed there.
    lock(inlineCommitted){string done;inlineCommitted.TryGetValue(lastSession,out done);inlineCommitted[lastSession]=(done??"")+text;}
    return;
   }
   Deliver(text);
   ui.Committed(text);reprobe=true;
  }
  public void Submit(Job job,bool cancelRunning){converter.Submit(job,cancelRunning);}
  public void SubmitCandidates(CandRequest request){converter.SubmitCandidates(request);}
  void StartOverlay(int session){
   lastCaret=new CaretInfo();probe.Start(Native.FocusWindow(),session);
  }
  public void ViewChanged(){
   View v=composer.Snapshot();
   if(v.Active&&v.Session!=lastSession){
    lastSession=v.Session;reprobe=false;
    IntPtr focus=Native.FocusWindow();
    if(settings.WriteIntoDocument&&InlineWriter.Supports(focus)){
     inlineSession=true;inlineWindow=focus;inlineAt=null;
     lock(inlineCommitted)inlineCommitted[v.Session]="";
     probe.CloseIme(focus);
     inline.Post(new InlineJob{Begin=true,Session=v.Session,Window=focus,DelayMs=(int)Math.Max(0,Math.Ceiling(passedAt-clock.Elapsed.TotalMilliseconds))});
    }else{inlineSession=false;StartOverlay(v.Session);}
   }else if(reprobe&&!inlineSession){reprobe=false;probe.AfterCommit(Native.FocusWindow(),lastSession,lastCaret);}
   lock(stamps)stamps[v.Serial]=new double[]{keyAt,inResult?1:0};
   if(inlineSession){
    string done;lock(inlineCommitted)inlineCommitted.TryGetValue(lastSession,out done);
    done=done??"";
    inline.Post(new InlineJob{Session=lastSession,Serial=v.Serial,Active=v.Active,Full=done+v.Text,CommitLen=done.Length,View=v});
    if(!v.Active)inlineSession=false;
   }else ui.Show(v);
  }
  // The editor could not be written to (a selection is in the way, or it has no text object model): use the overlay.
  void OnInlineFallback(int session,string why){
   try{invoker.BeginInvoke((Action)delegate{Guard(delegate{
    string done;lock(inlineCommitted){inlineCommitted.TryGetValue(session,out done);inlineCommitted.Remove(session);}
    if(!string.IsNullOrEmpty(done))Deliver(done);
    if(inlineSession&&session==lastSession){
     inlineSession=false;
     View v=composer.Snapshot();
     if(v.Active){StartOverlay(v.Session);ui.Show(v);}
    }
   });});}catch{}
  }
  // The document stopped matching what was written. Nothing more is touched; what is there stays.
  void OnInlineBroken(int session,string why){
   Log("writing into the document stopped: "+why);
   try{invoker.BeginInvoke((Action)delegate{Guard(delegate{
    lock(inlineCommitted)inlineCommitted.Remove(session);
    if(inlineSession&&session==lastSession){inlineSession=false;composer.Abandon();}
    ui.EndInline();
    ui.Flash("文書が変わったので入力を止めました",new CaretInfo(),1600);
   });});}catch{}
  }
  void OnInlineRendered(InlineJob job,InlineGeometry geometry){
   if(job.View!=null)ui.Inline(job.View,geometry);
   if(geometry!=null&&geometry.Ok){LineBox last=geometry.Text[geometry.Text.Count-1];inlineAt="document:"+last.Right+","+(last.Bottom-geometry.LineHeight)+","+geometry.LineHeight;}
   if(!job.Active)lock(inlineCommitted)inlineCommitted.Remove(job.Session);
  }
  public void Await(int milliseconds){awaitTimer.Stop();if(milliseconds>0){awaitTimer.Interval=milliseconds;awaitTimer.Start();}}
  public void Inject(KeyEvt key){
   if(inline.Busy)inline.Flush(250);   // the key acts on the document, which must be finished first
   Native.SendKey(key.Vk,key.Shift,key.Ctrl,key.Alt,Native.OwnMark);Passed(key.Ctrl||key.Alt?15:1);
  }
  // Finished text goes to the application as typed characters. A rich edit box takes it as one edit instead, in step
  // with whatever is written into it next, preserving character order.
  void Deliver(string text){
   IntPtr focus=Native.FocusWindow();
   if(InlineWriter.Supports(focus))inline.Post(new InlineJob{Direct=true,Window=focus,Full=text,DelayMs=(int)Math.Max(0,Math.Ceiling(passedAt-clock.Elapsed.TotalMilliseconds))});
   else{Native.SendText(text,Native.OwnMark);Passed(text.Length);}
  }
  // An editor takes a few milliseconds for each character it is sent.
  void Passed(int characters){
   double due=clock.Elapsed.TotalMilliseconds+Math.Min(400,30+8*characters);
   if(due>passedAt)passedAt=due;
  }

  void OnWorkerResult(ChunkResult r){
   if(r.Error!=null){Log("convert error "+r.Error);return;}
   if(r.Id<0||quitting)return;
   try{invoker.BeginInvoke((Action)delegate{inResult=true;try{Guard(delegate{composer.OnResult(r);});}finally{inResult=false;}});}catch{}
  }
  void OnWorkerCandidates(CandResult r){
   if(quitting)return;
   try{invoker.BeginInvoke((Action)delegate{inResult=true;try{Guard(delegate{composer.OnCandidates(r);});}finally{inResult=false;}});}catch{}
  }
  void OnRendered(long serial){
   double now=clock.Elapsed.TotalMilliseconds;
   lock(stamps){
    double[] s;
    if(stamps.TryGetValue(serial,out s)){(s[1]>0?convertedMs:shownMs).Add(now-s[0]);if(shownMs.Count>4000)shownMs.RemoveRange(0,2000);if(convertedMs.Count>4000)convertedMs.RemoveRange(0,2000);}
    List<long> old=new List<long>();foreach(long k in stamps.Keys)if(k<=serial)old.Add(k);
    foreach(long k in old)stamps.Remove(k);
   }
  }

  // ---- commands from the launcher and the tests -------------------------
  void PipeLoop(){
   while(!quitting){
    try{
     using(NamedPipeServerStream server=new NamedPipeServerStream(PipeName,PipeDirection.InOut,1)){
      server.WaitForConnection();
      StreamReader reader=new StreamReader(server,Encoding.UTF8);StreamWriter writer=new StreamWriter(server,new UTF8Encoding(false));
      string line=reader.ReadLine();
      writer.WriteLine(line==null?"":Answer(line.Trim()));writer.Flush();
      try{server.WaitForPipeDrain();}catch{}
     }
    }catch{if(!quitting)Thread.Sleep(200);}
   }
  }
  static string Join(List<double> list){
   StringBuilder b=new StringBuilder();
   foreach(double d in list){if(b.Length>0)b.Append(',');b.Append(d.ToString("F1",System.Globalization.CultureInfo.InvariantCulture));}
   return b.ToString();
  }
  string Answer(string command){
   switch(command){
    case "ping": return "ok";
    case "display": return testMode?ui.Inspect():"test mode required";
    case "test-recoverable-error":
     if(!testMode)return "test mode required";
     invoker.Invoke((Action)delegate{Guard(delegate{throw new InvalidOperationException("test recovery");});});return "ok";
    case "reset": invoker.Invoke((Action)delegate{composer.Abandon();composer.ResetContext();});return "ok";
    case "state":{
     string result="";
     invoker.Invoke((Action)delegate{View v=composer.Snapshot();result="mode="+mode+"\tcomposing="+v.Active+"\tcommits="+composer.Commits+"\tconversions="+composer.Conversions+"\trehooks="+rehooks+"\terrors="+errors+"\tsecure="+secure+"\tsurface="+(inlineSession?"inline":"overlay")+"\tinlineWrites="+inline.Writes+"\tinlineSlowestMs="+inline.SlowestMilliseconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+"\tinlineWriteMs="+(inline.Writes>0?inline.TotalMilliseconds/inline.Writes:0).ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+"\tinlineLocateMs="+(inline.Writes>0?inline.LocateMilliseconds/inline.Writes:0).ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+"\tkeys="+keysSeen+"\thookMaxMs="+hookMaxMs.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+"\thookWaitMaxMs="+hookWaitMaxMs+"\ttestKeys="+testKeysSeen+"\tcaret="+(inlineSession&&inlineAt!=null?inlineAt:lastCaret.Found?lastCaret.Source+":"+lastCaret.Left+","+lastCaret.Top+","+lastCaret.Height:"none")+"\ttext="+v.Text;});
     return result;
    }
    case "metrics":{
     lock(stamps){string s="shown="+Join(shownMs)+"\tconverted="+Join(convertedMs)+"\tthreads="+engine.Threads+"\tlastConvert="+converter.LastMilliseconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture);shownMs.Clear();convertedMs.Clear();return s;}
    }
    default: Execute(command);return "ok";
   }
  }
 }
}
