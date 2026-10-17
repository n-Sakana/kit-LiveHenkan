// Read-only access to the native document underneath the focused Office editor.
// Never attach to an arbitrary active Office instance or to its web task panes.
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml;

namespace Henkan {
 static class OfficeStyle {
  static object Get(object value,string property){return value.GetType().InvokeMember(property,BindingFlags.GetProperty,null,value,null);}
  static void Release(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
  static string ThemeName(object selection,string name){
   if(!name.StartsWith("+",StringComparison.Ordinal))return name;
   object range=null,source=null;
   try{
    source=Get(selection,"Range");range=Get(source,"Duplicate");
    range.GetType().InvokeMember("Collapse",BindingFlags.InvokeMethod,null,range,new object[]{1});
    bool heading=name.IndexOf("Heading",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("見出し",StringComparison.Ordinal)>=0;
    // Office themes normally leave the generic East Asian font empty and select
    // a script-specific face instead. The selected range's flat XML includes its
    // theme; reading it neither saves nor modifies the document.
    XmlDocument xml=new XmlDocument();xml.XmlResolver=null;xml.LoadXml(Convert.ToString(Get(range,"WordOpenXML")));
    XmlNamespaceManager ns=new XmlNamespaceManager(xml.NameTable);ns.AddNamespace("a","http://schemas.openxmlformats.org/drawingml/2006/main");
    XmlNode node=xml.SelectSingleNode("//a:fontScheme/a:"+(heading?"majorFont":"minorFont")+"/a:font[@script='Jpan']/@typeface",ns);
    return node==null?null:node.Value;
   }finally{Release(range);Release(source);}
  }
  public static StyleInfo Find(IntPtr focus,StyleInfo published){
   string cls=Native.ClassOf(focus);bool word=cls=="_WwG";
   if(!word&&cls!="EXCEL7")return published;
   object window=null,selection=null,font=null,view=null,zoom=null;
   try{
    Guid dispatch=new Guid("00020400-0000-0000-C000-000000000046");
    if(Native.AccessibleObjectFromWindow(focus,0xFFFFFFF0,ref dispatch,out window)!=0||window==null)return published;
    selection=Get(window,word?"Selection":"RangeSelection");font=Get(selection,"Font");
    string name=Convert.ToString(Get(font,word?"NameFarEast":"Name"));double points=Convert.ToDouble(Get(font,"Size"));
    if(word)name=ThemeName(selection,name);
    if(string.IsNullOrEmpty(name)||!CaretGeometry.ValidPoints(points))return published;
    double percent;
    if(word){view=Get(window,"View");zoom=Get(view,"Zoom");percent=Convert.ToDouble(Get(zoom,"Percentage"));}
    else percent=Convert.ToDouble(Get(window,"Zoom"));
    if(percent<10||percent>500)return published;
    return new StyleInfo{FontName=name,SizePoints=points*percent/100,Ink=published==null?-1:published.Ink,Back=published==null?-1:published.Back,Bold=Convert.ToBoolean(Get(font,"Bold"))};
   }catch{return published;}
   finally{Release(zoom);Release(view);Release(font);Release(selection);Release(window);}
  }
 }
}
