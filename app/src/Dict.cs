// The user's own words and what was learnt from corrections. Plain local files, never sent anywhere.
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;

namespace Henkan {
 public sealed class Entry {
  public string Reading,Surface,Comment="";
  public string From;      // learnt entries: the conversion the user turned down
  public bool Learned; public int Count=1;
 }

 public sealed class UserDictionary {
  readonly string wordsPath,learnedPath; readonly object gate=new object();
  List<Entry> words=new List<Entry>(),learned=new List<Entry>();
  Dictionary<string,List<Entry>> index=new Dictionary<string,List<Entry>>(StringComparer.Ordinal);
  int longest; int revision;
  public event Action Changed;
  public const int MaxLearned=2000;
  public readonly LanguageMemory Languages;

  public UserDictionary(string folder){
   wordsPath=Path.Combine(folder,"dictionary.tsv");learnedPath=Path.Combine(folder,"learning.tsv");
   Languages=new LanguageMemory(folder);
   Load();
  }
  public string WordsPath{get{return wordsPath;}}
  public int Revision{get{return Volatile.Read(ref revision);}}
  public int Longest{get{return Volatile.Read(ref longest);}}
  public static string NormalizeReading(string s){return Kana.ToHiragana((s??"").Trim().Normalize(NormalizationForm.FormKC));}

  // Same columns as the text files of MS-IME and Google Japanese Input: reading, word, part of speech, comment.
  public static List<Entry> Parse(string path){
   List<Entry> list=new List<Entry>();
   if(!File.Exists(path))return list;
   using(StreamReader reader=new StreamReader(path,Encoding.UTF8,true)){
    string line;
    while((line=reader.ReadLine())!=null){
     if(line.Length==0||line[0]=='#'||line[0]=='!')continue;
     string[] p=line.Split('\t');
     if(p.Length<2)continue;
     string reading=NormalizeReading(p[0]),surface=p[1].Trim();
     if(reading.Length==0||surface.Length==0||reading.Length>40||surface.Length>120)continue;
     list.Add(new Entry{Reading=reading,Surface=surface,Comment=p.Length>3?p[3]:""});
    }
   }
   return list;
  }
  void Load(){
   lock(gate){
    try{words=Parse(wordsPath);}catch{words=new List<Entry>();}
    learned=new List<Entry>();
    try{
     if(File.Exists(learnedPath))foreach(string line in File.ReadAllLines(learnedPath,Encoding.UTF8)){
      string[] p=line.Split('\t');if(p.Length<3||p[0].Length==0||p[1].Length==0)continue;
      int count=1;if(p.Length>3)int.TryParse(p[3],out count);
      learned.Add(new Entry{Reading=p[0],Surface=p[1],From=p[2],Learned=true,Count=Math.Max(1,count)});
     }
    }catch{}
    Rebuild();
   }
  }
  void Rebuild(){
   Dictionary<string,List<Entry>> map=new Dictionary<string,List<Entry>>(StringComparer.Ordinal);int max=0;
   // The user's words come before learnt choices; a later learnt choice comes before an earlier one.
   foreach(Entry e in words)Put(map,e,ref max);
   for(int i=learned.Count-1;i>=0;i--)Put(map,learned[i],ref max);
   Volatile.Write(ref index,map);Volatile.Write(ref longest,max);Interlocked.Increment(ref revision);
  }
  static void Put(Dictionary<string,List<Entry>> map,Entry e,ref int max){
   List<Entry> list;if(!map.TryGetValue(e.Reading,out list)){list=new List<Entry>();map[e.Reading]=list;}
   list.Add(e);if(e.Reading.Length>max)max=e.Reading.Length;
  }
  void SaveWords(){
   StringBuilder b=new StringBuilder();
   foreach(Entry e in words)b.Append(e.Reading).Append('\t').Append(e.Surface).Append('\t').Append("名詞").Append('\t').Append(e.Comment??"").Append("\r\n");
   WriteAtomic(wordsPath,b.ToString());
  }
  void SaveLearned(){
   StringBuilder b=new StringBuilder();
   foreach(Entry e in learned)b.Append(e.Reading).Append('\t').Append(e.Surface).Append('\t').Append(e.From??"").Append('\t').Append(e.Count).Append("\r\n");
   WriteAtomic(learnedPath,b.ToString());
  }
  // Written beside the file and swapped in, so a failure never leaves half a dictionary.
  static void WriteAtomic(string path,string content){
   string temp=path+".new";
   File.WriteAllText(temp,content,new UTF8Encoding(true));
   if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
  }
  void Raise(){Action a=Changed;if(a!=null)a();}

  public List<Entry> Words(){lock(gate)return new List<Entry>(words);}
  public int LearnedCount{get{lock(gate)return learned.Count;}}
  // Entries whose reading is exactly `reading`, in priority order. Safe from any thread.
  public List<Entry> Find(string reading){
   List<Entry> list;
   return Volatile.Read(ref index).TryGetValue(reading,out list)?list:null;
  }
  // Returns null on success, otherwise why the word was not added ("empty", "long", "same").
  public string Add(string reading,string surface,string comment){
   reading=NormalizeReading(reading);surface=(surface??"").Trim();
   if(reading.Length==0||surface.Length==0)return "empty";
   if(reading.Length>40||surface.Length>120)return "long";
   lock(gate){
    foreach(Entry e in words)if(e.Reading==reading&&e.Surface==surface)return "same";
    words.Add(new Entry{Reading=reading,Surface=surface,Comment=comment??""});
    SaveWords();Rebuild();
   }
   Raise();return null;
  }
  public void Remove(Entry entry){
   lock(gate){if(!words.Remove(entry))return;SaveWords();Rebuild();}
   Raise();
  }
  public int Import(string path){
   List<Entry> incoming=Parse(path);int added=0;
   lock(gate){
    HashSet<string> have=new HashSet<string>(StringComparer.Ordinal);
    foreach(Entry e in words)have.Add(e.Reading+"\t"+e.Surface);
    foreach(Entry e in incoming)if(have.Add(e.Reading+"\t"+e.Surface)){words.Add(e);added++;}
    if(added>0){SaveWords();Rebuild();}
   }
   if(added>0)Raise();
   return added;
  }
  public void Export(string path){lock(gate){StringBuilder b=new StringBuilder();foreach(Entry e in words)b.Append(e.Reading).Append('\t').Append(e.Surface).Append('\t').Append("名詞").Append('\t').Append(e.Comment??"").Append("\r\n");File.WriteAllText(path,b.ToString(),new UTF8Encoding(true));}}
  // The user picked `to` where the conversion had offered `from` for this reading.
  public void Learn(string reading,string from,string to){
   if(reading.Length==0||to.Length==0||from==to||reading.Length>40)return;
   lock(gate){
    Entry found=null;
    foreach(Entry e in learned)if(e.Reading==reading&&e.Surface==to){found=e;break;}
    // Choosing the original again takes back an earlier lesson that pointed the other way.
    learned.RemoveAll(delegate(Entry e){return e.Reading==reading&&e.Surface==from&&e.From==to;});
    if(found!=null){learned.Remove(found);found.Count++;found.From=from;learned.Add(found);}
    else learned.Add(new Entry{Reading=reading,Surface=to,From=from,Learned=true});
    if(learned.Count>MaxLearned)learned.RemoveRange(0,learned.Count-MaxLearned);
    try{SaveLearned();}catch{}
    Rebuild();
   }
  }
  public void ClearLearned(){
   lock(gate){learned.Clear();try{SaveLearned();}catch{}Rebuild();}
   Languages.Clear();
   Raise();
  }
 }

 // Puts dictionary words into a finished conversion where the model itself treats that stretch as one word.
 public static class DictionaryApplier {
  // `standalone` gives the model's own conversion of a reading on its own, or null when that is not available.
  public static bool Apply(UserDictionary dict,Func<string,string> standalone,string reading,ref string surface,ref Alignment align){
   if(dict==null||!align.Ok)return false;
   int n=reading.Length,max=Math.Min(dict.Longest,n);
   if(max==0)return false;
   bool changed=false;
   bool[] taken=new bool[n+1];
   HashSet<int> starts=new HashSet<int>(),ends=new HashSet<int>();
   foreach(Piece p in align.Pieces){
    starts.Add(p.R0);ends.Add(p.R1);
    if(!p.Content)for(int k=p.R0;k<=p.R1;k++){starts.Add(k);ends.Add(k);}   // kana pieces may be cut anywhere
   }
   for(int len=max;len>=1;len--){
    for(int i=0;i+len<=n;i++){
     int j=i+len;
     if(!starts.Contains(i)||!ends.Contains(j))continue;
     bool free=true;for(int k=i;k<j;k++)if(taken[k]){free=false;break;}
     if(!free)continue;
     List<Entry> found=dict.Find(reading.Substring(i,len));
     if(found==null)continue;
     string seen=align.SurfaceFor(i,j);
     foreach(Entry e in found){
      if(seen==e.Surface){for(int k=i;k<j;k++)taken[k]=true;break;}
      bool whole=i==0&&j==n;
      bool accept;
      if(e.Learned)accept=seen==e.From;
      else{
       string own=whole?null:standalone(e.Reading);
       accept=whole||seen==Kana.ToKatakana(e.Reading)||(own!=null&&seen==own&&!IsPlainKana(own));
      }
      if(!accept)continue;
      align=align.Splice(i,j,e.Surface);surface=align.Surface;changed=true;
      for(int k=i;k<j;k++)taken[k]=true;
      break;
     }
    }
   }
   return changed;
  }
  static bool IsPlainKana(string s){foreach(char c in s)if(!Kana.IsHiragana(c)&&c!='ー')return false;return true;}
 }
}
