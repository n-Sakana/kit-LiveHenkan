using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;
namespace Henkan.Tests {
 public static class FormattingChecks {
  sealed class Host:IHost {
   public Composer Composer;public Settings Settings=new Settings();public UserDictionary Dictionary;
   public string Document="";public int CandidateRequests;
   public Host(string table,string folder){Directory.CreateDirectory(folder);Dictionary=new UserDictionary(folder);Composer=new Composer(this,Settings,new RomajiTable(table),Dictionary);Composer.SymbolMap=delegate(char c){if(c>='0'&&c<='9')return Settings.FullWidthDigits?((char)(c+0xFEE0)).ToString():null;return Settings.FullWidthSymbols?Symbols.Map(c):null;};}
   public void Commit(string text){Document+=text;}
   public void Submit(Job job,bool cancel){foreach(ChunkRequest c in job.Chunks)if(c.KnownSurface==null)Composer.OnResult(new ChunkResult{Id=c.Id,Version=job.Version,Reading=c.Reading,Surface=c.Reading,Context=job.ContextLeft,Align=Alignment.Build(c.Reading,c.Reading)});}
   public void SubmitCandidates(CandRequest r){CandidateRequests++;Composer.OnCandidates(new CandResult{Version=r.Version,Reading=r.Reading,Context=r.Context,Texts=new List<string>()});}
   public void ViewChanged(){} public void Await(int ms){} public void Inject(KeyEvt key){}
   public void Type(string s){foreach(char c in s)Composer.Handle(new KeyEvt{Char=c});}
   public void Key(int vk){Composer.Handle(new KeyEvt{Vk=vk});}
   public string Text{get{return Composer.DisplayText();}}
   public void Reset(){Composer.Abandon();Composer.ResetContext();Document="";CandidateRequests=0;}
  }
  public static CheckResult[] Run(string table,string folder){
   List<CheckResult> checks=new List<CheckResult>();
   Action<string,string,string> equal=delegate(string name,string expected,string actual){checks.Add(new CheckResult{Name=name,Ok=expected==actual,Detail="expected ["+expected+"] actual ["+actual+"]"});};
   Func<DateTime> now=Composer.Now;Composer.Now=delegate{return new DateTime(2026,10,17);};
   try{
    Host h=new Host(table,folder);
    h.Type("2026/10/17");equal("date shown before commit","2026/10/17（土）",h.Text);equal("document untouched before commit","",h.Document);
    h.Key(32);equal("one space restores original width and spelling","2026／10／17",h.Text);
    View v=h.Composer.Snapshot();equal("original is a named candidate","元の形",v.Cands[v.Selected].Note);
    equal("format selection needs no model candidate search","0",h.CandidateRequests.ToString());
    h.Key(13);equal("chosen original is committed exactly","2026／10／17",h.Document);equal("format reversal is not dictionary learning","0",h.Dictionary.LearnedCount.ToString());
    h.Reset();h.Type("2026/10/17");h.Key(13);equal("Enter commits the visible formatted date","2026/10/17（土）",h.Document);
    h.Type("abc");equal("later typing does not rewrite committed date","2026/10/17（土）",h.Document);
    h.Reset();h.Type("2026/10/17");h.Key(8);equal("backspace edits input rather than inserted weekday","2026/10/1（木）",h.Text);h.Type("8");equal("continued date is recomputed","2026/10/18（日）",h.Text);
    h.Key(27);equal("Escape removes presentation with conversion","2026／10／18",h.Text);h.Key(27);equal("second Escape cancels","",h.Document+h.Text);
    h.Reset();h.Type("1234.50円");equal("decimal stays uncommitted","",h.Document);equal("decimal quantity preview","1,234.50円",h.Text);h.Key(32);equal("decimal original remains available","1234．50円",h.Text);h.Key(13);equal("decimal original commit","1234．50円",h.Document);
    h.Reset();h.Type("1,234円");equal("numeric comma is not a prose comma","1,234円",h.Text);equal("grouped amount not committed early","",h.Document);
    h.Reset();h.Type("午後3時半");equal("time preview","15:30",h.Text);h.Key(32);equal("time original","午後3時半",h.Text);h.Key(13);equal("time original commit","午後3時半",h.Document);
    h.Reset();h.Type("3時台");equal("time band unchanged","3時台",h.Text);
    h.Reset();h.Type("ID:2026/10/17");equal("ID never gets weekday","ID:2026/10/17",h.Text);
    h.Reset();h.Type("今日は晴れです");equal("no prose punctuation inserted","今日は晴れです",h.Text);
    h.Reset();h.Settings.FormatNumbers=false;h.Type("2026/10/17");equal("formatting can be disabled","2026／10／17",h.Text);h.Settings.FormatNumbers=true;
    h.Reset();h.Composer.DirectKana=true;h.Type("2026/10/17");equal("dictionary reading is never formatted","2026・10・17",h.Text);h.Composer.DirectKana=false;
    h.Reset();h.Settings.FullWidthDigits=true;h.Type("1234円");equal("explicit digit-width preference takes priority","１２３４円",h.Text);h.Settings.FullWidthDigits=false;
    h.Reset();h.Type("2026/10/17");h.Key(32);h.Key(38);equal("formatted option can be picked again","2026/10/17（土）",h.Text);h.Key(13);equal("formatting selection not learned","0",h.Dictionary.LearnedCount.ToString());
    h.Reset();h.Type("予定は2026/10/17の午後3時です");equal("multiple edits in one composition","予定は2026/10/17（土）の15:00です",h.Text);h.Composer.CommitNow();equal("commit-now uses same preview","予定は2026/10/17（土）の15:00です",h.Document);
    h.Reset();h.Type("ID:");h.Key(13);h.Type("2026/10/17");equal("committed identifier prefix protects following value","2026／10／17",h.Text);
    h.Reset();h.Type("10月17日");Composer.Now=delegate{return new DateTime(2027,1,1);};equal("year does not change midway through composition","2026/10/17（土）",h.Text);
   }finally{Composer.Now=now;}
   return checks.ToArray();
  }
  // No model, windows or hooks: measures the incremental cost of presentation
  // while driving identical key sequences, including repeated view reads.
  public static double[] Measure(string table,string folder,bool enabled,int count){
   Host h=new Host(table,folder);h.Settings.FormatNumbers=enabled;
   string[] texts={"予定は2026/10/17の午後3時です","1234.50円","ID:2026/10/17","今日は晴れです"};
   List<double> samples=new List<double>();Stopwatch watch=new Stopwatch();
   for(int i=-100;i<count;i++){
    h.Reset();string text=texts[(i+100)%texts.Length];
    foreach(char c in text){watch.Restart();h.Composer.Handle(new KeyEvt{Char=c});h.Composer.Snapshot();h.Composer.Snapshot();watch.Stop();if(i>=0)samples.Add(watch.Elapsed.TotalMilliseconds);}
   }
   return samples.ToArray();
  }
 }
}
