// Headless diagnostics: no resident, windows, hooks or foreground input.
using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Reflection;
using System.Collections.Generic;
namespace Henkan.Tests {
 public sealed class LongSample {
  public int Keys,OpenChars,PendingChars,CommittedChars,Completed;
  public double KeyP95,KeyMax,ConversionP95,ConversionMax;
  public string Text;
  public bool Final,ExpectedText,CommitPreserved;
 }
 public sealed class LongCheck {public string Name,Detail;public bool Ok;}
 public static class LongCompositionChecks {
  const string Phrase="kyounokaiginoshiryouwojunbishitenainodeashitamadeniwakaruyounishiteokitaitoomoimasu";
  static double Percentile(List<double> times,double p){if(times.Count==0)return 0;double[] a=times.ToArray();Array.Sort(a);return a[Math.Min(a.Length-1,(int)(a.Length*p))];}
  sealed class DelayedHost:IHost {
   public Composer Composer;public Job Last;public string Document="";public Settings Settings=new Settings();public int Requests;
   public DelayedHost(string table){Composer=new Composer(this,Settings,new RomajiTable(table),null);}
   public void Commit(string text){Document+=text;}
   public void Submit(Job job,bool cancel){Last=job;Requests++;}
   public void SubmitCandidates(CandRequest r){}public void ViewChanged(){}public void Await(int ms){}public void Inject(KeyEvt e){}
   public void Type(string text){foreach(char c in text)Composer.Handle(new KeyEvt{Char=c});}
   public string Text{get{return Document+Composer.DisplayText();}}
  }
  public static LongCheck[] Checks(string table){
   List<LongCheck> checks=new List<LongCheck>();
   Action<string,bool,string> check=delegate(string n,bool ok,string d){checks.Add(new LongCheck{Name=n,Ok=ok,Detail=d});};
   string reading="きょうのかいぎのしりょうをじゅんびしてないのであしたまでにわかるようにしておきたいとおもいます";
   string surface="今日の会議の資料を準備してないので明日までにわかるようにしておきたいと思います";
   foreach(string suffix in new[]{"", "あたらしい", "k"}){
    DelayedHost h=new DelayedHost(table);h.Type(reading);Job old=h.Last;ChunkRequest original=old.Chunks[old.Chunks.Count-1];h.Type(suffix);
    Alignment a=Alignment.Build(reading,surface);
    ChunkResult result=new ChunkResult{Id=original.Id,Reading=reading,Context="",Surface=surface,Align=a};
    h.Composer.OnResult(result);
    string expected=surface+suffix;
    check("split preserves every character: "+suffix,h.Text==expected,h.Text);
    FieldInfo field=typeof(Composer).GetField("chunks",BindingFlags.NonPublic|BindingFlags.Instance);
    List<Chunk> chunks=(List<Chunk>)field.GetValue(h.Composer);Chunk open=chunks[chunks.Count-1];
    check("converted prefix is split even with pending keys: "+suffix,open.Reading.Length<reading.Length,open.Reading);
    check("only the converted tail is marked current: "+suffix,open.Reading==open.ConvReading+(suffix=="k"?"":suffix),open.ConvReading+" / "+open.Reading);
    check("split uses a fresh request identity: "+suffix,open.Id!=original.Id,open.Id.ToString());
    string before=h.Text;h.Composer.OnResult(result);check("late pre-split result cannot duplicate text: "+suffix,h.Text==before,h.Text);
    if(suffix=="あたらしい")check("new tail is queued without another key",h.Last.Chunks[h.Last.Chunks.Count-1].Id==open.Id&&h.Last.Chunks[h.Last.Chunks.Count-1].Reading==open.Reading,h.Last.Chunks[h.Last.Chunks.Count-1].Reading);
    h.Composer.CommitNow();check("commit preserves split preview: "+suffix,h.Document==expected,h.Document);
   }
   DelayedHost edited=new DelayedHost(table);edited.Type(reading);Job stale=edited.Last;edited.Composer.Handle(new KeyEvt{Vk=8});string shown=edited.Text;
   edited.Composer.OnResult(new ChunkResult{Id=stale.Chunks[0].Id,Reading=reading,Context="",Surface=surface,Align=Alignment.Build(reading,surface)});
   check("editing rejects a mismatched old prefix",edited.Text==shown,edited.Text);
   DelayedHost bad=new DelayedHost(table);bad.Type(reading);Alignment failed=Alignment.Build(reading,new string('漢',100));
   bad.Composer.OnResult(new ChunkResult{Id=bad.Last.Chunks[0].Id,Reading=reading,Context="",Surface=failed.Surface,Align=failed});
   check("uncertain alignment is not automatically committed",bad.Document.Length==0,bad.Document);
   DelayedHost repeated=new DelayedHost(table);StringBuilder rr=new StringBuilder(),ss=new StringBuilder();
   for(int i=0;i<8;i++){rr.Append("かいぎの");ss.Append("会議の");}
   repeated.Type(rr.ToString());Job earlier=repeated.Last;repeated.Type(rr.ToString()+rr);
   repeated.Composer.OnResult(new ChunkResult{Id=earlier.Chunks[0].Id,Reading=rr.ToString(),Context="",Surface=ss.ToString(),Align=Alignment.Build(rr.ToString(),ss.ToString())});
   string repeatedBefore=repeated.Text;
   repeated.Composer.OnResult(new ChunkResult{Id=earlier.Chunks[0].Id,Reading=rr.ToString(),Context="",Surface="古い別の結果",Align=Alignment.Build(rr.ToString(),"古い別の結果")});
   check("repeated reading cannot accept a pre-split result",repeated.Text==repeatedBefore,repeated.Text);
   DelayedHost awaiting=new DelayedHost(table);awaiting.Type(reading);Job beforeEnter=awaiting.Last;awaiting.Type("あたらしい");awaiting.Composer.Handle(new KeyEvt{Vk=13});
   awaiting.Composer.OnResult(new ChunkResult{Id=beforeEnter.Chunks[0].Id,Reading=reading,Context="",Surface=surface,Align=Alignment.Build(reading,surface)});
   check("Enter still waits for unconverted appended text",awaiting.Composer.Awaiting&&awaiting.Document.Length==0,awaiting.Text);
   Job tailJob=awaiting.Last;ChunkRequest tail=tailJob.Chunks[tailJob.Chunks.Count-1];string context="";
   for(int i=0;i<tailJob.Chunks.Count-1;i++)context+=tailJob.Chunks[i].KnownSurface;
   string tailSurface=tail.Reading.Replace("あたらしい","新しい");
   awaiting.Composer.OnResult(new ChunkResult{Id=tail.Id,Reading=tail.Reading,Context=Composer.Tail(context,Composer.ContextChars),Surface=tailSurface,Align=Alignment.Build(tail.Reading,tailSurface)});
   check("Enter finishes only after the appended conversion",!awaiting.Composer.Awaiting&&!awaiting.Composer.Composing&&awaiting.Document.EndsWith("新しい",StringComparison.Ordinal),awaiting.Document);
   return checks.ToArray();
  }
  public static LongSample[] Stream(string model,string table,string folder,int interval,int blocks){
   List<LongSample> rows=new List<LongSample>();
   FieldInfo field=typeof(Composer).GetField("chunks",BindingFlags.NonPublic|BindingFlags.Instance);
   using(Headless h=new Headless(model,table,4,folder)){
    // Warm the same model and parser before measuring.
    h.Type("kyouhaiitenkidesune",25);h.Wait(500);h.Composer.Abandon();h.Composer.ResetContext();h.Document.Clear();
    int keys=0;
    for(int block=0;block<blocks;block++){
     List<double> keyTimes=new List<double>();h.ConvertMs.Clear();int maxOpen=0,maxPending=0;
     foreach(char c in Phrase){
      Stopwatch sw=Stopwatch.StartNew();h.Char(c);keyTimes.Add(sw.Elapsed.TotalMilliseconds);keys++;
      foreach(Chunk chunk in (List<Chunk>)field.GetValue(h.Composer))maxOpen=Math.Max(maxOpen,chunk.Reading.Length);
      maxPending=Math.Max(maxPending,h.Text.Length);h.Wait(interval);
     }
     rows.Add(new LongSample{Keys=keys,OpenChars=maxOpen,PendingChars=maxPending,CommittedChars=h.Document.Length,Completed=h.Converter.Completed,KeyP95=Percentile(keyTimes,.95),KeyMax=Percentile(keyTimes,1),ConversionP95=Percentile(h.ConvertMs,.95),ConversionMax=Percentile(h.ConvertMs,1),Text=h.Document+h.Text});
    }
    h.Wait(2000);string final=h.Document+h.Text;StringBuilder expected=new StringBuilder();
    for(int i=0;i<blocks;i++)expected.Append("今日の会議の資料を準備してないので明日までにわかるようにしておきたいと思います");
    int committed=h.Document.Length,pending=h.Text.Length;h.Key(13,false);h.Wait(2000);
    // Both spellings are valid model conversions of wakaru. Keep every other
    // character and repetition exact; this is not a length-only quality check.
    rows.Add(new LongSample{Final=true,ExpectedText=final.Replace("分かる","わかる")==expected.ToString(),CommitPreserved=h.Document.ToString()==final,Keys=keys,CommittedChars=committed,PendingChars=pending,Text=final});
   }
   return rows.ToArray();
  }
  public static string[] ParserCases(string table,string fixtures,string folder){
   Directory.CreateDirectory(folder);LanguageMemory memory=new LanguageMemory(folder);memory.Learn("api",true);memory.Learn("same",false);memory.Learn("Google",false);
   RomajiTable t=new RomajiTable(table);List<string> inputs=new List<string>();
   foreach(string line in File.ReadAllLines(fixtures,Encoding.UTF8))inputs.Add(line.Split('\t')[0]);
   inputs.AddRange(new[]{Phrase,"GitHubnipushshita","NewNamewotsukau","XMLParser","ABCmade","google same api","2026/11/2（月）","git 123.45/path","can' t","n'nya","kkyann","＠★😀"});
   Random random=new Random(825);const string alphabet="abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ' -_/.0123";
   for(int i=0;i<100;i++){StringBuilder b=new StringBuilder();for(int j=0;j<random.Next(1,45);j++)b.Append(alphabet[random.Next(alphabet.Length)]);inputs.Add(b.ToString());}
   List<string> answers=new List<string>();
   foreach(LanguageMemory lessons in new[]{null,memory}){
    MixedInput parser=new MixedInput(t,lessons);
    foreach(string input in inputs)for(int n=0;n<=input.Length;n++)foreach(bool flush in new[]{false,true}){
     List<Unit> units=new List<Unit>();string pending;bool latin;parser.Parse(input.Substring(0,n),flush,units,out pending,out latin);
     StringBuilder b=new StringBuilder();b.Append(pending).Append('|').Append(latin);
     foreach(Unit u in units)b.Append('|').Append(u.Raw).Append(':').Append(u.Text).Append(':').Append(u.English);
     answers.Add(b.ToString());
    }
   }
   return answers.ToArray();
  }
  public static LongSample[] Parse(string table){
   RomajiTable t=new RomajiTable(table);MixedInput mixed=new MixedInput(t,null);List<LongSample> rows=new List<LongSample>();
   foreach(int n in new[]{40,160,640,1280}){
    StringBuilder b=new StringBuilder();while(b.Length<n)b.Append(Phrase);string raw=b.ToString(0,n);
    List<double> ms=new List<double>();List<Unit> units=new List<Unit>();string pending;bool latin;
    for(int i=0;i<25;i++){Stopwatch sw=Stopwatch.StartNew();mixed.Parse(raw,false,units,out pending,out latin);if(i>=5)ms.Add(sw.Elapsed.TotalMilliseconds);}
    rows.Add(new LongSample{Keys=n,KeyP95=Percentile(ms,.95),KeyMax=Percentile(ms,1)});
   }
   return rows.ToArray();
  }
 }
}
