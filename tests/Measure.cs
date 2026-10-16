// Measures engine speed, delay per keystroke, candidate delay and conversion accuracy. No windows.
using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;

namespace LiveHenkan.Tests {
 public sealed class Sentence { public string Reading; public string[] Accept; public string Got; public bool Ok; }
 public sealed class Timing { public string Name; public int N; public double Median,P95,Max; }
 public static class Measure {
  static double At(List<double> sorted,double p){return sorted.Count==0?0:Math.Round(sorted[Math.Min(sorted.Count-1,(int)Math.Floor(sorted.Count*p))],1);}
  public static Timing Summary(string name,IEnumerable<double> values){
   List<double> v=new List<double>(values);v.Sort();
   return new Timing{Name=name,N=v.Count,Median=At(v,0.5),P95=At(v,0.95),Max=At(v,1.0)};
  }
  // Milliseconds per model step, with and without the output layer.
  public static Timing[] Steps(string model,int threads){
   using(Jinen.Engine e=new Jinen.Engine(model,threads)){
    e.StepTimes(0,20,true);
    return new Timing[]{Summary("prompt step, "+threads+" threads",e.StepTimes(0,80,false)),Summary("output step, "+threads+" threads",e.StepTimes(0,80,true))};
   }
  }
  public static Sentence[] Load(string file){
   List<Sentence> list=new List<Sentence>();
   foreach(string line in File.ReadAllLines(file,Encoding.UTF8)){
    if(line.Length==0||line[0]=='#')continue;
    string[] p=line.Split('\t');if(p.Length<2)continue;
    list.Add(new Sentence{Reading=p[0],Accept=p[1].Split('|')});
   }
   return list.ToArray();
  }
  // Kana to the keys a person would press, so delays are counted per keystroke as they are in use.
  public static string Keys(RomajiTable table,string kana){
   Dictionary<string,string> back=new Dictionary<string,string>(StringComparer.Ordinal);
   foreach(KeyValuePair<string,string> rule in table.Rules){
    string old;
    if(rule.Key.Length==0||rule.Key[0]=='z'||rule.Key[0]=='x'||rule.Key[0]=='l'||rule.Key=="nn"||rule.Key=="n'")continue;
    if(!back.TryGetValue(rule.Value,out old)||rule.Key.Length<old.Length)back[rule.Value]=rule.Key;
   }
   StringBuilder b=new StringBuilder();
   for(int i=0;i<kana.Length;i++){
    char c=kana[i];string two=i+1<kana.Length?kana.Substring(i,2):null,key;
    if(c=='っ'&&i+1<kana.Length){string next=null;if(i+2<kana.Length&&back.TryGetValue(kana.Substring(i+1,2),out next)){}else back.TryGetValue(kana[i+1].ToString(),out next);if(next!=null){b.Append(next[0]);continue;}}
    if(c=='ん'){b.Append("nn");continue;}
    if(two!=null&&back.TryGetValue(two,out key)){b.Append(key);i++;continue;}
    if(back.TryGetValue(c.ToString(),out key)){b.Append(key);continue;}
    b.Append(c);
   }
   return b.ToString();
  }
  // Types every sentence as romaji with the given pause between keys and commits it. Context starts empty each time.
  public static Sentence[] Accuracy(string model,string table,string userFolder,int threads,Sentence[] sentences,int intervalMs,List<double> keyToConverted,List<double> convertMs){
   if(Directory.Exists(userFolder))Directory.Delete(userFolder,true);
   Directory.CreateDirectory(userFolder);
   RomajiTable rt=new RomajiTable(table);
   using(Headless h=new Headless(model,table,threads,userFolder)){
    h.Settings.Learn=false;
    foreach(Sentence s in sentences){
     h.Composer.ResetContext();h.Document.Length=0;
     h.Type(Keys(rt,s.Reading),intervalMs);h.Settle(6000);
     h.Key(13,false);h.Settle(3000);
     s.Got=h.Document.ToString();s.Ok=Array.IndexOf(s.Accept,s.Got)>=0;
    }
    if(keyToConverted!=null)keyToConverted.AddRange(h.KeyToConverted);
    if(convertMs!=null)convertMs.AddRange(h.ConvertMs);
   }
   return sentences;
  }
  // Delay from Space to a full candidate list, for words and for a word inside a sentence.
  public static Timing Candidates(string model,string table,string userFolder,int threads,string[] readings){
   if(Directory.Exists(userFolder))Directory.Delete(userFolder,true);
   Directory.CreateDirectory(userFolder);
   List<double> ms=new List<double>();RomajiTable rt=new RomajiTable(table);
   using(Headless h=new Headless(model,table,threads,userFolder)){
    h.Settings.Learn=false;
    foreach(string r in readings){
     h.Composer.ResetContext();
     h.Type(Keys(rt,r),30);h.Settle(4000);
     Stopwatch sw=Stopwatch.StartNew();
     h.Key(32,false);
     while(sw.ElapsedMilliseconds<5000&&h.View.Cands==null){h.Pump();System.Threading.Thread.Sleep(1);}
     ms.Add(sw.Elapsed.TotalMilliseconds);
     h.Key(27,false);h.Settle(800);h.Key(27,false);h.Key(27,false);h.Settle(300);
    }
   }
   return Summary("space to candidate list",ms);
  }
 }
}
