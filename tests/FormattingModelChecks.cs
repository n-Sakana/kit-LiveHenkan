using System;
using System.IO;
using System.Collections.Generic;
namespace Henkan.Tests {
 public static class FormattingModelChecks {
  public static CheckResult[] Run(string model,string table,string folder) {
   List<CheckResult> results=new List<CheckResult>();
   Action<string,string,string> equal=delegate(string n,string e,string a){results.Add(new CheckResult{Name=n,Ok=e==a,Detail="expected ["+e+"] actual ["+a+"]"});};
   Func<DateTime> old=Composer.Now;Composer.Now=delegate{return new DateTime(2026,10,17);};
   Directory.CreateDirectory(folder);
   try{using(Headless h=new Headless(model,table,4,folder)){
    string[][] cases={
     new[]{"2026/10/17","2026/10/17（土）"},new[]{"1234enn","1,234円"},
     new[]{"gogo3jihann","15:30"},new[]{"9:05","09:05"},
     new[]{"hiyouha1234enndesu","費用は1,234円です"},
     new[]{"kaigiha2026/10/19nogogo3jikara","会議は2026/10/19（月）の15:00から"},
     new[]{"-1234.5enn","-1,234.5円"},new[]{"2026/10/17to2026/10/18","2026/10/17（土）と2026/10/18（日）"},
     new[]{"dai3kainokaigiha10gatsu5nichidesu","第3回の会議は10月5日（月）です"},
     new[]{"10/2","10/2（金）"},new[]{"11/22","11/22（日）"},new[]{"11・2","11/2（月）"},
     new[]{"11gatsu3nichi","11月3日（火）"},new[]{"2026nenn11gatsu3nichi","2026年11月3日（火）"},
     new[]{"2026/11/2","2026/11/2（月）"},new[]{"2026/11/3","2026/11/3（火）"},
     new[]{"sankahiha25000enndesu","参加費は25,000円です"}
    };
    foreach(string[] c in cases){
     h.Composer.Abandon();h.Composer.ResetContext();h.Document.Length=0;
     h.Type(c[0],20);h.Settle(4000);equal("preview "+c[0],c[1],h.Text);
     equal("uncommitted "+c[0],"",h.Document.ToString());
     h.Key(13,false);h.Wait(500);h.Settle(4000);equal("commit "+c[0],c[1],h.Document.ToString());
    }
    h.Composer.ResetContext();h.Document.Length=0;h.Type("yoteiha2026/10/17desu",20);h.Settle(4000);
    string before=h.Text;h.Key(39,false);h.Settle(4000);
    bool found=false;
    for(int i=0;i<12;i++){
     View v=h.View;
     if(v.FocusStart>=0&&v.Text.Substring(v.FocusStart,v.FocusLength).Contains("2026/10/17")){found=true;break;}
     int at=v.FocusStart;h.Key(39,false);if(h.View.FocusStart==at)break;
    }
    equal("date segment found","True",found.ToString());
    if(found){h.Key(32,false);h.Settle(4000);string original=h.Text;
     equal("only date formatting reversed",before.Replace("2026/10/17（土）","2026／10／17"),original);
     h.Wait(500);equal("late conversions keep original",original,h.Text);
     h.Key(13,false);h.Settle(4000);equal("sentence commits original",original,h.Document.ToString());
    }
    h.Composer.Abandon();h.Composer.ResetContext();h.Document.Length=0;
    h.Settings.LiveChars=18;h.Settings.KeepChars=10;h.Settings.HoldChars=12;
    h.Type("kyounokaigiha2026/10/17nogogo3jikarahajimarimasu.shiryouha12345enndeshita",25);h.Settle(4000);
    string pending=h.Document.ToString()+h.Text;string committed=h.Document.ToString();
    results.Add(new CheckResult{Name="overflow commits a prefix",Ok=committed.Length>0,Detail=committed});
    h.Key(13,false);h.Wait(500);h.Settle(4000);
    equal("overflow preserves preview",pending,h.Document.ToString());
    results.Add(new CheckResult{Name="overflow keeps formatted date and time intact",Ok=h.Document.ToString().Contains("2026/10/17（土）")&&h.Document.ToString().Contains("15:00"),Detail=h.Document.ToString()});
   }}finally{Composer.Now=old;}
   return results.ToArray();
  }
 }
}
