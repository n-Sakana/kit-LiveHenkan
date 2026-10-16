// Checks of the input logic with the real model but no windows, hooks or typing into applications.
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace LiveHenkan.Tests {
 public sealed class CheckResult { public string Name,Detail; public bool Ok; }
 public static class CoreChecks {
  static List<CheckResult> results; static Headless h; static string modelPath,tablePath,userRoot; static int threads,userSeq;
  static void Check(string name,bool ok,string detail){results.Add(new CheckResult{Name=name,Ok=ok,Detail=detail});}
  static void Equal(string name,string expected,string actual){Check(name,expected==actual,expected==actual?actual:"expected ["+expected+"] got ["+actual+"]");}
  // Every scenario starts with an empty user folder, so learning in one cannot leak into another.
  static void Fresh(){
   if(h!=null)h.Dispose();
   string dir=Path.Combine(userRoot,"u"+(userSeq++));
   if(Directory.Exists(dir))Directory.Delete(dir,true);
   Directory.CreateDirectory(dir);
   h=new Headless(modelPath,tablePath,threads,dir);
  }
  static string Doc{get{return h.Document.ToString();}}
  static void Type(string keys){h.Type(keys,25);h.Settle(2500);}
  static void Enter(){h.Key(13,false);h.Settle(1500);}
  static void Space(){h.Key(32,false);h.Settle(2500);}
  static string Romaji(RomajiTable t,string keys,bool flush){
   Romaji r=new Romaji(t);foreach(char c in keys)r.Push(c);if(flush)r.Flush();
   return r.Reading+r.Pending;
  }

  public static CheckResult[] Run(string model,string romajiTable,string scratchFolder,int threadCount){
   results=new List<CheckResult>();modelPath=model;tablePath=romajiTable;userRoot=scratchFolder;threads=threadCount;userSeq=0;h=null;
   try{
    Text();
    Live();
    Selection();
    Dictionary();
   }catch(Exception ex){Check("unexpected exception",false,ex.ToString());}
   finally{if(h!=null)h.Dispose();h=null;}
   return results.ToArray();
  }

  static void Text(){
   RomajiTable t=new RomajiTable(tablePath);
   Equal("romaji basic","きょうは",Romaji(t,"kyouha",false));
   Equal("romaji nn","こんにちは",Romaji(t,"konnnichiha",false));
   Equal("romaji n before consonant","かんじ",Romaji(t,"kanji",false));
   Equal("romaji n pending","かn",Romaji(t,"kan",false));
   Equal("romaji n at end","かん",Romaji(t,"kan",true));
   Equal("romaji n apostrophe","かんい",Romaji(t,"kan'i",false));
   Equal("romaji small tsu","きって",Romaji(t,"kitte",false));
   Equal("romaji long mark","さーばー",Romaji(t,"sa-ba-",false));
   Equal("romaji punctuation","あ、い。",Romaji(t,"a,i.",false));
   Equal("romaji latin after shift","Windows",Romaji(t,"Windows",false));
   Equal("romaji latin keeps symbols","Ver.2",Romaji(t,"Ver.2",false));
   Equal("half-width katakana","ｶﾞｲﾄﾞ",Kana.ToHalfKatakana("がいど"));
   Equal("full-width latin","ＡＢｃ１",Kana.ToFullWidth("ABc1"));
   Alignment a=Alignment.Build("きょうはいいてんきですね","今日はいい天気ですね");
   Check("alignment ok",a.Ok,a.Pieces.Count+" pieces");
   Equal("alignment segment starts","0,4,6",string.Join(",",a.SegmentStarts()));
   Equal("word before a particle","9",a.HeadEnd(6,12).ToString());
   Equal("particle","1",Alignment.FunctionPrefix("はいい").ToString());
   Equal("copula","2",Alignment.FunctionPrefix("ですね").ToString());
   Equal("verb ending is not a particle","0",Alignment.FunctionPrefix("がる").ToString());
   Equal("okurigana is not a particle","0",Alignment.FunctionPrefix("べる").ToString());
   Equal("alignment surface for reading range","天気ですね",a.SurfaceFor(6,12));
   int sl,ru;a.PrefixWithin(7,out sl,out ru);
   Equal("alignment prefix inside a kanji reading","今日はいい|6",a.Surface.Substring(0,sl)+"|"+ru);
   Alignment sp=a.Splice(0,3,"本日");
   Equal("alignment splice","本日はいい天気ですね",sp.Surface);
   Equal("alignment splice keeps the rest","天気ですね",sp.SurfaceFor(6,12));
   Alignment bad=Alignment.Build("あいう","完全に別の文字列です");
   Check("alignment of unrelated text falls back",bad.Pieces.Count>=1,"ok="+bad.Ok);
   Alignment sym=Alignment.Build("（かっこ）","(カッコ)");
   Equal("typed width is restored","（カッコ）",sym.RestoreTyped());
  }

  static void Live(){
   Fresh();
   Type("kyouhaiitenkidesune");
   Equal("live conversion shown","今日はいい天気ですね",h.Text);
   Equal("nothing reaches the document before commit","",Doc);
   Enter();
   Equal("enter commits","今日はいい天気ですね",Doc);
   Check("composition ended",!h.Composer.Composing,"");

   Fresh();
   h.Type("shinseishowoteishutsusuru\n",15);h.Settle(2500);
   Equal("enter right after the last key commits the conversion","申請書を提出する",Doc);

   Fresh();
   h.Type("shinseishowoteishutsusuru\nkakuninshimashita",15);h.Settle(2500);Enter();
   Equal("keys typed while a commit waits keep their order","申請書を提出する確認しました",Doc);

   Fresh();
   Type("shinseishowoteishutsusuru");
   h.Key(8,false);
   Equal("backspace keeps the part that is still valid","申請書を提出す",h.Text);
   h.Key(8,false);h.Key(8,false);h.Settle(1500);
   Check("backspace reconverts",h.Text.StartsWith("申請書を"),h.Text);
   for(int i=0;i<40&&h.Composer.Composing;i++)h.Key(8,false);
   Check("backspace to empty ends the composition",!h.Composer.Composing,h.Text);
   Check("backspace with nothing typed is passed on",h.Key(8,false)==Decision.Pass,"");

   Fresh();
   Type("korehatesutodesu.");
   Equal("sentence stays uncommitted until the next one starts","",Doc);
   Type("tsugi");
   Equal("previous sentence is committed when the next one starts","これはテストです。",Doc);
   Enter();
   Check("second part committed",Doc.StartsWith("これはテストです。")&&Doc.Length>9,Doc);

   Fresh();
   Type("kyouhakaigigaarunode,shiryouwojunbishitekaraeigyoubunohitotouchiawasewoshite,sonoatodehoukokushowokakuyoteidesu");
   string before=Doc+h.Text;
   Check("long input is committed from the front",Doc.Length>0&&h.Text.Length<before.Length,"doc="+Doc.Length+" view="+h.Text.Length);
   Enter();
   Equal("long input loses nothing","今日は会議があるので、資料を準備してから営業部の人と打ち合わせをして、その後で報告書を書く予定です",Doc);

   Fresh();
   Type("dai3kainokaigiha10gatsu5nichidesu");Enter();
   Equal("digits stay as typed","第3回の会議は10月5日です",Doc);

   Fresh();
   Type("denwaha03-1234-5678desu");Enter();
   Check("a phone number is never altered",Doc.Contains("03")&&Doc.Contains("1234")&&Doc.Contains("5678"),Doc);

   Fresh();
   Type("Windows");Enter();Type("wokidousuru");Enter();
   Equal("shift starts latin input","Windowsを起動する",Doc);

   Fresh();
   Type("kanji");h.Key(27,false);
   Equal("escape shows kana","かんじ",h.Text);
   h.Key(27,false);
   Check("second escape cancels",!h.Composer.Composing&&Doc=="",Doc);

   Fresh();
   Type("(kakko)to[kagi]!?");Enter();
   Check("symbols are full-width in kana mode",Doc.StartsWith("（")&&Doc.Contains("）と「")&&Doc.EndsWith("」！？"),Doc);

   Fresh();
   Check("space with nothing typed is a full-width space",h.Key(32,false)==Decision.Swallow&&Doc=="　",Doc);
   Check("shift+space with nothing typed is passed on",h.Key(32,true)==Decision.Pass,"");
   Check("enter with nothing typed is passed on",h.Key(13,false)==Decision.Pass,"");

   Fresh();
   Type("kaigi");
   Check("a shortcut commits first and is sent on behind the text",h.Composer.Handle(new KeyEvt{Vk='S',Ctrl=true,Base='s'})==Decision.Swallow&&Doc=="会議"&&h.Injected.Count==1&&h.Injected[0]=="vk83",Doc+" "+string.Join(",",h.Injected));
   Type("kaigi");
   Check("an arrow key enters selection instead of leaving",h.Key(37,false)==Decision.Swallow&&h.Composer.Selecting,"");
   h.Key(36,false);
   Check("home commits and is sent on",!h.Composer.Composing&&Doc=="会議会議"&&h.Injected.Count==2&&h.Injected[1]=="vk36",Doc+" "+string.Join(",",h.Injected));
  }

  static void Selection(){
   Fresh();
   Type("kikann");
   string first=h.Text;
   Space();
   View v=h.View;
   Check("space opens the candidate list",v.Cands!=null&&v.Cands.Count>=4,v.Cands==null?"no list":v.Cands.Count+" candidates");
   Check("space moves to the second candidate",v.Selected==1&&h.Text!=first,first+" -> "+h.Text);
   Check("the first candidate is the live conversion",v.Cands!=null&&v.Cands[0].Text==first,"");
   string second=h.Text;
   Space();
   Check("space again moves on",h.View.Selected==2,h.Text);
   h.Key(32,true);
   Equal("shift+space moves back",second,h.Text);
   Enter();
   Equal("enter commits the chosen candidate",second,Doc);
   h.Composer.ResetContext();   // otherwise the committed word itself would steer the next conversion
   Type("kikann");
   Equal("the choice is remembered",second,h.Text);
   Enter();

   Fresh();
   Type("kikann");Space();
   h.Char('3');
   View picked=h.View;
   Check("a number key picks from the list",picked.Cands==null&&h.Composer.Selecting,h.Text);
   string third=h.Text;Enter();
   Equal("picked candidate is committed",third,Doc);

   Fresh();
   Type("kyouhaiitenkidesune");
   h.Key(37,false);
   View left=h.View;
   Check("left arrow selects the stretch before the last",h.Composer.Selecting&&left.FocusStart==3&&left.FocusLength==2,"focus "+left.FocusStart+"+"+left.FocusLength);
   h.Key(37,false);
   Check("left arrow again reaches the first stretch",h.View.FocusStart==0&&h.View.FocusLength==3,"focus "+h.View.FocusStart+"+"+h.View.FocusLength);
   Space();
   bool particleKept=h.View.Cands!=null;
   if(particleKept)foreach(Cand c in h.View.Cands)if(c.Note.Length==0&&!c.Text.EndsWith("は"))particleKept=false;
   Check("candidates change the word and keep its particle",particleKept,h.View.Cands==null?"no list":h.View.Cands.Count+" candidates, now "+h.Text);
   h.Key(39,false);h.Key(39,false);
   Check("right arrow moves to the last stretch",h.View.FocusStart==h.View.Text.Length-5&&h.View.FocusLength==5,"focus "+h.View.FocusStart+" of "+h.View.Text);
   h.Key(27,false);h.Settle(1500);
   Check("escape returns to live conversion",!h.Composer.Selecting&&h.Text=="今日はいい天気ですね",h.Text);
   Enter();

   Fresh();
   Type("kyouhaiitenkidesune");
   h.Key(37,false);
   h.Key(37,true);h.Settle(2500);
   View cut=h.View;
   Check("shift+left shortens the stretch",h.Composer.Selecting&&cut.Text.Length>0,cut.Text+" focus "+cut.FocusStart+"+"+cut.FocusLength);
   h.Key(39,true);h.Settle(2500);
   Check("shift+right lengthens it again",h.Composer.Selecting,h.View.Text);
   Enter();
   Check("resized text is committed whole",Doc.Length>=8,Doc);

   Fresh();
   Type("tesuto");
   h.Key(0x75,false);Equal("F6 hiragana","てすと",h.Text);
   h.Key(0x76,false);Equal("F7 katakana","テスト",h.Text);
   h.Key(0x77,false);Equal("F8 half-width katakana","ﾃｽﾄ",h.Text);
   h.Key(0x78,false);Equal("F9 full-width latin","ｔｅｓｕｔｏ",h.Text);
   h.Key(0x78,false);Equal("F9 again upper case","ＴＥＳＵＴＯ",h.Text);
   h.Key(0x79,false);Equal("F10 half-width latin","tesuto",h.Text);
   h.Key(0x79,false);h.Key(0x79,false);Equal("F10 third press capitalised","Tesuto",h.Text);
   Enter();
   Equal("variant is committed","Tesuto",Doc);

   Fresh();
   Type("tesuto");
   h.Ctrl('k');Equal("ctrl+k katakana","テスト",h.Text);
   h.Ctrl('j');Equal("ctrl+j hiragana","てすと",h.Text);
   h.Ctrl('l');Equal("ctrl+l full-width latin","ｔｅｓｕｔｏ",h.Text);
   h.Composer.Handle(new KeyEvt{Vk=0xBB,Ctrl=true,Base=';'});Equal("ctrl+; half-width katakana","ﾃｽﾄ",h.Text);
   h.Composer.Handle(new KeyEvt{Vk=0xBA,Ctrl=true,Base=':'});Equal("ctrl+: half-width latin","tesuto",h.Text);
   h.Key(27,false);h.Settle(1500);
   Equal("escape after a variant converts again","テスト",h.Text);
   Enter();

   Fresh();
   Type("kikann");Space();
   string chosen=h.Text;
   h.Type("no",25);h.Settle(2000);
   Check("typing on commits the choice and continues",Doc==chosen&&h.Composer.Composing&&!h.Composer.Selecting,Doc+" | "+h.Text);
   Enter();

   Fresh();
   Composer.Now=delegate{return new DateTime(2026,10,5,9,7,0);};
   Type("kyou");Space();
   bool date=false,era=false;
   if(h.View.Cands!=null)foreach(Cand c in h.View.Cands){if(c.Text=="2026年10月5日"&&c.Note=="日付")date=true;if(c.Text=="令和8年10月5日")era=true;}
   Check("today can be turned into the date",date&&era,h.View.Cands==null?"no list":h.View.Cands.Count+" candidates");
   Enter();
   Composer.Now=delegate{return DateTime.Now;};

   Fresh();
   h.Settings.Learn=false;
   Type("kikann");string unlearned=h.Text;Space();Enter();h.Composer.ResetContext();Type("kikann");
   Equal("nothing is remembered when learning is off",unlearned,h.Text);
   Enter();
  }

  static void Dictionary(){
   Fresh();
   Check("add a word",h.Dictionary.Add("めあど","sample.text","")==null,"");
   Check("the same word twice is refused",h.Dictionary.Add("めあど","sample.text","")=="same","");
   Check("an empty reading is refused",h.Dictionary.Add(" ","x","")=="empty","");
   Check("add a word for use in a sentence",h.Dictionary.Add("ひまわり","登録語","")==null,"");
   Check("add a short word",h.Dictionary.Add("はし","端子","")==null,"");
   Type("meado");
   Equal("a reading typed on its own becomes the registered word","sample.text",h.Text);
   Enter();
   Type("himawariwotsukau");
   Check("a registered word is used inside a sentence",h.Text.StartsWith("登録語を"),h.Text);
   Enter();
   Type("watashihashiranai");
   Check("a registered word does not break other words",!h.Text.Contains("端子"),h.Text);
   Enter();
   Type("hashi");Space();
   bool listed=false;View v=h.View;
   if(v.Cands!=null)foreach(Cand c in v.Cands)if(c.Text=="端子"&&c.Note=="登録語")listed=true;
   Check("registered words are listed as candidates",listed,v.Cands==null?"no list":v.Cands.Count+" candidates");
   Enter();
   string file=h.Dictionary.WordsPath;
   Check("words are saved to a local file",File.Exists(file)&&File.ReadAllText(file,Encoding.UTF8).Contains("ひまわり\t登録語"),file);
   UserDictionary again=new UserDictionary(Path.GetDirectoryName(file));
   Check("words are read back",again.Words().Count==3,again.Words().Count+" words");
   Entry gone=again.Words()[0];again.Remove(gone);
   Check("a word can be removed",again.Words().Count==2&&again.Find(gone.Reading)==null,"");
   string import=Path.Combine(Path.GetDirectoryName(file),"import.txt");
   File.WriteAllText(import,"!Microsoft IME Dictionary Tool\r\nかぶしき\t(株)\t名詞\r\nひまわり\t登録語\t人名\r\n",Encoding.Unicode);
   Check("an exported MS-IME list can be imported",again.Import(import)==1&&again.Find("かぶしき")!=null,again.Words().Count+" words");
  }
 }
}
