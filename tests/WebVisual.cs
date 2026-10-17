// Exercises the real caret/style probe and overlay without a keyboard hook,
// resident, model, or connection to any existing Henkan process.
using System;
using System.Reflection;
using System.Threading;
using System.Windows.Threading;
namespace Henkan.Tests {
 public sealed class WebVisual : IDisposable {
  readonly Thread thread;readonly ManualResetEvent ready=new ManualResetEvent(false);
  Dispatcher dispatcher;OverlayWindow window;int session;View shown;
  public string Style;
  public WebVisual(){
   thread=new Thread(delegate(){dispatcher=Dispatcher.CurrentDispatcher;window=new OverlayWindow();ready.Set();Dispatcher.Run();});
   thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();ready.WaitOne();
  }
  public string Show(string text,long target){
   if(Native.GetForegroundWindow().ToInt64()!=target)throw new InvalidOperationException("Our test editor lost focus");
   IntPtr focus=Native.FocusWindow();object[] args={focus,null,null};
   MethodInfo find=typeof(Probe).GetMethod("FindTarget",BindingFlags.Static|BindingFlags.NonPublic);
   for(int retry=0;retry<30;retry++){
    if(Native.GetForegroundWindow().ToInt64()!=target)throw new InvalidOperationException("Our test editor lost focus");
    args[0]=focus=Native.FocusWindow();find.Invoke(null,args);if(((CaretInfo)args[1]).Found)break;Thread.Sleep(100);
   }
   CaretInfo caret=(CaretInfo)args[1];StyleInfo style=(StyleInfo)args[2];
   Style=style==null?"none":style.FontName+" "+style.SizePoints+"pt weight="+style.Weight+" web="+style.Web;
   if(!caret.Found)throw new InvalidOperationException("Our test editor did not publish a caret");
   string result=null;int current=++session;
   dispatcher.Invoke((Action)delegate{window.SetCaret(caret,current,false);window.SetStyle(style,focus,current);shown=new View{Active=true,Session=current,Text=text};window.Apply(shown);result=window.Inspect();});
   return result;
  }
  public string Inspect(){string result=null;dispatcher.Invoke((Action)delegate{window.Apply(shown);result=window.Inspect();});return result;}
  public void Hide(){dispatcher.Invoke((Action)delegate{window.Apply(new View{Active=false});});}
  public void Dispose(){dispatcher.Invoke((Action)delegate{window.Close();dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);});thread.Join(3000);ready.Dispose();}
 }
}
