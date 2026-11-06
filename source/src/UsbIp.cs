using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoTunel {
 // usbipd-win 5.3.0 'state' is a documented JSON automation interface.
 [DataContract] public sealed class UsbIpDevice {
  [DataMember] public string BusId;
  [DataMember] public string Description;
  [DataMember] public string InstanceId;
  [DataMember] public string PersistedGuid;
  [DataMember] public string ClientIPAddress;
  public bool Bound { get { Guid value; return Guid.TryParse(PersistedGuid,out value); } }
  public override string ToString(){return (Description??"Urządzenie USB")+" ["+BusId+"]";}
 }
 [DataContract] sealed class UsbIpState {
  [DataMember(IsRequired=true)] public List<UsbIpDevice> Devices {get;set;}
 }
 public interface ICommandRunner {
  string Run(string executable,string[] arguments,int timeout);
 }
 public sealed class UsbIpBackend {
  readonly ICommandRunner runner;
  readonly string hostTool,clientTool;
  public UsbIpBackend(ICommandRunner commands,string hostExecutable,string clientExecutable){runner=commands;hostTool=hostExecutable;clientTool=clientExecutable;}
  public static bool ValidBusId(string value){return value!=null&&value.Length<=31&&Regex.IsMatch(value,@"\A[1-9][0-9]*-[1-9][0-9]*(?:\.[1-9][0-9]*)*\z");}
  public static void CheckVersion(string output,bool server){
   var digits=Regex.Matches(output??"",@"[0-9]+").Cast<Match>().Select(m=>m.Value).ToArray();
   var expected=server?new[]{"5","3","0"}:new[]{"0","9","8","1"};
   if(digits.Length<expected.Length||!digits.Take(expected.Length).SequenceEqual(expected)){
    string detected=digits.Length>=expected.Length?string.Join(".",digits.Take(expected.Length)):"nierozpoznana";
    throw new IOException("Wykryta wersja "+(server?"usbipd-win":"usbip-win2")+": "+detected+". Wbudowana wersja wymagana przez AutoTunel: "+string.Join(".",expected)+". "+(server?"Kliknij Napraw USB, aby sprawdzić możliwość dostosowania instalacji.":"Kliknij Diagnostyka USB i sprawdź zainstalowaną wersję klienta."));
   }
  }
  public void CheckTools(bool server){CheckVersion(runner.Run(server?hostTool:clientTool,new[]{"--version"},5000),server);if(!server)runner.Run(clientTool,new[]{"port"},5000);}
  public static List<UsbIpDevice> ParseState(string json){
   if(json==null||json.Length>2*1024*1024)throw new InvalidDataException("Nieprawidłowy stan USB/IP.");
   using(var input=new MemoryStream(Encoding.UTF8.GetBytes(json.TrimStart('\uFEFF')))){
    var state=(UsbIpState)new DataContractJsonSerializer(typeof(UsbIpState)).ReadObject(input);
    if(state==null||state.Devices==null||state.Devices.Count>1024)throw new InvalidDataException("Nieobsługiwany stan USB/IP.");
    return state.Devices.Where(d=>d!=null&&ValidBusId(d.BusId)&&!string.IsNullOrEmpty(d.InstanceId)).ToList();
   }
  }
  public List<UsbIpDevice> Devices(){return ParseState(runner.Run(hostTool,new[]{"state"},10000));}
  public SharedUsb Share(UsbIpDevice selected){
   if(selected==null||!ValidBusId(selected.BusId))throw new ArgumentException("Wybierz urządzenie USB.");
   var before=Devices().SingleOrDefault(d=>d.BusId==selected.BusId&&d.InstanceId==selected.InstanceId);
   if(before==null)throw new IOException("Urządzenie odłączono lub zmieniono. Odśwież listę USB.");
   if(!string.IsNullOrEmpty(before.ClientIPAddress))throw new IOException("Urządzenie jest już używane przez inny komputer.");
   bool own=!before.Bound;
   if(own)runner.Run(hostTool,new[]{"bind","--busid",before.BusId},15000);
   try{
    var after=Devices().SingleOrDefault(d=>d.BusId==before.BusId&&d.InstanceId==before.InstanceId);
    if(after==null||!after.Bound||!string.IsNullOrEmpty(after.ClientIPAddress))throw new IOException("Nie potwierdzono udostępnienia wybranego urządzenia.");
    return new SharedUsb(this,after,own);
   }catch{
    // Re-read identity before rollback: USB bus numbers can be reused after unplug.
    if(own){var current=Devices().SingleOrDefault(d=>d.InstanceId==before.InstanceId);if(current!=null&&current.Bound)Unshare(current.PersistedGuid);}
    throw;
   }
  }
  public void Unshare(string guid){Guid value;if(!Guid.TryParse(guid,out value))throw new ArgumentException("Nieprawidłowy identyfikator udostępnienia.");runner.Run(hostTool,new[]{"unbind","--guid",value.ToString("D")},15000);}
  public UsbAttachment Attach(int tcpPort,string busId){
   if(tcpPort<1024||tcpPort>65535||!ValidBusId(busId))throw new ArgumentException("Nieprawidłowe połączenie USB.");
   if(FindPorts(tcpPort,busId).Count!=0)throw new IOException("Ten interfejs jest już podłączony lokalnie.");
   try{
    string output=runner.Run(clientTool,new[]{"--tcp-port",tcpPort.ToString(CultureInfo.InvariantCulture),"attach","--remote","127.0.0.1","--bus-id",busId,"--terse","--once"},20000);
    int port;if(!int.TryParse(output.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out port)||port<1||port>255)throw new IOException("USB/IP nie potwierdził numeru podłączonego portu.");
    if(!FindPorts(tcpPort,busId).Contains(port))throw new IOException("Sterownik nie potwierdził podłączenia tego urządzenia.");
    return new UsbAttachment(this,port,tcpPort,busId);
   }catch{
    // No detach-all and no background reconnect: only this unique loopback endpoint.
    ReleaseEndpoint(tcpPort,busId);
    throw;
   }
  }
  public List<int> FindPorts(int tcpPort,string busId){return ParsePorts(runner.Run(clientTool,new[]{"port"},5000),tcpPort,busId);}
  public void ReleaseEndpoint(int tcpPort,string busId){
   if(tcpPort<1024||tcpPort>65535||!ValidBusId(busId))throw new ArgumentException("Nieprawidłowe zapisane połączenie USB.");
   foreach(int port in FindPorts(tcpPort,busId))Detach(port);
   if(FindPorts(tcpPort,busId).Count!=0)throw new IOException("Sterownik nie zwolnił połączenia USB.");
  }
  public static List<int> ParsePorts(string output,int tcpPort,string busId){
   var found=new List<int>();int port=0;
   foreach(string line in (output??"").Split('\n')){
    var m=Regex.Match(line.Trim(),@"\APort ([0-9]+):");
    if(m.Success){port=int.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture);continue;}
    const string marker="-> usbip://";string row=line.Trim();
    if(port<1||port>255||!row.StartsWith(marker,StringComparison.Ordinal))continue;
    Uri uri;if(Uri.TryCreate(row.Substring(3),UriKind.Absolute,out uri)&&uri.Scheme=="usbip"&&uri.Host=="127.0.0.1"&&uri.Port==tcpPort&&uri.AbsolutePath=="/"+busId)found.Add(port);
   }
   return found;
  }
  public void Detach(int port){if(port<1||port>255)throw new ArgumentException("Nieprawidłowy port USB.");runner.Run(clientTool,new[]{"detach","--port",port.ToString(CultureInfo.InvariantCulture)},10000);}
 }
 public sealed class SharedUsb:IDisposable {
  readonly UsbIpBackend backend;public readonly UsbIpDevice Device;public readonly bool Owned;
  bool released;
  public SharedUsb(UsbIpBackend owner,UsbIpDevice device,bool owned){backend=owner;Device=device;Owned=owned;}
  public void Dispose(){if(released)return;if(Owned)backend.Unshare(Device.PersistedGuid);released=true;}
 }
 public sealed class UsbAttachment:IDisposable {
  readonly UsbIpBackend backend;readonly int tcpPort;readonly string busId;public readonly int Port;bool released;
  public UsbAttachment(UsbIpBackend owner,int port,int localPort,string bus){backend=owner;Port=port;tcpPort=localPort;busId=bus;}
  public bool Present(){return !released&&backend.FindPorts(tcpPort,busId).Contains(Port);}
  public void Dispose(){if(released)return;if(Present())backend.Detach(Port);released=true;}
 }
 // USB/IP 1.1.1 OP_REQ_IMPORT: an 8-byte header followed by a 32-byte bus id.
 // Buffer fragmented headers; do not forward device lists or another device's import.
 public sealed class UsbImportGuard {
  readonly string allowed;readonly byte[] prefix=new byte[40];int used;bool accepted;
  public UsbImportGuard(string busId){if(!UsbIpBackend.ValidBusId(busId))throw new ArgumentException("Nieprawidłowe urządzenie USB.");allowed=busId;}
  public byte[] Feed(byte[] data){
   if(accepted)return data;
   int n=Math.Min(40-used,data.Length);Buffer.BlockCopy(data,0,prefix,used,n);used+=n;
   if(used<40)return new byte[0];
   if(prefix[0]!=1||prefix[1]!=0x11||prefix[2]!=0x80||prefix[3]!=3||prefix.Skip(4).Take(4).Any(b=>b!=0))throw new InvalidDataException("Dozwolone jest tylko podłączenie wybranego USB/IP.");
   int end=Array.IndexOf(prefix,(byte)0,8,32);
   if(end<0||Encoding.ASCII.GetString(prefix,8,end-8)!=allowed||prefix.Skip(end).Any(b=>b!=0))throw new InvalidDataException("Żądanie dotyczy innego urządzenia USB.");
   accepted=true;var output=new byte[40+data.Length-n];Buffer.BlockCopy(prefix,0,output,0,40);Buffer.BlockCopy(data,n,output,40,data.Length-n);return output;
  }
 }
 public static class UsbIpProbe {
  public static void RequireExport(Stream stream,string busId){
   var request=new byte[]{1,0x11,0x80,5,0,0,0,0};stream.Write(request,0,request.Length);stream.Flush();
   var reply=Wire.Exact(stream,12);
   if(reply[0]!=1||reply[1]!=0x11||reply[2]!=0||reply[3]!=5||reply.Skip(4).Take(4).Any(b=>b!=0))throw new IOException("Nieprawidłowa odpowiedź usługi USB/IP.");
   uint count=((uint)reply[8]<<24)|((uint)reply[9]<<16)|((uint)reply[10]<<8)|reply[11];
   if(count>256)throw new IOException("Zbyt wiele urządzeń w odpowiedzi USB/IP.");
   bool found=false;
   for(uint i=0;i<count;i++){
    byte[] device=Wire.Exact(stream,312);int end=Array.IndexOf(device,(byte)0,256,32);
    if(end<0)throw new IOException("Uszkodzony identyfikator urządzenia USB/IP.");
    if(Encoding.ASCII.GetString(device,256,end-256)==busId)found=true;
    Wire.Exact(stream,device[311]*4);
   }
   if(!found)throw new IOException("Usługa USB/IP nie udostępnia wybranego urządzenia. Odśwież listę i spróbuj ponownie.");
  }
 }
}
