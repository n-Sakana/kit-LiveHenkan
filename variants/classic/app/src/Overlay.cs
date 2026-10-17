// The uncommitted text drawn over the caret, and the small mode indicator. Runs on its own WPF thread.
using System;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LiveHenkan {
 // Shared colours, type and radii for the dark theme.
 public static class Theme {
  public static Color Hex(string s){return (Color)ColorConverter.ConvertFromString(s);}
  public static Color Rgb(int v){return Color.FromRgb((byte)(v>>16),(byte)(v>>8),(byte)v);}
  public static readonly Color Page=Hex("#17181A"),Canvas=Hex("#1C1D1F"),Surface=Hex("#232427"),Field=Hex("#303135"),Hover=Hex("#313236"),Line=Hex("#2C2E31"),LineStrong=Hex("#4B4D51");
  public static readonly Color Ink=Hex("#F2F3F4"),Ink2=Hex("#AEB1B6"),Ink3=Hex("#8F9298"),Accent=Hex("#3D9AFF"),AccentInk=Hex("#7EC0FF"),AccentOnLight=Hex("#0076EC");
  public static readonly Color Edge=Color.FromArgb(0x21,0xFF,0xFF,0xFF);
  public const double RadiusChip=6,RadiusControl=8,RadiusWindow=12;
  public static readonly FontFamily Font=new FontFamily("Inter, Segoe UI Variable Text, Segoe UI, Noto Sans JP, Yu Gothic UI, Meiryo UI");
  public static readonly FontFamily DocumentFont=new FontFamily("Yu Gothic UI, Meiryo UI, Segoe UI");
  public static SolidColorBrush Brush(Color c){SolidColorBrush b=new SolidColorBrush(c);b.Freeze();return b;}
  public static bool IsLight(Color c){return (0.299*c.R+0.587*c.G+0.114*c.B)>140;}
 }

 // A window that never takes focus and lets the mouse through.
 class GhostWindow : Window {
  protected IntPtr handle;
  public GhostWindow(){
   WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;Topmost=true;ShowActivated=false;Focusable=false;
   ResizeMode=ResizeMode.NoResize;SizeToContent=SizeToContent.WidthAndHeight;IsHitTestVisible=false;UseLayoutRounding=true;
   Left=-10000;Top=-10000;
   handle=new WindowInteropHelper(this).EnsureHandle();
   long ex=Native.GetWindowLongPtr(handle,-20).ToInt64();
   Native.SetWindowLongPtr(handle,-20,new IntPtr(ex|0x08000000L|0x20L|0x80L));
  }
  // The scale WPF itself draws this window with, so sizes in pixels come out as asked on any monitor.
  protected double Scale{get{HwndSource src=HwndSource.FromHwnd(handle);return src!=null&&src.CompositionTarget!=null?src.CompositionTarget.TransformToDevice.M11:1.0;}}
  protected void MoveTo(int x,int y){Native.SetWindowPos(handle,new IntPtr(-1),x,y,0,0,0x0001|0x0010|0x0200);}
  public void Reveal(){if(!IsVisible)Show();}
  public void Conceal(){if(IsVisible)Hide();}
 }

 sealed class OverlayWindow : GhostWindow {
  const double UnderGap=1,UnderWidth=1;
  readonly Border frame=new Border(),under=new Border(); readonly TextBlock text=new TextBlock(); readonly Rectangle endCaret=new Rectangle(),focusBar=new Rectangle();
  int focusStart=-1,focusLength; int placedX,placedY,placedHeight; Color inkColor,backColor,lineColor;
  CaretInfo anchor; int session=-1,anchorSession=-2,sampledSession=-3; bool positioned,inline,holding; double shiftPx,lineDip,padTop; string shown="";
  Color sampled;
  // A single HWND can host differently styled editors. Styles belong to a composition, never to the HWND.
  StyleInfo currentStyle;int styleSession=-1;
  readonly DispatcherTimer fallback=new DispatcherTimer(),hold=new DispatcherTimer();
  public bool Composing; readonly DoubleAnimationUsingKeyFrames blink;
  public string Inspect(){return "visible="+IsVisible+"\tx="+placedX+"\ty="+placedY+"\theight="+placedHeight+"\tfont="+text.FontFamily.Source+"\tfontDip="+text.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)+"\tscale="+Scale.ToString(System.Globalization.CultureInfo.InvariantCulture)+"\tanchor="+anchor.Found+"\tsource="+anchor.Source;}

  public OverlayWindow(){
   StackPanel row=new StackPanel{Orientation=Orientation.Horizontal};
   Grid lined=new Grid{VerticalAlignment=VerticalAlignment.Top};
   under.Child=text;lined.Children.Add(under);
   // The stretch being chosen gets a heavier line and a tint, like the selected clause on macOS.
   focusBar.Height=2;focusBar.HorizontalAlignment=HorizontalAlignment.Left;focusBar.VerticalAlignment=VerticalAlignment.Bottom;focusBar.Visibility=Visibility.Collapsed;
   lined.Children.Add(focusBar);
   endCaret.Width=1;endCaret.VerticalAlignment=VerticalAlignment.Bottom;
   blink=new DoubleAnimationUsingKeyFrames{Duration=TimeSpan.FromMilliseconds(1060),RepeatBehavior=RepeatBehavior.Forever};
   blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1,KeyTime.FromPercent(0)));blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0,KeyTime.FromPercent(.5)));
   // A running animation keeps the whole drawing loop awake, so the caret blinks only while the text is on screen.
   IsVisibleChanged+=delegate{endCaret.BeginAnimation(UIElement.OpacityProperty,IsVisible?blink:null);};
   TextOptions.SetTextRenderingMode(text,TextRenderingMode.Grayscale);
   row.Children.Add(lined);row.Children.Add(endCaret);frame.Child=row;Content=frame;
   // No caret was reported in time: show the text as a small panel at the bottom of the window instead.
   fallback.Interval=TimeSpan.FromMilliseconds(120);
   fallback.Tick+=delegate{fallback.Stop();if(Composing&&!positioned){StylePanel();positioned=true;Place();Reveal();}};
   // The first time in a window the text style is still being asked for; do not show a guess that changes a moment later.
   hold.Interval=TimeSpan.FromMilliseconds(70);
   hold.Tick+=delegate{hold.Stop();if(holding){holding=false;if(Composing&&positioned){Place();Reveal();}}};
  }
  void Paint(Color back,Color ink,Color line){
   frame.Background=Theme.Brush(back);text.Foreground=Theme.Brush(ink);endCaret.Fill=Theme.Brush(ink);under.BorderBrush=Theme.Brush(line);focusBar.Fill=Theme.Brush(line);
   inkColor=ink;backColor=back;lineColor=line;
   SetText(shown,true);
  }
  // Writes the text, with the stretch in focus tinted. Runs again when the colours change.
  void SetText(string value,bool force){
   if(!force&&value==shown&&focusStart==shownFocus&&focusLength==shownFocusLength)return;
   shown=value;shownFocus=focusStart;shownFocusLength=focusLength;
   text.Inlines.Clear();
   if(focusStart<0||focusStart+focusLength>value.Length||focusLength==0){text.Text=value;focusBar.Visibility=Visibility.Collapsed;endCaret.Visibility=Visibility.Visible;return;}
   text.Inlines.Add(new System.Windows.Documents.Run(value.Substring(0,focusStart)));
   text.Inlines.Add(new System.Windows.Documents.Run(value.Substring(focusStart,focusLength)){Background=Theme.Brush(Color.FromArgb(0x38,lineColor.R,lineColor.G,lineColor.B))});
   text.Inlines.Add(new System.Windows.Documents.Run(value.Substring(focusStart+focusLength)));
   double scale=Scale;
   focusBar.Margin=new Thickness(TextWidthPx(value.Substring(0,focusStart))/scale,0,0,0);
   focusBar.Width=Math.Max(1,TextWidthPx(value.Substring(focusStart,focusLength))/scale);
   focusBar.Visibility=Visibility.Visible;endCaret.Visibility=Visibility.Hidden;
  }
  int shownFocus=-2,shownFocusLength;
  // Screen position of the start of the stretch in focus, for the candidate list. False while nothing is shown.
  public bool FocusPoint(out int x,out int top,out int bottom){
   x=placedX;top=placedY;bottom=placedY+placedHeight;
   if(!Composing||!positioned||holding||!IsVisible)return false;
   if(focusStart>0&&focusStart<=shown.Length)x+=(int)Math.Round(TextWidthPx(shown.Substring(0,focusStart)));
   if(!inline)x+=(int)Math.Round(10*Scale);
   return true;
  }
  void StylePanel(){
   inline=false;
   Paint(Theme.Surface,Theme.Ink,Theme.Accent);
   frame.BorderBrush=Theme.Brush(Theme.Edge);frame.BorderThickness=new Thickness(1);frame.CornerRadius=new CornerRadius(Theme.RadiusControl);frame.Padding=new Thickness(10,6,10,7);
   text.FontFamily=Theme.Font;text.FontSize=16;text.FontWeight=FontWeights.Normal;text.LineHeight=double.NaN;text.LineStackingStrategy=LineStackingStrategy.MaxHeight;
   TextOptions.SetTextFormattingMode(text,TextFormattingMode.Display);
   under.BorderThickness=new Thickness(0,0,0,UnderWidth);under.Padding=new Thickness(0,0,0,UnderGap+1);
   endCaret.Height=20;endCaret.Margin=new Thickness(1,0,0,UnderGap+1+UnderWidth);
  }
  static string JapaneseFallback(string name){
   string n=name.ToLowerInvariant();
   if(n.Contains("consolas")||n.Contains("courier")||n.Contains("cascadia")||n.Contains("lucida console")||n.Contains("mono"))return "MS Gothic";
   if(n.Contains("mincho")||n.Contains("明朝")||n.Contains("serif")||n.Contains("times")||n.Contains("century"))return "Yu Mincho";
   return "Yu Gothic UI";
  }
  // Dresses the text like the document around the caret, so it reads as if it were already in it.
  void StyleInline(CaretInfo c,StyleInfo style){
   inline=true;
   double scale=Scale,caretDip=c.Height/scale,fontDip;FontFamily family;bool known=style!=null&&style.FontName!=null;
   if(known){
    family=new FontFamily(style.FontName+", "+JapaneseFallback(style.FontName)+", Yu Gothic UI");
    double spacing=1.2;try{spacing=new FontFamily(style.FontName).LineSpacing;}catch{}
    fontDip=CaretGeometry.FontDip(style,caretDip,spacing);
    lineDip=fontDip*spacing;
   }else{
    // Nothing published: a spreadsheet cell is 11 pt Yu Gothic unless the user changed it, anything else is guessed from the caret.
    bool sheet=Native.ClassOf(c.Window).StartsWith("EXCEL",StringComparison.Ordinal);
    family=sheet?new FontFamily("Yu Gothic, Yu Gothic UI, Meiryo UI"):Theme.DocumentFont;
    fontDip=sheet?11*96.0/72.0:CaretGeometry.FontDip(null,caretDip,1.3);
    lineDip=Math.Max(caretDip,fontDip);
   }
   if(sampledSession!=anchorSession){sampled=Sample(c);sampledSession=anchorSession;}
   // The screen is the better witness for the background: some applications publish black for "not set".
   Color back=sampled;
   bool light=Theme.IsLight(back);
   Color ink=known&&style.Ink>=0&&Theme.IsLight(Theme.Rgb(style.Ink))!=light?Theme.Rgb(style.Ink):(light?Theme.Hex("#111111"):Theme.Hex("#F2F2F2"));
   Paint(back,ink,light?Theme.AccentOnLight:Theme.Accent);
   // Room above the caret is taken only when the glyphs are taller than the caret itself.
   padTop=lineDip>caretDip+0.5?2:0;
   frame.BorderBrush=null;frame.BorderThickness=new Thickness(0);frame.CornerRadius=new CornerRadius(0);frame.Padding=new Thickness(0,padTop,1,0);
   text.FontFamily=family;text.FontSize=fontDip;text.FontWeight=known&&style.Bold?FontWeights.Bold:FontWeights.Normal;
   text.LineHeight=lineDip;text.LineStackingStrategy=LineStackingStrategy.BlockLineHeight;
   TextOptions.SetTextFormattingMode(text,TextFormattingMode.Ideal);
   under.BorderThickness=new Thickness(0,0,0,UnderWidth);under.Padding=new Thickness(0,0,0,UnderGap);
   endCaret.Height=lineDip;endCaret.Margin=new Thickness(1,0,0,UnderGap+UnderWidth);
  }
  // The colour the document shows around the caret: the most frequent pixel of a thin strip beside it.
  static Color Sample(CaretInfo c){
   Color result=Colors.White;
   try{
    int w=14,h=Math.Max(4,c.Height);
    using(System.Drawing.Bitmap bmp=new System.Drawing.Bitmap(w,h)){
     using(System.Drawing.Graphics g=System.Drawing.Graphics.FromImage(bmp))g.CopyFromScreen(c.Left+3,c.Top,0,0,new System.Drawing.Size(w,h));
     Dictionary<int,int> count=new Dictionary<int,int>();int best=0,bestN=0;
     for(int y=0;y<h;y+=2)for(int x=0;x<w;x+=2){int argb=bmp.GetPixel(x,y).ToArgb();int n;count.TryGetValue(argb,out n);count[argb]=++n;if(n>bestN){bestN=n;best=argb;}}
     result=Theme.Rgb(best&0xFFFFFF);
    }
   }catch{}
   return result;
  }
  double TextWidthPx(string s){
   FormattedText ft=new FormattedText(s,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(text.FontFamily,text.FontStyle,text.FontWeight,text.FontStretch),text.FontSize,Brushes.Black,null,TextFormattingMode.Ideal);
   return ft.WidthIncludingTrailingWhitespace*Scale;
  }
  public void SetCaret(CaretInfo c,int forSession,bool afterCommit){
   if(forSession<session)return;
   bool hadInline=inline&&anchorSession==forSession&&anchor.Found;
   if(!c.Found&&hadInline)return;   // keep the place we have
   anchor=c;anchorSession=forSession;
   if(c.Found){
    shiftPx=0;
    bool known=styleSession==forSession;
    StyleInline(c,known?currentStyle:null);
    if(!hadInline){holding=!known&&!afterCommit;hold.Stop();if(holding)hold.Start();}
   }else{StylePanel();holding=false;}
   if(Composing&&session==forSession){positioned=true;fallback.Stop();if(!holding){Place();Reveal();}}
  }
  public void SetStyle(StyleInfo style,IntPtr window,int forSession){
   if(forSession<session&&forSession<anchorSession)return;
   bool ours=anchorSession==forSession&&anchor.Found;
   currentStyle=style;styleSession=forSession;
   if(ours&&inline)StyleInline(anchor,style);
   if(holding){holding=false;hold.Stop();}
   if(Composing&&positioned&&session==forSession){Place();Reveal();}
  }
  public void Committed(string committed){
   if(!inline||!anchor.Found||committed.Length==0)return;
   double w=TextWidthPx(committed);
   shiftPx+=w;
  }
  public void Apply(View v){
   if(!v.Active){Composing=false;fallback.Stop();hold.Stop();holding=false;shown="";shownFocus=-2;positioned=false;Conceal();return;}
   if(!Composing||v.Session!=session){
    Composing=true;session=v.Session;shiftPx=0;
    positioned=anchorSession==session;   // the caret may have been reported first
    if(!positioned)fallback.Start();
   }
   focusStart=v.FocusStart;focusLength=v.FocusLength;
   SetText(v.Text,false);
   if(positioned&&!holding){Place();Reveal();}
  }
  void Place(){
   UpdateLayout();
   double scale=Scale;int widthPx=(int)Math.Ceiling(ActualWidth*scale),heightPx=(int)Math.Ceiling(ActualHeight*scale);
   int x,y;
   if(inline&&anchor.Found){
    // A native caret can include paragraph leading. Anchor the text at its top;
    // stretching a line to the full caret height moves the baseline in Word.
    x=anchor.Left+(int)Math.Round(shiftPx);
    y=anchor.Top;
   }else{
    Native.Rect r;IntPtr fg=Native.GetForegroundWindow();
    if(fg!=IntPtr.Zero&&Native.GetWindowRect(fg,out r)&&r.right>r.left){x=r.left+(int)(24*scale);y=r.bottom-heightPx-(int)(28*scale);}
    else{System.Drawing.Rectangle a=System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;x=a.Left+(int)(24*scale);y=a.Bottom-heightPx-(int)(28*scale);}
   }
   System.Drawing.Rectangle area=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x,y)).WorkingArea;
   if(x+widthPx>area.Right)x=Math.Max(area.Left,area.Right-widthPx);
   if(y+heightPx>area.Bottom)y=area.Bottom-heightPx;
   placedX=x;placedY=y;placedHeight=heightPx;
   MoveTo(x,y);
  }
 }

 // The list of other conversions for the stretch in focus. Nine rows to a page, as on macOS and MS-IME.
 sealed class CandidateWindow : GhostWindow {
  const int Rows=Composer.PageSize; const double Shadow=14;
  readonly Border frame=new Border(); readonly StackPanel panel=new StackPanel(); readonly TextBlock pageLabel=new TextBlock();
  readonly List<Border> rows=new List<Border>(); List<Cand> shown; int shownSelected=-1;
  static readonly Brush tint=Theme.Brush(Color.FromArgb(0x3D,0x3D,0x9A,0xFF)),clear=Brushes.Transparent;
  public CandidateWindow(){
   frame.Background=Theme.Brush(Theme.Surface);frame.BorderBrush=Theme.Brush(Theme.Edge);frame.BorderThickness=new Thickness(1);frame.CornerRadius=new CornerRadius(10);frame.Padding=new Thickness(5);
   frame.Margin=new Thickness(Shadow);
   frame.Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=18,ShadowDepth=5,Direction=270,Opacity=0.38,Color=Colors.Black};
   frame.Child=panel;Content=frame;
   pageLabel.FontFamily=Theme.Font;pageLabel.FontSize=13;pageLabel.Foreground=Theme.Brush(Theme.Ink3);pageLabel.HorizontalAlignment=HorizontalAlignment.Right;pageLabel.Margin=new Thickness(0,3,9,2);
  }
  static TextBlock Label(double size,Color color){return new TextBlock{FontFamily=Theme.Font,FontSize=size,Foreground=Theme.Brush(color),VerticalAlignment=VerticalAlignment.Center};}
  // The list keeps one size for all its pages: as many rows as the fullest page and as wide as the widest candidate.
  void Build(List<Cand> list){
   panel.Children.Clear();rows.Clear();
   double widest=0,widestNote=0;
   foreach(Cand c in list){
    FormattedText t=new FormattedText(c.Text,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(Theme.Font,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),16,Brushes.White,null,TextFormattingMode.Display);
    widest=Math.Max(widest,t.WidthIncludingTrailingWhitespace);
    if(c.Note.Length>0){FormattedText n=new FormattedText(c.Note,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(Theme.Font,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),13,Brushes.White,null,TextFormattingMode.Display);widestNote=Math.Max(widestNote,n.WidthIncludingTrailingWhitespace+18);}
   }
   double width=Math.Max(150,Math.Min(520,22+widest+widestNote+22));
   int count=Math.Min(Rows,list.Count);
   for(int i=0;i<count;i++){
    Grid g=new Grid();
    g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(22)});g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});g.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
    TextBlock number=Label(13,Theme.Ink3);number.Text=(i+1).ToString();
    TextBlock text=Label(16,Theme.Ink);text.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(text,1);
    TextBlock note=Label(13,Theme.Ink3);note.Margin=new Thickness(18,0,0,0);Grid.SetColumn(note,2);
    g.Children.Add(number);g.Children.Add(text);g.Children.Add(note);
    Border row=new Border{CornerRadius=new CornerRadius(Theme.RadiusChip),Padding=new Thickness(8,0,9,1),Height=30,Width=width,Child=g};
    rows.Add(row);panel.Children.Add(row);
   }
   if(list.Count>Rows)panel.Children.Add(pageLabel);
  }
  void Fill(List<Cand> list,int selected){
   int page=selected/Rows,first=page*Rows;
   for(int i=0;i<rows.Count;i++){
    Grid g=(Grid)rows[i].Child;int index=first+i;bool has=index<list.Count,on=has&&index==selected;
    ((TextBlock)g.Children[0]).Text=has?(i+1).ToString():"";((TextBlock)g.Children[0]).Foreground=Theme.Brush(on?Theme.AccentInk:Theme.Ink3);
    ((TextBlock)g.Children[1]).Text=has?list[index].Text:"";
    ((TextBlock)g.Children[2]).Text=has?list[index].Note:"";
    rows[i].Background=on?tint:clear;
   }
   if(list.Count>Rows)pageLabel.Text=(page+1)+" / "+((list.Count+Rows-1)/Rows);
  }
  // `x` is where the stretch in focus starts; the list hangs below the line, or sits above it near the bottom of the screen.
  public void Update(List<Cand> list,int selected,int x,int lineTop,int lineBottom){
   if(list==null||list.Count==0){shown=null;Conceal();return;}
   if(!ReferenceEquals(list,shown)){Build(list);shown=list;shownSelected=-1;}
   if(selected!=shownSelected){Fill(list,selected);shownSelected=selected;}
   UpdateLayout();
   double scale=Scale;int w=(int)Math.Ceiling(ActualWidth*scale),h=(int)Math.Ceiling(ActualHeight*scale),pad=(int)Math.Round(Shadow*scale);
   int left=x-pad-(int)Math.Round(8*scale),top=lineBottom+(int)Math.Round(4*scale)-pad;
   System.Drawing.Rectangle area=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x,lineTop)).WorkingArea;
   if(top+h-pad>area.Bottom)top=lineTop-(int)Math.Round(4*scale)-h+pad;
   left=Math.Max(area.Left-pad,Math.Min(left,area.Right-w+pad));
   MoveTo(left,top);Reveal();
  }
 }

 // The marks under text that is written into the document itself: the underline of uncommitted text and the
 // tint and heavier line of the stretch in focus. The text is the document's own; only the marks are drawn here.
 sealed class DecorWindow : GhostWindow {
  readonly Canvas canvas=new Canvas(); bool light=true; int sampledSession=-1;
  public DecorWindow(){SizeToContent=SizeToContent.Manual;Width=10;Height=10;Content=canvas;}
  static bool LightAt(int x,int y,int height){
   try{
    int w=8,h=Math.Max(4,height);
    using(System.Drawing.Bitmap bmp=new System.Drawing.Bitmap(w,h)){
     using(System.Drawing.Graphics g=System.Drawing.Graphics.FromImage(bmp))g.CopyFromScreen(x,y,0,0,new System.Drawing.Size(w,h));
     double sum=0;int n=0;
     for(int j=0;j<h;j+=2)for(int i=0;i<w;i+=2){System.Drawing.Color c=bmp.GetPixel(i,j);sum+=0.299*c.R+0.587*c.G+0.114*c.B;n++;}
     return n==0||sum/n>140;
    }
   }catch{return true;}
  }
  void Add(double left,double top,double width,double height,Brush fill){
   Rectangle r=new Rectangle{Width=Math.Max(0,width),Height=Math.Max(0,height),Fill=fill,SnapsToDevicePixels=true};
   RenderOptions.SetEdgeMode(r,EdgeMode.Aliased);
   Canvas.SetLeft(r,left);Canvas.SetTop(r,top);canvas.Children.Add(r);
  }
  public void Apply(InlineGeometry g,int session){
   if(g==null||!g.Ok){Conceal();return;}
   int left=int.MaxValue,top=int.MaxValue,right=int.MinValue,bottom=int.MinValue;
   foreach(LineBox b in g.Text){left=Math.Min(left,b.Left);top=Math.Min(top,b.Bottom-Math.Max(g.LineHeight,4));right=Math.Max(right,b.Right);bottom=Math.Max(bottom,b.Bottom);}
   foreach(LineBox b in g.Focus){left=Math.Min(left,b.Left);right=Math.Max(right,b.Right);}
   if(right<=left||bottom<=top){Conceal();return;}
   if(session!=sampledSession){
    // Light or dark document: looked at once, to the right of the text where the page is usually bare.
    LineBox last=g.Text[g.Text.Count-1];
    light=LightAt(last.Right+6,last.Bottom-Math.Max(g.LineHeight,4),Math.Max(g.LineHeight,4));sampledSession=session;
   }
   Color line=light?Theme.AccentOnLight:Theme.Accent;
   Brush solid=Theme.Brush(line),tint=Theme.Brush(Color.FromArgb(0x38,line.R,line.G,line.B));
   double scale=Scale;int thin=Math.Max(1,(int)Math.Round(scale)),thick=Math.Max(2,(int)Math.Round(2*scale));
   canvas.Children.Clear();
   foreach(LineBox b in g.Text)Add((b.Left-left)/scale,(b.Bottom-thin-top)/scale,(b.Right-b.Left)/scale,thin/scale,solid);
   foreach(LineBox b in g.Focus){
    int h=Math.Max(g.LineHeight,4);
    Add((b.Left-left)/scale,(b.Bottom-h-top)/scale,(b.Right-b.Left)/scale,h/scale,tint);
    Add((b.Left-left)/scale,(b.Bottom-thick-top)/scale,(b.Right-b.Left)/scale,thick/scale,solid);
   }
   Width=(right-left)/scale;Height=(bottom-top)/scale;
   MoveTo(left,top);Reveal();
  }
 }

 sealed class HudWindow : GhostWindow {
  readonly Border frame=new Border(); readonly TextBlock label=new TextBlock(); readonly DispatcherTimer timer=new DispatcherTimer();
  public HudWindow(){
   frame.Background=Theme.Brush(Theme.Surface);frame.BorderBrush=Theme.Brush(Theme.Edge);frame.BorderThickness=new Thickness(1);frame.CornerRadius=new CornerRadius(Theme.RadiusControl);frame.Padding=new Thickness(12,6,12,7);
   label.FontFamily=Theme.Font;label.FontSize=15;label.Foreground=Theme.Brush(Theme.Ink);
   frame.Child=label;Content=frame;
   timer.Tick+=delegate{
    timer.Stop();
    DoubleAnimation fade=new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(220));
    fade.Completed+=delegate{if(!timer.IsEnabled)Conceal();};
    BeginAnimation(OpacityProperty,fade);
   };
  }
  public void Flash(string message,CaretInfo at,int milliseconds){
   label.Text=message;BeginAnimation(OpacityProperty,null);Opacity=1;
   UpdateLayout();double scale=Scale;int w=(int)Math.Ceiling(ActualWidth*scale),h=(int)Math.Ceiling(ActualHeight*scale);
   int x,y;
   if(at.Found){x=at.Left;y=at.Top+at.Height+(int)(8*scale);}
   else{
    Native.Rect r;IntPtr fg=Native.GetForegroundWindow();
    if(fg!=IntPtr.Zero&&Native.GetWindowRect(fg,out r)&&r.right>r.left){x=(r.left+r.right-w)/2;y=r.bottom-h-(int)(48*scale);}
    else{System.Drawing.Rectangle a=System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;x=a.Left+(a.Width-w)/2;y=a.Bottom-h-(int)(64*scale);}
   }
   System.Drawing.Rectangle area=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x,y)).WorkingArea;
   x=Math.Max(area.Left+4,Math.Min(x,area.Right-w-4));y=Math.Max(area.Top+4,Math.Min(y,area.Bottom-h-4));
   MoveTo(x,y);Reveal();
   timer.Stop();timer.Interval=TimeSpan.FromMilliseconds(milliseconds);timer.Start();
  }
 }

 public sealed class Ui {
  Thread thread; Dispatcher dispatcher; OverlayWindow overlay; HudWindow hud; CandidateWindow candidates; DecorWindow decor; readonly ManualResetEvent ready=new ManualResetEvent(false);
  View current; InlineGeometry inlineGeometry; bool kana;
  View pending; int scheduled; string failure;
  public Action<long> Rendered;   // called on the UI thread once a view has been laid out
  public Action<string> Failed;   // a drawing error is reported and survived: typing must go on
  public void Start(){
   thread=new Thread(Loop);thread.IsBackground=true;thread.Name="ui";thread.SetApartmentState(ApartmentState.STA);thread.Start();
   if(!ready.WaitOne(15000))throw new TimeoutException("UI thread");
   if(failure!=null)throw new InvalidOperationException(failure);
  }
  void Loop(){
   try{
    Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
    dispatcher=Dispatcher.CurrentDispatcher;
    dispatcher.UnhandledException+=delegate(object s,DispatcherUnhandledExceptionEventArgs e){e.Handled=true;Action<string> f=Failed;if(f!=null)f(e.Exception.ToString());};
    overlay=new OverlayWindow();hud=new HudWindow();candidates=new CandidateWindow();decor=new DecorWindow();
   }catch(Exception ex){failure=ex.ToString();ready.Set();return;}
   ready.Set();
   Dispatcher.Run();
  }
  public void Stop(){if(dispatcher!=null)dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);if(thread!=null)thread.Join(2000);}
  public Dispatcher Dispatcher{get{return dispatcher;}}
  public string Inspect(){return (string)dispatcher.Invoke(new Func<string>(delegate{return overlay.Inspect();}));}
  void Post(Action a){dispatcher.BeginInvoke(DispatcherPriority.Send,a);}
  public void Show(View v){
   Volatile.Write(ref pending,v);
   if(Interlocked.Exchange(ref scheduled,1)==0)Post(Apply);
  }
  void Apply(){
   Interlocked.Exchange(ref scheduled,0);
   View v=Volatile.Read(ref pending);if(v==null)return;
   overlay.Apply(v);current=v;inlineGeometry=null;decor.Apply(null,0);
   Candidates();
   Action<long> done=Rendered;
   if(done!=null){long serial=v.Serial;dispatcher.BeginInvoke(DispatcherPriority.Loaded,(Action)delegate{done(serial);});}
  }
  void Candidates(){
   int x,top,bottom;
   if(current==null||!current.Active||current.Cands==null){candidates.Update(null,0,0,0,0);return;}
   if(inlineGeometry!=null&&inlineGeometry.Ok){
    // Text written into the document: the list hangs under the stretch in focus, or under the start of the text.
    LineBox b=inlineGeometry.Focus.Count>0?inlineGeometry.Focus[0]:inlineGeometry.Text[0];
    candidates.Update(current.Cands,current.Selected,b.Left,b.Bottom-Math.Max(inlineGeometry.LineHeight,4),b.Bottom+2);
   }else if(overlay.FocusPoint(out x,out top,out bottom))candidates.Update(current.Cands,current.Selected,x,top,bottom);
   else candidates.Update(null,0,0,0,0);
  }
  // A view that the writer has put into the document, with where it now sits on the screen.
  public void Inline(View v,InlineGeometry g){
   Post(delegate{
    current=v;inlineGeometry=v.Active?g:null;
    decor.Apply(inlineGeometry,v.Session);
    Candidates();
   });
  }
  public void EndInline(){Post(delegate{inlineGeometry=null;decor.Apply(null,0);Candidates();});}
  public void Caret(CaretInfo c,int session,bool afterCommit){Post(delegate{overlay.SetCaret(c,session,afterCommit);Candidates();});}
  public void Style(StyleInfo style,IntPtr window,int session){Post(delegate{overlay.SetStyle(style,window,session);Candidates();});}
  public void Committed(string text){Post(delegate{overlay.Committed(text);});}
  public void Flash(string message,CaretInfo at,int milliseconds){Post(delegate{hud.Flash(message,at,milliseconds);});}

  DictionaryWindow words; SettingsWindow options;
  // In our own windows the input mode is shown in the title row, where it covers nothing.
  public void Mode(bool isKana){Post(delegate{kana=isKana;if(words!=null)words.ShowMode(kana);if(options!=null)options.ShowMode(kana);});}
  public void ShowWords(UserDictionary dict,Action<bool> readingFocus){
   Post(delegate{
    if(words==null){words=new DictionaryWindow(dict);words.ReadingFocus=readingFocus;words.Closed+=delegate{words=null;};}
    words.ShowMode(kana);words.Present();
   });
  }
  public void ShowSettings(Settings settings,UserDictionary dict,Action changed,string footer){
   Post(delegate{
    if(options==null){options=new SettingsWindow(settings,dict,changed,footer);options.Closed+=delegate{options=null;};}
    options.ShowMode(kana);options.Present();
   });
  }
  public void CloseWindows(){Post(delegate{if(words!=null)words.Close();if(options!=null)options.Close();});}
 }
}
