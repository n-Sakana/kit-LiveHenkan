// Composition state and the conversion worker. No Win32 here, so everything in this file runs headless in tests.
using System;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;

namespace LiveHenkan {
 public sealed class Settings {
  public bool FullWidthSpace=true;        // Space with nothing typed: full-width, as on macOS and MS-IME
  public bool FullWidthSymbols=true;      // ( ) : ; etc. typed in kana mode
  public bool FullWidthDigits=false;
  public bool WesternPunctuation=false;   // ，． instead of 、。
  public bool CommitAtSentenceEnd=true;   // the sentence before is committed once the next one is started
  public bool WriteIntoDocument=true;     // editors that can take it show the text being typed in the document itself
  public bool Learn=true;                 // remember candidates the user picked
  public bool ToggleWithHankaku=true;
  public bool ToggleWithCtrlShiftSpace=false; // opt in: Office and editors already use this shortcut
  public bool StartInKana=true;
  public int LiveChars=28,KeepChars=16;   // an open chunk longer than LiveChars is cut down to about KeepChars
  public int HoldChars=24;                // closed text beyond this is committed from the front
  public int AwaitMs=450;                 // longest wait for the final conversion on Enter
  public int Threads=0;                   // 0: chosen from the processor count
  public bool HoldMemory=true;            // keep the model and code in physical memory so the first key after a pause is not slow
 }

 public static class Symbols {
  // Full-width form for an ASCII symbol typed in kana mode; null leaves the character as typed.
  public static string Map(char c){
   if(c=='\\')return "￥";
   if(c>='!'&&c<='~'&&!char.IsLetterOrDigit(c))return ((char)(c+0xFEE0)).ToString();
   return null;
  }
 }

 public sealed class KeyEvt {
  public int Vk,Scan; public bool Shift,Ctrl,Alt,Win;
  public char Char;   // what the key types in kana mode, or '\0'
  public char Base;   // what the key would type without Ctrl; set for Ctrl combinations
 }
 public enum Decision { Pass, Swallow }

 public sealed class Chunk {
  public int Id; public List<Unit> Units=new List<Unit>(); public string Reading=""; public bool Closed;
  public string ConvReading,ConvContext,Surface; public Alignment Align;
  public bool Literal{get{foreach(char c in Reading)if(Kana.IsKanaLike(c))return false;return true;}}
  public bool Current(string context){return Literal||(Surface!=null&&ConvReading==Reading&&ConvContext==context);}
  public void Recount(){StringBuilder b=new StringBuilder();foreach(Unit u in Units)b.Append(u.Text);Reading=b.ToString();}
 }

 public sealed class Cand {
  public string Text,Note; public bool Learnable=true;
  public Cand(string text,string note){Text=text;Note=note;}
 }
 // One stretch of the text while the user is choosing between conversions.
 public sealed class Seg {
  public List<Unit> Units=new List<Unit>(); public string Reading="",Surface="";
  public string Original;         // what live conversion showed; null when the stretch was re-cut by hand
  public string Head="",TailText="";  // candidates are asked for Head only; TailText (a particle) is put back behind each
  public List<Cand> Cands; public int Selected; public bool Loading,Picked;
  public int VariantKind=-1,VariantStep;
  public int LanguageChoice=-1;
  public void Recount(){StringBuilder b=new StringBuilder();foreach(Unit u in Units)b.Append(u.Text);Reading=b.ToString();}
  public string Raw{get{StringBuilder b=new StringBuilder();foreach(Unit u in Units)b.Append(u.Raw);return b.ToString();}}
  public string RawHead{get{StringBuilder b=new StringBuilder();int n=0;foreach(Unit u in Units){if(n>=Head.Length)break;b.Append(u.Raw);n+=u.Text.Length;}return b.ToString();}}
 }

 public sealed class ChunkRequest { public int Id; public string Reading,KnownSurface,KnownContext; }
 public sealed class Job { public int Version; public string ContextLeft; public List<ChunkRequest> Chunks=new List<ChunkRequest>(); }
 public sealed class ChunkResult { public int Version,Id; public string Reading,Context,Surface,Error; public Alignment Align; public double Milliseconds; }
 public sealed class CandRequest { public int Version; public string Context,Reading; public int Count=9; }
 public sealed class CandResult { public int Version; public string Reading,Context; public List<string> Texts; public double Milliseconds; }

 public sealed class View {
  public bool Active; public string Text=""; public long Serial;
  public int FocusStart=-1,FocusLength;   // the stretch being chosen, as an offset into Text
  public List<Cand> Cands; public int Selected; // candidate list, shown when not null
  public int Session;          // counts compositions, so a late caret report is matched to the right one
  public int CommittedChars;   // characters committed from the front since the composition started
 }

 public interface IHost {
  void Commit(string text);
  void Submit(Job job,bool cancelRunning);
  void SubmitCandidates(CandRequest request);
  void ViewChanged();
  void Await(int milliseconds);   // 0 stops the timer
  void Inject(KeyEvt key);
 }

 public sealed class Composer {
  public const int ContextChars=40,PageSize=9;
  const int Hira=0,Kata=1,HalfKata=2,FullLatin=3,HalfLatin=4;
  readonly IHost host; readonly Settings settings; readonly Romaji roma; readonly UserDictionary dict; readonly MixedInput mixed;
  readonly List<Chunk> chunks=new List<Chunk>();
  readonly List<KeyEvt> deferred=new List<KeyEvt>();
  List<Seg> segs; int focus,candVersion,pendingAdvance,tailId=-1; bool listOpen;
  string contextLeft=""; bool kanaOnly,awaiting,awaitCommit,candWaiting,replaying,forcing; KeyEvt resumeKey;
  int nextId=1,version,session; long serial; int committedChars;
  public int Commits,Conversions;
  public Func<char,string> SymbolMap;
  public bool DirectKana;   // conversion is off: the reading box of the word window wants the kana itself
  bool Plain{get{return kanaOnly||DirectKana;}}

  public Composer(IHost h,Settings s,RomajiTable table,UserDictionary dictionary){host=h;settings=s;roma=new Romaji(table);dict=dictionary;mixed=new MixedInput(table,dictionary==null?null:dictionary.Languages);roma.Mixed=mixed;}
  Chunk Open{get{return chunks.Count>0&&!chunks[chunks.Count-1].Closed?chunks[chunks.Count-1]:null;}}
  public bool Composing{get{return chunks.Count>0||segs!=null;}}
  public bool Selecting{get{return segs!=null;}}
  public bool Awaiting{get{return awaiting;}}
  public bool LatinMode{get{return roma.Latin;}}
  public string ContextLeft{get{return contextLeft;}}
  public static string Tail(string s,int n){return s.Length<=n?s:s.Substring(s.Length-n);}

  // ---- display ---------------------------------------------------------
  string Display(Chunk c){
   if(Plain||c.Surface==null||c.Literal)return c.Reading;
   if(c.ConvReading==c.Reading)return c.Surface;
   int p=0,max=Math.Min(c.Reading.Length,c.ConvReading.Length);
   while(p<max&&c.Reading[p]==c.ConvReading[p])p++;
   if(p==c.ConvReading.Length)return c.Surface+c.Reading.Substring(p);
   if(c.Align==null)return c.Reading;
   int sl,ru;c.Align.PrefixWithin(p,out sl,out ru);
   return c.Surface.Substring(0,sl)+c.Reading.Substring(ru);
  }
  public string DisplayText(){
   StringBuilder b=new StringBuilder();
   if(segs!=null){foreach(Seg s in segs)b.Append(s.Surface);return b.ToString();}
   foreach(Chunk c in chunks)b.Append(Display(c));
   b.Append(roma.Pending);
   return b.ToString();
  }
  public View Snapshot(){
   View v=new View{Active=Composing,Serial=serial,Session=session,CommittedChars=committedChars};
   if(!v.Active)return v;
   v.Text=DisplayText();
   if(segs!=null&&focus<segs.Count){
    int start=0;for(int i=0;i<focus;i++)start+=segs[i].Surface.Length;
    v.FocusStart=start;v.FocusLength=segs[focus].Surface.Length;
    if(listOpen&&segs[focus].Cands!=null){v.Cands=segs[focus].Cands;v.Selected=segs[focus].Selected;}
   }
   return v;
  }
  void Changed(){serial++;host.ViewChanged();}

  // ---- conversion requests --------------------------------------------
  void SyncOpen(){
   if(SymbolMap!=null)for(int i=0;i<roma.Units.Count;i++){Unit u=roma.Units[i];if(!u.English&&u.Text.Length==1){string m=SymbolMap(u.Text[0]);if(m!=null)roma.Units[i]=new Unit(m,u.Raw);}}
   Chunk o=Open;if(o!=null){o.Units=new List<Unit>(roma.Units);o.Recount();}
  }
  bool AllCurrent(){
   string ctx=contextLeft;
   foreach(Chunk c in chunks){if(!c.Current(Tail(ctx,ContextChars)))return false;ctx+=c.Literal?c.Reading:c.Surface;}
   return true;
  }
  void Request(bool urgent){
   if(Plain||chunks.Count==0||AllCurrent())return;
   Job job=new Job{Version=++version,ContextLeft=contextLeft};
   foreach(Chunk c in chunks){
    ChunkRequest r=new ChunkRequest{Id=c.Id,Reading=c.Reading};
    if(c.Literal)r.KnownSurface=c.Reading;
    else if(c.Surface!=null&&c.ConvReading==c.Reading){r.KnownSurface=c.Surface;r.KnownContext=c.ConvContext;}
    job.Chunks.Add(r);
   }
   host.Submit(job,urgent);
  }
  public void OnResult(ChunkResult r){
   Conversions++;
   if(segs!=null){OnTail(r);return;}
   Chunk c=chunks.Find(delegate(Chunk x){return x.Id==r.Id;});
   if(c==null||r.Surface==null||!c.Reading.StartsWith(r.Reading,StringComparison.Ordinal))return;
   // A result for a shorter reading is still worth showing, unless a longer one is already in place.
   bool newer=c.ConvReading==null||r.Reading.Length>=c.ConvReading.Length||!c.Reading.StartsWith(c.ConvReading,StringComparison.Ordinal);
   if(!newer)return;
   c.ConvReading=r.Reading;c.ConvContext=r.Context;c.Surface=r.Surface;c.Align=r.Align;
   Settle();
   if(awaiting&&AllCurrent())FinishAwait();
   else Changed();
  }

  // Cuts an over-long open chunk at a segment boundary and commits closed text that is ready.
  void Settle(){
   Chunk o=Open;
   if(o!=null&&!Plain&&roma.Pending.Length==0&&o.Reading.Length>settings.LiveChars&&o.Surface!=null&&o.ConvReading==o.Reading&&o.Align!=null&&o.Align.Ok){
    List<int> starts=o.Align.SegmentStarts();int cut=0;
    foreach(int s in starts)if(s>0&&o.Reading.Length-s>=settings.KeepChars&&UnitBoundary(o.Units,s))cut=s;
    if(cut==0)foreach(int s in starts)if(s>0&&o.Reading.Length-s>=4&&UnitBoundary(o.Units,s)){cut=s;break;}
    if(cut>0)Split(o,cut);
   }
   Overflow();
  }
  static bool UnitBoundary(List<Unit> units,int offset){int n=0;foreach(Unit u in units){if(n==offset)return true;n+=u.Text.Length;}return n==offset;}
  void Split(Chunk o,int cut){
   Chunk head=new Chunk{Id=nextId++,Closed=true};int n=0,count=0;
   foreach(Unit u in o.Units){if(n>=cut)break;head.Units.Add(u);n+=u.Text.Length;count++;}
   head.Recount();
   string headSurface=o.Align.SurfaceFor(0,cut),tailSurface=o.Align.SurfaceFor(cut,o.Reading.Length);
   head.ConvReading=head.Reading;head.ConvContext=o.ConvContext;head.Surface=headSurface;head.Align=Alignment.Build(head.Reading,headSurface);
   roma.Units.RemoveRange(0,count);SyncOpen();
   o.ConvReading=o.Reading;o.Surface=tailSurface;o.Align=Alignment.Build(o.Reading,tailSurface);
   o.ConvContext=Tail((head.ConvContext??"")+headSurface,ContextChars);
   chunks.Insert(chunks.Count-1,head);
  }
  void Overflow(){
   if(Plain||awaiting||segs!=null)return;
   while(chunks.Count>1){
    Chunk first=chunks[0];
    if(!first.Closed||!first.Current(Tail(contextLeft,ContextChars)))return;
    int held=0;for(int i=0;i<chunks.Count-1;i++)held+=Display(chunks[i]).Length;
    bool sentence=settings.CommitAtSentenceEnd&&first.Reading.Length>0&&Kana.IsSentenceEnd(first.Reading[first.Reading.Length-1]);
    if(!sentence&&held<=settings.HoldChars)return;
    string text=Display(first);
    chunks.RemoveAt(0);
    host.Commit(text);Commits++;committedChars+=text.Length;
    contextLeft=Tail(contextLeft+text,ContextChars*2);
   }
  }

  // ---- commit ----------------------------------------------------------
  void Clear(){chunks.Clear();roma.Clear();segs=null;listOpen=false;pendingAdvance=0;candWaiting=false;tailId=-1;kanaOnly=false;committedChars=0;}
  void CommitAll(){
   if(segs!=null){
    // Picking another candidate from the list is a correction worth remembering.
    if(settings.Learn&&dict!=null)foreach(Seg s in segs){
     if(s.LanguageChoice>=0)dict.Languages.Learn(s.RawHead,s.LanguageChoice==1);
     if(!s.Picked||s.Original==null||s.Surface==s.Original)continue;
     int t=s.TailText.Length;
     // When only the word in front of a particle was changed, the word alone is the lesson.
     if(t>0&&s.Surface.EndsWith(s.TailText,StringComparison.Ordinal)&&s.Original.EndsWith(s.TailText,StringComparison.Ordinal))dict.Learn(s.Head,s.Original.Substring(0,s.Original.Length-t),s.Surface.Substring(0,s.Surface.Length-t));
     else dict.Learn(s.Reading,s.Original,s.Surface);
    }
   }else{roma.Flush();SyncOpen();}
   string text=DisplayText();
   Clear();version++;candVersion++;
   if(text.Length>0){host.Commit(text);Commits++;contextLeft=Tail(contextLeft+text,ContextChars*2);}
   Changed();
  }
  void BeginAwait(bool commit,KeyEvt resume){awaiting=true;awaitCommit=commit;resumeKey=resume;host.Await(settings.AwaitMs);}
  void FinishAwait(){
   awaiting=false;host.Await(0);
   if(awaitCommit)CommitAll();
   else if(resumeKey!=null){
    // The key that was waiting for the conversion is handled now, with whatever has arrived.
    KeyEvt e=resumeKey;resumeKey=null;forcing=true;
    try{if(Handle(e)==Decision.Pass)host.Inject(e);}finally{forcing=false;}
   }
   Replay();
  }
  public void OnTimeout(){
   if(awaiting){FinishAwait();return;}
   if(candWaiting&&segs!=null&&focus<segs.Count){
    // The candidates did not come in time: go on with what is known without the model.
    candWaiting=false;Seg s=segs[focus];s.Loading=false;
    if(pendingAdvance!=0){int d=pendingAdvance;pendingAdvance=0;Move(d);}
    if(tailId==-2)RequestTail();
    Changed();
   }
  }
  void Replay(){
   if(replaying)return;replaying=true;
   try{
    while(deferred.Count>0&&!awaiting){
     KeyEvt e=deferred[0];deferred.RemoveAt(0);
     if(Handle(e)==Decision.Pass)host.Inject(e);
    }
   }finally{replaying=false;}
  }
  // Used when the text must be in the application before the key that follows.
  public void CommitNow(){
   for(int guard=0;guard<4;guard++){
    if(awaiting){awaiting=false;resumeKey=null;host.Await(0);}
    if(Composing)CommitAll();
    if(deferred.Count==0)break;
    Replay();   // keys typed while waiting belong before whatever comes next
   }
  }
  public void Abandon(){if(!Composing&&deferred.Count==0&&!awaiting)return;awaiting=false;resumeKey=null;host.Await(0);deferred.Clear();Clear();version++;candVersion++;Changed();}
  public void ResetContext(){contextLeft="";}
  // The convert key ends a run of Latin letters started with Shift, without ending the composition.
  public void EndLatin(){roma.Latin=false;}

  // ---- keys ------------------------------------------------------------
  const int ActNone=0,ActBack=1,ActEnter=2,ActNext=3,ActPrev=4,ActEscape=5,ActLeft=6,ActRight=7,ActShrink=8,ActExtend=9,ActPageDown=10,ActPageUp=11,ActVariant=20;
  static int ActionOf(KeyEvt e){
   if(e.Alt||e.Win)return ActNone;
   if(e.Ctrl){
    if(e.Shift)return ActNone;
    switch(e.Vk){
     case 'H': return ActBack; case 'M': return ActEnter; case 'N': return ActNext; case 'P': return ActPrev;
     case 'B': return ActLeft; case 'F': return ActRight; case 'I': return ActShrink; case 'O': return ActExtend;
     case 'J': return ActVariant+Hira; case 'K': return ActVariant+Kata; case 'L': return ActVariant+FullLatin;
    }
    if(e.Base==';')return ActVariant+HalfKata;
    if(e.Base==':')return ActVariant+HalfLatin;
    return ActNone;
   }
   switch(e.Vk){
    case 8: return ActBack; case 13: return ActEnter; case 27: return ActEscape;
    case 32: return e.Shift?ActPrev:ActNext;
    case 40: return ActNext; case 38: return ActPrev;
    case 37: return e.Shift?ActShrink:ActLeft; case 39: return e.Shift?ActExtend:ActRight;
    case 34: return ActPageDown; case 33: return ActPageUp;
    case 0x75: return ActVariant+Hira; case 0x76: return ActVariant+Kata; case 0x77: return ActVariant+HalfKata; case 0x78: return ActVariant+FullLatin; case 0x79: return ActVariant+HalfLatin;
   }
   return ActNone;
  }
  public Decision Handle(KeyEvt e){
   if(awaiting){deferred.Add(e);return Decision.Swallow;}
   bool mods=e.Ctrl||e.Alt||e.Win;
   if(segs!=null)return HandleSelecting(e,mods);
   if(!mods&&!e.Shift&&e.Vk==32&&Composing&&roma.Latin&&!DirectKana)return OnChar(' ');
   if(!mods&&e.Char!='\0'&&e.Vk!=32)return OnChar(e.Char);
   if(!Composing){
    if(e.Vk==32&&!mods&&!e.Shift&&settings.FullWidthSpace){host.Commit("　");contextLeft=Tail(contextLeft+"　",ContextChars*2);return Decision.Swallow;}
    // Keys that move the caret make the remembered left context unreliable.
    if(e.Vk==8&&!mods){if(contextLeft.Length>0)contextLeft=contextLeft.Substring(0,contextLeft.Length-1);}
    else if(e.Vk==13||e.Vk==9||(e.Vk>=33&&e.Vk<=40)||e.Vk==46||mods)contextLeft="";
    return Decision.Pass;
   }
   int action=ActionOf(e);
   switch(action){
    case ActBack: return OnBackspace();
    case ActEnter: return OnCommitKey();
    case ActEscape: return OnEscape();
    case ActNext: case ActPrev: case ActLeft: case ActRight: case ActShrink: case ActExtend:
     if(!Ready(e))return Decision.Swallow;
     BuildSegments();
     if(action==ActNext)Advance(1);else if(action==ActPrev)Advance(-1);
     else if(action==ActLeft)focus=Math.Max(0,segs.Count-2);
     else if(action==ActRight)focus=0;
     else if(action==ActShrink)Resize(-1);
     Changed();
     return Decision.Swallow;
   }
   if(action>=ActVariant){
    roma.Flush();SyncOpen();BuildSegments();
    ApplyVariant(segs[focus],action-ActVariant);
    Changed();
    return Decision.Swallow;
   }
   // Anything else acts on the document, so the text goes in first and the key is sent again behind it.
   // (A key that is merely let through would reach the application before the text.)
   CommitNow();host.Inject(e);
   return Decision.Swallow;
  }
  // True when every chunk shows its final conversion. Otherwise the key waits for it and is handled again.
  bool Ready(KeyEvt e){
   roma.Flush();SyncOpen();
   if(Plain||forcing||AllCurrent())return true;
   Request(true);
   BeginAwait(false,e);Changed();
   return false;
  }
  Decision OnChar(char c){
   roma.Mixed=DirectKana?null:mixed;
   if(!Composing){chunks.Add(new Chunk{Id=nextId++});committedChars=0;session++;}
   kanaOnly=false;
   Chunk o=Open;
   if(o==null){o=new Chunk{Id=nextId++};chunks.Add(o);}
   // A new sentence starts its own chunk, so the one before stops changing.
   if(roma.Pending.Length==0&&o.Reading.Length>0&&Kana.IsSentenceEnd(o.Reading[o.Reading.Length-1])&&!IsCloser(c)){
    o.Closed=true;roma.Clear();o=new Chunk{Id=nextId++};chunks.Add(o);
   }
   roma.Push(c);
   SyncOpen();
   Overflow();
   Request(false);
   Changed();
   return Decision.Swallow;
  }
  static bool IsCloser(char c){return c==']'||c==')'||c=='}'||c=='"'||c=='\''||c=='.'||c=='!'||c=='?';}
  Decision OnBackspace(){
   if(roma.IsEmpty&&chunks.Count>1){
    // Step back into the chunk before.
    chunks.RemoveAt(chunks.Count-1);
    Chunk prev=chunks[chunks.Count-1];prev.Closed=false;roma.Clear();roma.Units.AddRange(prev.Units);
   }
   roma.Backspace();SyncOpen();
   if(roma.IsEmpty&&chunks.Count>1){chunks.RemoveAt(chunks.Count-1);Chunk prev=chunks[chunks.Count-1];prev.Closed=false;roma.Clear();roma.Units.AddRange(prev.Units);}
   else if(roma.IsEmpty){Clear();version++;Changed();return Decision.Swallow;}
   Request(true);
   Changed();
   return Decision.Swallow;
  }
  Decision OnCommitKey(){
   roma.Flush();SyncOpen();
   if(Plain||AllCurrent()){CommitAll();return Decision.Swallow;}
   Request(true);
   BeginAwait(true,null);Changed();
   return Decision.Swallow;
  }
  Decision OnEscape(){
   if(!Plain){kanaOnly=true;version++;Changed();return Decision.Swallow;}
   Clear();version++;Changed();
   return Decision.Swallow;
  }

  // ---- choosing between conversions -------------------------------------
  Decision HandleSelecting(KeyEvt e,bool mods){
   Seg s=segs[focus];
   if(!mods&&e.Char!='\0'&&e.Vk!=32){
    if(listOpen&&s.Cands!=null&&e.Char>='1'&&e.Char<='9'){
     int index=s.Selected/PageSize*PageSize+(e.Char-'1');
     if(index<s.Cands.Count){Pick(s,index);listOpen=false;Changed();}
     return Decision.Swallow;
    }
    // Typing on confirms what is shown and starts the next stretch.
    CommitAll();
    return OnChar(e.Char);
   }
   int action=ActionOf(e);
   if(e.Vk==9&&!mods)action=e.Shift?ActPrev:ActNext;
   switch(action){
    case ActNext: Advance(1);break;
    case ActPrev: Advance(-1);break;
    case ActPageDown: Advance(PageSize);break;
    case ActPageUp: Advance(-PageSize);break;
    case ActLeft: MoveFocus(-1);break;
    case ActRight: MoveFocus(1);break;
    case ActShrink: Resize(-1);break;
    case ActExtend: Resize(1);break;
    case ActEnter: CommitAll();return Decision.Swallow;
    case ActEscape: case ActBack: BackToLive();break;
    default:
     if(action>=ActVariant){ApplyVariant(s,action-ActVariant);listOpen=false;break;}
     CommitNow();host.Inject(e);
     return Decision.Swallow;
   }
   Changed();
   return Decision.Swallow;
  }
  static Seg Slice(List<Unit> units,int from,int to,string surface){
   Seg s=new Seg();int n=0;
   foreach(Unit u in units){if(n>=from&&n<to)s.Units.Add(u);n+=u.Text.Length;}
   s.Recount();s.Surface=surface;s.Original=surface;s.Head=s.Reading;
   return s;
  }
  static void Segment(List<Seg> list,List<Unit> units,string reading,string surface,Alignment align){
   if(align==null&&reading==surface&&units.Exists(delegate(Unit u){return u.English;}))align=Alignment.Build(reading,surface);
   if(align==null||!align.Ok||align.Reading!=reading){list.Add(Slice(units,0,reading.Length,surface));return;}
   List<int> cuts=new List<int>();
   foreach(int s in align.SegmentStarts())if(UnitBoundary(units,s))cuts.Add(s);
   int at=0;Unit previous=null;
   foreach(Unit u in units){
    if(previous!=null&&previous.English&&u.English&&previous.Text.EndsWith(" ",StringComparison.Ordinal)&&u.Text!=" "&&!cuts.Contains(at))cuts.Add(at);
    at+=u.Text.Length;previous=u;
   }
   cuts.Sort();
   cuts.Add(reading.Length);
   for(int i=0;i+1<cuts.Count;i++){
    if(cuts[i+1]<=cuts[i])continue;
    Seg s=Slice(units,cuts[i],cuts[i+1],align.SurfaceFor(cuts[i],cuts[i+1]));
    // 天気|ですね: other conversions are wanted for 天気, not for the ending.
    int head=align.HeadEnd(cuts[i],cuts[i+1]);
    if(s.Units.Exists(delegate(Unit u){return u.English;}))while(head>cuts[i]&&reading[head-1]==' ')head--;
    // A katakana word may align like plain kana (アピ|を); its following particle still belongs to the tail.
    if(head==cuts[i+1]&&i+2<cuts.Count&&head-cuts[i]>2){
     char last=reading[head-1];
     if("をはがにのとへ".IndexOf(last)>=0&&Kana.Fold(s.Surface[s.Surface.Length-1])==last)head--;
    }
    if(head>cuts[i]&&head<cuts[i+1]){s.Head=reading.Substring(cuts[i],head-cuts[i]);s.TailText=align.SurfaceFor(head,cuts[i+1]);}
    list.Add(s);
   }
  }
  void BuildSegments(){
   List<Seg> list=new List<Seg>();
   foreach(Chunk c in chunks){
    if(c.Reading.Length==0)continue;
    bool converted=!Plain&&!c.Literal&&c.Surface!=null&&c.ConvReading==c.Reading;
    Segment(list,c.Units,c.Reading,converted?c.Surface:Display(c),converted?c.Align:null);
   }
   if(list.Count==0)list.Add(new Seg());
   chunks.Clear();roma.Clear();kanaOnly=false;
   segs=list;focus=list.Count-1;listOpen=false;pendingAdvance=0;
  }
  void MergeAll(){
   Seg all=new Seg();StringBuilder b=new StringBuilder();
   foreach(Seg s in segs){all.Units.AddRange(s.Units);b.Append(s.Surface);}
   all.Recount();all.Head=all.Reading;all.Surface=b.ToString();all.Original=null;
   segs=new List<Seg>{all};focus=0;
  }
  string LeftContext(){
   StringBuilder b=new StringBuilder(contextLeft);
   for(int i=0;i<focus;i++)b.Append(segs[i].Surface);
   return Tail(b.ToString(),ContextChars);
  }
  static void AddCand(List<Cand> list,string text,string note){
   if(string.IsNullOrEmpty(text))return;
   foreach(Cand c in list)if(c.Text==text){if(c.Note.Length==0&&note=="登録語")c.Note=note;return;}
   // Width forms are picked for one spot, not as the way a word should be written from now on.
   list.Add(new Cand(text,note){Learnable=note!="半角カナ"&&note!="全角英数"&&note!="半角英数"&&note!="日付"});
  }
  // What can be offered without the model: the text shown now, the user's words, and the kana and Latin forms.
  List<Cand> Compose(Seg s,List<string> model){
   List<Cand> list=new List<Cand>();
   if(s.Original!=null)AddCand(list,s.Original,"");
   if(dict!=null){
    List<Entry> found=dict.Find(s.Reading);if(found!=null)foreach(Entry e in found)AddCand(list,e.Surface,e.Learned?"":"登録語");
    if(s.TailText.Length>0){found=dict.Find(s.Head);if(found!=null)foreach(Entry e in found)AddCand(list,e.Surface+s.TailText,e.Learned?"":"登録語");}
   }
   if(model!=null)foreach(string t in model)AddCand(list,t+s.TailText,"");
   foreach(string d in Dates(s.Head))AddCand(list,d+s.TailText,"日付");
   bool kana=false;foreach(char c in s.Reading)if(Kana.IsKanaLike(c)){kana=true;break;}
   if(kana){AddCand(list,Kana.ToHiragana(s.Reading),"ひらがな");AddCand(list,Kana.ToKatakana(s.Reading),"カタカナ");AddCand(list,Kana.ToHalfKatakana(s.Reading),"半角カナ");}
   string raw=s.Raw;
   if(raw.Length>0){AddCand(list,Kana.ToFullWidth(raw),"全角英数");AddCand(list,raw,"半角英数");}
   return list;
  }
  public static Func<DateTime> Now=delegate{return DateTime.Now;};
  // きょう, あした and the like can be turned into the date itself, as on macOS.
  static List<string> Dates(string reading){
   List<string> list=new List<string>();int days=0;bool day=true;
   switch(reading){
    case "きょう": break; case "あした": case "あす": days=1;break; case "あさって": days=2;break;
    case "きのう": days=-1;break; case "おととい": days=-2;break;
    case "いま":{DateTime t=Now();list.Add(t.ToString("H:mm"));list.Add(t.Hour+"時"+t.Minute+"分");return list;}
    case "ことし": case "らいねん": case "きょねん":{int y=Now().Year+(reading=="らいねん"?1:reading=="きょねん"?-1:0);list.Add(y+"年");if(y>=2019)list.Add("令和"+(y-2018==1?"元":(y-2018).ToString())+"年");return list;}
    default: day=false;break;
   }
   if(!day)return list;
   DateTime d=Now().Date.AddDays(days);string week="日月火水木金土".Substring((int)d.DayOfWeek,1);
   list.Add(d.ToString("yyyy/MM/dd"));
   list.Add(d.Year+"年"+d.Month+"月"+d.Day+"日");
   list.Add(d.Month+"月"+d.Day+"日（"+week+"）");
   if(d.Year>=2019)list.Add("令和"+(d.Year-2018==1?"元":(d.Year-2018).ToString())+"年"+d.Month+"月"+d.Day+"日");
   return list;
  }
  void Load(Seg s){
   if(s.Cands!=null||s.Loading)return;
   s.Loading=true;candWaiting=true;
   host.SubmitCandidates(new CandRequest{Version=++candVersion,Context=LeftContext(),Reading=s.Head});
   host.Await(900);
  }
  public void OnCandidates(CandResult r){
   if(segs==null||r.Version!=candVersion||focus>=segs.Count)return;
   Seg s=segs[focus];
   if(s.Head!=r.Reading)return;
   candWaiting=false;host.Await(0);
   string current=s.Cands!=null&&s.Selected<s.Cands.Count?s.Cands[s.Selected].Text:null;
   s.Cands=Compose(s,r.Texts);s.Loading=false;s.Selected=0;
   if(current!=null)for(int i=0;i<s.Cands.Count;i++)if(s.Cands[i].Text==current){s.Selected=i;break;}
   // A stretch that was re-cut by hand has no conversion yet: take the first one.
   if(s.Original==null&&s.VariantKind<0&&current==null&&s.Cands.Count>0)s.Surface=s.Cands[0].Text;
   if(pendingAdvance!=0){int d=pendingAdvance;pendingAdvance=0;Move(d);}
   if(tailId==-2)RequestTail();
   Changed();
  }
  void Pick(Seg s,int index){
   s.Selected=index;s.Surface=s.Cands[index].Text;s.Picked=s.Cands[index].Learnable&&(index!=0||s.Original==null);s.VariantKind=-1;s.LanguageChoice=-1;
  }
  void Move(int delta){
   Seg s=segs[focus];
   if(s.Cands==null||s.Cands.Count==0)s.Cands=Compose(s,null);
   int n=s.Cands.Count;if(n==0)return;
   int index=Math.Abs(delta)>=PageSize?Math.Max(0,Math.Min(n-1,s.Selected+delta)):((s.Selected+delta)%n+n)%n;
   Pick(s,index);listOpen=true;
  }
  void Advance(int delta){
   Seg s=segs[focus];
   if(s.Cands==null||s.Loading){pendingAdvance+=delta;Load(s);return;}
   Move(delta);
  }
  void MoveFocus(int delta){
   int next=Math.Max(0,Math.Min(segs.Count-1,focus+delta));
   if(next==focus)return;
   focus=next;listOpen=false;pendingAdvance=0;
  }
  // Moves the border between the focused stretch and the text after it by one typed unit, then converts both again.
  void Resize(int delta){
   Seg f=segs[focus];
   if(delta<0){
    if(f.Units.Count<=1)return;
    Unit u=f.Units[f.Units.Count-1];f.Units.RemoveAt(f.Units.Count-1);
    if(focus+1>=segs.Count)segs.Add(new Seg());
    segs[focus+1].Units.Insert(0,u);
   }else{
    if(focus+1>=segs.Count)return;
    Seg n=segs[focus+1];Unit u=n.Units[0];n.Units.RemoveAt(0);f.Units.Add(u);
    if(n.Units.Count==0)segs.RemoveAt(focus+1);
   }
   // Everything after the focus becomes one stretch again; it is cut anew once it has been converted.
   if(focus+1<segs.Count){
    Seg tail=new Seg();
    for(int i=focus+1;i<segs.Count;i++)tail.Units.AddRange(segs[i].Units);
    tail.Recount();tail.Head=tail.Reading;tail.Surface=tail.Reading;tail.Original=null;
    segs.RemoveRange(focus+1,segs.Count-focus-1);segs.Add(tail);
   }
   f.Recount();f.Head=f.Reading;f.TailText="";f.Surface=f.Reading;f.Original=null;f.Cands=null;f.Loading=false;f.Picked=false;f.VariantKind=-1;f.LanguageChoice=-1;f.Selected=0;
   listOpen=false;pendingAdvance=0;tailId=focus+1<segs.Count?-2:-1;
   Load(f);
  }
  void RequestTail(){
   if(focus+1>=segs.Count){tailId=-1;return;}
   Seg tail=segs[focus+1];
   tailId=nextId++;
   StringBuilder ctx=new StringBuilder(contextLeft);
   for(int i=0;i<=focus;i++)ctx.Append(segs[i].Surface);
   Job job=new Job{Version=++version,ContextLeft=ctx.ToString()};
   job.Chunks.Add(new ChunkRequest{Id=tailId,Reading=tail.Reading});
   host.Submit(job,true);
  }
  void OnTail(ChunkResult r){
   if(r.Id!=tailId||focus+1>=segs.Count||r.Surface==null)return;
   Seg tail=segs[focus+1];
   if(tail.Reading!=r.Reading)return;
   List<Seg> cut=new List<Seg>();
   Segment(cut,tail.Units,tail.Reading,r.Surface,r.Align);
   segs.RemoveAt(focus+1);segs.AddRange(cut);tailId=-1;
   Changed();
  }
  void BackToLive(){
   Chunk c=new Chunk{Id=nextId++};StringBuilder shown=new StringBuilder();
   foreach(Seg s in segs){c.Units.AddRange(s.Units);shown.Append(s.Surface);}
   c.Recount();
   // Keep showing the text until the fresh conversion arrives; the odd context makes sure one is asked for.
   c.Surface=shown.ToString();c.ConvReading=c.Reading;c.ConvContext="\u0001";
   segs=null;listOpen=false;pendingAdvance=0;candWaiting=false;tailId=-1;candVersion++;host.Await(0);
   chunks.Add(c);roma.Clear();roma.Units.AddRange(c.Units);
   Request(false);
  }
  static string Capitalize(string s){return s.Length==0?s:char.ToUpperInvariant(s[0])+s.Substring(1);}
  void ApplyVariant(Seg s,int kind){
   // Pressing the same key again steps through lower case, upper case and capitalised for the Latin forms.
   s.VariantStep=s.VariantKind==kind?(s.VariantStep+1)%3:0;s.VariantKind=kind;
   string text;
   if(kind==Hira){
    if(s.Units.Exists(delegate(Unit u){return u.English;})){
     Romaji r=new Romaji(mixed.Table);foreach(char c in s.RawHead.ToLowerInvariant())r.Push(c);r.Flush();text=r.Reading+s.TailText;
    }else text=Kana.ToHiragana(s.Reading);
   }
   else if(kind==Kata)text=Kana.ToKatakana(s.Reading);
   else if(kind==HalfKata)text=Kana.ToHalfKatakana(s.Reading);
   else{
    string raw=s.RawHead;
    raw=s.VariantStep==1?raw.ToUpperInvariant():s.VariantStep==2?Capitalize(raw.ToLowerInvariant()):raw;
    text=(kind==FullLatin?Kana.ToFullWidth(raw):raw)+s.TailText;
   }
   if(text.Length>0)s.Surface=text;
   s.Picked=false;
   s.LanguageChoice=kind==Hira?0:kind==HalfLatin?1:-1;
  }
 }

 // Runs conversion jobs on one thread. The newest job replaces any job that is waiting.
 public sealed class Converter : IDisposable {
  readonly Jinen.Engine engine; readonly Action<ChunkResult> deliver; readonly Action<CandResult> deliverCands; readonly UserDictionary dict;
  readonly Thread thread; readonly AutoResetEvent signal=new AutoResetEvent(false);
  readonly object gate=new object(); Job waiting; CandRequest waitingCands; volatile bool stop; volatile int newest;
  readonly Dictionary<string,string> standalone=new Dictionary<string,string>(StringComparer.Ordinal); int standaloneRevision=-1;
  // Other conversions are cached on demand. Pauses in typing must not start a beam search.
  readonly Dictionary<string,List<string>> candidateCache=new Dictionary<string,List<string>>(StringComparer.Ordinal);
  public double LastMilliseconds,LastCandidateMilliseconds; public int Completed,Cancelled;
  public Converter(Jinen.Engine e,UserDictionary dictionary,Action<ChunkResult> sink,Action<CandResult> candSink){
   engine=e;dict=dictionary;deliver=sink;deliverCands=candSink;
   thread=new Thread(Loop);thread.IsBackground=true;thread.Name="converter";thread.Start();
  }
  public void Submit(Job job,bool cancelRunning){
   lock(gate){waiting=job;newest=job.Version;}
   if(cancelRunning)engine.Cancel=true;
   signal.Set();
  }
  public void SubmitCandidates(CandRequest request){
   lock(gate)waitingCands=request;
   signal.Set();
  }
  public void Dispose(){stop=true;engine.Cancel=true;signal.Set();thread.Join(1500);}
  void Loop(){
   while(!stop){
    signal.WaitOne();
    while(!stop){
     Job job;CandRequest cands=null;
     lock(gate){job=waiting;waiting=null;if(job==null){cands=waitingCands;waitingCands=null;}}
     if(job==null&&cands==null)break;
     engine.Cancel=false;
     try{if(job!=null)Run(job);else RunCandidates(cands);}
     catch(Exception ex){deliver(new ChunkResult{Version=job!=null?job.Version:0,Id=-1,Reading="",Error=ex.ToString()});}
    }
   }
  }
  void Run(Job job){
   string ctx=job.ContextLeft??"";
   foreach(ChunkRequest c in job.Chunks){
    string context=Composer.Tail(ctx,Composer.ContextChars);
    if(c.Reading.Length==0)continue;
    if(c.KnownSurface!=null&&(c.KnownContext==null||c.KnownContext==context)){ctx+=c.KnownSurface;continue;}
    Stopwatch sw=Stopwatch.StartNew();
    Alignment align;
    string surface=ConvertAligned(context,c.Reading,out align);
    if(surface==null){Cancelled++;return;}
    surface=align.RestoreTyped();
    try{DictionaryApplier.Apply(dict,Standalone,c.Reading,ref surface,ref align);}catch(OperationCanceledException){Cancelled++;return;}
    sw.Stop();LastMilliseconds=sw.Elapsed.TotalMilliseconds;Completed++;
    deliver(new ChunkResult{Version=job.Version,Id=c.Id,Reading=c.Reading,Context=context,Surface=surface,Align=align,Milliseconds=LastMilliseconds});
    ctx+=surface;
    if(newest!=job.Version)return;   // a newer job is waiting and repeats whatever is still needed
   }
  }
  // The model's conversions of `reading` on its own, most likely first. Null when a typed key cut the search short.
  List<string> Alternatives(string reading){
   List<string> texts;
   if(candidateCache.TryGetValue(reading,out texts))return texts;
   texts=new List<string>();
   bool kana=false;foreach(char c in reading)if(Kana.IsKanaLike(c)){kana=true;break;}
   if(kana&&reading.Length<=40){
    engine.SelectCache(1);
    try{
     foreach(Jinen.Candidate c in engine.Candidates("",reading,12,80)){
      Alignment a=Alignment.Build(reading,c.Text);
      // Candidates that add characters the reading does not have are noise.
      bool extra=!a.Ok;foreach(Piece p in a.Pieces)if(p.R0==p.R1)extra=true;
      if(!extra)texts.Add(a.RestoreTyped());
     }
    }finally{engine.SelectCache(0);}
    if(engine.Cancel)return null;
   }
   if(candidateCache.Count>400)candidateCache.Clear();
   candidateCache[reading]=texts;
   return texts;
  }
  void RunCandidates(CandRequest r){
   Stopwatch sw=Stopwatch.StartNew();
   List<string> texts=Alternatives(r.Reading);
   sw.Stop();LastCandidateMilliseconds=sw.Elapsed.TotalMilliseconds;
   if(texts==null){lock(gate)if(waitingCands==null)waitingCands=r;return;}   // a typed key cut in; try again after it
   deliverCands(new CandResult{Version=r.Version,Reading=r.Reading,Context=r.Context,Texts=texts,Milliseconds=LastCandidateMilliseconds});
  }
  // The model's own conversion of a dictionary reading, used to see whether a sentence contains that word.
  string Standalone(string reading){
   if(dict!=null&&standaloneRevision!=dict.Revision){standalone.Clear();standaloneRevision=dict.Revision;}
   string known;
   if(standalone.TryGetValue(reading,out known))return known;
   Jinen.Result r;
   engine.SelectCache(1);
   try{r=engine.Convert("",reading,Math.Min(Jinen.Engine.MaxNewTokens,reading.Length+8));}finally{engine.SelectCache(0);}
   if(r.StopReason=="cancelled")throw new OperationCanceledException();
   known=Plausible(r,reading)?r.Text:null;
   standalone[reading]=known;
   return known;
  }
  static bool IsLiteral(char c){return (c>='0'&&c<='9')||Kana.IsAsciiLetter(c);}
  // Every run of digits or Latin letters that was typed must come back unchanged and in order.
  static bool LiteralsKept(string reading,string output){
   string r=reading.Normalize(NormalizationForm.FormKC),o=output.Normalize(NormalizationForm.FormKC);int from=0,p=0;
   while(p<r.Length){
    if(!IsLiteral(r[p])){p++;continue;}
    int q=p;bool digits=r[p]>='0'&&r[p]<='9';
    while(q<r.Length&&IsLiteral(r[q])&&((r[q]>='0'&&r[q]<='9')==digits))q++;
    string run=r.Substring(p,q-p);int at=from;
    while(true){
     at=o.IndexOf(run,at,StringComparison.Ordinal);
     if(at<0)return false;
     bool whole=!digits||((at==0||!(o[at-1]>='0'&&o[at-1]<='9'))&&(at+run.Length>=o.Length||!(o[at+run.Length]>='0'&&o[at+run.Length]<='9')));
     if(whole)break;
     at++;
    }
    from=at+run.Length;p=q;
   }
   return true;
  }
  static bool Plausible(Jinen.Result r,string reading){return r.StopReason=="eos"&&r.Text.Length>0&&r.Text.Length<=reading.Length*2+4;}
  string ConvertAligned(string context,string reading,out Alignment align){
   bool mixedReading=false;foreach(char c in reading)if(Kana.IsAsciiLetter(c)||c==' ')mixedReading=true;
   if(!mixedReading){string text=Convert(context,reading);align=text==null?null:Alignment.Build(reading,text);return text;}
   align=new Alignment{Reading=reading,Ok=true};StringBuilder b=new StringBuilder();
   for(int p=0;p<reading.Length;){
    bool literal=Kana.IsAsciiLetter(reading[p])||reading[p]==' ';int q=p+1;
    if(literal)while(q<reading.Length&&reading[q]<128)q++;
    else while(q<reading.Length&&!Kana.IsAsciiLetter(reading[q])&&reading[q]!=' ')q++;
    string part=reading.Substring(p,q-p);
    string text=literal?part:Convert(Composer.Tail(context+b,Composer.ContextChars),part);
    if(text==null){align=null;return null;}
    Alignment a=Alignment.Build(part,text);if(!a.Ok)align.Ok=false;
    int s0=b.Length;b.Append(text);
    foreach(Piece piece in a.Pieces)align.Pieces.Add(new Piece{R0=piece.R0+p,R1=piece.R1+p,S0=piece.S0+s0,S1=piece.S1+s0,Content=piece.Content,Literal=literal});
    p=q;
   }
   align.Surface=b.ToString();return align.Surface;
  }
  // Null when cancelled. Falls back to the kana when the model stops short or runs on.
  public string Convert(string context,string reading){
   bool hasKana=false,hasLiteral=false;
   foreach(char c in reading){if(Kana.IsKanaLike(c))hasKana=true;else if(IsLiteral(c))hasLiteral=true;}
   if(!hasKana)return reading;
   Jinen.Result r=engine.Convert(context,reading,Math.Min(Jinen.Engine.MaxNewTokens,reading.Length+8));
   if(r.StopReason=="cancelled")return null;
   if(!Plausible(r,reading))return reading;
   if(!hasLiteral||LiteralsKept(reading,r.Text))return r.Text;
   // The model changed a number or a Latin word: convert the kana runs on their own and keep the rest verbatim.
   StringBuilder result=new StringBuilder();int p=0;
   while(p<reading.Length){
    bool lit=IsLiteral(reading[p]);int q=p;
    while(q<reading.Length&&IsLiteral(reading[q])==lit)q++;
    string part=reading.Substring(p,q-p);
    if(lit)result.Append(part);
    else{
     Jinen.Result pr=engine.Convert(Composer.Tail(context+result,Composer.ContextChars),part,Math.Min(Jinen.Engine.MaxNewTokens,part.Length+8));
     if(pr.StopReason=="cancelled")return null;
     result.Append(Plausible(pr,part)?pr.Text:part);
    }
    p=q;
   }
   return result.ToString();
  }
 }
}
