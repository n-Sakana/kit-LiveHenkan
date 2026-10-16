// Word registration and settings windows, with a shared dark theme built in code.
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LiveHenkan {
 public static class SettingsStore {
  // One "name=value" per line; unknown names are ignored so older and newer versions can share a file.
  public static void Load(string path,Settings s){
   if(!File.Exists(path))return;
   foreach(string line in File.ReadAllLines(path,Encoding.UTF8)){
    int i=line.IndexOf('=');if(i<=0)continue;
    string name=line.Substring(0,i).Trim(),value=line.Substring(i+1).Trim();
    System.Reflection.FieldInfo f=typeof(Settings).GetField(name);
    if(f==null)continue;
    try{
     if(f.FieldType==typeof(bool))f.SetValue(s,value=="1"||value.Equals("true",StringComparison.OrdinalIgnoreCase));
     else if(f.FieldType==typeof(int))f.SetValue(s,int.Parse(value));
    }catch{}
   }
  }
  public static void Save(string path,Settings s){
   StringBuilder b=new StringBuilder();
   foreach(System.Reflection.FieldInfo f in typeof(Settings).GetFields()){
    object v=f.GetValue(s);
    b.Append(f.Name).Append('=').Append(v is bool?((bool)v?"1":"0"):v.ToString()).Append("\r\n");
   }
   File.WriteAllText(path,b.ToString(),new UTF8Encoding(false));
  }
 }

 static class Styles {
  const string Xaml=@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style x:Key='Field' TargetType='TextBox'>
  <Setter Property='Foreground' Value='#F2F3F4'/><Setter Property='CaretBrush' Value='#F2F3F4'/><Setter Property='SelectionBrush' Value='#3D9AFF'/><Setter Property='FontSize' Value='15'/><Setter Property='Height' Value='38'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
   <Border x:Name='b' Background='#303135' BorderBrush='#29FFFFFF' BorderThickness='1' CornerRadius='8'><ScrollViewer x:Name='PART_ContentHost' Margin='9,0' VerticalAlignment='Center'/></Border>
   <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='#3D9AFF'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='Primary' TargetType='Button'>
  <Setter Property='Foreground' Value='#FFFFFF'/><Setter Property='FontSize' Value='15'/><Setter Property='Height' Value='36'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
   <Border x:Name='b' Background='#2F86E6' CornerRadius='8' Padding='16,0'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='0,0,0,1'/></Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#3D9AFF'/></Trigger>
    <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Background' Value='#2877CF'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='#7EC0FF'/><Setter TargetName='b' Property='BorderThickness' Value='1'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='Plain' TargetType='Button'>
  <Setter Property='Foreground' Value='#F2F3F4'/><Setter Property='FontSize' Value='14'/><Setter Property='Height' Value='34'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
   <Border x:Name='b' Background='#303135' CornerRadius='8' Padding='13,0'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='0,0,0,1'/></Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#3A3B40'/></Trigger>
    <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Background' Value='#2A2B2F'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='0.4'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='#3D9AFF'/><Setter TargetName='b' Property='BorderThickness' Value='1'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='Quiet' TargetType='Button'>
  <Setter Property='Foreground' Value='#AEB1B6'/><Setter Property='FontSize' Value='13'/><Setter Property='Focusable' Value='False'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
   <Border x:Name='b' Background='Transparent' CornerRadius='6'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
   <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#303135'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='Switch' TargetType='CheckBox'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
   <Grid Background='Transparent' Width='42' Height='24'>
    <Border x:Name='track' CornerRadius='12' Background='#4B4D51'/>
    <Ellipse x:Name='thumb' Width='18' Height='18' Fill='#F2F3F4' HorizontalAlignment='Left' Margin='3,0,0,0'/>
    <Border x:Name='ring' CornerRadius='12' BorderBrush='#7EC0FF' BorderThickness='1' Opacity='0'/>
   </Grid>
   <ControlTemplate.Triggers>
    <Trigger Property='IsChecked' Value='True'><Setter TargetName='track' Property='Background' Value='#2F86E6'/><Setter TargetName='thumb' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='thumb' Property='Margin' Value='0,0,3,0'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='ring' Property='Opacity' Value='1'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ScrollBar'>
  <Setter Property='Width' Value='8'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
   <Grid Background='Transparent'><Track x:Name='PART_Track' IsDirectionReversed='True'>
    <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='#4B4D51' CornerRadius='3' Margin='2,0,0,0'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
   </Track></Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='Words' TargetType='ListBox'>
  <Setter Property='Background' Value='Transparent'/><Setter Property='BorderThickness' Value='0'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBox'>
   <ScrollViewer x:Name='scroll' Focusable='False' CanContentScroll='True' HorizontalScrollBarVisibility='Disabled'>
    <ScrollViewer.Template><ControlTemplate TargetType='ScrollViewer'>
     <Grid><ScrollContentPresenter CanContentScroll='True'/>
      <ScrollBar x:Name='PART_VerticalScrollBar' HorizontalAlignment='Right' Opacity='0' Value='{TemplateBinding VerticalOffset}' Maximum='{TemplateBinding ScrollableHeight}' ViewportSize='{TemplateBinding ViewportHeight}' Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}'/>
     </Grid>
    </ControlTemplate></ScrollViewer.Template>
    <ItemsPresenter/>
   </ScrollViewer>
  </ControlTemplate></Setter.Value></Setter>
  <Setter Property='ItemContainerStyle'><Setter.Value><Style TargetType='ListBoxItem'>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'>
    <Border x:Name='row' Background='Transparent' CornerRadius='6' Height='38' Padding='10,0,14,0'><ContentPresenter VerticalAlignment='Center'/></Border>
    <ControlTemplate.Triggers>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='row' Property='Background' Value='#26272B'/></Trigger>
     <Trigger Property='IsSelected' Value='True'><Setter TargetName='row' Property='Background' Value='#333D9AFF'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate></Setter.Value></Setter>
  </Style></Setter.Value></Setter>
  <Setter Property='ItemTemplate'><Setter.Value><DataTemplate>
   <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='150'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
    <TextBlock Text='{Binding Reading}' Foreground='#AEB1B6' FontSize='15' TextTrimming='CharacterEllipsis'/>
    <TextBlock Grid.Column='1' Text='{Binding Surface}' Foreground='#F2F3F4' FontSize='15' TextTrimming='CharacterEllipsis'/>
   </Grid>
  </DataTemplate></Setter.Value></Setter>
 </Style>
</ResourceDictionary>";
  static ResourceDictionary cache;
  public static ResourceDictionary Resources{get{if(cache==null)cache=(ResourceDictionary)XamlReader.Parse(Xaml);return cache;}}
  public static Style Get(string key){return (Style)Resources[key];}
 }

 // A normal, activatable window drawn in the theme: rounded, with its own title row.
 class PanelWindow : Window {
  protected readonly StackPanel Body=new StackPanel();
  readonly Border modeChip=new Border(); readonly TextBlock modeText=new TextBlock();
  public PanelWindow(string title,double width){
   WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;SizeToContent=SizeToContent.Height;Width=width+36;
   // Kept above other windows: the resident cannot always take the foreground, and a small panel must not open hidden.
   Title=title;ShowInTaskbar=true;Topmost=true;UseLayoutRounding=true;WindowStartupLocation=WindowStartupLocation.CenterScreen;
   FontFamily=Theme.Font;FontSize=15;Foreground=Theme.Brush(Theme.Ink);
   Resources.MergedDictionaries.Add(Styles.Resources);
   Border frame=new Border{Margin=new Thickness(18),CornerRadius=new CornerRadius(Theme.RadiusWindow),Background=Theme.Brush(Theme.Page),BorderBrush=Theme.Brush(Theme.Edge),BorderThickness=new Thickness(1)};
   frame.Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=22,ShadowDepth=6,Direction=270,Opacity=0.4,Color=Colors.Black};
   DockPanel dock=new DockPanel();
   Grid bar=new Grid{Height=46,Background=Brushes.Transparent};
   bar.Children.Add(new TextBlock{Text=title,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(20,2,0,0),FontSize=15,Foreground=Theme.Brush(Theme.Ink)});
   Button close=new Button{Content="✕",Width=34,Height=30,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,8,0),Style=Styles.Get("Quiet"),ToolTip="閉じる"};
   close.Click+=delegate{Close();};
   bar.Children.Add(close);
   // Input mode, in a fixed place: the floating indicator at the caret would sit on the field below.
   modeText.FontSize=13;modeText.Foreground=Theme.Brush(Theme.Ink);modeText.HorizontalAlignment=HorizontalAlignment.Center;modeText.VerticalAlignment=VerticalAlignment.Center;modeText.Margin=new Thickness(0,0,0,1);
   modeChip.Child=modeText;modeChip.Width=28;modeChip.Height=22;modeChip.CornerRadius=new CornerRadius(Theme.RadiusChip);modeChip.HorizontalAlignment=HorizontalAlignment.Right;modeChip.Margin=new Thickness(0,0,50,0);
   modeChip.ToolTip="変換キーで かな、無変換キーで 英数";
   bar.Children.Add(modeChip);ShowMode(true);
   bar.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){if(e.ButtonState==MouseButtonState.Pressed)try{DragMove();}catch{}};
   DockPanel.SetDock(bar,Dock.Top);dock.Children.Add(bar);
   Body.Margin=new Thickness(20,2,20,20);dock.Children.Add(Body);
   frame.Child=dock;Content=frame;
   PreviewKeyDown+=delegate(object s,KeyEventArgs e){if(e.Key==Key.Escape){Close();e.Handled=true;}};
  }
  public void ShowMode(bool kana){
   modeText.Text=kana?"あ":"A";
   modeChip.Background=Theme.Brush(kana?Theme.Hex("#2F86E6"):Theme.Field);
  }
  [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
  [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool AttachThreadInput(uint a,uint b,bool attach);
  // Opened from a menu of another process, so the window has to ask for the foreground itself.
  public void Present(){
   Show();
   if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
   IntPtr h=new WindowInteropHelper(this).Handle;uint me=Native.GetCurrentThreadId(),fg=Native.ThreadOf(Native.GetForegroundWindow());bool attached=false;
   try{if(fg!=0&&fg!=me)attached=AttachThreadInput(me,fg,true);SetForegroundWindow(h);}
   finally{if(attached)AttachThreadInput(me,fg,false);}
   Activate();
  }
  protected static TextBlock Caption(string text,Color color,double size){return new TextBlock{Text=text,Foreground=Theme.Brush(color),FontSize=size,VerticalAlignment=VerticalAlignment.Center};}
  protected static Border Rule(){return new Border{Height=1,Background=Theme.Brush(Theme.Line),Margin=new Thickness(0,14,0,12)};}
 }

 public sealed class WordRow {
  public Entry Entry; public string Reading{get{return Entry.Reading;}} public string Surface{get{return Entry.Surface;}}
 }

 sealed class DictionaryWindow : PanelWindow {
  readonly UserDictionary dict; readonly TextBox reading=new TextBox(),word=new TextBox(); readonly TextBlock message=new TextBlock(),count=new TextBlock();
  readonly ListBox list=new ListBox(); readonly Button remove=new Button(); ScrollBar bar;
  public Action<bool> ReadingFocus;   // true while the reading box has the keyboard: kana must arrive unconverted
  public DictionaryWindow(UserDictionary dictionary):base("単語の登録",440){
   dict=dictionary;
   Body.Children.Add(Row("よみ",reading));
   Body.Children.Add(Row("単語",word));
   reading.GotKeyboardFocus+=delegate{if(ReadingFocus!=null)ReadingFocus(true);};
   reading.LostKeyboardFocus+=delegate{if(ReadingFocus!=null)ReadingFocus(false);};
   Grid action=new Grid{Margin=new Thickness(0,4,0,0),Height=36};
   message.Foreground=Theme.Brush(Theme.Ink2);message.FontSize=14;message.VerticalAlignment=VerticalAlignment.Center;message.TextTrimming=TextTrimming.CharacterEllipsis;message.Margin=new Thickness(56,0,112,0);
   Button add=new Button{Content="登録する",Style=Styles.Get("Primary"),HorizontalAlignment=HorizontalAlignment.Right,Width=104,IsDefault=true};
   add.Click+=delegate{Add();};
   action.Children.Add(message);action.Children.Add(add);
   Body.Children.Add(action);
   Body.Children.Add(Rule());
   StackPanel head=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,6)};
   head.Children.Add(Caption("登録した単語",Theme.Ink2,14));
   count.Foreground=Theme.Brush(Theme.Ink3);count.FontSize=14;count.Margin=new Thickness(10,0,0,0);count.VerticalAlignment=VerticalAlignment.Center;
   head.Children.Add(count);Body.Children.Add(head);
   list.Style=Styles.Get("Words");list.Height=266;list.Margin=new Thickness(-10,0,-14,0);
   list.SelectionChanged+=delegate{remove.IsEnabled=list.SelectedItem!=null;};
   list.KeyDown+=delegate(object s,KeyEventArgs e){if(e.Key==Key.Delete){Remove();e.Handled=true;}};
   Body.Children.Add(list);
   Grid foot=new Grid{Margin=new Thickness(0,12,0,0)};
   StackPanel left=new StackPanel{Orientation=Orientation.Horizontal};
   remove.Content="削除";remove.Style=Styles.Get("Plain");remove.IsEnabled=false;remove.Click+=delegate{Remove();};
   Button import=new Button{Content="読み込み",Style=Styles.Get("Plain"),Margin=new Thickness(8,0,0,0),ToolTip="MS-IME や Google 日本語入力から書き出したテキストを読み込みます"};
   Button export=new Button{Content="書き出し",Style=Styles.Get("Plain"),Margin=new Thickness(8,0,0,0)};
   import.Click+=delegate{Import();};export.Click+=delegate{Export();};
   left.Children.Add(remove);left.Children.Add(import);left.Children.Add(export);foot.Children.Add(left);
   Body.Children.Add(foot);
   Loaded+=delegate{Refresh();HookScroll();reading.Focus();};
   Closed+=delegate{if(ReadingFocus!=null)ReadingFocus(false);};
  }
  static Grid Row(string label,TextBox box){
   Grid g=new Grid{Margin=new Thickness(0,0,0,10)};
   g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(56)});g.ColumnDefinitions.Add(new ColumnDefinition());
   g.Children.Add(Caption(label,Theme.Ink2,14));
   box.Style=Styles.Get("Field");Grid.SetColumn(box,1);g.Children.Add(box);
   return g;
  }
  void Say(string text,bool problem){message.Text=text;message.Foreground=Theme.Brush(problem?Theme.AccentInk:Theme.Ink2);}
  void Add(){
   string r=reading.Text,w=word.Text;
   string why=dict.Add(r,w,"");
   if(why==null){Say("登録しました",false);reading.Text="";word.Text="";Refresh();list.SelectedIndex=0;reading.Focus();return;}
   if(why=="same")Say("同じ単語が登録されています",true);
   else if(why=="long")Say("長すぎます（よみは 40 字まで）",true);
   else{Say(r.Trim().Length==0?"よみを入力してください":"単語を入力してください",true);if(r.Trim().Length==0)reading.Focus();else word.Focus();}
  }
  void Remove(){
   WordRow row=list.SelectedItem as WordRow;if(row==null)return;
   int index=list.SelectedIndex;
   dict.Remove(row.Entry);Refresh();
   if(list.Items.Count>0)list.SelectedIndex=Math.Min(index,list.Items.Count-1);
   Say("削除しました",false);
  }
  void Import(){
   Microsoft.Win32.OpenFileDialog d=new Microsoft.Win32.OpenFileDialog{Filter="テキスト (*.txt;*.tsv)|*.txt;*.tsv|すべてのファイル|*.*",Title="単語の一覧を読み込む"};
   if(d.ShowDialog(this)!=true)return;
   try{int n=dict.Import(d.FileName);Refresh();Say(n+" 語を読み込みました",false);}catch(Exception ex){Say("読み込めませんでした: "+ex.Message,true);}
  }
  void Export(){
   Microsoft.Win32.SaveFileDialog d=new Microsoft.Win32.SaveFileDialog{Filter="テキスト (*.txt)|*.txt",FileName="LiveHenkan-words.txt",Title="単語の一覧を書き出す"};
   if(d.ShowDialog(this)!=true)return;
   try{dict.Export(d.FileName);Say("書き出しました",false);}catch(Exception ex){Say("書き出せませんでした: "+ex.Message,true);}
  }
  public void Refresh(){
   List<WordRow> rows=new List<WordRow>();
   foreach(Entry e in dict.Words())rows.Add(new WordRow{Entry=e});
   rows.Reverse();   // newest first
   list.ItemsSource=rows;count.Text=rows.Count.ToString();remove.IsEnabled=false;
  }
  // The scroll bar is visible only while the list is moving.
  void HookScroll(){
   list.ApplyTemplate();
   ScrollViewer sv=list.Template.FindName("scroll",list) as ScrollViewer;if(sv==null)return;
   sv.ApplyTemplate();bar=sv.Template.FindName("PART_VerticalScrollBar",sv) as ScrollBar;if(bar==null)return;
   sv.ScrollChanged+=delegate(object s,ScrollChangedEventArgs e){
    if(e.VerticalChange==0)return;
    DoubleAnimationUsingKeyFrames fade=new DoubleAnimationUsingKeyFrames();
    fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.Zero)));
    fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(800))));
    fade.KeyFrames.Add(new LinearDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1100))));
    bar.BeginAnimation(OpacityProperty,fade);
   };
  }
 }

 sealed class SettingsWindow : PanelWindow {
  readonly Settings settings; readonly UserDictionary dict; readonly Action changed; readonly TextBlock learned=new TextBlock();
  public SettingsWindow(Settings s,UserDictionary dictionary,Action onChanged,string footer):base("LiveHenkanの設定",440){
   settings=s;dict=dictionary;changed=onChanged;
   Toggle("半角/全角キーで かな と 英数 を切り替える","ToggleWithHankaku");
   Toggle("オンにしたとき かな入力 から始める","StartInKana");
   Body.Children.Add(Rule());
   Toggle("何も打っていないときの Space を全角にする","FullWidthSpace");
   Toggle("記号を全角で入力する","FullWidthSymbols");
   Toggle("数字を全角で入力する","FullWidthDigits");
   Toggle("句読点を「，．」にする","WesternPunctuation");
   Toggle("文末のあと、次の文を打ち始めたら確定する","CommitAtSentenceEnd");
   Toggle("変換中の文字を文書の中に出す（メモ帳など）","WriteIntoDocument");
   Body.Children.Add(Rule());
   Toggle("選び直した候補を覚える","Learn");
   Grid row=new Grid{Height=44};
   StackPanel left=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
   left.Children.Add(Caption("覚えた件数",Theme.Ink,15));
   learned.Foreground=Theme.Brush(Theme.Ink2);learned.FontSize=15;learned.Margin=new Thickness(12,0,0,0);learned.VerticalAlignment=VerticalAlignment.Center;learned.MinWidth=44;
   left.Children.Add(learned);row.Children.Add(left);
   Button reset=new Button{Content="忘れる",Style=Styles.Get("Plain"),HorizontalAlignment=HorizontalAlignment.Right,Width=84};
   reset.Click+=delegate{dict.ClearLearned();learned.Text=dict.LearnedCount.ToString();};
   row.Children.Add(reset);Body.Children.Add(row);
   Body.Children.Add(Rule());
   Body.Children.Add(new TextBlock{Text=footer,Foreground=Theme.Brush(Theme.Ink3),FontSize=13,TextWrapping=TextWrapping.Wrap,LineHeight=20});
   Loaded+=delegate{learned.Text=dict.LearnedCount.ToString();};
   Activated+=delegate{learned.Text=dict.LearnedCount.ToString();};
  }
  void Toggle(string label,string field){
   System.Reflection.FieldInfo f=typeof(Settings).GetField(field);
   Grid row=new Grid{Height=44,Background=Brushes.Transparent};
   TextBlock text=Caption(label,Theme.Ink,15);text.Margin=new Thickness(0,0,54,0);text.TextTrimming=TextTrimming.CharacterEllipsis;
   CheckBox box=new CheckBox{Style=Styles.Get("Switch"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,IsChecked=(bool)f.GetValue(settings)};
   box.Checked+=delegate{f.SetValue(settings,true);changed();};
   box.Unchecked+=delegate{f.SetValue(settings,false);changed();};
   // The whole row is the target, not just the small switch.
   row.MouseLeftButtonUp+=delegate(object s,MouseButtonEventArgs e){if(!(e.OriginalSource is DependencyObject&&IsInside((DependencyObject)e.OriginalSource,box)))box.IsChecked=box.IsChecked!=true;};
   row.Children.Add(text);row.Children.Add(box);Body.Children.Add(row);
  }
  static bool IsInside(DependencyObject o,DependencyObject ancestor){
   while(o!=null){if(o==ancestor)return true;o=o is Visual?VisualTreeHelper.GetParent(o):LogicalTreeHelper.GetParent(o);}
   return false;
  }
 }
}
