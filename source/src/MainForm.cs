using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

namespace AutoTunel {
 public sealed class MainForm:Form {
  readonly bool server,previewMode;
  readonly BorderPanel statusCard=new BorderPanel();
  readonly ComboBox mode=new ComboBox(),vpn=new ComboBox(),equipment=new ComboBox();
  readonly TextBox code=new TextBox(),tool=new TextBox(),log=new TextBox();
  readonly Button changeRole=new Button(),start=new Button(),stop=new Button(),refresh=new Button(),copy=new Button(),recover=new Button();
  readonly Button prepareUsb=new Button();
  readonly Label statusTitle=new Label(),deviceHint=new Label(),status=new Label(),deviceLabel=new Label(),transfer=new Label();
  readonly GroupBox settings=new GroupBox();readonly CheckBox details=new CheckBox(),advanced=new CheckBox();
  readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=500};
  Tunnel tunnel;UsbIpBackend backend;SharedUsb share;UsbAttachment attachment;
  CancellationTokenSource cancellation;Task sessionTask;bool busy,refreshing,closing,usbRestartRequired;string pendingCode;
  const int SessionPort=24832;
  bool UsbMode{get{return mode.SelectedIndex==0;}}
  string Journal{get{return Path.Combine(Platform.DataDir,server?"owned-usb-share.txt":"owned-usb-client.txt");}}

  public bool ChangeRoleRequested{get;private set;}
  public MainForm(bool atVehicle,bool preview=false){
   server=atVehicle;previewMode=preview;
   Text="AutoTunel MB 0.8.1 • "+(server?"Przy samochodzie":"Diagnosta");
   Size=Ui.Fit(new Size(1180,900));MinimumSize=Ui.Fit(new Size(860,620));StartPosition=FormStartPosition.CenterScreen;
   AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("Segoe UI",10);Ui.Window(this);
   var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,232));shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(shell);
   var sidebar=new Panel{Dock=DockStyle.Fill,BackColor=Ui.Navy,Padding=new Padding(24),Margin=Padding.Empty,AutoScroll=true};shell.Controls.Add(sidebar,0,0);
   var side=Ui.Stack();side.Dock=DockStyle.Top;sidebar.Controls.Add(side);
   Ui.Add(side,new BrandMark{Size=new Size(76,76),Margin=new Padding(0,0,0,14)});
   Ui.Add(side,Ui.Label("AutoTunel",24,true));
   var brandSub=Ui.Label("MB  /  ZDALNA DIAGNOSTYKA",8,true);brandSub.ForeColor=Ui.Muted;brandSub.Margin=new Padding(0,0,0,30);Ui.Add(side,brandSub);
   var roleTag=Ui.Label(server?"01  /  PRZY INTERFEJSIE":"02  /  ZDALNIE",8,true);roleTag.ForeColor=Ui.Teal;Ui.Add(side,roleTag);
   var roleTitle=Ui.Label(server?"Przy samochodzie":"Diagnosta",15,true);roleTitle.Margin=new Padding(0,0,0,12);Ui.Add(side,roleTitle);
   var roleDescription=Ui.Label(server?"Udostępnij interfejs podłączony do samochodu.":"Połącz się ze zdalnym interfejsem i rozpocznij pracę.",10);roleDescription.MaximumSize=new Size(180,0);roleDescription.ForeColor=Ui.Muted;roleDescription.Margin=new Padding(0,0,0,18);Ui.Add(side,roleDescription);
   changeRole.Text="Zmień rolę";Ui.Button(changeRole);changeRole.BackColor=Ui.Surface;changeRole.Margin=new Padding(0,0,0,30);changeRole.Click+=delegate{if(!busy&&!refreshing&&share==null&&attachment==null){ChangeRoleRequested=true;Close();}};Ui.Add(side,changeRole);
   var steps=Ui.Label(server?"01   Podłącz interfejs\n\n02   Udostępnij połączenie\n\n03   Przekaż kod diagnoście":"01   Odbierz kod sesji\n\n02   Połącz z samochodem\n\n03   Uruchom diagnostykę",9);steps.MaximumSize=new Size(180,0);steps.ForeColor=Ui.Muted;steps.Margin=new Padding(0,0,0,30);Ui.Add(side,steps);
   var version=Ui.Label("USB / J2534 + ENET\nObsługa USB wbudowana\n\nWersja 0.8.1 • prototyp",8);version.ForeColor=Ui.Muted;Ui.Add(side,version);
   var viewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(28,24,28,24),Margin=Padding.Empty};shell.Controls.Add(viewport,1,0);
   var root=Ui.Stack();root.Dock=DockStyle.Top;viewport.Controls.Add(root);
   var heading=Ui.Stack();heading.Margin=new Padding(0,0,0,17);
   var overline=Ui.Label("TWOJA SESJA DIAGNOSTYCZNA",8,true);overline.ForeColor=Ui.Teal;Ui.Add(heading,overline);
   var title=Ui.Label(server?"Udostępnij interfejs":"Połącz się z samochodem",24,true);Ui.Add(heading,title);
   var lead=Ui.Label(server?"Wybierz interfejs, uruchom sesję i przekaż kod diagnoście.":"Wklej kod otrzymany od osoby przy samochodzie.",10);lead.ForeColor=Ui.Muted;Ui.Add(heading,lead);Ui.Add(root,heading);
   heading.SizeChanged+=delegate{title.MaximumSize=new Size(Math.Max(200,heading.ClientSize.Width),0);lead.MaximumSize=new Size(Math.Max(200,heading.ClientSize.Width),0);};

   var connection=Ui.Stack();
   var selection=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,RowCount=2,Margin=Padding.Empty};selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,42));selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,58));
   selection.Controls.Add(Ui.Label("Rodzaj połączenia",9),0,0);selection.Controls.Add(Ui.Label("Sieć Radmin VPN",9),1,0);
   mode.DropDownStyle=ComboBoxStyle.DropDownList;mode.Dock=DockStyle.Top;mode.Margin=new Padding(0,0,12,13);mode.Items.AddRange(new object[]{"USB • interfejs J2534","ENET • przewód Ethernet"});mode.SelectedIndex=0;mode.Enabled=server;mode.AccessibleName="Rodzaj połączenia";Ui.Combo(mode);
   vpn.DropDownStyle=ComboBoxStyle.DropDownList;vpn.Dock=DockStyle.Top;vpn.Margin=new Padding(0,0,0,13);vpn.AccessibleName="Adres RadminVPN";Ui.Combo(vpn);selection.Controls.Add(mode,0,1);selection.Controls.Add(vpn,1,1);Ui.Add(connection,selection);
   deviceLabel.AutoSize=true;deviceLabel.Font=new Font("Segoe UI",9);deviceLabel.ForeColor=Ui.Ink;deviceLabel.Margin=new Padding(0,0,0,7);Ui.Add(connection,deviceLabel);
   var hardware=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,RowCount=1,Margin=Padding.Empty};hardware.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));hardware.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,114));
   equipment.DropDownStyle=ComboBoxStyle.DropDownList;equipment.Dock=DockStyle.Top;equipment.Margin=new Padding(0,0,12,0);equipment.AccessibleName="Interfejs USB lub karta sieciowa";Ui.Combo(equipment);
   deviceHint.Text="Zdalny interfejs USB pojawi się w Windows po połączeniu.";deviceHint.AutoSize=true;deviceHint.ForeColor=Ui.Muted;deviceHint.Dock=DockStyle.Fill;deviceHint.Margin=new Padding(0,8,0,0);
   refresh.Text="Odśwież";Ui.Button(refresh);refresh.Height=36;refresh.Dock=DockStyle.Top;refresh.Margin=Padding.Empty;refresh.Click+=async delegate{if(!previewMode)await RefreshEquipment();};hardware.Controls.Add(equipment,0,0);hardware.Controls.Add(deviceHint,0,0);hardware.Controls.Add(refresh,1,0);Ui.Add(connection,hardware);
   Ui.Add(root,Ui.Card("01  /  POŁĄCZENIE",connection));

   var invitation=Ui.Stack();var codeHint=Ui.Label(server?"Kod pojawi się po udostępnieniu interfejsu.":"Kod automatycznie dobierze tryb USB lub ENET.",9);codeHint.ForeColor=Ui.Muted;codeHint.Margin=new Padding(0,0,0,10);Ui.Add(invitation,codeHint);
   var codeLayout=new TableLayoutPanel{Dock=DockStyle.Top,ColumnCount=2,RowCount=1,Height=84,Margin=Padding.Empty};codeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));codeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,132));
   var codeFrame=new BorderPanel{Dock=DockStyle.Fill,BackColor=Ui.Navy,Padding=new Padding(12,10,12,10),Margin=new Padding(0,0,12,0)};
   code.Multiline=true;code.MaxLength=2048;code.ReadOnly=server;code.ScrollBars=ScrollBars.Vertical;code.Font=new Font("Consolas",10);code.Dock=DockStyle.Fill;code.BorderStyle=BorderStyle.None;code.BackColor=Ui.Navy;code.ForeColor=Ui.Ink;code.Margin=Padding.Empty;code.AccessibleName=server?"Kod dla diagnosty":"Kod z komputera przy samochodzie";codeFrame.Controls.Add(code);
   copy.Text=server?"Kopiuj kod":"Wklej kod";Ui.Button(copy);copy.Dock=DockStyle.Top;copy.Margin=Padding.Empty;copy.Click+=delegate{try{if(server){if(code.TextLength>0){Clipboard.SetText(code.Text);SetStatus("Kod skopiowany. Przekaż go diagnoście.");}}else if(Clipboard.ContainsText())code.Text=Clipboard.GetText().Trim();}catch(Exception e){Error(e);}};
   codeLayout.Controls.Add(codeFrame,0,0);codeLayout.Controls.Add(copy,1,0);Ui.Add(invitation,codeLayout);Ui.Add(root,Ui.Card("02  /  KOD SESJI",invitation));

   statusCard.BackColor=Color.FromArgb(23,48,48);statusCard.BorderColor=Color.FromArgb(42,92,85);statusCard.Padding=new Padding(18,14,18,14);statusCard.AutoSize=true;statusCard.AutoSizeMode=AutoSizeMode.GrowAndShrink;statusCard.Dock=DockStyle.Top;statusCard.Margin=new Padding(0,0,0,16);
   var state=Ui.Stack();statusTitle.Text="STATUS SESJI";statusTitle.AutoSize=true;statusTitle.Font=new Font("Segoe UI",8,FontStyle.Bold);statusTitle.ForeColor=Ui.Teal;statusTitle.Margin=new Padding(0,0,0,5);Ui.Add(state,statusTitle);
   status.AutoSize=true;status.MaximumSize=new Size(740,0);status.ForeColor=Ui.Ink;status.Text="Przygotowanie…";status.Margin=Padding.Empty;Ui.Add(state,status);statusCard.Controls.Add(state);state.SizeChanged+=delegate{status.MaximumSize=new Size(Math.Max(180,state.ClientSize.Width),0);};Ui.Add(root,statusCard);
   var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Margin=new Padding(0,0,0,18)};
   start.Text=server?"Udostępnij interfejs  →":"Połącz z samochodem  →";start.Width=250;Ui.Button(start,true);start.Height=48;stop.Text=server?"Zakończ sesję":"Rozłącz";stop.Width=150;Ui.Button(stop);stop.Height=48;
   recover.Text="Zwolnij USB po awarii";recover.Width=207;Ui.Button(recover);recover.Height=48;recover.Click+=async delegate{if(!previewMode)await Recover();};
   start.Click+=async delegate{if(!previewMode)await StartSession();};stop.Click+=delegate{if(!previewMode)StopSession();};actions.Controls.Add(start);actions.Controls.Add(stop);actions.Controls.Add(recover);Ui.Add(root,actions);AcceptButton=start;

   var footer=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Margin=new Padding(0,0,0,10)};
   advanced.Text="Ustawienia USB";advanced.AutoSize=true;advanced.ForeColor=Ui.Muted;advanced.FlatStyle=FlatStyle.Flat;advanced.Cursor=Cursors.Hand;advanced.Margin=new Padding(0,5,18,5);advanced.CheckedChanged+=delegate{settings.Visible=advanced.Checked;};details.Text="Szczegóły sesji";details.AutoSize=true;details.ForeColor=Ui.Muted;details.FlatStyle=FlatStyle.Flat;details.Cursor=Cursors.Hand;details.Margin=new Padding(0,5,18,5);details.CheckedChanged+=delegate{log.Visible=details.Checked;};
   var help=new LinkLabel{Text="Instrukcja",AutoSize=true,Margin=new Padding(0,5,18,5),LinkColor=Ui.Teal,ActiveLinkColor=Ui.Ink,VisitedLinkColor=Ui.Teal,LinkBehavior=LinkBehavior.HoverUnderline};help.LinkClicked+=delegate{ShowHelp();};transfer.AutoSize=true;transfer.ForeColor=Ui.Muted;transfer.Margin=new Padding(0,5,0,5);
   var diagnostics=new LinkLabel{Text="Diagnostyka USB",AutoSize=true,Margin=new Padding(0,5,18,5),LinkColor=Ui.Teal,ActiveLinkColor=Ui.Ink,VisitedLinkColor=Ui.Teal,LinkBehavior=LinkBehavior.HoverUnderline};diagnostics.LinkClicked+=delegate{if(!previewMode)ShowUsbDiagnostics();};
   footer.Controls.Add(advanced);footer.Controls.Add(details);footer.Controls.Add(help);footer.Controls.Add(diagnostics);footer.Controls.Add(transfer);Ui.Add(root,footer);
   settings.Text="Obsługa USB — wbudowana w AutoTunel";settings.ForeColor=Ui.Muted;settings.BackColor=Ui.Canvas;settings.Dock=DockStyle.Top;settings.Height=146;settings.Visible=false;settings.Padding=new Padding(14);settings.Margin=new Padding(0,4,0,16);
   var config=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Margin=Padding.Empty};config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155));config.RowStyles.Add(new RowStyle(SizeType.Absolute,40));config.RowStyles.Add(new RowStyle(SizeType.Absolute,30));config.RowStyles.Add(new RowStyle(SizeType.Percent,100));
   string configError=null;try{tool.Text=previewMode?"Wbudowana obsługa USB/IP":new WindowsUsbSetupHost().FindTool(server);}catch(Exception e){configError=MessageOf(e);}
   tool.ReadOnly=true;tool.Dock=DockStyle.Fill;tool.Margin=new Padding(0,8,12,0);tool.Font=new Font("Segoe UI",9);tool.BackColor=Ui.Raised;tool.ForeColor=Ui.Muted;tool.BorderStyle=BorderStyle.FixedSingle;tool.AccessibleName="Lokalizacja obsługi USB";
   prepareUsb.Text=server?"Napraw USB":"Przygotuj USB";prepareUsb.Dock=DockStyle.Fill;Ui.Button(prepareUsb);prepareUsb.Margin=Padding.Empty;prepareUsb.Click+=async delegate{if(!previewMode)await RefreshEquipment(true,server);};config.Controls.Add(tool,0,0);config.Controls.Add(prepareUsb,1,0);
   var configNote=Ui.Label("Składniki USB są w tym EXE. AutoTunel przygotuje je automatycznie.",8);configNote.ForeColor=Ui.Muted;configNote.Margin=new Padding(0,9,0,0);config.Controls.Add(configNote,0,1);config.SetColumnSpan(configNote,2);
   var licenses=new LinkLabel{Text="Licencje składników USB",AutoSize=true,LinkColor=Ui.Teal,ActiveLinkColor=Ui.Ink,VisitedLinkColor=Ui.Teal,LinkBehavior=LinkBehavior.HoverUnderline,Margin=new Padding(0,5,0,0)};licenses.LinkClicked+=delegate{ShowEmbeddedDocument("AutoTunel.Usb.Notices.txt","Licencje-USB.txt");};config.Controls.Add(licenses,0,2);config.SetColumnSpan(licenses,2);settings.Controls.Add(config);Ui.Add(root,settings);
   log.Dock=DockStyle.Top;log.Height=170;log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Both;log.WordWrap=false;log.Font=new Font("Consolas",9);log.BackColor=Ui.Navy;log.ForeColor=Ui.Muted;log.BorderStyle=BorderStyle.FixedSingle;log.Visible=false;log.AccessibleName="Dziennik sesji";Ui.Add(root,log);
   mode.SelectedIndexChanged+=async delegate{if(!busy&&!previewMode){UpdateControls();await RefreshEquipment();}};
   code.TextChanged+=delegate{if(!server&&!busy&&!previewMode){try{var invite=Invitation.Parse(code.Text);mode.SelectedIndex=invite.Mode=="USB"?0:1;SetStatus(usbRestartRequired&&invite.Mode=="USB"?new UsbRestartRequiredException().Message:"Kod gotowy. Tryb "+invite.Mode+" zostanie ustawiony automatycznie.");}catch(FormatException e){if(code.TextLength>0)SetStatus(e.Message);}UpdateControls();}};
   timer.Tick+=delegate{if(tunnel!=null)transfer.Text="Wysłano "+Interlocked.Read(ref tunnel.Sent)/1024+" KB  •  Odebrano "+Interlocked.Read(ref tunnel.Received)/1024+" KB";};if(!previewMode)timer.Start();
   Shown+=async delegate{if(previewMode){SetStatus(server?"Interfejs wykryty. Możesz udostępnić połączenie.":"Gotowy. Wklej kod z komputera przy samochodzie.");}else if(configError!=null){SetStatus(configError);statusTitle.Text="SPRAWDŹ USTAWIENIA";advanced.Checked=true;UpdateControls();}else await RefreshEquipment();};
   FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy||refreshing){closing=true;e.Cancel=true;StopSession();SetStatus("Kończenie sesji i zwalnianie interfejsu…");}else timer.Dispose();};
   if(previewMode){vpn.Items.Add(new VpnAddress{Adapter="Radmin VPN",Address=System.Net.IPAddress.Parse("26.104.18.42")});vpn.SelectedIndex=0;if(server){equipment.Items.Add(new UsbIpDevice{Description="Openport 2.0 · J2534",BusId="1-3",InstanceId="PREVIEW"});equipment.SelectedIndex=0;}}
   UpdateControls();
  }
  void ShowHelp(){ShowEmbeddedDocument("AutoTunel.Help.html","AutoTunel-0.8.1.html");}
  void ShowUsbDiagnostics(){
   try{string folder=WindowsUsbSetupHost.DiagnosticsDirectory;Directory.CreateDirectory(folder);Open(folder);}
   catch(Exception e){Error(e);}
  }
  void ShowEmbeddedDocument(string resourceName,string fileName){
   try{using(var resource=typeof(MainForm).Assembly.GetManifestResourceStream(resourceName)){
    if(resource==null)throw new IOException("Nie znaleziono instrukcji w aplikacji.");
    string folder=Path.Combine(Platform.DataDir,"help");Directory.CreateDirectory(folder);string path=Path.Combine(folder,fileName);using(var file=File.Create(path))resource.CopyTo(file);Open(path);
   }}catch(Exception e){Error(e);}
  }
  static string MessageOf(Exception error){while(error is AggregateException&&error.InnerException!=null)error=error.InnerException;return error.Message;}
  void SetStatus(string text){if(!IsDisposed){status.Text=text;statusTitle.Text="STATUS SESJI";statusTitle.ForeColor=Ui.Teal;statusCard.BorderColor=Color.FromArgb(42,92,85);}}
  void Error(Exception error){string message=MessageOf(error);SetStatus(message);statusTitle.Text="WYMAGA UWAGI";statusTitle.ForeColor=Ui.Warning;statusCard.BorderColor=Color.FromArgb(121,93,53);Log(message);}
  void Log(string message){
   if(IsDisposed||Disposing)return;
   if(InvokeRequired){try{BeginInvoke((Action)(()=>Log(message)));}catch(InvalidOperationException){}return;}
   if(log.TextLength>60000)log.Clear();log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);
   if(message.StartsWith("OCZEKIWANIE",StringComparison.Ordinal)&&busy&&cancellation!=null&&!cancellation.IsCancellationRequested){code.Text=pendingCode;SetStatus("Gotowe. Przekaż kod diagnoście i pozostaw program otwarty.");UpdateControls();}
   if(server&&busy&&cancellation!=null&&!cancellation.IsCancellationRequested&&message.StartsWith("POŁĄCZONO",StringComparison.Ordinal))SetStatus("Diagnosta połączony. Interfejs jest udostępniany.");
  }
  void Open(string target){try{Process.Start(new ProcessStartInfo(target){UseShellExecute=true});}catch(Exception e){Error(e);}}
  UsbIpBackend NewBackend(){return new UsbIpBackend(new SystemCommandRunner(),server?tool.Text:"",server?"":tool.Text);}
  void UpdateControls(){
   bool idle=!busy&&!refreshing;bool hasJournal=!previewMode&&File.Exists(Journal);
   changeRole.Enabled=idle&&share==null&&attachment==null;
   start.Enabled=idle&&share==null&&attachment==null&&!hasJournal&&(!UsbMode||!usbRestartRequired);stop.Enabled=busy;refresh.Enabled=idle;prepareUsb.Enabled=idle&&!usbRestartRequired;
   mode.Enabled=server&&idle;vpn.Enabled=server&&idle;equipment.Enabled=idle&&(server||!UsbMode);advanced.Enabled=idle;
   copy.Enabled=server?busy&&code.TextLength>0:idle;code.ReadOnly=server||busy||refreshing;settings.Enabled=idle;
   recover.Visible=hasJournal||share!=null&&!busy||attachment!=null&&!busy;recover.Enabled=idle;
   deviceLabel.Text=UsbMode?(server?"Interfejs USB":"USB z kodu"):(server?"Przewód ENET":"Karta TAP");
   equipment.Visible=server||!UsbMode;deviceHint.Visible=!server&&UsbMode;refresh.Visible=server||!UsbMode;
  }
  void ReportUsbSetup(string message){
   if(IsDisposed||Disposing)return;
   if(InvokeRequired){try{BeginInvoke((Action)(()=>ReportUsbSetup(message)));}catch(InvalidOperationException){}return;}
   Log(message);
   if(message.StartsWith("Przygotowywanie",StringComparison.Ordinal)||message.StartsWith("Instalowanie",StringComparison.Ordinal)||message.StartsWith("Obsługa USB",StringComparison.Ordinal))SetStatus(message);
  }
  async Task EnsureUsbReady(CancellationToken token,bool repair=false){
   SetStatus("Sprawdzanie wbudowanej obsługi USB…");
   try{tool.Text=await Task.Run(()=>new UsbSetup(new WindowsUsbSetupHost()).EnsureReady(server,token,ReportUsbSetup,repair));usbRestartRequired=false;}
   catch(UsbRestartRequiredException){usbRestartRequired=true;throw;}
   catch(UsbInstallationUncertainException){usbRestartRequired=true;throw;}
   catch(OperationCanceledException){throw;}
   catch{usbRestartRequired=false;throw;}
  }
  async Task RefreshEquipment(bool prepareClient=false,bool repair=false){
   if(repair&&(busy||refreshing||share!=null||attachment!=null||File.Exists(Journal))){Error(new IOException("Najpierw zwolnij USB po poprzedniej sesji. Zakończ bieżącą sesję lub kliknij Zwolnij USB po awarii, a następnie napraw obsługę USB."));return;}
   if(busy||refreshing)return;refreshing=true;UpdateControls();
   try{
    string previous=(equipment.SelectedItem as UsbIpDevice)?.InstanceId??(equipment.SelectedItem as AdapterInfo)?.Id;
    var selected=vpn.SelectedItem as VpnAddress;vpn.Items.Clear();foreach(var item in WindowsUsb.VpnAddresses())vpn.Items.Add(item);
    vpn.SelectedIndex=-1;for(int i=0;i<vpn.Items.Count;i++)if(((VpnAddress)vpn.Items[i]).Address.Equals(selected?.Address))vpn.SelectedIndex=i;
    if(vpn.SelectedIndex<0&&vpn.Items.Count==1)vpn.SelectedIndex=0;
    equipment.Items.Clear();
    if(prepareClient&&!UsbMode)await EnsureUsbReady(CancellationToken.None,repair);
    if(UsbMode){if(server||prepareClient)await EnsureUsbReady(CancellationToken.None,repair);
     else{
      try{usbRestartRequired=await Task.Run(()=>{var host=new WindowsUsbSetupHost();host.CheckPlatform(false);using(host.EnterSetup(CancellationToken.None))return host.RestartPending(false);});}
      catch{usbRestartRequired=false;throw;}
      if(usbRestartRequired)throw new UsbRestartRequiredException();
     }
     if(server){var engine=NewBackend();var devices=await Task.Run(()=>engine.Devices());foreach(var device in devices)equipment.Items.Add(device);
      var matches=devices.Where(d=>d.InstanceId==previous).ToList();if(matches.Count==0)matches=devices.Where(d=>(d.Description??"").IndexOf("Openport",StringComparison.OrdinalIgnoreCase)>=0||(d.Description??"").IndexOf("Tactrix",StringComparison.OrdinalIgnoreCase)>=0).ToList();
      if(matches.Count==1)equipment.SelectedItem=matches[0];else if(devices.Count==1)equipment.SelectedIndex=0;
     }
    }else{
     var adapters=AdapterInfo.List(!server);foreach(var adapter in adapters)equipment.Items.Add(adapter);
     equipment.SelectedIndex=-1;for(int i=0;i<equipment.Items.Count;i++)if(((AdapterInfo)equipment.Items[i]).Id==previous)equipment.SelectedIndex=i;
     if(equipment.SelectedIndex<0&&equipment.Items.Count==1)equipment.SelectedIndex=0;
    }
    SetStatus(UsbMode&&usbRestartRequired?new UsbRestartRequiredException().Message:vpn.Items.Count==0?"Uruchom RadminVPN i połącz oba komputery z tą samą siecią.":File.Exists(Journal)?"Pozostało USB z poprzedniej sesji. Kliknij Zwolnij USB po awarii.":"Gotowy. "+(server?"Wybierz interfejs i kliknij Udostępnij.":"Wklej kod z komputera przy samochodzie. Obsługa USB zostanie przygotowana automatycznie."));
   }catch(Exception e){Error(e);if(UsbMode)advanced.Checked=true;}
   finally{refreshing=false;UpdateControls();if(closing)Close();}
  }
  static Task Blocking(Action action){return Task.Factory.StartNew(action,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);}
  void StopSession(){if(cancellation!=null)cancellation.Cancel();tunnel?.Dispose();SetStatus("Zwalnianie interfejsu…");}
  async Task StartSession(){
   if(busy||refreshing||share!=null||attachment!=null)return;
   busy=true;cancellation=new CancellationTokenSource();var token=cancellation.Token;UpdateControls();X509Certificate2 certificate=null;sessionTask=null;
   string failure=null;
   try{
    if(File.Exists(Journal))throw new IOException("Najpierw zwolnij USB po poprzedniej sesji.");
    if(WindowsUsb.VpnAddresses().Count==0)throw new IOException("Uruchom RadminVPN i dołącz do sieci klienta.");
    Invitation invite;var address=vpn.SelectedItem as VpnAddress;
    if(server){
     if(address==null)throw new IOException("Wybierz swój adres RadminVPN.");
     if(UsbMode&&!(equipment.SelectedItem is UsbIpDevice))throw new IOException("Podłącz Openport i wybierz go na liście USB.");
     invite=new Invitation{Host=address.Address.ToString(),Port=SessionPort,Pin=new string('A',64),Key=Wire.Secret(),Mode=UsbMode?"USB":"LAN",UsbBusId=UsbMode?((UsbIpDevice)equipment.SelectedItem).BusId:""};
    }else {invite=Invitation.Parse(code.Text);mode.SelectedIndex=invite.Mode=="USB"?0:1;}
    invite.Validate();var adapter=equipment.SelectedItem as AdapterInfo;
    if(invite.Mode=="LAN"){
     if(adapter==null)throw new IOException(server?"Wybierz kartę połączoną przewodem z autem.":"Wybierz wirtualną kartę TAP-Windows6.");
     if(server){var nic=NetworkInterface.GetAllNetworkInterfaces().SingleOrDefault(n=>n.Id==adapter.Id);if(nic==null||nic.GetIPProperties().GatewayAddresses.Count>0)throw new IOException("Wybierz dedykowaną kartę ENET bez bramy. Internet pozostaw na drugiej karcie.");}
    }
    SetStatus("Przygotowanie interfejsu…");
    if(invite.Mode=="USB"){
     await EnsureUsbReady(token);backend=NewBackend();token.ThrowIfCancellationRequested();
     if(server){
      if(File.Exists(Journal))throw new IOException("Najpierw zwolnij USB po poprzedniej sesji.");
      var selected=equipment.SelectedItem as UsbIpDevice;
      share=await Task.Run(()=>backend.Share(selected));
      if(share.Owned){Directory.CreateDirectory(Platform.DataDir);File.WriteAllText(Journal,share.Device.PersistedGuid);}
      await Task.Run(()=>WindowsUsb.ProbeExport(invite.UsbBusId));token.ThrowIfCancellationRequested();
     }
    }
    token.ThrowIfCancellationRequested();
    if(server){certificate=await Task.Run(()=>Platform.Certificate());token.ThrowIfCancellationRequested();invite.Pin=Wire.Pin(certificate.RawData);pendingCode=invite.Encode();code.Clear();}
    var current=new Tunnel(server,invite.Mode,Log,0,3240,server?address.Address:null);tunnel=current;var cert=certificate;
    sessionTask=Blocking(()=>current.Run(invite,cert,()=>server?(IEthernet)new PcapAdapter(adapter.Id):new TapAdapter(adapter.Id)));
    if(!server){
     var deadline=DateTime.UtcNow.AddSeconds(30);SetStatus("Łączenie z komputerem przy samochodzie…");
     while(!current.Connected){token.ThrowIfCancellationRequested();if(sessionTask.IsCompleted){await sessionTask;throw new IOException("Nie utworzono połączenia.");}if(DateTime.UtcNow>deadline)throw new IOException("Przekroczono czas zestawiania połączenia.");await Task.Delay(50,token);}
     if(invite.Mode=="USB"){
      Directory.CreateDirectory(Platform.DataDir);File.WriteAllText(Journal,current.UsbPort+"|"+invite.UsbBusId);
      SetStatus("Podłączanie USB do Windows…");attachment=await Task.Run(()=>backend.Attach(current.UsbPort,invite.UsbBusId));token.ThrowIfCancellationRequested();
      SetStatus("USB podłączone do Windows. Sprawdź Openport w Xentry PassThru.");
     }else SetStatus("Tunel ENET połączony. Sprawdź adresację TAP i wykrywanie auta.");
    }
    if(!server&&attachment!=null){
     while(!sessionTask.IsCompleted){
      await Task.WhenAny(sessionTask,Task.Delay(2000,token));token.ThrowIfCancellationRequested();
      if(!sessionTask.IsCompleted&&!await Task.Run(()=>attachment.Present()))throw new IOException("Utracono interfejs USB. Sprawdź przewód i rozpocznij nową sesję.");
     }
    }
    await sessionTask;
   }catch(Exception e){if(e is UsbRestartRequiredException||!token.IsCancellationRequested){failure=MessageOf(e);Log(failure);}}
   finally{
    tunnel?.Dispose();if(sessionTask!=null)try{await sessionTask;}catch(Exception){}
    bool clean=await Cleanup();certificate?.Dispose();tunnel=null;pendingCode=null;if(server)code.Clear();cancellation.Dispose();cancellation=null;busy=false;
    UpdateControls();SetStatus(!clean?"Nie udało się zwolnić USB. Użyj przycisku zwalniania i sprawdź szczegóły.":failure??"Sesja zakończona. Interfejs został zwolniony.");if(closing)Close();
   }
  }
  async Task<bool> Cleanup(){
   bool clean=true;
   if(attachment!=null)try{await Task.Run(()=>attachment.Dispose());attachment=null;}catch(Exception e){clean=false;Log(MessageOf(e));}
   if(share!=null)try{await Task.Run(()=>share.Dispose());share=null;if(File.Exists(Journal))File.Delete(Journal);}catch(Exception e){clean=false;Log(MessageOf(e));}
   if(!server&&attachment==null&&File.Exists(Journal))try{await ReleaseClientJournal();}catch(Exception e){clean=false;Log(MessageOf(e));}
   return clean;
  }
  async Task ReleaseClientJournal(){
   string[] fields=File.ReadAllText(Journal).Trim().Split('|');int port;
   if(fields.Length!=2||!int.TryParse(fields[0],out port)||port<1024||port>65535||!UsbIpBackend.ValidBusId(fields[1]))throw new IOException("Uszkodzony zapis poprzedniej sesji USB. Sprawdź instrukcję odzyskiwania.");
   var engine=NewBackend();await Task.Run(()=>{engine.CheckTools(false);engine.ReleaseEndpoint(port,fields[1]);});File.Delete(Journal);
  }
  async Task Recover(){
   if(busy||refreshing)return;refreshing=true;UpdateControls();
   try{
    tool.Text=await Task.Run(()=>{var host=new WindowsUsbSetupHost();string path=host.FindTool(server);host.CheckVersion(server,path);return path;});
    if(!await Cleanup())throw new IOException("Nie udało się zwolnić interfejsu. Sprawdź szczegóły.");
    if(server&&File.Exists(Journal)){string guid=File.ReadAllText(Journal).Trim();var engine=NewBackend();await Task.Run(()=>{engine.CheckTools(true);engine.Unshare(guid);});File.Delete(Journal);}
    SetStatus("USB zwolnione. Możesz rozpocząć nową sesję.");
   }catch(Exception e){Error(e);}finally{refreshing=false;UpdateControls();if(closing)Close();}
  }
 }
 static class Program {
  [STAThread] static void Main(){
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   bool created;using(var mutex=new Mutex(true,"Local\\AutoTunel-MB",out created)){
    if(!created){MessageBox.Show("AutoTunel jest już uruchomiony.","AutoTunel MB");return;}
    try{
     while(true){
      bool server;using(var picker=new RolePicker()){if(picker.ShowDialog()!=DialogResult.OK)return;server=picker.Server;}
      bool roleCreated;using(var roleMutex=new Mutex(true,"Local\\AutoTunel-MB-"+(server?"Server":"Client"),out roleCreated)){
       if(!roleCreated){MessageBox.Show("Ta rola jest już uruchomiona w innym oknie AutoTunel.","AutoTunel MB");continue;}
       try{using(var form=new MainForm(server)){Application.Run(form);if(!form.ChangeRoleRequested)return;}}
       finally{roleMutex.ReleaseMutex();}
      }
     }
    }finally{mutex.ReleaseMutex();}
   }
  }
 }
}
