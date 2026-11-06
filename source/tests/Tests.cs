using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AutoTunel;
class Tests {
 static int passed;
 static string filter;
 static void Assert(bool b,string m){if(!b)throw new Exception(m);}
 static void Test(string name,Action test){if(filter!=null&&name.IndexOf(filter,StringComparison.OrdinalIgnoreCase)<0)return;test();Console.WriteLine("PASS "+name);passed++;}
 static void Throws<T>(Action a)where T:Exception{try{a();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
 static int Port(){var l=new TcpListener(IPAddress.Loopback,0);l.Start();int p=((IPEndPoint)l.LocalEndpoint).Port;l.Stop();return p;}
 static X509Certificate2 Cert(){using(var rsa=RSA.Create(2048)){var r=new CertificateRequest("CN=AutoTunel-MB",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);return r.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));}}
 static void Until(Func<bool> fn,string label){var end=DateTime.UtcNow.AddSeconds(8);while(DateTime.UtcNow<end){if(fn())return;Thread.Sleep(10);}throw new Exception("Timeout: "+label);}
 static Task Blocking(Action action){return Task.Factory.StartNew(action,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);}
 static Task Run(Tunnel t,Invitation i,X509Certificate2 c,FakeAdapter a=null){return Blocking(()=>{try{t.Run(i,c,()=>a);}catch(Exception){}});}
 sealed class FakeAdapter:IEthernet{public ConcurrentQueue<byte[]> Input=new ConcurrentQueue<byte[]>(),Output=new ConcurrentQueue<byte[]>();public bool Closed;public byte[] Read(){if(Closed)throw new ObjectDisposedException("fake");byte[] b;return Input.TryDequeue(out b)?b:null;}public void Write(byte[] b){Output.Enqueue(b);}public void Dispose(){Closed=true;}}
 sealed class Partial:MemoryStream{public Partial(byte[] b):base(b){}public override int Read(byte[] b,int o,int n){return base.Read(b,o,Math.Min(n,3));}}
 static Invitation Invite(X509Certificate2 c,int p,string mode="USB"){return new Invitation{Mode=mode,UsbBusId=mode=="USB"?"1-2":"",Host="127.0.0.1",Port=p,Pin=Wire.Pin(c.RawData),Key=Wire.Secret()};}
  static byte[] ImportRequest(string bus){var b=new byte[40];b[0]=1;b[1]=0x11;b[2]=0x80;b[3]=3;Encoding.ASCII.GetBytes(bus).CopyTo(b,8);return b;}
 static void RejectOtherUsb(){
  using(var cert=Cert()){
   var engine=new TcpListener(IPAddress.Loopback,0);engine.Start();int remote=((IPEndPoint)engine.LocalEndpoint).Port;
   bool forwarded=false;var observed=Blocking(()=>{try{using(var tcp=engine.AcceptTcpClient()){tcp.ReceiveTimeout=5000;var bytes=Wire.Exact(tcp.GetStream(),40);forwarded=bytes.SequenceEqual(ImportRequest("1-999"));}}catch(IOException){}catch(SocketException){}});
   var invite=Invite(cert,Port());var ready=new ManualResetEventSlim();int local=Port();
   using(var vehicle=new Tunnel(true,"USB",x=>{if(x.StartsWith("OCZEKIWANIE"))ready.Set();},Port(),remote))
   using(var diagnostic=new Tunnel(false,"USB",x=>{},local,remote)){
    try{
     var car=Run(vehicle,invite,cert);Assert(ready.Wait(8000),"listen");var desk=Run(diagnostic,invite,null);
     Until(()=>vehicle.Connected&&diagnostic.Connected,"USB session");
     using(var socket=new TcpClient()){socket.Connect(IPAddress.Loopback,local);var b=ImportRequest("1-999");socket.GetStream().Write(b,0,b.Length);Assert(observed.Wait(8000),"USB request outcome");}
     Assert(!forwarded,"A request for another USB device reached the engine");
    }finally{engine.Stop();}
   }
  }
 }
 static void Main(string[] args){
  if(args.Length==2&&args[0]=="--verify-bundle"){UsbSetupTests.VerifyBundle(args[1]);return;}
  if(args.Length>0&&args[0]=="--echo-args"){foreach(var value in args.Skip(1))Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));return;}
  if(args.Length>0&&args[0]=="--sleep-fixture"){Thread.Sleep(5000);return;}
  filter=args.Length==0?null:args[0];
  UsbIpTests.Run(Test);
  AppSettingsTests.Run(Test);
  UsbSetupTests.Run(Test);
  Test("USB gate rejects another device before forwarding",RejectOtherUsb);
  Test("Session code version separates USB/IP from legacy sessions",()=>{var i=new Invitation{Host="test.example.com",Port=24832,Pin=Wire.Secret(),Key=Wire.Secret()};Assert(i.Encode().StartsWith("AMB4-"),"New role layout must use AMB4, not AMB2");string old="AMB2-"+Convert.ToBase64String(Encoding.UTF8.GetBytes(i.Host+"|"+i.Port+"|"+i.Pin+"|"+i.Key));Throws<FormatException>(()=>Invitation.Parse(old));});
  Test("Invitation round trip, invalid formats and secret comparison",()=>{var i=new Invitation{Host="test.example.com",Port=24832,Pin=Wire.Secret(),Key=Wire.Secret()};var j=Invitation.Parse(i.Encode());Assert(j.Key==i.Key&&j.Pin==i.Pin&&j.Host==i.Host,"round trip");Throws<FormatException>(()=>Invitation.Parse("ABC"));Throws<FormatException>(()=>Invitation.Parse("AMB3-!!!!"));i.Host="host|injection";Throws<FormatException>(()=>i.Encode());Assert(!Wire.Equal("abc","abcd")&&Wire.Equal("same","same"),"compare");});
  Test("Frame round trip across fragmented reads and coalesced frames",()=>{var s=new MemoryStream();var bytes=Enumerable.Range(0,65536).Select(x=>(byte)x).ToArray();Wire.Write(s,10,123,bytes);Wire.Write(s,30,0,new byte[0]);using(var p=new Partial(s.ToArray())){var f=Wire.Read(p);Assert(f.Id==123&&f.Data.SequenceEqual(bytes),"frame");Assert(Wire.Read(p).Type==30,"second");}});
  Test("Reject oversized, negative and truncated frames",()=>{Throws<InvalidDataException>(()=>Wire.Write(new MemoryStream(),1,0,new byte[65537]));var h=new byte[9];Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(65537)),0,h,5,4);Throws<InvalidDataException>(()=>Wire.Read(new MemoryStream(h)));h[5]=255;Throws<InvalidDataException>(()=>Wire.Read(new MemoryStream(h)));Throws<EndOfStreamException>(()=>Wire.Read(new MemoryStream(new byte[4])));});
  Test("LAN TLS bidirectional bytes, broadcast and disconnect cleanup",()=>{using(var cert=Cert()){int p=Port();var i=Invite(cert,p,"LAN");var a=new FakeAdapter();var b=new FakeAdapter();var ready=new ManualResetEventSlim();using(var server=new Tunnel(true,"LAN",x=>{if(x.StartsWith("OCZEKIWANIE"))ready.Set();}))using(var client=new Tunnel(false,"LAN",x=>{})){var ts=Run(server,i,cert,a);Assert(ready.Wait(8000),"listen");var tc=Run(client,i,null,b);Until(()=>server.Connected&&client.Connected,"TLS connect");var packet=new byte[1514];RandomNumberGenerator.Fill(packet);for(int k=0;k<6;k++)packet[k]=255;a.Input.Enqueue(packet);b.Input.Enqueue(packet.Reverse().ToArray());Until(()=>a.Output.Count==1&&b.Output.Count==1,"LAN data");byte[] received;Assert(b.Output.TryDequeue(out received)&&received.SequenceEqual(packet),"broadcast bytes");Assert(a.Output.TryDequeue(out received)&&received.SequenceEqual(packet.Reverse()),"reverse bytes");client.Dispose();Assert(Task.WaitAll(new[]{ts,tc},5000),"stop threads");Assert(a.Closed&&b.Closed,"adapters closed");}}});
  Test("Bad certificate pin and key rejected; server accepts later valid client",()=>{using(var cert=Cert()){var i=Invite(cert,Port());var ready=new ManualResetEventSlim();using(var s=new Tunnel(true,"USB",x=>{if(x.StartsWith("OCZEKIWANIE"))ready.Set();},Port())){var ts=Run(s,i,cert);Assert(ready.Wait(8000),"listen");var bad=Invitation.Parse(i.Encode());bad.Pin=Wire.Secret();using(var c=new Tunnel(false,"USB",x=>{})){Assert(Run(c,bad,null).Wait(8000),"bad pin exit");Assert(!c.Connected&&!s.Connected,"pin reject");}bad=Invitation.Parse(i.Encode());bad.Key=Wire.Secret();using(var c=new Tunnel(false,"USB",x=>{})){Assert(Run(c,bad,null).Wait(8000),"bad key exit");Assert(!c.Connected&&!s.Connected,"key reject");}bad=Invitation.Parse(i.Encode());bad.UsbBusId="1-3";using(var c=new Tunnel(false,"USB",x=>{})){Assert(Run(c,bad,null).Wait(8000),"wrong device exit");Assert(!c.Connected&&!s.Connected,"device code tampering rejected");}using(var c=new Tunnel(false,"USB",x=>{})){var tc=Run(c,i,null);Until(()=>c.Connected&&s.Connected,"valid after rejection");c.Dispose();Assert(Task.WaitAll(new[]{ts,tc},5000),"shutdown");}}}});
  Test("Mode mismatch rejected",()=>{using(var cert=Cert()){var i=Invite(cert,Port());var ready=new ManualResetEventSlim();using(var s=new Tunnel(true,"USB",x=>{if(x.StartsWith("OCZEKIWANIE"))ready.Set();},Port()))using(var c=new Tunnel(false,"LAN",x=>{})){var ts=Run(s,i,cert);Assert(ready.Wait(8000),"listen");Assert(Run(c,i,null,new FakeAdapter()).Wait(8000),"mismatch exits");Assert(!s.Connected,"mismatch rejected");s.Dispose();Assert(ts.Wait(5000),"stop");}}});
  Test("USB Client consumes vehicle Server device, 4 streams / 2 MiB total",()=>{using(var cert=Cert()){int target=Port(),vehicleLocal=Port();var reserved=new TcpListener(IPAddress.Loopback,vehicleLocal);reserved.Start();try{var echo=new TcpListener(IPAddress.Loopback,target);echo.Start();var echoTasks=new ConcurrentBag<Task>();var accept=Blocking(()=>{try{while(true){var c=echo.AcceptTcpClient();echoTasks.Add(Blocking(()=>{using(c){try{var s=c.GetStream();var b=new byte[16384];int n;while((n=s.Read(b,0,b.Length))>0)s.Write(b,0,n);}catch{}}}));}}catch{}});var i=Invite(cert,Port());var ready=new ManualResetEventSlim();using(var s=new Tunnel(true,"USB",x=>{if(x.StartsWith("OCZEKIWANIE"))ready.Set();},vehicleLocal,target))using(var c=new Tunnel(false,"USB",x=>{},0,Port())){var ts=Run(s,i,cert);Assert(ready.Wait(8000),"listen");var tc=Run(c,i,null);Until(()=>s.Connected&&c.Connected,"USB TLS");Assert(c.UsbPort>=1024,"ephemeral loopback port");var tasks=Enumerable.Range(0,4).Select(_=>Task.Run(()=>{using(var tcp=new TcpClient()){tcp.ReceiveTimeout=10000;tcp.SendTimeout=10000;tcp.Connect(IPAddress.Loopback,c.UsbPort);var data=new byte[524288];RandomNumberGenerator.Fill(data);ImportRequest("1-2").CopyTo(data,0);var ns=tcp.GetStream();var write=Blocking(()=>ns.Write(data,0,data.Length));var received=Wire.Exact(ns,data.Length);write.GetAwaiter().GetResult();Assert(received.SequenceEqual(data),"USB bytes");}})).ToArray();Assert(Task.WaitAll(tasks,20000),"parallel transfer");s.Dispose();Assert(Task.WaitAll(new[]{ts,tc},5000),"USB shutdown");}echo.Stop();Assert(accept.Wait(3000),"echo stop");Assert(Task.WaitAll(echoTasks.ToArray(),3000),"all USB sockets closed");}finally{reserved.Stop();}}});
  Test("Stop while accepting releases port; cancelled start rejects",()=>{using(var cert=Cert()){int p=Port();var i=Invite(cert,p);var ready=new ManualResetEventSlim();using(var s=new Tunnel(true,"USB",x=>{if(x.StartsWith("OCZEKIWANIE"))ready.Set();})){var task=Run(s,i,cert);Assert(ready.Wait(8000),"listen");s.Dispose();Assert(task.Wait(3000),"cancel accept");}var probe=new TcpListener(IPAddress.Any,p);probe.Start();probe.Stop();using(var s=new Tunnel(true,"USB",x=>{})){s.Dispose();Throws<OperationCanceledException>(()=>s.Run(i,cert,null));}}});
  if(passed==0)throw new Exception("No tests matched the requested filter.");
  Console.WriteLine("RESULT: "+passed+" tests passed. Windows GUI, TAP/Npcap and real USB/vehicle not tested here.");
 }
}
