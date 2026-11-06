using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using AutoTunel;

static class UsbSetupTests {
 static void Assert(bool value,string message){if(!value)throw new Exception(message);}
 static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
 sealed class Held:IDisposable{readonly Action release;public Held(Action value){release=value;}public void Dispose(){release();}}
 sealed class Host:IUsbSetupHost {
  public bool Exists,Pending,WrongVersion,NotReady,MissingAfterInstall,Closed,InvalidPayload,Unsupported,BusyRepair,LaunchFails,Timeout;
  public int Exit,Installs,Stages,Checks,Marks,Clears,Discards,Versions,Repairs,PlatformChecks,RepairChecks,RestartChecks;
  public Action OnStage,OnInstall,OnMark;
  public IDisposable EnterSetup(CancellationToken token){token.ThrowIfCancellationRequested();Closed=false;return new Held(()=>Closed=true);}
  public void CheckPlatform(bool server){PlatformChecks++;if(Unsupported)throw new IOException("unsupported Windows build");}
  public bool RestartPending(bool server){RestartChecks++;return Pending;}
  public string FindTool(bool server){return server?"server.exe":"client.exe";}
  public bool ToolExists(string path){return Exists;}
  public void CheckVersion(bool server,string path){Versions++;if(WrongVersion)throw new IOException("incompatible existing installation");}
  public void CheckReady(bool server,string path){Checks++;if(NotReady||WrongVersion)throw new IOException("incomplete installation");}
  public void CheckRepairAllowed(bool server,string path){RepairChecks++;if(BusyRepair)throw new IOException("active unrelated USB session");}
  public string Stage(UsbPackage package){Stages++;if(InvalidPayload)throw new InvalidDataException("damaged embedded payload");OnStage?.Invoke();return package.FileName;}
  public void DiscardStage(string installer){Discards++;}
  public void MarkRestart(bool server){Pending=true;Marks++;OnMark?.Invoke();}
  public void ClearRestart(bool server){Pending=false;Clears++;}
  public int Install(UsbPackage package,string installer,Action<string> progress,bool repair=false){
   Installs++;if(repair)Repairs++;OnInstall?.Invoke();
   if(LaunchFails)throw new UsbInstallerLaunchException("process not started");
   if(Timeout)throw new UsbInstallationUncertainException("still running");
   Exists=!MissingAfterInstall;if(repair){NotReady=false;WrongVersion=false;}return Exit;
  }
 }
 static string Ready(Host host,bool server,CancellationToken token=default(CancellationToken)){return new UsbSetup(host).EnsureReady(server,token,_=>{});}
 public static void Run(Action<string,Action> test){
  test("USB compatibility allows LTSC1809 server but requires1903 for client",()=>{
   UsbPlatformPolicy.RequireSupported(true,10,17763,true);
   UsbPlatformPolicy.RequireSupported(false,10,18362,true);
   UsbPlatformPolicy.RequireSupported(false,10,22621,true);
   Throws<IOException>(()=>UsbPlatformPolicy.RequireSupported(true,10,17762,true));
   Throws<IOException>(()=>UsbPlatformPolicy.RequireSupported(false,10,17763,true));
   Throws<IOException>(()=>UsbPlatformPolicy.RequireSupported(true,6,99999,true));
   Throws<IOException>(()=>UsbPlatformPolicy.RequireSupported(true,10,22621,false));
  });
  test("USB setup reports unsupported Windows before stale markers or machine changes",()=>{
   var host=new Host{Unsupported=true,Pending=true};Throws<IOException>(()=>Ready(host,true));
   Assert(host.PlatformChecks==1&&host.RestartChecks==0&&host.Stages==0&&host.Marks==0&&host.Installs==0&&host.Pending&&host.Closed,"unsupported system touched");
  });
  test("USB version error identifies installed and required versions",()=>{
   bool rejected=false;
   try{UsbIpBackend.CheckVersion("usbipd-win 4.4.0",true);}catch(IOException e){rejected=true;Assert(e.Message.Contains("4.4.0")&&e.Message.Contains("5.3.0"),"version explanation incomplete");}
   Assert(rejected,"incorrect version accepted");
  });
  test("USB setup reuses healthy installations of both roles without reinstalling",()=>{
   foreach(bool server in new[]{true,false}){
    var host=new Host{Exists=true};Assert(Ready(host,server)==host.FindTool(server),"tool path");
    Assert(host.Checks==1&&host.Installs==0&&host.Stages==0&&host.Marks==0&&host.Closed,"existing install changed");
   }
  });
  test("USB setup prepares a clean server once and verifies readiness before success",()=>{
   var host=new Host();Ready(host,true);Ready(host,true);
   Assert(host.Installs==1&&host.Stages==1&&host.Checks==2&&host.Clears==1&&!host.Pending&&host.Closed,"server lifecycle");
  });
  test("USB setup persists client restart even when the installer returns zero",()=>{
   foreach(int exit in new[]{0,3010}){
    var host=new Host{Exit=exit};Throws<UsbRestartRequiredException>(()=>Ready(host,false));
    Throws<UsbRestartRequiredException>(()=>Ready(host,false));
    Assert(host.Installs==1&&host.Pending&&host.Checks==0&&host.Clears==0&&host.Closed,"restart was lost");
    host.Pending=false;Ready(host,false);Assert(host.Installs==1&&host.Checks==1,"reboot caused reinstall");
   }
  });
  test("USB setup treats Windows reboot exit codes as pending rather than ready",()=>{
   foreach(int exit in new[]{3010,1641}){
    var host=new Host{Exit=exit};Throws<UsbRestartRequiredException>(()=>Ready(host,true));
    Assert(host.Pending&&host.Checks==0&&host.Clears==0&&host.Closed,"reboot reported as success");
   }
  });
  test("USB setup does not turn definitive installer failures into restart loops",()=>{
   foreach(int exit in new[]{1603,1633,1618,1639,2}){
    var host=new Host{Exit=exit};Throws<IOException>(()=>Ready(host,true));
    Assert(host.Installs==1&&!host.Pending&&host.Clears==1&&host.Checks==0&&host.Closed,"failure fabricated restart");
    host.Exit=0;Ready(host,true);Assert(host.Checks==1&&host.Installs==1,"failed attempt cannot be retried");
   }
  });
  test("USB setup rejects a damaged package before any machine change",()=>{
   var host=new Host{InvalidPayload=true};Throws<InvalidDataException>(()=>Ready(host,true));
   Assert(host.Installs==0&&host.Marks==0&&host.Stages==1&&host.Closed,"unverified installer executed");
  });
  test("USB setup cancellation before installation leaves no staged package or machine change",()=>{
   using(var token=new CancellationTokenSource()){
    var host=new Host{OnStage=()=>token.Cancel()};Throws<OperationCanceledException>(()=>Ready(host,true,token.Token));
    Assert(host.Installs==0&&host.Marks==0&&host.Discards==1&&host.Closed,"cancelled installer left state");
   }
   using(var token=new CancellationTokenSource()){
    token.Cancel();var host=new Host();Throws<OperationCanceledException>(()=>Ready(host,true,token.Token));
    Assert(host.Stages==0&&host.Installs==0,"pre-cancelled setup ran");
   }
  });
  test("USB setup preserves an incompatible existing installation",()=>{
   var host=new Host{Exists=true,WrongVersion=true};Throws<IOException>(()=>Ready(host,true));
   Assert(host.Versions==1&&host.Checks==0&&host.Installs==0&&host.Stages==0&&host.Marks==0&&host.Closed,"existing install overwritten");
  });
  test("USB setup leaves incomplete installations untouched until explicit repair",()=>{
   var host=new Host{Exists=true,NotReady=true};Throws<IOException>(()=>Ready(host,true));
   Assert(host.Versions==1&&host.Checks==1&&host.Installs==0&&host.Marks==0,"automatic repair started");
   new UsbSetup(host).EnsureReady(true,CancellationToken.None,_=>{},true);
   Assert(host.RepairChecks==1&&host.Repairs==1&&host.Installs==1&&!host.NotReady&&!host.Pending,"explicit repair failed");
  });
  test("USB setup checks active-device safety before staging an explicit repair",()=>{
   var host=new Host{Exists=true,NotReady=true,BusyRepair=true};
   Throws<IOException>(()=>new UsbSetup(host).EnsureReady(true,CancellationToken.None,_=>{},true));
   Assert(host.RepairChecks==1&&host.Installs==0&&host.Stages==0&&host.Marks==0&&host.Closed,"busy devices disturbed");
  });
  test("USB setup delegates explicit older-version upgrade eligibility to the host",()=>{
   var host=new Host{Exists=true,WrongVersion=true};
   new UsbSetup(host).EnsureReady(true,CancellationToken.None,_=>{},true);
   Assert(host.RepairChecks==1&&host.Versions==0&&host.Repairs==1&&!host.WrongVersion&&!host.Pending,"guarded upgrade did not finish");
  });
  test("USB setup launch failure releases unused reboot and staged-file reservations",()=>{
   var host=new Host{LaunchFails=true};Throws<UsbInstallerLaunchException>(()=>Ready(host,true));
   Assert(!host.Pending&&host.Clears==1&&host.Discards==1&&host.Closed,"launch failure fabricated reboot");
  });
  test("USB setup retains uncertain in-flight state and never starts a second installer",()=>{
   var host=new Host{Timeout=true};Throws<UsbInstallationUncertainException>(()=>Ready(host,true));
   Assert(host.Pending&&host.Clears==0&&host.Discards==0&&host.Closed,"running installer state lost");
   Throws<UsbRestartRequiredException>(()=>Ready(host,true));Assert(host.Installs==1,"second installer started");
  });
  test("USB setup rolls back a reservation when cancelled just before installer launch",()=>{
   using(var token=new CancellationTokenSource()){
    var host=new Host{OnMark=()=>token.Cancel()};Throws<OperationCanceledException>(()=>Ready(host,true,token.Token));
    Assert(host.Installs==0&&host.Marks==1&&host.Clears==1&&!host.Pending&&host.Discards==1&&host.Closed,"unused reservation retained");
   }
  });
  test("USB setup finishes an in-flight installation before honoring cancellation",()=>{
   using(var token=new CancellationTokenSource()){
    var host=new Host{OnInstall=()=>token.Cancel()};Throws<OperationCanceledException>(()=>Ready(host,true,token.Token));
    Assert(host.Installs==1&&host.Checks==1&&host.Clears==1&&!host.Pending&&host.Closed,"server setup interrupted");
   }
   using(var token=new CancellationTokenSource()){
    var host=new Host{OnInstall=()=>token.Cancel()};Throws<UsbRestartRequiredException>(()=>Ready(host,false,token.Token));
    Assert(host.Installs==1&&host.Pending&&host.Clears==0&&host.Closed,"client restart hidden by cancellation");
   }
  });
  test("USB setup does not report success when an installer leaves no usable tool",()=>{
   var host=new Host{MissingAfterInstall=true};Throws<IOException>(()=>Ready(host,true));
   Assert(host.Pending&&host.Clears==0&&host.Checks==0&&host.Closed,"missing tool accepted");
   host=new Host{WrongVersion=true};Throws<IOException>(()=>Ready(host,true));
   Assert(host.Pending&&host.Clears==0&&host.Checks==1,"broken tool accepted");
  });
  test("USB installers suppress automatic reboots and include the client runtime",()=>{
   string path=@"C:\USB setup\payload.msi",log=@"C:\USB setup\install.log";
   var server=UsbInstallCommand.Arguments(UsbPackage.ForRole(true),path,log);
   Assert(server.Contains(path)&&server.Contains(log)&&server.Contains("/norestart")&&server.Contains("REBOOT=ReallySuppress"),"MSI reboot or path contract");
   Assert(!server.Contains("REINSTALL=ALL"),"first install incorrectly repairs absent product");
   var repair=UsbInstallCommand.Arguments(UsbPackage.ForRole(true),path,log,true);
   Assert(repair.Contains("REINSTALL=ALL")&&repair.Contains("REINSTALLMODE=amus")&&repair.Contains("MSIRESTARTMANAGERCONTROL=Disable")&&repair.Contains("REBOOT=ReallySuppress"),"repair mode or restart safety contract");
   var client=UsbInstallCommand.Arguments(UsbPackage.ForRole(false),path,log);
   Assert(client.Contains("/SP-")&&client.Contains("/NORESTART")&&client.Contains("/RESTARTEXITCODE=3010")&&client.Contains("/TASKS=vcredist"),"client prompt/reboot/runtime contract");
   Assert(client.Contains("/COMPONENTS=main,client")&&client.Contains("/NOCLOSEAPPLICATIONS")&&client.Contains("/NORESTARTAPPLICATIONS"),"unnecessary GUI or application closing");
  });
  test("MSI command line keeps switches and property names unquoted for install and repair",()=>{
   string path=@"C:\USB setup\payload.msi",log=@"C:\USB setup\install.log";
   string expected=@"/i ""C:\USB setup\payload.msi"" /qn /norestart REBOOT=ReallySuppress MSIRESTARTMANAGERCONTROL=Disable ADDLOCAL=Server,Drivers /L*V ""C:\USB setup\install.log""";
   foreach(bool repair in new[]{false,true}){
    string actual=UsbInstallCommand.CommandLine(UsbPackage.ForRole(true),path,log,repair);
    string suffix=repair?" REINSTALL=ALL REINSTALLMODE=amus":"";
    Assert(actual==expected+suffix,"Invalid MSI command syntax: "+actual);
   }
  });
  test("MSI command line preserves Unicode spaces and equals signs inside file paths",()=>{
   string path=@"C:\Dane=USB\żółć i auto\payload.msi",log=@"C:\Raporty kierowcy\próba.log";
   string expected=@"/i ""C:\Dane=USB\żółć i auto\payload.msi"" /qn /norestart REBOOT=ReallySuppress MSIRESTARTMANAGERCONTROL=Disable ADDLOCAL=Server,Drivers /L*V ""C:\Raporty kierowcy\próba.log""";
   Assert(UsbInstallCommand.CommandLine(UsbPackage.ForRole(true),path,log)==expected,"literal file paths changed");
  });
  test("MSI command line rejects quote and control characters before launch",()=>{
   foreach(string invalid in new[]{"", "   ", "C:\\bad\"name.msi", "C:\\bad\nname.msi", "C:\\bad\rname.msi", "C:\\bad\0name.msi"}){
    Throws<ArgumentException>(()=>UsbInstallCommand.CommandLine(UsbPackage.ForRole(true),invalid,@"C:\install.log"));
    Throws<ArgumentException>(()=>UsbInstallCommand.CommandLine(UsbPackage.ForRole(true),@"C:\payload.msi",invalid));
   }
  });
  test("Client installer keeps conventional argument boundaries separate from MSI",()=>{
   string installer=@"C:\USB setup\client.exe",log=@"C:\USB setup\client.log";
   var package=UsbPackage.ForRole(false);
   string expected=string.Join(" ",UsbInstallCommand.Arguments(package,installer,log).Select(SystemCommandRunner.QuoteArgument));
   Assert(UsbInstallCommand.CommandLine(package,installer,log)==expected,"client argument contract changed");
  });
  test("USB payload rejects corruption, missing data and existing output without destroying files",()=>{
   string folder=Path.Combine(Path.GetTempPath(),"autotunel-payload-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
   try{
    string target=Path.Combine(folder,"usb.bin");byte[] data=Encoding.UTF8.GetBytes("official fixture");
    string hash;using(var sha=SHA256.Create())hash=Convert.ToHexString(sha.ComputeHash(data));
    using(var input=new MemoryStream(data))UsbPayload.WriteVerified(input,target,hash);
    Assert(File.ReadAllBytes(target).SequenceEqual(data),"valid bytes changed");
    using(var input=new MemoryStream(data))Throws<IOException>(()=>UsbPayload.WriteVerified(input,target,hash));
    Assert(File.ReadAllBytes(target).SequenceEqual(data),"existing output destroyed");File.Delete(target);
    using(var input=new MemoryStream(data))Throws<InvalidDataException>(()=>UsbPayload.WriteVerified(input,target,new string('0',64)));
    Assert(!File.Exists(target),"corrupt output retained");
    Throws<InvalidDataException>(()=>UsbPayload.WriteVerified(null,target,hash));Assert(!File.Exists(target),"missing payload accepted");
   }finally{Directory.Delete(folder,true);}
  });
 }
 public static void VerifyBundle(string file){
  string root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file),"../../.."));
  string version=System.Xml.Linq.XDocument.Load(Path.Combine(root,"source/AutoTunel.csproj")).Descendants("Version").Single().Value;
  var assembly=Assembly.LoadFile(Path.GetFullPath(file));
  Assert(assembly.GetName().Version==new Version(version+".0"),"wrong EXE version");
  foreach(bool server in new[]{true,false}){
   var package=UsbPackage.ForRole(server);
   using(var stream=assembly.GetManifestResourceStream(package.ResourceName))using(var sha=SHA256.Create()){
    Assert(stream!=null,"missing "+package.ResourceName);
    Assert(Convert.ToHexString(sha.ComputeHash(stream)).Equals(package.Sha256,StringComparison.OrdinalIgnoreCase),"changed "+package.FileName);
   }
   Console.WriteLine("Verified embedded official package: "+package.FileName);
  }
  using(var stream=assembly.GetManifestResourceStream("AutoTunel.Help.html"))using(var output=new MemoryStream()){
   Assert(stream!=null,"missing help");stream.CopyTo(output);
   Assert(output.ToArray().SequenceEqual(File.ReadAllBytes(Path.Combine(root,"INSTRUKCJA.html"))),"stale help");
  }
  foreach(var pair in new[]{new[]{"AutoTunel.Usb.Notices.txt","vendor/licenses/USB-NOTICES.txt"},new[]{"AutoTunel.Usb.Packages.json","vendor/usb/packages.json"},new[]{"AutoTunel.Brand.ico","source/AutoTunel.ico"},new[]{"AutoTunel.Brand.png","source/AutoTunel.png"}}){
   using(var stream=assembly.GetManifestResourceStream(pair[0]))using(var output=new MemoryStream()){
    Assert(stream!=null,"missing "+pair[0]);stream.CopyTo(output);
    Assert(output.ToArray().SequenceEqual(File.ReadAllBytes(Path.Combine(root,pair[1]))),"stale "+pair[0]);
   }
  }
  Assert(!File.Exists(file+".config"),"EXE config required");
  Assert(!Directory.GetFiles(Path.GetDirectoryName(file),"*.dll").Any(),"external managed DLLs");
  Assert(Directory.GetFiles(Path.GetDirectoryName(file)).Length==1,"build output contains files alongside the EXE");
  Console.WriteLine("Verified EXE "+version+".0, current embedded help, licenses and branding, no DLL/config sidecars.");
 }
}
