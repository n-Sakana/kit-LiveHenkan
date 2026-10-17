// Local language boundaries. The vocabulary here is authored for this app; no external dictionary is loaded.
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace Henkan {
 public sealed class LanguageMemory {
  readonly string path; readonly object gate=new object();
  Dictionary<string,bool> choices=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
  public LanguageMemory(string folder){
   path=Path.Combine(folder,"languages.tsv");
   try{if(File.Exists(path))foreach(string line in File.ReadAllLines(path,Encoding.UTF8)){
    string[] p=line.Split('\t');if(p.Length==2&&Valid(p[0])&&(p[1]=="en"||p[1]=="ja"))choices[p[0]]=p[1]=="en";
   }}catch(IOException){}
  }
  static bool Valid(string raw){if(raw.Length==0||raw.Length>40)return false;foreach(char c in raw)if(!Kana.IsAsciiLetter(c)&&c!='\'')return false;return true;}
  public Dictionary<string,bool> Snapshot(){lock(gate)return new Dictionary<string,bool>(choices,StringComparer.OrdinalIgnoreCase);}
  public void Learn(string raw,bool english){
   if(!Valid(raw))return;
   lock(gate){
    choices[raw]=english;
    StringBuilder b=new StringBuilder();foreach(KeyValuePair<string,bool> p in choices)b.Append(p.Key).Append('\t').Append(p.Value?"en":"ja").Append("\r\n");
    string temp=path+".new";
    File.WriteAllText(temp,b.ToString(),new UTF8Encoding(true));
    if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
   }
  }
  public void Clear(){lock(gate){choices.Clear();if(File.Exists(path))File.Delete(path);}}
 }

 public sealed class MixedInput {
  readonly RomajiTable table; readonly LanguageMemory memory;
  readonly HashSet<string> kanaWords=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  // Short spellings that are also ordinary kana are considered English only in an English phrase.
  static readonly HashSet<string> words=new HashSet<string>((
   "about after again also always another any application back before between browser build bug button cache call cancel change check class click close code command commit computer config control copy current data database debug default delete desktop document download drive editor email empty end enter error example excel false feature file filter find first folder font form function future get git github google graph group help home image import index input install issue javascript json keyboard last later learn left library line link list live load local log login main make map menu message method microsoft mode model mouse name network next node note number object office online open option origin output page password path people phone picture please point power powershell preview print problem process project public pull push python query read reader ready really release remove reply report request reset result return right run save screen script scroll search select send server set settings sheet show source space start state stop store string style support system table task test text thank then there these thing thread time title today token tool true type undo uninstall update upload user value version view visual want watch web website widget window windows with without word work world write wrong yesterday zoom " +
   "accept account action address android answer apple array async audio available background backup boolean branch break bytes capture case cell center chrome clear client clipboard color column comment common connect connection console content context count create cursor custom date dictionary direct display drawing edit engine english event export focus format framework handle header history hook host html http https information japanese key language length linux mac memory merge monitor native notepad notification null order pipe position power precision program progress property question range register resize response root row safe selection shortcut signature simple size software speed status stream surface symbol syntax terminal testing timeout toggle tray underline unicode validate video warning while width worker workspace").Split(new char[]{' '},StringSplitOptions.RemoveEmptyEntries),StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> phraseWords=new HashSet<string>("a an and are as at be been but by can do for from go had has have he her here him his how i if in is it its me mine more my no not of on one or our out same she so some than that the their them they this to too up us use was we were what when where which who will would yes you your".Split(' '),StringComparer.OrdinalIgnoreCase);
  public MixedInput(RomajiTable t,LanguageMemory m){
   table=t;memory=m;
   foreach(string word in words){Romaji r=new Romaji(table);foreach(char c in word)r.Push(c);r.Flush();bool kana=true;foreach(char c in r.Reading)if(!Kana.IsKanaLike(c))kana=false;if(kana)kanaWords.Add(word);}
  }
  public RomajiTable Table{get{return table;}}
  public void Parse(string raw,bool flush,List<Unit> units,out string pending,out bool latin){
   units.Clear();pending="";latin=false;
   Dictionary<string,bool> lessons=memory==null?new Dictionary<string,bool>():memory.Snapshot();
   bool phrase=false;int p=0;
   while(p<raw.Length){
    if(!Kana.IsAsciiLetter(raw[p])&&raw[p]!='\''){
     char c=raw[p++];
     if(phrase&&c==' '){units.Add(English(c));continue;}
     if(phrase&&(char.IsDigit(c)||".-_/@:\\!?()[]{}\"".IndexOf(c)>=0)){units.Add(English(c));continue;}
     Romaji symbol=new Romaji(table);symbol.Push(c);symbol.Flush();units.AddRange(symbol.Units);phrase=false;continue;
    }
    int end=p;while(end<raw.Length&&(Kana.IsAsciiLetter(raw[end])||raw[end]=='\''))end++;
    string token=raw.Substring(p,end-p);
    List<Unit> part=Split(token,phrase,lessons);
    bool tailEnglish=part.Count>0&&IsEnglish(part[part.Count-1]);
    // Leave an unfinished kana syllable waiting, exactly as in the original romaji input.
    if(!tailEnglish&&part.Count>0){
     Unit last=part[part.Count-1];if(table.Prefixes.Contains(last.Raw.ToLowerInvariant())&&last.Text==last.Raw.ToLowerInvariant()&&last.Raw!="nn"&&last.Raw!="n'"){
      if(end==raw.Length&&!flush){pending=last.Raw;part.RemoveAt(part.Count-1);}
      else if(last.Raw.ToLowerInvariant()=="n")part[part.Count-1]=new Unit("ん",last.Raw);
     }
    }
    units.AddRange(part);latin=tailEnglish;
    phrase=tailEnglish;p=end;
   }
  }
  public static bool IsEnglish(Unit u){return u.English;}
  static Unit English(char c){return new Unit(c.ToString(),c.ToString()){English=true};}
  static readonly string[] attachments={"wo","ha","ga","ni","no","de","to","he","kara","made","shita","suru"};
  static bool Attachment(string token,int at){
   foreach(string p in attachments)if(token.Length-at>=p.Length&&string.Compare(token,at,p,0,p.Length,StringComparison.OrdinalIgnoreCase)==0)return true;
   return false;
  }
  List<Unit> Split(string token,bool phrase,Dictionary<string,bool> lessons){
   int n=token.Length;double[] cost=new double[n+1];int[] take=new int[n];bool[] en=new bool[n];
   cost[n]=0;
   for(int p=n-1;p>=0;p--){
    cost[p]=1000;int remaining=n-p;
    // A romaji group is the ordinary path. English wins only when there is evidence for a whole word.
    for(int len=1;len<=Math.Min(6,remaining);len++){
     string raw=token.Substring(p,len),key=raw.ToLowerInvariant(),value;
     double own;bool english=false;
     if(table.Rules.TryGetValue(key,out value))own=0.05*len;
     else if(len==1&&key=="n")own=0.07;
     else if(len==1&&p+1<n&&char.ToLowerInvariant(token[p])==char.ToLowerInvariant(token[p+1])&&"aiueon".IndexOf(key[0])<0)own=0.08;
     else if(p+len==n&&table.Prefixes.Contains(key))own=0.15;
     else if(len==1){own=2.5;english=true;}
     else continue;
     if(own+cost[p+len]<cost[p]){cost[p]=own+cost[p+len];take[p]=len;en[p]=english;}
    }
    for(int len=1;len<=Math.Min(40,remaining);len++){
     string raw=token.Substring(p,len),key=raw.ToLowerInvariant();bool learned;
     bool known=words.Contains(key)||(phrase&&phraseWords.Contains(key));
     bool taught=lessons.TryGetValue(key,out learned);
     // Inside a Japanese run, a spelling such as node is also just の+で.
     if(p>0&&!phrase&&!taught&&!char.IsUpper(raw[0])&&kanaWords.Contains(key))continue;
     // Unknown lowercase strings cannot win the English path. Do not allocate
     // and scan the entire remaining sentence for each of their 40 lengths.
     bool capital=char.IsUpper(raw[0])&&(p==0||!char.IsUpper(token[p-1]));bool name=false;
     if(!known&&!taught&&!capital)continue;
     if(p+len<n&&!Attachment(token,p+len))continue;
     // Uppercase initials protect arbitrary names; a following romaji suffix can still be Japanese.
     if(capital&&!known&&!taught){
      if(p+len<n)continue;
      bool hasWord=false;
      for(int k=1;k<len;k++)if(words.Contains(raw.Substring(0,k))&&Attachment(raw,k)){hasWord=true;break;}
      if(hasWord)continue;
      known=true;name=true;
     }
     if(!known&&!taught)continue;
     double own=taught?-5.0-len:name?-0.05:phrase?-0.22*len:0.7-0.22*len;
     if(own+cost[p+len]>=cost[p])continue;
     cost[p]=own+cost[p+len];take[p]=len;en[p]=!taught||learned;
    }
   }
   List<Unit> result=new List<Unit>();
   for(int p=0;p<n;){
    int len=take[p];string raw=token.Substring(p,len);
    if(en[p])foreach(char c in raw)result.Add(English(c));
    else{
     Romaji r=new Romaji(table);foreach(char c in raw.ToLowerInvariant())r.Push(c);
     // The last n stays pending until Flush. Other syllables are already complete.
     string key=raw.ToLowerInvariant();
     if(len==1&&key=="n"&&p+len<n)r.Flush();
     if(len==1&&p+1<n&&key==token.Substring(p+1,1).ToLowerInvariant()&&"aiueon".IndexOf(key[0])<0)result.Add(new Unit("っ",raw));
     else{
      int offset=0;foreach(Unit u in r.Units){result.Add(new Unit(u.Text,raw.Substring(offset,u.Raw.Length)));offset+=u.Raw.Length;}
      if(r.Pending.Length>0)result.Add(new Unit(r.Pending,raw.Substring(offset)));
     }
    }
    p+=len;
   }
   // Unknown words with a non-romaji cluster keep their complete spelling, rather than a mixture of kana and letters.
   bool invalid=false;foreach(Unit u in result)if(IsEnglish(u))invalid=true;
   if(invalid&&cost[0]>=2.4){result.Clear();foreach(char c in token)result.Add(English(c));}
   return result;
  }
 }
}
