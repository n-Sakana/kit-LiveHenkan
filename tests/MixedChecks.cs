using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace Henkan.Tests {
 public static class MixedChecks {
  static readonly List<CheckResult> results=new List<CheckResult>();
  static void Equal(string name,string expected,string actual){results.Add(new CheckResult{Name=name,Ok=expected==actual,Detail="expected ["+expected+"] actual ["+actual+"]"});}
  static void Reset(Headless h){h.Composer.Abandon();h.Composer.ResetContext();h.Document.Length=0;h.Commits.Clear();}
  static void Type(Headless h,string text,int interval){h.Type(text,interval);h.Settle(4000);}
  static void Enter(Headless h){h.Key(13,false);h.Settle(4000);}
  public static CheckResult[] Run(string model,string table,string scratch,string cases,int threads){
   results.Clear();Directory.CreateDirectory(scratch);
   using(Headless h=new Headless(model,table,threads,scratch)){
    foreach(string line in File.ReadAllLines(cases,Encoding.UTF8)){
     string[] p=line.Split('\t');if(p.Length!=2)continue;
     Reset(h);Type(h,p[0],15);Enter(h);Equal(p[0],p[1],h.Document.ToString());
    }
    Reset(h);Type(h,"apiwohiraku",15);h.Key(37,false);h.Settle(4000);h.Key(0x79,false);Equal("F10 changes just the word","apiを開く",h.Text);Enter(h);
    Reset(h);Type(h,"apiwohiraku",0);Enter(h);Equal("F10 language is remembered","apiを開く",h.Document.ToString());
    UserDictionary reload=new UserDictionary(scratch);
    bool api;Dictionary<string,bool> saved=reload.Languages.Snapshot();
    Equal("language choice saved","True",saved.TryGetValue("api",out api)?api.ToString():"missing; saved keys: "+string.Join(",",saved.Keys));
    Reset(h);Type(h,"same",0);h.Key(0x79,false);Enter(h);
    Reset(h);Type(h,"same",0);Equal("ambiguous word can become English","same",h.Text);h.Key(0x75,false);Equal("F6 returns English to kana","さめ",h.Text);Enter(h);
    Reset(h);Type(h,"same",0);Enter(h);Equal("F6 language is remembered","サメ",h.Document.ToString());
    Reset(h);Type(h,"google",0);h.Key(8,false);Type(h,"e",0);Enter(h);Equal("backspace and retyping English","google",h.Document.ToString());
    Reset(h);Type(h,"kyo",0);h.Key(8,false);h.Key(0x75,false);Equal("backspace small kana","き",h.Text);h.Composer.Abandon();
    Reset(h);Type(h,"github",0);h.Key(27,false);h.Key(27,false);Equal("escape cancels mixed input","",h.Document.ToString()+h.Text);
    Reset(h);h.Type("github\ngoogle\n",0);h.Settle(4000);Equal("fast commit preserves order","githubgoogle",h.Document.ToString());
    Reset(h);h.Settings.Learn=false;Type(h,"mine",0);h.Key(0x79,false);Enter(h);Reset(h);Type(h,"mine",0);Enter(h);Equal("learning off","峰",h.Document.ToString());h.Settings.Learn=true;
    Reset(h);Type(h,"git mine",0);h.Key(0x75,false);Equal("F6 affects one English word","git みね",h.Text);h.Key(0x79,false);Equal("F10 preserves preceding English and its space","git mine",h.Text);Enter(h);
    Reset(h);h.Composer.DirectKana=true;Type(h,"google",0);h.Key(0x75,false);Equal("dictionary reading remains romaji","ごおgぇ",h.Text);h.Composer.Abandon();h.Composer.DirectKana=false;
   }
   return results.ToArray();
  }
 }
}
