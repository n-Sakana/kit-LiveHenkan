// Local, reversible presentation of numbers/dates/times. Never adds punctuation
// to prose, touches a document, or changes the conversion model's input.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Henkan {
 public sealed class FormatEdit {
  public int Start,Length; public string Text;
  public int End{get{return Start+Length;}}
 }
 public sealed class FormatResult {
  public string Source,Text;
  public readonly List<FormatEdit> Edits=new List<FormatEdit>();
  public string Slice(int start,int length){
   int end=start+length,at=start;StringBuilder b=new StringBuilder();
   foreach(FormatEdit e in Edits){
    if(e.Start<start||e.End>end)continue;
    b.Append(Source,at,e.Start-at).Append(e.Text);at=e.End;
   }
   return b.Append(Source,at,end-at).ToString();
  }
 }
 public static class NumericFormatting {
  static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;
  static Regex Rx(string s){return new Regex(s,RegexOptions.CultureInvariant|RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(10));}
  // Compare a width-normalized copy; edits always address the untouched source.
  static readonly Regex protectedText=Rx(@"https?://|www\.|[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+|[A-Za-z]:[\\/]|`|[{}]|(?:^|\s)[=+][A-Za-z]|\b(?:SELECT|function|var)\b");
  static readonly Regex identifier=Rx(@"(?:ID|識別子|番号|型番|品番|コード|パス|URL|ファイル名|バージョン|比率|スコア|得点)(?:は|が|を|の)?[\s:=#]*$");
  const string Left=@"(?<![0-9A-Za-z_./,:+\-])";
  const string Week=@"(?:\([日月火水木金土](?:曜日)?\))?";
  const string DateEnd=@"(?![0-9A-Za-z_./\-]|\()";
  static readonly Regex fullDate=Rx(Left+@"(?<y>[0-9]{4})(?:/(?<m>[0-9]{1,2})/(?<d>[0-9]{1,2})|-(?<m>[0-9]{1,2})-(?<d>[0-9]{1,2})|年(?<m>[0-9]{1,2})月(?<d>[0-9]{1,2})日)"+Week+DateEnd);
  static readonly Regex monthDate=Rx(@"(?<![0-9A-Za-z_年月])(?<m>[0-9]{1,2})月(?<d>[0-9]{1,2})日"+Week+DateEnd);
  static readonly Regex colonTime=Rx(Left+@"(?<h>[0-9]{1,2}):(?<m>[0-9]{2})(?![0-9A-Za-z_:])");
  static readonly Regex japaneseTime=Rx(Left+@"(?<ap>午前|午後)?(?<h>[0-9]{1,2})時(?:(?<half>半)|(?<m>[0-9]{1,2})分)?(?![0-9A-Za-z]|間|台|代|限|期|点|差)");
  static readonly Regex quantity=Rx(Left+@"(?<sign>[-−]?)(?<n>(?:[0-9]{1,3}(?:,[0-9]{3})+|[0-9]{1,15}))(?<fraction>\.[0-9]{1,6})?(?<unit>円|人|件|個|台|枚|kg|km|%)(?![A-Za-z])");
  static int Number(Match m,string name){return int.Parse(m.Groups[name].Value,Invariant);}
  static string DateText(int y,int m,int d){
   DateTime date;
   string s=y.ToString("0000",Invariant)+"/"+m.ToString("00",Invariant)+"/"+d.ToString("00",Invariant);
   if(!DateTime.TryParseExact(s,"yyyy/MM/dd",Invariant,DateTimeStyles.None,out date))return null;
   return date.ToString("yyyy/M/d",Invariant)+"（"+"日月火水木金土"[(int)date.DayOfWeek]+"）";
  }
  static string TimeText(int h,int m){return h>=0&&h<24&&m>=0&&m<60?h.ToString("00",Invariant)+":"+m.ToString("00",Invariant):null;}
  static void Add(FormatResult result,string compare,string left,Regex pattern,Func<Match,string> replacement){
   foreach(Match m in pattern.Matches(compare)){
    bool overlaps=false;foreach(FormatEdit e in result.Edits)if(m.Index<e.End&&m.Index+m.Length>e.Start){overlaps=true;break;}
    if(overlaps)continue;
    string prefix=left+compare.Substring(0,m.Index);
    if(identifier.IsMatch(Composer.Tail(prefix,80)))continue;
    string text=replacement(m);if(text==null||text==result.Source.Substring(m.Index,m.Length))continue;
    result.Edits.Add(new FormatEdit{Start=m.Index,Length=m.Length,Text=text});
   }
  }
  public static FormatResult Apply(string input,string left,DateTime reference){
   FormatResult result=new FormatResult{Source=input,Text=input};
   if(string.IsNullOrEmpty(input)||input.Length>1024)return result;
   bool digits=false;foreach(char c in input)if(Kana.IsDigit(c)){digits=true;break;}if(!digits)return result;
   try{
    string compare=Kana.ToHalfWidth(input),prefix=Kana.ToHalfWidth(left??"");
    if(protectedText.IsMatch(prefix+compare))return result;
    Add(result,compare,prefix,fullDate,delegate(Match m){return DateText(Number(m,"y"),Number(m,"m"),Number(m,"d"));});
    Add(result,compare,prefix,monthDate,delegate(Match m){return DateText(reference.Year,Number(m,"m"),Number(m,"d"));});
    Add(result,compare,prefix,colonTime,delegate(Match m){return TimeText(Number(m,"h"),Number(m,"m"));});
    Add(result,compare,prefix,japaneseTime,delegate(Match m){
     int h=Number(m,"h"),minute=m.Groups["half"].Success?30:(m.Groups["m"].Success?Number(m,"m"):0);
     string ap=m.Groups["ap"].Value;
     if(ap.Length>0){if(h>12||(h==0&&ap=="午後"))return null;h=h%12+(ap=="午後"?12:0);}
     return TimeText(h,minute);
    });
    Add(result,compare,prefix,quantity,delegate(Match m){
     string n=m.Groups["n"].Value.Replace(",","");if(n.Length>15||(n.Length>1&&n[0]=='0'))return null;
     long value;if(!long.TryParse(n,NumberStyles.None,Invariant,out value))return null;
     return m.Groups["sign"].Value.Replace('−','-')+value.ToString("#,0",Invariant)+m.Groups["fraction"].Value+m.Groups["unit"].Value;
    });
    result.Edits.Sort(delegate(FormatEdit a,FormatEdit b){return a.Start.CompareTo(b.Start);});
    if(result.Edits.Count>0)result.Text=result.Slice(0,input.Length);
   }catch(RegexMatchTimeoutException){result.Edits.Clear();result.Text=input;}
   return result;
  }
  // Automatic chunk splitting must not cut a numeric token before its suffix.
  public static bool SafeBoundary(string text,int at){
   if(at<=0||at>=text.Length)return true;
   return !NumericPart(text[at-1])||!NumericPart(text[at]);
  }
  static bool NumericPart(char c){return Kana.IsDigit(c)||"/／:：.．,，-－−年月日時分秒半午前後台代間円人件個枚%％kgm".IndexOf(c)>=0;}
 }
}
