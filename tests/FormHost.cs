// A small window of the test's own with a classic edit box, a password box and a rich edit box.
using System;
using System.Threading;
using System.Windows.Forms;

namespace LiveHenkan.Tests {
 // The rich edit control of msftedit.dll (class RICHEDIT50W), which many programs use; Windows Forms on its own
 // still creates the older one (RichEdit20W).
 sealed class ModernRichBox : RichTextBox {
  [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern IntPtr LoadLibrary(string name);
  protected override CreateParams CreateParams{get{CreateParams p=base.CreateParams;if(LoadLibrary("msftedit.dll")!=IntPtr.Zero)p.ClassName="RICHEDIT50W";return p;}}
 }
 public sealed class FormHost : IDisposable {
  Form form; TextBox plain,secret; RichTextBox rich,older,modern; Thread thread; readonly ManualResetEvent ready=new ManualResetEvent(false);
  public long Hwnd;
  public volatile string Mouse="";   // what the edit box saw: D<clicks> for a button press, U for a release, X for a double click
  public FormHost():this(160,160){}
  public FormHost(int left,int top){
   thread=new Thread(delegate(){
    form=new Form{Text="LiveHenkan test form",Width=560,Height=360,StartPosition=FormStartPosition.Manual,Left=left,Top=top,TopMost=true};
    plain=new TextBox{Left=20,Top=20,Width=500,Font=new System.Drawing.Font("Yu Gothic UI",12f)};
    secret=new TextBox{Left=20,Top=80,Width=500,UseSystemPasswordChar=true,Font=new System.Drawing.Font("Yu Gothic UI",12f)};
    plain.MouseDown+=delegate(object o,MouseEventArgs e){Mouse+="D"+e.Clicks;};plain.MouseUp+=delegate{Mouse+="U";};plain.DoubleClick+=delegate{Mouse+="X";};
    older=new RichTextBox{Left=20,Top=140,Width=500,Height=70,Font=new System.Drawing.Font("Yu Gothic UI",12f),DetectUrls=false};
    modern=new ModernRichBox{Left=20,Top=225,Width=500,Height=70,Font=new System.Drawing.Font("Yu Gothic UI",12f),DetectUrls=false};
    rich=older;
    form.Controls.Add(plain);form.Controls.Add(secret);form.Controls.Add(older);form.Controls.Add(modern);
    form.Shown+=delegate{Hwnd=form.Handle.ToInt64();ready.Set();};
    Application.Run(form);
   });
   thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();
   if(!ready.WaitOne(8000))throw new TimeoutException("test form");
  }
  T On<T>(Func<T> f){return (T)form.Invoke(f);}
  public void FocusPlain(){form.Invoke((Action)delegate{plain.Focus();plain.SelectionStart=plain.TextLength;});}
  public void FocusSecret(){form.Invoke((Action)delegate{secret.Focus();});}
  public string Plain{get{return On<string>(delegate{return plain.Text;});}}
  public string Secret{get{return On<string>(delegate{return secret.Text;});}}
  public string Selection{get{return On<string>(delegate{return plain.SelectionStart+"+"+plain.SelectionLength;});}}
  public void Clear(){form.Invoke((Action)delegate{plain.Text="";secret.Text="";});}
  // The classic edit box written to from outside the way another program would have to: by replacing its selection.
  [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern IntPtr SendMessage(IntPtr h,uint msg,IntPtr w,string l);
  public void PlainInsert(string text,bool undoable){form.Invoke((Action)delegate{SendMessage(plain.Handle,0xC2,new IntPtr(undoable?1:0),text);});}
  public bool PlainCanUndo{get{return On<bool>(delegate{return plain.CanUndo;});}}
  public void PlainUndo(){form.Invoke((Action)delegate{plain.Undo();});}
  // The rich edit boxes: text with \n between lines, the caret as a character position. One of the two is in use.
  public void UseRich(bool newer){form.Invoke((Action)delegate{rich=newer?modern:older;});}
  public void SetRich(string text,int caret,int selected){form.Invoke((Action)delegate{rich.Text=text;rich.ClearUndo();rich.Focus();rich.Select(caret,selected);});}
  public string Rich{get{return On<string>(delegate{return rich.Text;});}}
  public string RichSelection{get{return On<string>(delegate{return rich.SelectionStart+"+"+rich.SelectionLength;});}}
  public string RichClass{get{return On<string>(delegate{return Native.ClassOf(rich.Handle);});}}
  public bool RichCanUndo{get{return On<bool>(delegate{return rich.CanUndo;});}}
  public void RichUndo(){form.Invoke((Action)delegate{rich.Undo();});}
  // Screen position of a point inside the plain box, for mouse tests.
  public int[] PlainPoint(int dx,int dy){return On<int[]>(delegate{System.Drawing.Point p=plain.PointToScreen(new System.Drawing.Point(dx,dy));return new int[]{p.X,p.Y};});}
  public void Dispose(){try{form.Invoke((Action)delegate{form.Close();});}catch{}thread.Join(2000);}
 }
}
