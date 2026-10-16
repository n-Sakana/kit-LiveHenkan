// Kana helpers, romaji input and reading-to-surface alignment. No Win32 here.
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace LiveHenkan {
 public static class Kana {
  public static bool IsHiragana(char c){return (c>='ぁ'&&c<='ゖ')||c=='ゝ'||c=='ゞ';}
  public static bool IsKatakana(char c){return (c>='ァ'&&c<='ヺ')||c=='ヽ'||c=='ヾ';}
  public static bool IsKanaLike(char c){return IsHiragana(c)||IsKatakana(c)||c=='ー';}
  public static bool IsKanji(char c){return (c>='一'&&c<='鿿')||(c>='㐀'&&c<='䶿')||(c>='豈'&&c<='﫿')||c=='々'||c=='〆'||c=='〇'||char.IsSurrogate(c);}
  public static bool IsSentenceEnd(char c){return c=='。'||c=='？'||c=='！'||c=='．';}
  public static bool IsAsciiLetter(char c){return (c>='a'&&c<='z')||(c>='A'&&c<='Z');}
  public static bool IsDigit(char c){return (c>='0'&&c<='9')||(c>='０'&&c<='９');}
  public static char ToHiragana(char c){return c>='ァ'&&c<='ヶ'?(char)(c-0x60):c;}
  public static char ToKatakana(char c){return c>='ぁ'&&c<='ゖ'?(char)(c+0x60):c;}
  public static string ToHiragana(string s){StringBuilder b=new StringBuilder(s.Length);foreach(char c in s)b.Append(ToHiragana(c));return b.ToString();}
  public static string ToKatakana(string s){StringBuilder b=new StringBuilder(s.Length);foreach(char c in s)b.Append(ToKatakana(c));return b.ToString();}
  // Half-width katakana through the compatibility decomposition table of the framework is not
  // available, so the voiced marks are split by hand.
  const string FullKata="アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲンァィゥェォャュョッー、。「」・";
  const string HalfKata="ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜｦﾝｧｨｩｪｫｬｭｮｯｰ､｡｢｣･";
  const string Voiced="ガギグゲゴザジズゼゾダヂヅデドバビブベボヴ",VoicedBase="カキクケコサシスセソタチツテトハヒフヘホウ";
  const string SemiVoiced="パピプペポ",SemiBase="ハヒフヘホ";
  public static string ToHalfKatakana(string s){
   StringBuilder b=new StringBuilder(s.Length*2);
   foreach(char raw in s){
    char c=ToKatakana(raw);int i;
    if((i=Voiced.IndexOf(c))>=0){b.Append(HalfKata[FullKata.IndexOf(VoicedBase[i])]).Append('ﾞ');}
    else if((i=SemiVoiced.IndexOf(c))>=0){b.Append(HalfKata[FullKata.IndexOf(SemiBase[i])]).Append('ﾟ');}
    else if((i=FullKata.IndexOf(c))>=0)b.Append(HalfKata[i]);
    else if(c>='！'&&c<='～')b.Append((char)(c-0xFEE0));
    else b.Append(c);
   }
   return b.ToString();
  }
  public static string ToFullWidth(string s){
   StringBuilder b=new StringBuilder(s.Length);
   foreach(char c in s)b.Append(c>='!'&&c<='~'?(char)(c+0xFEE0):c==' '?'　':c);
   return b.ToString();
  }
  public static string ToHalfWidth(string s){
   StringBuilder b=new StringBuilder(s.Length);
   foreach(char c in s)b.Append(c>='！'&&c<='～'?(char)(c-0xFEE0):c=='　'?' ':c);
   return b.ToString();
  }
  // Comparison form used by the aligner: katakana and width differences are ignored.
  public static char Fold(char c){
   c=ToHiragana(c);
   if(c>='！'&&c<='～')c=(char)(c-0xFEE0);
   return char.ToLowerInvariant(c);
  }
 }

 // One keystroke group and the kana it produced. Raw keeps the typed letters for F9/F10.
 public sealed class Unit {
  public string Text,Raw;
  public bool English;
  public Unit(string text,string raw){Text=text;Raw=raw;}
 }

 public sealed class RomajiTable {
  public readonly Dictionary<string,string> Rules=new Dictionary<string,string>(StringComparer.Ordinal);
  public readonly HashSet<string> Prefixes=new HashSet<string>(StringComparer.Ordinal);
  public RomajiTable(string file){
   foreach(string l in File.ReadAllLines(file,Encoding.UTF8)){
    string[] p=l.Split('\t');
    if(p.Length!=2||p[0].Length==0)continue;
    Rules[p[0]]=p[1];
    for(int i=1;i<p[0].Length;i++)Prefixes.Add(p[0].Substring(0,i));
   }
  }
 }

 public sealed class Romaji {
  readonly RomajiTable table;
  public MixedInput Mixed;
  public readonly List<Unit> Units=new List<Unit>();
  public string Pending="";
  public bool Latin;   // after Shift+letter: letters are kept as typed until the composition ends
  public Romaji(RomajiTable t){table=t;}
  public string Reading{get{StringBuilder b=new StringBuilder();foreach(Unit u in Units)b.Append(u.Text);return b.ToString();}}
  public bool IsEmpty{get{return Units.Count==0&&Pending.Length==0;}}
  public void Clear(){Units.Clear();Pending="";Latin=false;}
  void Emit(string text,string raw){Units.Add(new Unit(text,raw));}
  public void Literal(char c){Flush();Emit(c.ToString(),c.ToString());}
  void Reparse(bool flush){
   StringBuilder b=new StringBuilder();foreach(Unit u in Units)b.Append(u.Raw);b.Append(Pending);
   Mixed.Parse(b.ToString(),flush,Units,out Pending,out Latin);
  }
  public void Push(char c){
   if(Mixed!=null){Pending+=c;Reparse(false);return;}
   if(Latin){Emit(c.ToString(),c.ToString());return;}
   if(c>='A'&&c<='Z'){Flush();Latin=true;Emit(c.ToString(),c.ToString());return;}
   Pending+=c;Process(false);
  }
  public void Flush(){if(Mixed!=null)Reparse(true);else Process(true);}
  // Removes one visible character. Returns false when nothing was left to remove.
  public bool Backspace(){
   if(Pending.Length>0){Pending=Pending.Substring(0,Pending.Length-1);if(Mixed!=null)Reparse(false);return true;}
   if(Units.Count==0)return false;
   Unit u=Units[Units.Count-1];
   if(u.Text.Length<=1)Units.RemoveAt(Units.Count-1);else{
    string text=u.Text.Substring(0,u.Text.Length-1),raw=u.Raw;
    if(Mixed!=null)foreach(KeyValuePair<string,string> rule in table.Rules)if(rule.Value==text&&(raw==u.Raw||rule.Key.Length<raw.Length))raw=rule.Key;
    Units[Units.Count-1]=new Unit(text,raw);
   }
   if(Mixed!=null)Reparse(false);
   if(Units.Count==0)Latin=false;
   return true;
  }
  void Process(bool flush){
   while(Pending.Length>0){
    int n=Pending.Length;
    if(n>=3&&Pending.StartsWith("nn",StringComparison.Ordinal)){Emit("ん","nn");Pending=Pending.Substring(2);continue;}
    if(n>=2&&Pending[n-2]=='n'&&"aiueoy'".IndexOf(Pending[n-1])<0&&!(n==2&&Pending[n-1]=='n')){Emit("ん","n");Pending=Pending.Remove(n-2,1);continue;}
    if(n==2&&Pending[0]==Pending[1]&&Kana.IsAsciiLetter(Pending[0])&&"aiueon".IndexOf(Pending[1])<0){Emit("っ",Pending.Substring(0,1));Pending=Pending.Substring(1);return;}
    string value=null;int matched=0;
    for(int i=1;i<=Pending.Length;i++){string v;if(table.Rules.TryGetValue(Pending.Substring(0,i),out v)){value=v;matched=i;}}
    if(value!=null){
     if(!flush&&matched==Pending.Length&&table.Prefixes.Contains(Pending)&&Pending!="nn"&&Pending!="n'")return;
     Emit(value,Pending.Substring(0,matched));Pending=Pending.Substring(matched);continue;
    }
    if(!flush&&table.Prefixes.Contains(Pending))return;
    // A lone n at the end of the input is ん, as on macOS and MS-IME.
    if(flush&&Pending=="n"){Emit("ん","n");Pending="";continue;}
    Emit(Pending.Substring(0,1),Pending.Substring(0,1));Pending=Pending.Substring(1);
   }
  }
 }

 // A stretch of the surface and the stretch of the reading it stands for.
 public sealed class Piece {
  public int R0,R1,S0,S1; public bool Content;   // Content: kanji or other text that is not the reading itself
  public bool Literal;
 }
 public sealed class Alignment {
  public string Reading,Surface; public bool Ok; public List<Piece> Pieces=new List<Piece>();
  const int MaxSpan=10; const float Inf=1e9f;

  public static Alignment Build(string reading,string surface){
   Alignment a=new Alignment{Reading=reading,Surface=surface};
   int n=surface.Length,m=reading.Length;
   if(n==0||m==0||n>96||m>96){a.Whole();return a;}
   char[] rs=new char[m],ss=new char[n];
   for(int j=0;j<m;j++)rs[j]=Kana.Fold(reading[j]);
   for(int i=0;i<n;i++)ss[i]=Kana.Fold(surface[i]);
   // cost[i,j]: cheapest way to explain surface[i..] with reading[j..]; take[i,j]: reading chars used by surface[i], -1 = same char
   float[,] cost=new float[n+1,m+1];sbyte[,] take=new sbyte[n+1,m+1];
   for(int j=0;j<=m;j++)cost[n,j]=j==m?0:Inf;
   for(int i=n-1;i>=0;i--){
    bool kana=Kana.IsKanaLike(surface[i]);
    // A low surrogate rides along with its high surrogate.
    bool low=char.IsLowSurrogate(surface[i]);
    for(int j=m;j>=0;j--){
     float best=Inf;sbyte how=0;
     if(low){best=cost[i+1,j];how=0;}
     else{
      if(j<m&&ss[i]==rs[j]&&cost[i+1,j+1]<Inf){best=cost[i+1,j+1];how=-1;}
      int from=kana?1:0;
      for(int k=from;k<=MaxSpan&&j+k<=m;k++){
       float rest=cost[i+1,j+k];if(rest>=Inf)continue;
       float c=rest+(kana?6f:1f)+(k==0?2f:(k-2)*(k-2)*0.15f);
       if(c<best){best=c;how=(sbyte)k;}
      }
     }
     cost[i,j]=best;take[i,j]=how;
    }
   }
   if(cost[0,0]>=Inf){a.Whole();return a;}
   int si=0,rj=0;Piece cur=null;
   while(si<n){
    int k=take[si,rj];bool content=k!=-1;int used=k==-1?1:k;
    if(cur==null||cur.Content!=content){cur=new Piece{R0=rj,R1=rj,S0=si,S1=si,Content=content};a.Pieces.Add(cur);}
    cur.S1=si+1;cur.R1=rj+used;si++;rj+=used;
   }
   a.Ok=true;return a;
  }
  void Whole(){Pieces.Clear();Pieces.Add(new Piece{R0=0,R1=Reading.Length,S0=0,S1=Surface.Length,Content=true});Ok=false;}

  // The model works on NFKC text, so full-width symbols come back half-width. Where a surface
  // character is the reading character in another width, the typed form is put back.
  public string RestoreTyped(){
   if(!Ok)return Surface;
   char[] t=Surface.ToCharArray();bool changed=false;
   foreach(Piece p in Pieces){
    if(p.Content)continue;
    for(int k=0;k<p.S1-p.S0&&p.R0+k<Reading.Length;k++){
     char rc=Reading[p.R0+k],sc=t[p.S0+k];
     if(rc!=sc&&!Kana.IsKanaLike(rc)&&!Kana.IsKanaLike(sc)){t[p.S0+k]=rc;changed=true;}
    }
   }
   if(changed)Surface=new string(t);
   return Surface;
  }
  // Longest surface prefix that is explained by no more than `readingLength` reading characters.
  public void PrefixWithin(int readingLength,out int surfaceLength,out int readingUsed){
   surfaceLength=0;readingUsed=0;
   foreach(Piece p in Pieces){
    if(p.R1<=readingLength){surfaceLength=p.S1;readingUsed=p.R1;continue;}
    if(!p.Content&&p.R0<readingLength){int keep=readingLength-p.R0;surfaceLength=p.S0+keep;readingUsed=p.R0+keep;}
    break;
   }
  }
  static readonly string[] functionWords={"について","として","という","による","だった","でした","である","から","まで","より","など","って","です","では","には","とは","ので","のに","のは","のが","のを","のも"};
  // Length of the particle or copula that `kana` starts with, 0 when it starts with something else.
  // A kana run after a kanji word is either the word's own ending (食|べる, 上|がる) or grammar hung on it (天気|ですね, 今日|は).
  public static int FunctionPrefix(string kana){
   if(kana.Length==0)return 0;
   foreach(string w in functionWords)if(kana.StartsWith(w,StringComparison.Ordinal))return w.Length;
   if("はがをにへともので".IndexOf(kana[0])<0)return 0;
   // がる, もる, でる and the like are verb endings, not particles.
   if(kana.Length>1&&"るらりれろっ".IndexOf(kana[1])>=0)return 0;
   return 1;
  }
  // Segment boundaries as reading offsets: a word with the particle that follows it, as a clause is cut on macOS.
  public List<int> SegmentStarts(){
   List<int> starts=new List<int>();starts.Add(0);
   if(!Ok)return starts;
   for(int i=1;i<Pieces.Count;i++){
    Piece p=Pieces[i],prev=Pieces[i-1];
    if(p.R0==p.R1||p.R0==0)continue;
    if(p.Literal||prev.Literal){if(p.Literal!=prev.Literal&&starts[starts.Count-1]!=p.R0)starts.Add(p.R0);continue;}
    bool afterStop=false;
    if(prev.S1>prev.S0){char last=Surface[prev.S1-1];afterStop=last=='\u3001'||last=='\u3002'||last=='\uFF0C'||last=='\uFF0E'||last=='\uFF01'||last=='\uFF1F';}
    if(p.Content&&!prev.Content){
     // お/ご directly before content stays with it
     string lead=Surface.Substring(prev.S0,prev.S1-prev.S0);
     if(i==1&&(lead=="お"||lead=="ご"))continue;
     if(starts[starts.Count-1]!=p.R0)starts.Add(p.R0);
    }else if(!p.Content&&afterStop){if(starts[starts.Count-1]!=p.R0)starts.Add(p.R0);}
    else if(!p.Content&&prev.Content){
     // 今日|は|いい: what follows the particle and is long enough to be a word starts its own stretch.
     string kana=Reading.Substring(p.R0,p.R1-p.R0);int f=FunctionPrefix(kana);
     bool wordAfter=i+1<Pieces.Count&&Pieces[i+1].Content;
     if(f>0&&kana.Length-f>=2&&!(wordAfter&&kana.Length-f<=1))starts.Add(p.R0+f);
    }
   }
   return starts;
  }
  // Within the stretch [r0,r1): where its word ends and the grammar hung on it begins. r1 when there is no such tail.
  public int HeadEnd(int r0,int r1){
   if(!Ok)return r1;
   int end=r1;
   for(int i=Pieces.Count-1;i>=0;i--){
    Piece p=Pieces[i];
    if(p.R0>=r1||p.R1<=r0||p.R0==p.R1)continue;
    if(p.Content)break;                     // the stretch ends in a word: nothing to split off
    if(i==0||!Pieces[i-1].Content||Pieces[i-1].R1<=r0)break;   // kana with no word before it
    int from=Math.Max(p.R0,r0);
    string kana=Reading.Substring(from,Math.Min(p.R1,r1)-from);
    if(from==p.R0&&FunctionPrefix(kana)>0)end=p.R0;
    break;
   }
   return end;
  }
  // Pieces that use no reading (a mark the model added) go with the stretch before them.
  public string SurfaceFor(int r0,int r1){
   int s0=-1,s1=-1;
   foreach(Piece p in Pieces){
    bool empty=p.R0==p.R1;
    if(p.R1<=r0&&!(empty&&r0==0&&p.R0==0))continue;
    if(p.R0>=r1&&!(empty&&p.R0==r1&&s0>=0))break;
    int a=p.S0,b=p.S1;
    if(!p.Content){if(p.R0<r0)a=p.S0+(r0-p.R0);if(p.R1>r1)b=p.S1-(p.R1-r1);}
    if(s0<0)s0=a;s1=b;
   }
   return s0<0?"":Surface.Substring(s0,s1-s0);
  }
  // The same alignment with the stretch [r0,r1) of the reading standing for `replacement` instead.
  public Alignment Splice(int r0,int r1,string replacement){
   Alignment a=new Alignment{Reading=Reading,Ok=true};StringBuilder sb=new StringBuilder();bool placed=false;
   foreach(Piece p in Pieces){
    bool empty=p.R0==p.R1;
    if(p.R1<=r0&&!(empty&&p.R0==r1)){Copy(a,sb,p,p.R0,p.R1);continue;}
    if(p.R0>=r1&&!(empty&&p.R0==r1&&!placed)){
     if(!placed){Insert(a,sb,r0,r1,replacement);placed=true;}
     Copy(a,sb,p,p.R0,p.R1);continue;
    }
    // overlapping: only the parts of a kana piece outside the stretch survive
    if(!p.Content&&p.R0<r0)Copy(a,sb,p,p.R0,r0);
    if(!placed){Insert(a,sb,r0,r1,replacement);placed=true;}
    if(!p.Content&&p.R1>r1)Copy(a,sb,p,r1,p.R1);
   }
   if(!placed)Insert(a,sb,r0,r1,replacement);
   a.Surface=sb.ToString();
   return a;
  }
  void Copy(Alignment a,StringBuilder sb,Piece p,int from,int to){
   string part=p.Content?Surface.Substring(p.S0,p.S1-p.S0):Surface.Substring(p.S0+(from-p.R0),to-from);
   a.Pieces.Add(new Piece{R0=from,R1=to,S0=sb.Length,S1=sb.Length+part.Length,Content=p.Content,Literal=p.Literal});sb.Append(part);
  }
  static void Insert(Alignment a,StringBuilder sb,int r0,int r1,string text){
   a.Pieces.Add(new Piece{R0=r0,R1=r1,S0=sb.Length,S1=sb.Length+text.Length,Content=true});sb.Append(text);
  }
 }
}
