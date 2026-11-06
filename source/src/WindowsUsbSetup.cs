using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AutoTunel {
 public sealed class WindowsUsbSetupHost:IUsbSetupHost {
  const string RegistryPath=@"SOFTWARE\AutoTunel-MB\UsbSetup";
  static readonly object diagnosticsLock=new object();
  public static string DiagnosticsDirectory {get{return Path.Combine(Platform.DataDir,"diagnostics");}}
  static string Role(bool server){return server?"usb-server":"usb-client";}
  static string DiagnosticPath(bool server){return Path.Combine(DiagnosticsDirectory,Role(server)+"-last.log");}
  static void Record(bool server,string phase,string detail,Exception error=null,bool required=false){
   try{
    lock(diagnosticsLock){
     Directory.CreateDirectory(DiagnosticsDirectory);string path=DiagnosticPath(server);
     if(File.Exists(path)&&new FileInfo(path).Length>4*1024*1024){string previous=Path.ChangeExtension(path,"previous.log");File.Copy(path,previous,true);File.Delete(path);}
     using(var file=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.ReadWrite))
     using(var writer=new StreamWriter(file,new UTF8Encoding(false))){
      writer.WriteLine(DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture)+" ["+phase+"] "+detail);
      if(error!=null){writer.WriteLine(error.ToString());for(Exception current=error;current!=null;current=current.InnerException){var native=current as Win32Exception;if(native!=null)writer.WriteLine("NativeErrorCode="+native.NativeErrorCode.ToString(CultureInfo.InvariantCulture));}}
      writer.Flush();file.Flush(true);
     }
    }
   }catch(Exception failure){if(required)throw new IOException("Nie można zapisać diagnostyki przygotowania USB w "+DiagnosticsDirectory+". Instalacja nie została uruchomiona.",failure);}
  }
  static void RecordFailure(bool server,string phase,Exception error){Record(server,phase,"Failure",error);CaptureSetupApi(server);}
  static void CopyInstallerLog(bool server,string original){
   try{
    if(!File.Exists(original))return;Directory.CreateDirectory(DiagnosticsDirectory);
    string copy=Path.Combine(DiagnosticsDirectory,Role(server)+"-msi-last.log");
    using(var input=new FileStream(original,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
    using(var output=new FileStream(copy,FileMode.Create,FileAccess.Write,FileShare.Read)){input.CopyTo(output);output.Flush(true);}
    Record(server,"installer-log","Original: "+original+"; copy: "+copy);
   }catch(Exception error){Record(server,"installer-log","Could not copy original log: "+original,error);}
  }
  static void CaptureSetupApi(bool server){
   string source=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"INF","setupapi.dev.log");
   try{
    const int limit=256*1024;
    using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
     input.Position=Math.Max(0,input.Length-limit);var bytes=new byte[(int)Math.Min(limit,input.Length-input.Position)];int used=0,read;
     while(used<bytes.Length&&(read=input.Read(bytes,used,bytes.Length-used))>0)used+=read;
     Directory.CreateDirectory(DiagnosticsDirectory);string target=Path.Combine(DiagnosticsDirectory,Role(server)+"-setupapi-last.log");
     using(var output=new FileStream(target,FileMode.Create,FileAccess.Write,FileShare.Read)){output.Write(bytes,0,used);output.Flush(true);}
     Record(server,"setupapi","Last "+used+" bytes copied from "+source+" to "+target);
    }
   }catch(Exception error){Record(server,"setupapi","Could not collect SetupAPI log tail: "+source,error);}
  }
  sealed class HeldMutex:IDisposable {
   readonly Mutex mutex;public HeldMutex(Mutex value){mutex=value;}
   public void Dispose(){mutex.ReleaseMutex();mutex.Dispose();}
  }
  public IDisposable EnterSetup(CancellationToken token){
   var mutex=new Mutex(false,@"Global\AutoTunel-MB-USB-Setup");
   try{
    int result;
    try{result=WaitHandle.WaitAny(new WaitHandle[]{mutex,token.WaitHandle},TimeSpan.FromMinutes(10));}catch(AbandonedMutexException){result=0;}
    if(result==1)throw new OperationCanceledException(token);
    if(result!=0)throw new IOException("Inne okno przygotowuje USB. Poczekaj na zakończenie i spróbuj ponownie.");
    return new HeldMutex(mutex);
   }catch{mutex.Dispose();throw;}
  }
  static RegistryKey Machine(){return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);}
  static string Marker(bool server){return server?"ServerRestart":"ClientRestart";}
  static string ReadBootStamp(){
   var scope=new ManagementScope(@"\\.\root\cimv2");scope.Options.Timeout=TimeSpan.FromSeconds(5);
   var options=new EnumerationOptions{Timeout=TimeSpan.FromSeconds(5),ReturnImmediately=false};
   using(var query=new ManagementObjectSearcher(scope,new ObjectQuery("SELECT LastBootUpTime FROM Win32_OperatingSystem"),options))
   using(var results=query.Get())foreach(ManagementObject item in results)using(item){
    var value=item["LastBootUpTime"] as string;
    if(!string.IsNullOrEmpty(value))return ManagementDateTimeConverter.ToDateTime(value).ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
   }
   throw new IOException("Windows nie podał czasu ostatniego uruchomienia.");
  }
  static string BootStamp(bool server){
   try{
    // Bound the caller too: some WMI providers ignore their enumeration timeout.
    // A late query may finish, but cannot change any registry or driver state.
    var pending=Task.Run((Func<string>)ReadBootStamp);
    if(Task.WhenAny(pending,Task.Delay(TimeSpan.FromSeconds(6))).GetAwaiter().GetResult()!=pending){
     pending.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
     throw new System.TimeoutException("Zapytanie WMI LastBootUpTime nie zakończyło się w ciągu 6 sekund.");
    }
    return pending.GetAwaiter().GetResult();
   }catch(Exception error){RecordFailure(server,"boot-time",error);throw new IOException("Windows nie udostępnił informacji o ostatnim uruchomieniu. Nie można potwierdzić zakończenia przygotowania USB. Diagnostyka: "+DiagnosticPath(server),error);}
  }
  public void CheckPlatform(bool server){
   try{var host=WindowsHostInfo.Read();Record(server,"platform",host+"; bundled version="+UsbPackage.ForRole(server).Version,null,true);UsbPlatformPolicy.RequireSupported(server,host.Major,host.Build,host.NativeX64);}
   catch(Exception error){RecordFailure(server,"platform",error);throw;}
  }
  public bool RestartPending(bool server){
   try{
    using(var machine=Machine())using(var key=machine.OpenSubKey(RegistryPath,true)){
     var value=key?.GetValue(Marker(server));if(value==null)return false;
     if(!(value is string))throw new IOException("Uszkodzony zapis przygotowania USB w Windows.");
     if((string)value==BootStamp(server)){Record(server,"restart","Preparation belongs to current boot.");return true;}
     key.DeleteValue(Marker(server));Record(server,"restart","New Windows boot confirmed; old marker removed.");return false;
    }
   }catch(Exception error){Record(server,"restart","Cannot verify restart marker",error);throw;}
  }
  public void MarkRestart(bool server){using(var machine=Machine())using(var key=machine.CreateSubKey(RegistryPath))key.SetValue(Marker(server),BootStamp(server),RegistryValueKind.String);Record(server,"restart","Preparation marker saved.");}
  public void ClearRestart(bool server){using(var machine=Machine())using(var key=machine.OpenSubKey(RegistryPath,true))key?.DeleteValue(Marker(server),false);Record(server,"restart","Preparation marker cleared.");}
  public string FindTool(bool server){
   using(var machine=Machine())using(var key=machine.OpenSubKey(server?@"SOFTWARE\usbipd-win":@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{199505b0-b93d-4521-a8c7-897818e0205a}_is1")){
    string folder=key?.GetValue(server?"APPLICATIONFOLDER":"InstallLocation") as string;
    if(!string.IsNullOrWhiteSpace(folder)){
     string registered=Path.Combine(folder,server?"usbipd.exe":"usbip.exe");
     if(File.Exists(registered)){Record(server,"tool-path","Registered installation: "+registered);return registered;}
     Record(server,"tool-path","Registered installation tool missing: "+registered);
     if(server&&RegisteredServerVersion()!=null)return registered;
    }
   }
   string known=WindowsUsb.FindTool(server);Record(server,"tool-path","Settings/default fallback: "+known+"; exists="+File.Exists(known));return known;
  }
  public bool ToolExists(string path){return File.Exists(path);}
  string ReportVersion(bool server,string path){string output=new SystemCommandRunner().Run(path,new[]{"--version"},5000);Record(server,"version","Tool: "+path+"; reported: "+output);return output;}
  public void CheckVersion(bool server,string path){
   try{
    string output=ReportVersion(server,path);UsbIpBackend.CheckVersion(output,server);
    if(server&&ParseServerVersion(output)!=ExpectedServerVersion())throw new IOException("Wykryto inną wersję obsługi USB. AutoTunel wymaga usbipd-win "+UsbPackage.ForRole(true).Version+".");
   }catch(Exception error){RecordFailure(server,"version",error);throw;}
  }
  [DllImport("msi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern int MsiQueryProductStateW(string productCode);
  static Version ReleaseVersion(Version value){return new Version(value.Major,value.Minor,Math.Max(0,value.Build),Math.Max(0,value.Revision));}
  static Version ExpectedServerVersion(){return ReleaseVersion(new Version(UsbPackage.ForRole(true).Version));}
  static Version RegisteredServerVersion(){
   Version installed=null;
   using(var machine=Machine())using(var uninstall=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")){
    if(uninstall==null)return null;
    foreach(string name in uninstall.GetSubKeyNames())using(var entry=uninstall.OpenSubKey(name)){
     string display=entry?.GetValue("DisplayName") as string;
     if(display==null||!(display.Equals("usbipd-win",StringComparison.OrdinalIgnoreCase)||display.StartsWith("usbipd-win ",StringComparison.OrdinalIgnoreCase)))continue;
     string text=entry.GetValue("DisplayVersion") as string;Guid product;
     if(!Guid.TryParse(name,out product)||Convert.ToString(entry.GetValue("WindowsInstaller"),CultureInfo.InvariantCulture)!="1")throw new IOException("Nie można potwierdzić rejestracji usbipd-win w Windows Installer. Sprawdź diagnostykę przed naprawą.");
     int state=MsiQueryProductStateW(product.ToString("B"));Record(true,"msi-registration","Product="+product.ToString("B")+"; version="+(text??"unknown")+"; state="+state);
     if(state==-1)continue; // A stale uninstall entry is not an installed MSI.
     if(state!=5)throw new IOException("Windows Installer nie potwierdził kompletnej instalacji usbipd-win (stan "+state+"). Najpierw dokończ poprzednią instalację.");
     Version version;
     if(!Version.TryParse(text,out version)||version.Build<0)throw new IOException("Windows Installer nie podał rozpoznanej wersji usbipd-win.");
     version=ReleaseVersion(version);
     if(installed!=null&&installed!=version)throw new IOException("Windows Installer zawiera kilka wersji usbipd-win. Nie można bezpiecznie wybrać instalacji do naprawy.");
     installed=version;
    }
   }
   return installed;
  }
  static Version ParseServerVersion(string output){
   var match=Regex.Match((output??"").Trim(),@"\A(?:usbipd(?:-win)?\s+)?([0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?)(?:[+-][^\r\n]*|\s*)\z",RegexOptions.IgnoreCase);
   Version result;if(!match.Success||!Version.TryParse(match.Groups[1].Value,out result))throw new IOException("Narzędzie nie zgłosiło rozpoznanej wersji usbipd-win. Wybierz właściwy plik usbipd.exe przed naprawą.");return ReleaseVersion(result);
  }
  static bool ServiceRunning(){
   using(var service=new ServiceController("usbipd"))try{
    var state=service.Status;
    if(state!=ServiceControllerStatus.Running&&state!=ServiceControllerStatus.Stopped)throw new IOException("Usługa USB zmienia stan. Poczekaj na zakończenie jej pracy przed naprawą.");
    return state==ServiceControllerStatus.Running;
   }catch(InvalidOperationException error){var native=error.InnerException as Win32Exception;if(native!=null&&native.NativeErrorCode==1060)return false;throw;}
  }
  public void CheckRepairAllowed(bool server,string path){
   try{
    if(!server)throw new IOException("Naprawa jest dostępna tylko dla obsługi USB przy samochodzie.");
    var registered=RegisteredServerVersion();var expected=ExpectedServerVersion();
    Version tool=File.Exists(path)?ParseServerVersion(ReportVersion(true,path)):null;
    Record(true,"repair-version","Tool="+(tool==null?"missing":tool.ToString())+"; registered="+(registered==null?"none":registered.ToString())+"; target="+expected);
    if(tool!=null&&tool>expected||registered!=null&&registered>expected)throw new IOException("Wykryto nowszą wersję usbipd-win. AutoTunel nie obniża wersji obsługi USB.");
    if(tool!=null&&(registered==null&&tool!=expected||registered!=null&&tool!=registered))throw new IOException("Wersja wybranego usbipd.exe nie zgadza się z instalacją Windows Installer. Wybierz właściwy plik; AutoTunel nie zastępuje nierozpoznanej instalacji.");
    if(File.Exists(Path.Combine(Platform.DataDir,"owned-usb-share.txt")))throw new IOException("Zachowano zapis udostępnienia USB. Najpierw zakończ sesję lub odzyskaj interfejs, a potem ponów naprawę.");
    // Do not change any journal, driver, VM or client while proving repair is idle.
    foreach(var process in Process.GetProcesses())using(process){
     string name;try{name=process.ProcessName;}catch(InvalidOperationException){continue;}
     if(name.Equals("VirtualBoxVM",StringComparison.OrdinalIgnoreCase)||name.Equals("VBoxHeadless",StringComparison.OrdinalIgnoreCase)||name.Equals("VBoxSDL",StringComparison.OrdinalIgnoreCase))throw new IOException("Działa maszyna wirtualna VirtualBox. Zamknij ją przed naprawą obsługi USB.");
    }
    if(IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Any(connection=>connection.LocalEndPoint.Port==3240&&connection.State==TcpState.Established))throw new IOException("Trwa połączenie z serwerem USB. Odłącz wszystkich klientów przed naprawą.");
    bool running=ServiceRunning();
    if(running){
     if(tool==null)throw new IOException("Usługa USB działa, ale brakuje usbipd.exe do sprawdzenia klientów. Zakończ sesje USB i zatrzymaj usługę USBIP Device Host w Usługach Windows przed naprawą.");
     if(tool!=expected)throw new IOException("Starsza usługa USB działa i nie można potwierdzić braku klientów. Zakończ sesje USB i zatrzymaj usługę USBIP Device Host w Usługach Windows przed aktualizacją.");
     if(new UsbIpBackend(new SystemCommandRunner(),path,path).Devices().Any(device=>!string.IsNullOrWhiteSpace(device.ClientIPAddress)))throw new IOException("Interfejs USB jest używany przez klienta. Zakończ wszystkie sesje USB przed naprawą.");
    }
    Record(true,"repair-check","No owned share, VirtualBox VM or active USB client detected; service="+(running?"running":"stopped or absent"));
   }catch(Exception error){RecordFailure(server,"repair-check",error);throw;}
  }
  public void CheckReady(bool server,string path){
   try{
    var engine=new UsbIpBackend(new SystemCommandRunner(),path,path);engine.CheckTools(server);
    if(server){
     using(var service=new ServiceController("usbipd"))try{
      Record(true,"service","usbipd status="+service.Status);if(service.Status==ServiceControllerStatus.Stopped)service.Start();service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(30));
     }catch(Exception error)when(error is InvalidOperationException||error is System.ServiceProcess.TimeoutException||error is Win32Exception){throw new IOException("Windows nie uruchomił obsługi USB. Sprawdź diagnostykę: "+DiagnosticPath(true)+". "+error.Message,error);}
     engine.Devices();
    }
    Record(server,"ready","USB readiness confirmed: "+path);
   }catch(Exception error){RecordFailure(server,"ready",error);throw;}
  }
  public string Stage(UsbPackage package){
   // Unpredictable staging path, inaccessible to unelevated users, never TEMP.
   string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"AutoTunel-MB-USB-"+Guid.NewGuid().ToString("N"));
   var security=new DirectorySecurity();security.SetAccessRuleProtection(true,false);
   foreach(var sid in new[]{WellKnownSidType.BuiltinAdministratorsSid,WellKnownSidType.LocalSystemSid})security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
   Directory.CreateDirectory(folder,security);string target=Path.Combine(folder,package.FileName);
   try{using(var input=typeof(WindowsUsbSetupHost).Assembly.GetManifestResourceStream(package.ResourceName))UsbPayload.WriteVerified(input,target,package.Sha256);}
   catch(Exception error){RecordFailure(package.Server,"stage",error);try{Directory.Delete(folder,true);}catch(Exception cleanup){Record(package.Server,"stage-cleanup",folder,cleanup);}throw;}
   Record(package.Server,"stage","Verified version="+package.Version+"; SHA256="+package.Sha256+"; path="+target);return target;
  }
  public void DiscardStage(string installer){File.Delete(installer);string folder=Path.GetDirectoryName(installer);if(!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);}
  public int Install(UsbPackage package,string installer,Action<string> progress,bool repair=false){
   bool server=package.Server;string folder=null,log=null;
   Process process;
   try{
    folder=Path.GetDirectoryName(installer);log=Path.Combine(folder,"install.log");
    Version registered=server?RegisteredServerVersion():null;
    if(server&&!repair&&registered!=null)throw new IOException("Windows Installer zawiera już usbipd-win "+registered+", ale brakuje prawidłowego narzędzia. Kliknij Napraw USB, aby naprawić tę instalację.");
    if(registered!=null&&registered>ExpectedServerVersion())throw new IOException("Zainstalowano nowszą wersję usbipd-win; AutoTunel nie obniża wersji.");
    if(server)CheckRepairAllowed(true,FindTool(true));
    bool msiRepair=repair&&server&&registered==ExpectedServerVersion();
    string executable=server?Path.Combine(Environment.SystemDirectory,"msiexec.exe"):installer;
    string commandLine=UsbInstallCommand.CommandLine(package,installer,log,msiRepair);
    Record(server,"installer-before-launch",WindowsHostInfo.Read()+"; package="+package.Version+"; previous MSI="+(registered==null?"none":registered.ToString())+"; repair requested="+repair+"; registered MSI repair="+msiRepair+"; executable="+executable+"; original protected log="+log+"; arguments="+commandLine,null,true);
    progress("Diagnostyka USB: "+DiagnosticPath(server));progress("Dziennik instalatora: "+log);
    process=Process.Start(new ProcessStartInfo(executable,commandLine){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=folder});
    if(process==null)throw new IOException("Windows nie zwrócił uruchomionego procesu instalatora.");
   }catch(Exception error){RecordFailure(server,"installer-launch",error);CopyInstallerLog(server,log);throw new UsbInstallerLaunchException("Windows nie uruchomił instalatora USB. Przyczyna: "+error.Message+" Diagnostyka: "+DiagnosticPath(server),error);}
   bool exited=false;
   try{
    using(process){
     int exit;
     try{Record(server,"installer-started","PID="+process.Id);if(!process.WaitForExit(15*60*1000))throw new System.TimeoutException("Instalator nie zakończył pracy w ciągu 15 minut.");exit=process.ExitCode;exited=true;}
     catch(Exception error){RecordFailure(server,"installer-uncertain",error);throw new UsbInstallationUncertainException("Nie potwierdzono zakończenia instalacji USB. Instalator może nadal działać. Nie uruchamiaj drugiej instalacji. Dziennik: "+log+". Diagnostyka: "+DiagnosticPath(server),error);}
     Record(server,"installer-exit","ExitCode="+exit+"; original log="+log);if(exit!=0&&exit!=3010&&exit!=1641)CaptureSetupApi(server);
     progress("Przygotowanie USB: kod "+exit+". Dziennik: "+log);return exit;
    }
   }finally{CopyInstallerLog(server,log);if(exited)try{File.Delete(installer);}catch(Exception error){Record(server,"installer-cleanup","Installer remains at "+installer,error);}}
  }
 }
}
