// Drives Composer and Converter without windows or hooks.
using System;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace LiveHenkan.Tests {
 public sealed class Headless : IHost, IDisposable {
  public readonly Composer Composer; public readonly Converter Converter; public readonly Jinen.Engine Engine; public readonly Settings Settings=new Settings();
  public readonly StringBuilder Document=new StringBuilder(); public readonly List<string> Commits=new List<string>(),Views=new List<string>(),Injected=new List<string>();
  public readonly List<double> KeyToView=new List<double>(),KeyToConverted=new List<double>(),ConvertMs=new List<double>();
  readonly ConcurrentQueue<ChunkResult> results=new ConcurrentQueue<ChunkResult>(); readonly ConcurrentQueue<CandResult> candidates=new ConcurrentQueue<CandResult>();
  public readonly UserDictionary Dictionary; public readonly List<double> CandidateMs=new List<double>();
  readonly Stopwatch clock=Stopwatch.StartNew(); double awaitUntil=-1,lastKeyAt; bool waitingConversion;
  public Headless(string model,string romajiTable,int threads,string userFolder){
   Engine=new Jinen.Engine(model,threads);
   Dictionary=new UserDictionary(userFolder);
   Converter=new Converter(Engine,Dictionary,delegate(ChunkResult r){results.Enqueue(r);},delegate(CandResult r){candidates.Enqueue(r);});
   Composer=new Composer(this,Settings,new RomajiTable(romajiTable),Dictionary);
   Composer.SymbolMap=Symbols.Map;
  }
  public void SubmitCandidates(CandRequest request){Converter.SubmitCandidates(request);}
  public View View{get{return Composer.Snapshot();}}
  public void Dispose(){Converter.Dispose();Engine.Dispose();}
  public void Commit(string text){Commits.Add(text);Document.Append(text);}
  public void Submit(Job job,bool cancelRunning){waitingConversion=true;Converter.Submit(job,cancelRunning);}
  public void ViewChanged(){Views.Add(Composer.Snapshot().Text);}
  public void Await(int milliseconds){awaitUntil=milliseconds>0?clock.Elapsed.TotalMilliseconds+milliseconds:-1;}
  public void Inject(KeyEvt key){Injected.Add("vk"+key.Vk);if(key.Vk==13)Document.Append('\n');if(key.Vk==8&&Document.Length>0)Document.Length--;}
  public string Text{get{return Composer.Snapshot().Text;}}
  public void Pump(){
   ChunkResult r;
   while(results.TryDequeue(out r)){
    if(r.Error!=null)throw new InvalidOperationException(r.Error);
    ConvertMs.Add(r.Milliseconds);
    Composer.OnResult(r);
    if(waitingConversion){KeyToConverted.Add(clock.Elapsed.TotalMilliseconds-lastKeyAt);}
   }
   CandResult c;
   while(candidates.TryDequeue(out c)){CandidateMs.Add(c.Milliseconds);Composer.OnCandidates(c);}
   if(awaitUntil>=0&&clock.Elapsed.TotalMilliseconds>=awaitUntil){awaitUntil=-1;Composer.OnTimeout();}
  }
  public void Wait(int milliseconds){double end=clock.Elapsed.TotalMilliseconds+milliseconds;while(clock.Elapsed.TotalMilliseconds<end){Pump();Thread.Sleep(1);}}
  // Waits until no conversion is running or queued and results are applied.
  public void Settle(int limitMs){
   double end=clock.Elapsed.TotalMilliseconds+limitMs;int quiet=0;int seen=Converter.Completed+Converter.Cancelled;
   while(clock.Elapsed.TotalMilliseconds<end){
    Pump();Thread.Sleep(2);
    int now=Converter.Completed+Converter.Cancelled;
    if(now==seen&&results.IsEmpty&&candidates.IsEmpty)quiet++;else{quiet=0;seen=now;}
    if(quiet>40)break;
   }
   waitingConversion=false;
  }
  public Decision Ctrl(char letter){Pump();return Composer.Handle(new KeyEvt{Vk=char.ToUpperInvariant(letter),Ctrl=true,Base=letter});}
  public Decision Key(int vk,bool shift){
   Pump();lastKeyAt=clock.Elapsed.TotalMilliseconds;
   Decision d=Composer.Handle(new KeyEvt{Vk=vk,Shift=shift});
   if(d==Decision.Pass&&vk==13)Document.Append('\n');
   if(d==Decision.Pass&&vk==8&&Document.Length>0)Document.Length--;
   return d;
  }
  public void Char(char c){
   Pump();lastKeyAt=clock.Elapsed.TotalMilliseconds;
   Composer.Handle(new KeyEvt{Vk=0,Char=c});
  }
  public void Type(string keys,int intervalMs){
   foreach(char c in keys){
    if(c=='\n')Key(13,false);else if(c=='\b')Key(8,false);else if(c==' ')Key(32,false);else if(c=='\u001b')Key(27,false);else Char(c);
    if(intervalMs>0)Wait(intervalMs);
   }
  }
 }
}
