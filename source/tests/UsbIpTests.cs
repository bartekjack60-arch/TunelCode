using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using AutoTunel;

static class UsbIpTests {
 static void Assert(bool value,string message){if(!value)throw new Exception(message);}
 static void Throws<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
 const string GuidValue="ad9d8376-6284-495e-a80b-ff1826d7447d";
 static string State(bool bound,string instance="USB\\VID_0403&PID_CC4D\\A",bool attached=false){return JsonSerializer.Serialize(new{Devices=new[]{new{BusId="1-2",Description="Tactrix Openport 2.0",InstanceId=instance,PersistedGuid=bound?GuidValue:null,ClientIPAddress=attached?"127.0.0.1":null},new{BusId=(string)null,Description="Disconnected",InstanceId="gone",PersistedGuid=(string)null,ClientIPAddress=(string)null}}});}
 sealed class FakeCommands:ICommandRunner {
  public bool Bound,Mounted,Attached,Malformed;public string Instance="USB\\VID_0403&PID_CC4D\\A";
  public bool OtherDeviceMounted=true;
  public string Run(string file,string[] a,int timeout){
   if(file=="host"){
    if(a.SequenceEqual(new[]{"state"}))return State(Bound,Instance,Attached);
    if(a.SequenceEqual(new[]{"bind","--busid","1-2"})){Bound=true;return "";}
    if(a.SequenceEqual(new[]{"unbind","--guid",GuidValue})){Bound=false;return "";}
   }
   if(file=="client"){
    if(a.SequenceEqual(new[]{"port"}))return (Mounted?"Port 07: device in use at high-speed\n Tactrix\n -> usbip://127.0.0.1:49152/1-2\n":"")+"Port 08: device in use at high-speed\n Other\n -> usbip://26.1.2.3:3240/1-2\n";
    if(a.SequenceEqual(new[]{"--tcp-port","49152","attach","--remote","127.0.0.1","--bus-id","1-2","--terse","--once"})){Mounted=true;return Malformed?"unexpected output":"7";}
    if(a.SequenceEqual(new[]{"detach","--port","7"})){Mounted=false;return "";}
   }
   throw new Exception("Unexpected command: "+file+" "+string.Join(" ",a));
  }
 }
 sealed class Duplex:Stream {
  readonly MemoryStream input;public readonly MemoryStream Output=new MemoryStream();
  public Duplex(byte[] reply){input=new MemoryStream(reply);}
  public override int Read(byte[] b,int o,int n){return input.Read(b,o,Math.Min(n,3));}
  public override void Write(byte[] b,int o,int n){Output.Write(b,o,n);}
  public override bool CanRead=>true;public override bool CanWrite=>true;public override bool CanSeek=>false;
  public override long Length=>input.Length;public override long Position{get=>input.Position;set=>throw new NotSupportedException();}
  public override void Flush(){}public override long Seek(long o,SeekOrigin s){throw new NotSupportedException();}public override void SetLength(long l){throw new NotSupportedException();}
 }
 static byte[] Request(string bus){var b=new byte[40];b[0]=1;b[1]=0x11;b[2]=0x80;b[3]=3;Encoding.ASCII.GetBytes(bus).CopyTo(b,8);return b;}
 static byte[] Exports(string bus){var b=new byte[12+312+8];b[0]=1;b[1]=0x11;b[3]=5;b[11]=1;Encoding.ASCII.GetBytes(bus).CopyTo(b,12+256);b[12+311]=2;return b;}
 static string RunFixture(SystemCommandRunner runner,string mode,string[] values,int timeout){
  string path=Environment.ProcessPath;var args=new List<string>();
  if(Path.GetFileNameWithoutExtension(path).Equals("dotnet",StringComparison.OrdinalIgnoreCase))args.Add(typeof(UsbIpTests).Assembly.Location);
  args.Add(mode);args.AddRange(values);return runner.Run(path,args.ToArray(),timeout);
 }
 public static void Run(Action<string,Action> test){
  test("USB/IP code carries exact device and rejects old or malformed codes",()=>{
   var i=new Invitation{Host="26.1.2.3",Port=24832,Pin=Wire.Secret(),Key=Wire.Secret(),Mode="USB",UsbBusId="1-2"};
   var parsed=Invitation.Parse(" \r\n"+i.Encode()+"\r\n");Assert(parsed.Mode=="USB"&&parsed.UsbBusId=="1-2","device selection lost");
   Throws<FormatException>(()=>Invitation.Parse("AMB3-"+i.Encode().Substring(5)));i.UsbBusId="1-2 --all";Throws<FormatException>(()=>i.Encode());i.UsbBusId="";Throws<FormatException>(()=>i.Encode());
   string corrupt="AMB4-"+Convert.ToBase64String(Encoding.UTF8.GetBytes("26.1.2.3|999999999999999999999|"+i.Pin+"|"+i.Key+"|USB|1-2"));Throws<FormatException>(()=>Invitation.Parse(corrupt));
  });
  test("USB/IP state uses official JSON fields and excludes disconnected devices",()=>{
   var devices=UsbIpBackend.ParseState(State(true));Assert(devices.Count==1&&devices[0].Bound&&devices[0].BusId=="1-2"&&devices[0].Description=="Tactrix Openport 2.0","state interpretation");
   Throws<System.Runtime.Serialization.SerializationException>(()=>UsbIpBackend.ParseState("{}"));Throws<InvalidDataException>(()=>UsbIpBackend.ParseState("{\"Devices\":null}"));
  });
  test("USB/IP share releases only a binding created by this session",()=>{
   var commands=new FakeCommands();var engine=new UsbIpBackend(commands,"host","client");var selected=engine.Devices()[0];
   using(var lease=engine.Share(selected)){Assert(lease.Owned&&commands.Bound,"device was not shared");}Assert(!commands.Bound,"owned binding leaked");
  });
  test("USB/IP preserves pre-existing shares and refuses busy or replaced devices",()=>{
   var commands=new FakeCommands{Bound=true};var engine=new UsbIpBackend(commands,"host","client");var selected=engine.Devices()[0];
   using(var lease=engine.Share(selected)){Assert(!lease.Owned,"external binding ownership stolen");}Assert(commands.Bound,"external binding removed");
   commands.Attached=true;Throws<IOException>(()=>engine.Share(selected));commands.Attached=false;commands.Instance="replacement";Throws<IOException>(()=>engine.Share(selected));Assert(commands.Bound,"replacement was modified");
  });
  test("USB/IP attach is single-attempt and detach preserves unrelated devices",()=>{
   var commands=new FakeCommands();var engine=new UsbIpBackend(commands,"host","client");
   using(var lease=engine.Attach(49152,"1-2")){Assert(lease.Port==7&&lease.Present(),"mount was not verified");}Assert(!commands.Mounted&&commands.OtherDeviceMounted,"detach damaged another device");
  });
  test("USB/IP failed attach rolls back only its own endpoint",()=>{
   var commands=new FakeCommands{Malformed=true};var engine=new UsbIpBackend(commands,"host","client");Throws<IOException>(()=>engine.Attach(49152,"1-2"));Assert(!commands.Mounted&&commands.OtherDeviceMounted,"failed attach leaked or detached another device");
   commands.Mounted=true;Throws<IOException>(()=>engine.Attach(49152,"1-2"));Assert(commands.Mounted,"pre-existing attachment was detached");
  });
  test("USB/IP detects lost devices and recovers its saved endpoint",()=>{
   var commands=new FakeCommands{Mounted=true};var engine=new UsbIpBackend(commands,"host","client");var lease=new UsbAttachment(engine,7,49152,"1-2");
   Assert(lease.Present(),"initial device absent");commands.Mounted=false;Assert(!lease.Present(),"lost device still reported as present");
   commands.Mounted=true;engine.ReleaseEndpoint(49152,"1-2");Assert(!commands.Mounted&&commands.OtherDeviceMounted,"recovery removed the wrong device");
   Throws<ArgumentException>(()=>engine.ReleaseEndpoint(0,"1-2"));
  });
  test("USB/IP import guard handles split headers and preserves payload bytes",()=>{
   var header=Request("1-2");var tail=new byte[]{0,255,1,2,3};var guard=new UsbImportGuard("1-2");
   Assert(guard.Feed(header.Take(7).ToArray()).Length==0,"partial request escaped");
   Assert(guard.Feed(header.Skip(7).Concat(tail).ToArray()).SequenceEqual(header.Concat(tail)),"request/payload changed");Assert(guard.Feed(tail).SequenceEqual(tail),"stream changed");
  });
  test("USB/IP import guard rejects other devices, device lists and corrupt headers",()=>{
   Throws<InvalidDataException>(()=>new UsbImportGuard("1-2").Feed(Request("1-3")));
   var b=Request("1-2");b[3]=5;Throws<InvalidDataException>(()=>new UsbImportGuard("1-2").Feed(b));
   b=Request("1-2");b[4]=1;Throws<InvalidDataException>(()=>new UsbImportGuard("1-2").Feed(b));
   b=Request("1-2");b[39]=1;Throws<InvalidDataException>(()=>new UsbImportGuard("1-2").Feed(b));
  });
  test("USB/IP readiness probe parses packed device and interface records",()=>{
   var stream=new Duplex(Exports("1-2"));UsbIpProbe.RequireExport(stream,"1-2");Assert(stream.Position==stream.Length,"interface records were not consumed");
   Assert(stream.Output.ToArray().SequenceEqual(new byte[]{1,0x11,0x80,5,0,0,0,0}),"wrong USB/IP query");
   Throws<IOException>(()=>UsbIpProbe.RequireExport(new Duplex(Exports("1-3")),"1-2"));var b=Exports("1-2");b[8]=1;Throws<IOException>(()=>UsbIpProbe.RequireExport(new Duplex(b),"1-2"));
  });
  test("USB/IP parser matches complete local endpoints and validates tool versions",()=>{
   string text="Port 02: device in use\n -> usbip://127.0.0.1:49152/1-20\nPort 03: device in use\n -> usbip://127.0.0.1:49152/1-2\n";
   Assert(UsbIpBackend.ParsePorts(text,49152,"1-2").SequenceEqual(new[]{3}),"partial or unrelated endpoint matched");
   UsbIpBackend.CheckVersion("5.3.0+build",true);UsbIpBackend.CheckVersion("0.9.8.1",false);Throws<IOException>(()=>UsbIpBackend.CheckVersion("0.9.7.8",false));
  });
  test("Native command runner preserves argument boundaries without a shell",()=>{
   var values=new[]{"plain","with space","quote\"inside","tail\\","x\\\"y","$(not-a-shell)"};
   string result=RunFixture(new SystemCommandRunner(),"--echo-args",values,5000);
   var actual=result.Split('\n').Select(s=>Encoding.UTF8.GetString(Convert.FromBase64String(s.Trim()))).ToArray();Assert(actual.SequenceEqual(values),"native arguments changed");
  });
  test("Native command runner enforces timeout on its own process",()=>{
   Throws<IOException>(()=>RunFixture(new SystemCommandRunner(),"--sleep-fixture",new string[0],100));
  });
 }
}
