using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoTunel {
 internal static class Ui {
  internal static readonly Color Navy=Color.FromArgb(13,21,30), Canvas=Color.FromArgb(16,24,33), Surface=Color.FromArgb(24,35,47), Raised=Color.FromArgb(35,49,64), Teal=Color.FromArgb(66,218,194), Ink=Color.FromArgb(231,237,245), Muted=Color.FromArgb(157,174,191), Line=Color.FromArgb(45,60,75), Warning=Color.FromArgb(248,194,105);
  [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
  internal static void Window(Form form){
   form.Icon=Icon();form.ForeColor=Ink;form.BackColor=Canvas;
   // Shown runs after WinForms applies DPI scaling to size constraints and layout.
   form.Shown+=delegate{
    var area=Screen.FromControl(form).WorkingArea;
    var available=new Rectangle(area.Left+14,area.Top+14,Math.Max(1,area.Width-28),Math.Max(1,area.Height-28));
    form.MinimumSize=new Size(Math.Min(form.MinimumSize.Width,available.Width),Math.Min(form.MinimumSize.Height,available.Height));
    form.Size=new Size(Math.Min(form.Width,available.Width),Math.Min(form.Height,available.Height));
    form.Location=new Point(Math.Max(available.Left,Math.Min(form.Left,available.Right-form.Width)),Math.Max(available.Top,Math.Min(form.Top,available.Bottom-form.Height)));
   };
   form.HandleCreated+=delegate{if(Environment.OSVersion.Platform==PlatformID.Win32NT)try{int value=1;if(DwmSetWindowAttribute(form.Handle,20,ref value,4)!=0)DwmSetWindowAttribute(form.Handle,19,ref value,4);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}};
  }
  internal static Icon Icon(){using(var stream=typeof(Ui).Assembly.GetManifestResourceStream("AutoTunel.Brand.ico")){if(stream==null)return (Icon)SystemIcons.Application.Clone();using(var icon=new Icon(stream))return (Icon)icon.Clone();}}
  internal static Label Label(string text,float size=10,bool bold=false){return new Label{Text=text,AutoSize=true,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=Ink,BackColor=Color.Transparent,Margin=new Padding(0,0,0,7),UseMnemonic=false};}
  internal static GraphicsPath Rounded(RectangleF bounds,float radius){
   float d=Math.Max(1,Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height)));var path=new GraphicsPath();
   path.AddArc(bounds.X,bounds.Y,d,d,180,90);path.AddArc(bounds.Right-d,bounds.Y,d,d,270,90);path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);path.AddArc(bounds.X,bounds.Bottom-d,d,d,90,90);path.CloseFigure();return path;
  }
  internal static Color Background(Control control){while(control!=null){if(control.BackColor.A==255)return control.BackColor;control=control.Parent;}return Canvas;}
  static Color Blend(Color a,Color b,float amount){return Color.FromArgb((int)(a.R+(b.R-a.R)*amount),(int)(a.G+(b.G-a.G)*amount),(int)(a.B+(b.B-a.B)*amount));}
  internal static void Button(Button button,bool primary=false){
   button.AutoSize=false;button.Height=44;button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=0;
   button.BackColor=primary?Teal:Raised;button.ForeColor=primary?Navy:Ink;button.Font=new Font("Segoe UI",10,FontStyle.Bold);button.Cursor=Cursors.Hand;button.Margin=new Padding(0,0,10,0);button.UseVisualStyleBackColor=false;
   bool hover=false,pressed=false;
   button.MouseEnter+=delegate{hover=true;button.Invalidate();};button.MouseLeave+=delegate{hover=false;pressed=false;button.Invalidate();};
   button.MouseDown+=delegate{pressed=true;button.Invalidate();};button.MouseUp+=delegate{pressed=false;button.Invalidate();};button.GotFocus+=delegate{button.Invalidate();};button.LostFocus+=delegate{button.Invalidate();};button.EnabledChanged+=delegate{button.Invalidate();};
   button.Paint+=delegate(object sender,PaintEventArgs e){
    if(button.Width<3||button.Height<3)return;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Background(button.Parent));
    Color fill=button.Enabled?button.BackColor:Blend(button.BackColor,Canvas,.58f),text=button.Enabled?button.ForeColor:Muted;
    if(button.Enabled&&hover)fill=Blend(fill,Color.White,.07f);if(button.Enabled&&pressed)fill=Blend(fill,Navy,.16f);
    float scale=button.Font.Height/16f;
    using(var path=Rounded(new RectangleF(.5f,.5f,button.Width-1,button.Height-1),8*scale))using(var brush=new SolidBrush(fill)){g.FillPath(brush,path);if(!primary)using(var pen=new Pen(button.Enabled?Line:Blend(Line,Canvas,.3f)))g.DrawPath(pen,path);}
    var bounds=new Rectangle(10,2,Math.Max(1,button.Width-20),Math.Max(1,button.Height-4));
    TextRenderer.DrawText(g,button.Text,button.Font,bounds,text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
    if(button.Focused)ControlPaint.DrawFocusRectangle(g,new Rectangle(6,6,Math.Max(1,button.Width-12),Math.Max(1,button.Height-12)),text,fill);
   };
  }
  internal static void Combo(ComboBox box){
   box.FlatStyle=FlatStyle.Flat;box.BackColor=Raised;box.ForeColor=Ink;box.Font=new Font("Segoe UI",10);box.DrawMode=DrawMode.OwnerDrawFixed;box.ItemHeight=box.Font.Height+12;box.IntegralHeight=false;box.DropDownHeight=240;
   box.FontChanged+=delegate{box.ItemHeight=box.Font.Height+12;};
   box.DrawItem+=delegate(object sender,DrawItemEventArgs e){
    bool selected=(e.State&DrawItemState.Selected)!=0;Color background=selected?Color.FromArgb(40,75,80):Raised;
    using(var brush=new SolidBrush(background))e.Graphics.FillRectangle(brush,e.Bounds);
    string text=e.Index>=0?box.GetItemText(box.Items[e.Index]):"Wybierz…";
    TextRenderer.DrawText(e.Graphics,text,box.Font,new Rectangle(e.Bounds.X+10,e.Bounds.Y,e.Bounds.Width-18,e.Bounds.Height),box.Enabled?Ink:Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
    if((e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();
   };
  }
  internal static TableLayoutPanel Stack(){var panel=new TableLayoutPanel{ColumnCount=1,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,Margin=Padding.Empty,BackColor=Color.Transparent};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));return panel;}
  internal static void Add(TableLayoutPanel parent,Control control){int row=parent.RowCount++;parent.RowStyles.Add(new RowStyle(SizeType.AutoSize));control.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;parent.Controls.Add(control,0,row);}
  internal static Panel Card(string heading,Control body){var contents=Stack();var title=Label(heading,9,true);title.ForeColor=Muted;title.Margin=new Padding(0,0,0,13);Add(contents,title);Add(contents,body);var card=new BorderPanel{BackColor=Surface,Padding=new Padding(20,17,20,18),AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,Margin=new Padding(0,0,0,14)};card.Controls.Add(contents);return card;}
  internal static Size Fit(Size desired){var area=Screen.PrimaryScreen.WorkingArea;return new Size(Math.Min(desired.Width,Math.Max(1,area.Width-28)),Math.Min(desired.Height,Math.Max(1,area.Height-28)));}
 }
 internal sealed class BorderPanel:Panel {
  Color borderColor=Ui.Line;
  internal Color BorderColor{get{return borderColor;}set{borderColor=value;Invalidate();}}
  internal BorderPanel(){DoubleBuffered=true;ResizeRedraw=true;}
  protected override void OnPaintBackground(PaintEventArgs e){
   e.Graphics.Clear(Ui.Background(Parent));if(Width<2||Height<2)return;
   e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Ui.Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),12))using(var brush=new SolidBrush(BackColor))e.Graphics.FillPath(brush,path);
  }
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Width<2||Height<2)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Ui.Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),12))using(var pen=new Pen(borderColor))e.Graphics.DrawPath(pen,path);}
 }
 internal sealed class BrandMark:Control {
  readonly Image image;
  internal BrandMark(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;Size=new Size(64,64);TabStop=false;
   using(var stream=typeof(BrandMark).Assembly.GetManifestResourceStream("AutoTunel.Brand.png"))if(stream!=null)using(var loaded=Image.FromStream(stream))image=new Bitmap(loaded);
  }
  protected override void OnPaint(PaintEventArgs e){
   base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
   if(image!=null){g.InterpolationMode=InterpolationMode.HighQualityBicubic;int size=Math.Min(Width,Height);g.DrawImage(image,new Rectangle((Width-size)/2,(Height-size)/2,size,size));return;}
   float s=Math.Min(Width,Height)/64f;using(var pen=new Pen(Ui.Teal,5*s)){g.DrawArc(pen,13*s,10*s,38*s,38*s,180,180);g.DrawLine(pen,13*s,29*s,13*s,53*s);g.DrawLine(pen,51*s,29*s,51*s,53*s);g.DrawLine(pen,25*s,42*s,39*s,42*s);}
  }
  protected override void Dispose(bool disposing){if(disposing)image?.Dispose();base.Dispose(disposing);}
 }
 internal sealed class RoleIllustration:Control {
  readonly bool server;
  internal RoleIllustration(bool vehicle){server=vehicle;Size=new Size(96,64);TabStop=false;BackColor=Ui.Surface;}
  protected override void OnPaint(PaintEventArgs e){
   base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(Width/96f,Height/64f);
   using(var pen=new Pen(Ui.Teal,2.4f)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
    if(server){
     g.DrawLines(pen,new[]{new PointF(21,29),new PointF(29,13),new PointF(66,13),new PointF(75,29)});
     using(var path=Ui.Rounded(new RectangleF(13,28,70,23),7))g.DrawPath(pen,path);
     g.DrawLine(pen,20,51,20,57);g.DrawLine(pen,76,51,76,57);g.DrawLine(pen,25,37,34,37);g.DrawLine(pen,62,37,71,37);g.DrawLine(pen,42,42,54,42);
    }else{
     using(var path=Ui.Rounded(new RectangleF(19,9,58,39),5))g.DrawPath(pen,path);
     g.DrawLines(pen,new[]{new PointF(19,49),new PointF(10,57),new PointF(86,57),new PointF(77,49)});
     g.DrawLine(pen,31,31,40,31);g.DrawLine(pen,56,31,65,31);g.DrawLine(pen,40,31,45,24);g.DrawLine(pen,45,24,52,38);g.DrawLine(pen,52,38,56,31);
    }
   }
  }
 }
 internal sealed class RolePicker:Form {
  internal bool Server{get;private set;}
  internal RolePicker(){
   Text="AutoTunel MB • wybierz rolę";Font=new Font("Segoe UI",10);AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;Ui.Window(this);
   StartPosition=FormStartPosition.CenterScreen;Size=Ui.Fit(new Size(940,740));MinimumSize=Ui.Fit(new Size(710,540));MaximizeBox=true;
   var viewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(30)};Controls.Add(viewport);
   var root=Ui.Stack();root.Dock=DockStyle.Top;viewport.Controls.Add(root);
   var brand=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=false,Margin=new Padding(0,0,0,28)};
   brand.Controls.Add(new BrandMark{Size=new Size(60,60),Margin=new Padding(0,0,14,0)});
   var wordmark=Ui.Stack();wordmark.Margin=new Padding(0,3,0,0);Ui.Add(wordmark,Ui.Label("AutoTunel MB",22,true));var caption=Ui.Label("ZDALNA DIAGNOSTYKA  /  USB + ENET",8,true);caption.ForeColor=Ui.Muted;Ui.Add(wordmark,caption);brand.Controls.Add(wordmark);Ui.Add(root,brand);
   var title=Ui.Label("Gdzie teraz pracujesz?",27,true);Ui.Add(root,title);
   var subtitle=Ui.Label("Jeden program, dwa komputery. Wybierz rolę tego urządzenia.",10);subtitle.ForeColor=Ui.Muted;subtitle.Margin=new Padding(0,0,0,25);Ui.Add(root,subtitle);
   var choices=new TableLayoutPanel{Dock=DockStyle.Top,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,20),Height=310};choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));choices.RowStyles.Add(new RowStyle(SizeType.Percent,100));
   var vehicle=Choice(true);var diagnostic=Choice(false);choices.Controls.Add(vehicle,0,0);choices.Controls.Add(diagnostic,1,0);Ui.Add(root,choices);
   bool stacked=false;choices.SizeChanged+=delegate{
    bool narrow=choices.ClientSize.Width<650*Font.Height/17f;if(narrow==stacked)return;stacked=narrow;choices.SuspendLayout();
    choices.ColumnCount=narrow?1:2;choices.RowCount=narrow?2:1;choices.ColumnStyles.Clear();choices.RowStyles.Clear();choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,narrow?100:50));if(!narrow)choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
    choices.RowStyles.Add(new RowStyle(SizeType.Percent,narrow?50:100));if(narrow)choices.RowStyles.Add(new RowStyle(SizeType.Percent,50));choices.SetCellPosition(diagnostic,new TableLayoutPanelCellPosition(narrow?0:1,narrow?1:0));choices.Height=(int)((narrow?620:310)*Font.Height/17f);
    vehicle.Margin=narrow?new Padding(0,0,0,8):new Padding(0,0,9,0);diagnostic.Margin=narrow?new Padding(0,8,0,0):new Padding(9,0,0,0);choices.ResumeLayout();
   };
   var note=Ui.Label("Połącz oba komputery z tą samą siecią Radmin VPN.",9);note.ForeColor=Ui.Muted;Ui.Add(root,note);
   var version=Ui.Label("AutoTunel MB 0.8.1  ·  Obsługa USB wbudowana w program",8);version.ForeColor=Ui.Muted;version.Margin=new Padding(0,9,0,0);Ui.Add(root,version);
  }
  Control Choice(bool server){
   var card=new BorderPanel{BackColor=Ui.Surface,Dock=DockStyle.Fill,Padding=new Padding(22),Margin=server?new Padding(0,0,9,0):new Padding(9,0,0,0)};
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Margin=Padding.Empty};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,72));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
   layout.Controls.Add(new RoleIllustration(server){Margin=new Padding(0,0,0,8)},0,0);
   var tag=Ui.Label(server?"01  /  PRZY INTERFEJSIE":"02  /  ZDALNIE",8,true);tag.ForeColor=Ui.Teal;layout.Controls.Add(tag,0,1);
   layout.Controls.Add(Ui.Label(server?"Przy samochodzie":"Diagnosta",20,true),0,2);
   var description=Ui.Label(server?"Udostępnij podłączony interfejs USB lub przewód ENET i przekaż kod diagnoście.":"Połącz się kodem z interfejsem przy samochodzie i rozpocznij diagnostykę.",10);description.AutoSize=false;description.Dock=DockStyle.Fill;description.ForeColor=Ui.Muted;description.Margin=new Padding(0,0,0,12);layout.Controls.Add(description,0,3);
   var button=new Button{Text=server?"Udostępnij interfejs  →":"Połącz z samochodem  →",Dock=DockStyle.Fill,AccessibleName=server?"Wybierz rolę: przy samochodzie, Server":"Wybierz rolę: diagnosta, Client"};Ui.Button(button,true);button.Margin=Padding.Empty;
   button.Click+=delegate{Server=server;DialogResult=DialogResult.OK;Close();};layout.Controls.Add(button,0,4);card.Controls.Add(layout);return card;
  }
 }
}
