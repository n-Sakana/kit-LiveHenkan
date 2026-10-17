// UIA in Chromium reports the primary font, not the fallback used for Japanese.
// Keep explicitly selected Japanese fonts and native document rendering intact.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Henkan {
 public static class DocumentFonts {
  static readonly Dictionary<string,bool> japanese=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
  static string webJapanese;
  static readonly Dictionary<string,string> primary=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  public static bool HasJapanese(string name){
   lock(japanese){
    bool result;if(japanese.TryGetValue(name,out result))return result;
    try{
     GlyphTypeface glyph;
     Typeface face=new Typeface(new FontFamily(name),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
     if(face.TryGetGlyphTypeface(out glyph)){
      // An unknown name can silently resolve to another font. Require its real family name.
      bool same=false;foreach(string family in glyph.FamilyNames.Values)if(string.Equals(family,name,StringComparison.OrdinalIgnoreCase))same=true;
      result=same&&glyph.CharacterToGlyphMap.ContainsKey(0x3042)&&glyph.CharacterToGlyphMap.ContainsKey(0x8868);
     }
    }catch{}
    japanese[name]=result;return result;
   }
  }
  public static string WebJapanese {
   get{
    lock(japanese){
     if(webJapanese!=null)return webJapanese;
     // Chromium's Windows Japanese fallback order. Choose only locally installed fonts.
     foreach(string name in new[]{"Noto Sans JP","Noto Sans CJK JP","Meiryo","Yu Gothic","MS PGothic"})if(HasJapanese(name)){webJapanese=name;return name;}
     return webJapanese="Yu Gothic UI";
    }
   }
  }
  public static string Fallback(StyleInfo style){
   if(style.Web&&!HasJapanese(style.FontName))return WebJapanese;
   string n=style.FontName.ToLowerInvariant();
   if(n.Contains("consolas")||n.Contains("courier")||n.Contains("cascadia")||n.Contains("lucida console")||n.Contains("mono"))return "MS Gothic";
   if(n.Contains("mincho")||n.Contains("明朝")||n.Contains("serif")||n.Contains("times")||n.Contains("century"))return "Yu Mincho";
   return "Yu Gothic UI";
  }
  static string Primary(StyleInfo style){
   if(!style.Web||HasJapanese(style.FontName))return style.FontName;
   lock(japanese){
    string name;if(primary.TryGetValue(style.FontName,out name))return name;
    name=style.FontName;
    try{
     GlyphTypeface glyph;
     if(new Typeface(name).TryGetGlyphTypeface(out glyph))foreach(string family in glyph.FamilyNames.Values){
      // UIA can report a face alias such as "Segoe UI Semibold". Leaving it
      // in a fallback list forces that face's weight onto Japanese as well,
      // even when the published CSS weight is 500. Keep the numeric weight.
      if(name.StartsWith(family+" ",StringComparison.OrdinalIgnoreCase)){name=family;break;}
     }
    }catch{}
    primary[style.FontName]=name;return name;
   }
  }
  public static FontFamily Family(StyleInfo style){return new FontFamily(Primary(style)+", "+Fallback(style)+", Yu Gothic UI");}
 }
}
