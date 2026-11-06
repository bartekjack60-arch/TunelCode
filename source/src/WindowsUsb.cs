using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace AutoTunel {
 public sealed class SystemCommandRunner:ICommandRunner {
  public static string QuoteArgument(string value){
   if(value==null||value.IndexOf('\0')>=0)throw new ArgumentException("Nieprawidłowy argument.");
   var s=new StringBuilder("\"");int slashes=0;
   foreach(char c in value){if(c=='\\'){slashes++;continue;}s.Append('\\',c=='"'?slashes*2+1:slashes);s.Append(c);slashes=0;}
   s.Append('\\',slashes*2);return s.Append('"').ToString();
  }
  public string Run(string executable,string[] arguments,int timeout){
   if(string.IsNullOrWhiteSpace(executable)||!File.Exists(executable))throw new FileNotFoundException("Brak lokalnej obsługi USB. Otwórz Ustawienia USB i kliknij Przygotuj USB.");
   using(var process=Process.Start(new ProcessStartInfo(executable,string.Join(" ",arguments.Select(QuoteArgument))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8})){
    var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
    if(!process.WaitForExit(timeout)){try{process.Kill();process.WaitForExit(3000);}catch(InvalidOperationException){}throw new IOException("Narzędzie USB/IP nie odpowiedziało w wyznaczonym czasie.");}
    string text=output.GetAwaiter().GetResult(),detail=error.GetAwaiter().GetResult();
    if(process.ExitCode!=0)throw new IOException("USB/IP: "+(string.IsNullOrWhiteSpace(detail)?text:detail).Trim());
    return text.Trim();
   }
  }
 }
 public sealed class VpnAddress {
  public string Adapter;public IPAddress Address;
  public override string ToString(){return Adapter+" — "+Address;}
 }
 public static class WindowsUsb {
  public static List<VpnAddress> VpnAddresses(){
   var list=new List<VpnAddress>();
   foreach(var nic in NetworkInterface.GetAllNetworkInterfaces()){
    if(nic.OperationalStatus!=OperationalStatus.Up||(nic.Name+" "+nic.Description).IndexOf("Radmin",StringComparison.OrdinalIgnoreCase)<0)continue;
    foreach(var address in nic.GetIPProperties().UnicastAddresses)if(address.Address.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(address.Address))list.Add(new VpnAddress{Adapter=nic.Name,Address=address.Address});
   }
   return list;
  }
  public static string FindTool(bool server){
   string saved=new AppSettingsStore(Platform.DataDir).GetTool(server);
   if(!string.IsNullOrEmpty(saved)&&File.Exists(saved))return saved;
   string root=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
   return Path.Combine(root,server?"usbipd-win":"USBip",server?"usbipd.exe":"usbip.exe");
  }
  public static void RememberTool(bool server,string path){new AppSettingsStore(Platform.DataDir).SetTool(server,path);}
  public static void ProbeExport(string busId){
   using(var tcp=new TcpClient()){
    if(!tcp.ConnectAsync(IPAddress.Loopback,3240).Wait(2000))throw new IOException("Usługa USB/IP nie odpowiada. Sprawdź usługę usbipd.");
    tcp.ReceiveTimeout=3000;tcp.SendTimeout=3000;UsbIpProbe.RequireExport(tcp.GetStream(),busId);
   }
  }
 }
}
