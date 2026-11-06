using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;

namespace AutoTunel {
 [DataContract] public sealed class UsbPackage {
  [DataMember(Name="role",IsRequired=true)] public string Role {get;private set;}
  [DataMember(Name="version",IsRequired=true)] public string Version {get;private set;}
  [DataMember(Name="fileName",IsRequired=true)] public string FileName {get;private set;}
  [DataMember(Name="resourceName",IsRequired=true)] public string ResourceName {get;private set;}
  [DataMember(Name="sha256",IsRequired=true)] public string Sha256 {get;private set;}
  public bool Server {get{return Role=="server";}}
  static readonly Lazy<UsbPackage[]> packages=new Lazy<UsbPackage[]>(()=>{
   using(var stream=typeof(UsbPackage).Assembly.GetManifestResourceStream("AutoTunel.Usb.Packages.json")){
    if(stream==null)throw new InvalidDataException("Brak opisu wbudowanej obsługi USB. Pobierz kompletny AutoTunel.exe.");
    var items=(UsbPackage[])new DataContractJsonSerializer(typeof(UsbPackage[])).ReadObject(stream);
    if(items==null||items.Length!=2||items.Count(p=>p.Role=="server")!=1||items.Count(p=>p.Role=="client")!=1)
     throw new InvalidDataException("Nieprawidłowy opis wbudowanej obsługi USB.");
    foreach(var item in items){
     if(string.IsNullOrWhiteSpace(item.FileName)||Path.GetFileName(item.FileName)!=item.FileName||item.FileName.IndexOfAny(new[]{'\\','/'})>=0||
        string.IsNullOrWhiteSpace(item.ResourceName)||!item.ResourceName.StartsWith("AutoTunel.Usb.",StringComparison.Ordinal)||
        item.Sha256==null||item.Sha256.Length!=64||item.Sha256.Any(c=>!Uri.IsHexDigit(c)))
      throw new InvalidDataException("Nieprawidłowy pakiet USB.");
    }
    return items;
   }
  });
  public static UsbPackage ForRole(bool server){return packages.Value.Single(p=>p.Server==server);}
 }

 public sealed class UsbRestartRequiredException:IOException {
  public UsbRestartRequiredException():base("Aby dokończyć przygotowanie USB, uruchom ponownie komputer (Start → Uruchom ponownie), a następnie otwórz AutoTunel. Program nie restartuje komputera automatycznie."){}
 }
 public sealed class UsbInstallerLaunchException:IOException {
  public UsbInstallerLaunchException(string message,Exception inner=null):base(message,inner){}
 }
 public sealed class UsbInstallationUncertainException:IOException {
  public UsbInstallationUncertainException(string message,Exception inner=null):base(message,inner){}
 }
 public static class UsbPlatformPolicy {
  public static void RequireSupported(bool server,int major,int build,bool nativeX64){
   int minimum=server?17763:18362;
   if(!nativeX64)throw new IOException("Obsługa USB w tej wersji AutoTunel wymaga systemu Windows x64 (AMD64).");
   if(major<10||(major==10&&build<minimum))throw new IOException("Wykryto Windows "+major+", kompilacja "+build+". Rola „"+(server?"Przy samochodzie":"Diagnosta")+"” wymaga Windows 10 "+(server?"1809 (LTSC 2019)":"1903")+" lub nowszego, kompilacja co najmniej "+minimum+". Zobacz Diagnostyka USB.");
  }
 }

 // All machine-wide effects are behind this boundary; policy tests do not install drivers.
 public interface IUsbSetupHost {
  IDisposable EnterSetup(CancellationToken token);
  void CheckPlatform(bool server);
  bool RestartPending(bool server);
  string FindTool(bool server);
  bool ToolExists(string path);
  void CheckVersion(bool server,string path);
  void CheckReady(bool server,string path);
  void CheckRepairAllowed(bool server,string path);
  string Stage(UsbPackage package);
  void DiscardStage(string installer);
  void MarkRestart(bool server);
  void ClearRestart(bool server);
  int Install(UsbPackage package,string installer,Action<string> progress,bool repair=false);
 }
 public sealed class UsbSetup {
  readonly IUsbSetupHost host;
  public UsbSetup(IUsbSetupHost platform){host=platform;}
  public string EnsureReady(bool server,CancellationToken token,Action<string> progress,bool repair=false){
   using(host.EnterSetup(token)){
    token.ThrowIfCancellationRequested();
    host.CheckPlatform(server);
    if(host.RestartPending(server))throw new UsbRestartRequiredException();
    string tool=host.FindTool(server);
    bool existing=host.ToolExists(tool);
    if(existing){
     // Normal reuse requires the pinned version; explicit repair eligibility is checked by the host.
     if(!repair)host.CheckVersion(server,tool);
     if(!repair){
      try{host.CheckReady(server,tool);return tool;}
      catch(IOException error){throw new IOException(server?"Obsługa USB jest zainstalowana, ale nie działa prawidłowo. Otwórz Ustawienia USB i kliknij Napraw USB. Przyczyna: "+error.Message:error.Message,error);}
     }
    }
    if(repair){
     if(!server)throw new IOException("Naprawa wbudowana dotyczy komputera przy samochodzie.");
     host.CheckRepairAllowed(server,tool);
    }
    var package=UsbPackage.ForRole(server);
    progress(repair?"Przygotowywanie naprawy obsługi USB…":"Przygotowywanie wbudowanej obsługi USB…");
    string installer=host.Stage(package);bool installing=false,marked=false;
    try{
     token.ThrowIfCancellationRequested();
     // Survives a crash or a timed-out setup. A fresh boot is required before retrying.
     host.MarkRestart(server);marked=true;token.ThrowIfCancellationRequested();
     progress(repair?"Instalowanie obsługi USB w trybie naprawy… Poczekaj na zakończenie.":"Instalowanie sterownika USB… Poczekaj na zakończenie.");
     installing=true;int exit;
     try{exit=host.Install(package,installer,progress,repair);}
     catch(UsbInstallerLaunchException){host.ClearRestart(server);marked=false;installing=false;throw;}
     if(exit!=0&&exit!=3010&&exit!=1641){
      host.ClearRestart(server);
      string reason=exit==1639?"Instalator Windows odrzucił parametry polecenia. Uruchom najnowszą wersję AutoTunel.":exit==1618?"Inna instalacja Windows jest w toku. Poczekaj na jej zakończenie.":exit==1633?"Pakiet nie obsługuje architektury lub wersji tego Windows.":exit==1603?"Instalator Windows zgłosił błąd instalacji sterownika.":"Instalator zakończył pracę z błędem.";
      throw new IOException("Nie udało się przygotować USB (kod "+exit+"). "+reason+" Kliknij Diagnostyka USB. Po usunięciu przyczyny użyj Napraw USB.");
     }
     // The upstream client explicitly declares AlwaysRestart=yes, even if its launcher returns 0.
     if(exit==3010||exit==1641||!server)throw new UsbRestartRequiredException();
     tool=host.FindTool(server);
     if(!host.ToolExists(tool))throw new IOException("Instalator zakończył pracę, ale brakuje obsługi USB. Sprawdź dziennik instalacji w Szczegółach.");
     host.CheckReady(server,tool);
     host.ClearRestart(server);token.ThrowIfCancellationRequested();
     progress("Obsługa USB jest gotowa.");return tool;
    }finally{if(!installing){if(marked)host.ClearRestart(server);host.DiscardStage(installer);}}
   }
  }
 }
 public static class UsbPayload {
  public static void WriteVerified(Stream input,string destination,string expectedHash){
   if(input==null)throw new InvalidDataException("Brak pakietu USB w pliku EXE. Pobierz kompletny AutoTunel.exe.");
   bool created=false;
   try{
    using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
     created=true;
     using(var digest=SHA256.Create()){
      var buffer=new byte[65536];int count;
      while((count=input.Read(buffer,0,buffer.Length))>0){output.Write(buffer,0,count);digest.TransformBlock(buffer,0,count,buffer,0);}
      digest.TransformFinalBlock(new byte[0],0,0);
      string actual=BitConverter.ToString(digest.Hash).Replace("-","");
      if(!string.Equals(actual,expectedHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Pakiet USB jest uszkodzony. Pobierz ponownie kompletny AutoTunel.exe.");
     }
     output.Flush(true);
    }
   }catch{if(created)File.Delete(destination);throw;}
  }
 }
 public static class UsbInstallCommand {
  public static string CommandLine(UsbPackage package,string installer,string log,bool repair=false){
   var arguments=Arguments(package,installer,log,repair);
   if(!package.Server)return string.Join(" ",arguments.Select(SystemCommandRunner.QuoteArgument));
   // MSI parses its own grammar: switches and PROPERTY names must remain literal.
   // Quote only the path following each file-taking option; do not use CRT argv quoting.
   for(int i=arguments.Length-1;i>0;i--)
    if(arguments[i-1]=="/i"||arguments[i-1]=="/L*V")arguments[i]=MsiFilePath(arguments[i]);
   return string.Join(" ",arguments);
  }
  static string MsiFilePath(string value){
   if(string.IsNullOrWhiteSpace(value)||value.IndexOf('"')>=0||value.Any(char.IsControl))throw new ArgumentException("Nieprawidłowa ścieżka pliku dla instalatora Windows.");
   return "\""+value+"\"";
  }
  public static string[] Arguments(UsbPackage package,string installer,string log,bool repair=false){
   if(package.Server){
    var args=new System.Collections.Generic.List<string>{"/i",installer,"/qn","/norestart","REBOOT=ReallySuppress","MSIRESTARTMANAGERCONTROL=Disable","ADDLOCAL=Server,Drivers","/L*V",log};
    if(repair){args.Add("REINSTALL=ALL");args.Add("REINSTALLMODE=amus");}
    return args.ToArray();
   }
   return new[]{"/SP-","/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/RESTARTEXITCODE=3010","/NOCANCEL","/NOCLOSEAPPLICATIONS","/NORESTARTAPPLICATIONS","/NOICONS","/TYPE=custom","/COMPONENTS=main,client","/TASKS=vcredist","/LOG="+log};
  }
 }
}
