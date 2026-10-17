// Windows' in-box UI Automation COM API exposes TextPattern2, which .NET Framework's
// managed wrapper does not. In particular, a web editor may publish a caret even when empty.
using System;
using System.Runtime.InteropServices;

namespace LiveHenkan {
 static class AutomationCaret {
  // Prefixes of SDK interfaces; unused slots are deliberately never called.
  [ComImport,Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface Client {
   void CompareElements();void CompareRuntimeIds();void GetRootElement();void ElementFromHandle();void ElementFromPoint();
   Element GetFocusedElement();
  }
  [ComImport,Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface Element {
   void SetFocus();void GetRuntimeId();void FindFirst();void FindAll();void FindFirstBuildCache();void FindAllBuildCache();void BuildUpdatedCache();
   [return:MarshalAs(UnmanagedType.Struct)]object GetCurrentPropertyValue(int id);
   void GetCurrentPropertyValueEx();void GetCachedPropertyValue();void GetCachedPropertyValueEx();void GetCurrentPatternAs();void GetCachedPatternAs();
   [return:MarshalAs(UnmanagedType.IUnknown)]object GetCurrentPattern(int id);
  }
  [ComImport,Guid("506a921a-fcc9-409f-b23b-37eb74106872"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface Text2 {
   void RangeFromPoint();void RangeFromChild();void GetSelection();void GetVisibleRanges();void GetDocumentRange();void GetSupportedTextSelection();void RangeFromAnnotation();
   Range GetCaretRange([MarshalAs(UnmanagedType.Bool)]out bool active);
  }
  [ComImport,Guid("a543cc6a-f4ae-494b-8239-c814481187a8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface Range {
   void Clone();void Compare();void CompareEndpoints();void ExpandToEnclosingUnit();void FindAttribute();void FindText();
   [return:MarshalAs(UnmanagedType.Struct)]object GetAttributeValue(int id);
   [return:MarshalAs(UnmanagedType.SafeArray,SafeArraySubType=VarEnum.VT_R8)]double[] GetBoundingRectangles();
  }
  static void Release(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
  public static bool Find(IntPtr focus,out CaretInfo caret,out StyleInfo style){
   caret=new CaretInfo();style=null;Client client=null;Element element=null;object pattern=null;Range range=null;
   try{
    client=(Client)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e")));
    element=client.GetFocusedElement();if(element==null)return false;
    pattern=element.GetCurrentPattern(10024);Text2 text=pattern as Text2;if(text==null)return false;
    bool active;range=text.GetCaretRange(out active);if(!active||range==null)return false;
    double[] rects=range.GetBoundingRectangles();if(rects==null||rects.Length<4||!CaretGeometry.IsCaretRectangle(new System.Windows.Rect(rects[0],rects[1],Math.Max(0,rects[2]),Math.Max(0,rects[3]))))return false;
    caret=new CaretInfo{Found=true,Window=focus,Left=(int)Math.Round(rects[0]),Top=(int)Math.Round(rects[1]),Height=(int)Math.Round(rects[3]),Source="automation-caret"};
    try{
     object name=range.GetAttributeValue(40005),size=range.GetAttributeValue(40006);
     if(name is string&&size is double&&CaretGeometry.ValidPoints((double)size)){
      style=new StyleInfo{FontName=(string)name,SizePoints=(double)size};
      object ink=range.GetAttributeValue(40008),back=range.GetAttributeValue(40001),weight=range.GetAttributeValue(40007);
      if(ink is int)style.Ink=Rgb((int)ink);if(back is int)style.Back=Rgb((int)back);style.Bold=weight is int&&(int)weight>=600;
     }
    }catch{} // Font attributes are optional; keep a valid caret even if a provider rejects them.
    return focus==Native.FocusWindow();
   }catch{return false;}
   finally{Release(range);Release(pattern);Release(element);Release(client);}
  }
  static int Rgb(int value){return ((value&255)<<16)|(value&0xFF00)|((value>>16)&255);}
 }
}
