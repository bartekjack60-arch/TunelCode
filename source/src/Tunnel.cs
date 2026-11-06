using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
namespace AutoTunel {
 public sealed class Tunnel:IDisposable {
  readonly IPAddress listenAddress;string allowedUsb;readonly ConcurrentDictionary<int,UsbImportGuard> usbGuards=new ConcurrentDictionary<int,UsbImportGuard>();
  readonly bool server;readonly string mode;readonly Action<string> log;readonly object life=new object(),sendGate=new object();
  readonly HashSet<IDisposable> resources=new HashSet<IDisposable>();readonly ConcurrentDictionary<int,TcpClient> channels=new ConcurrentDictionary<int,TcpClient>();
  volatile bool stopped,connected;SslStream stream;IEthernet ethernet;int nextId;
  readonly int localUsbPort,remoteUsbPort;public long Sent,Received;
  public bool Connected{get{return connected;}}
  public int UsbPort{get;private set;}
  public Tunnel(bool isServer,string selectedMode,Action<string> logger,int localPort=17570,int remotePort=3240,IPAddress bindAddress=null){if(selectedMode!="LAN"&&selectedMode!="USB")throw new ArgumentException("Tryb LAN/USB.");listenAddress=bindAddress??IPAddress.Any;server=isServer;mode=selectedMode;log=logger;localUsbPort=localPort;remoteUsbPort=remotePort;}
  T Own<T>(T resource)where T:IDisposable{lock(life){if(stopped){resource.Dispose();throw new OperationCanceledException();}resources.Add(resource);}return resource;}
  void Drop(IDisposable resource){if(resource==null)return;lock(life)resources.Remove(resource);try{resource.Dispose();}catch{}}
  void Check(){if(stopped)throw new OperationCanceledException();}
  public void Run(Invitation invite,X509Certificate2 cert,Func<IEthernet> adapter){
   invite.Validate();allowedUsb=invite.UsbBusId;if(invite.Mode!=mode)throw new InvalidDataException("Tryb połączenia nie odpowiada kodowi sesji.");try{
    Check();if(mode=="LAN")ethernet=Own(adapter());
    if(server)Accept(invite,cert);else Connect(invite);
    Check();stream.ReadTimeout=30000;stream.WriteTimeout=10000;
    // The diagnostic Client consumes devices hosted by the vehicle Server.
    if(mode=="USB"&&!server){var listener=new TcpListener(IPAddress.Loopback,localUsbPort);var lease=Own(new ListenerLease(listener));listener.Start(16);UsbPort=((IPEndPoint)listener.LocalEndpoint).Port;Background(()=>AcceptUsb(listener));}
    connected=true;log("POŁĄCZONO • "+mode+" • TLS 1.2");
    Own(new Timer(_=>{try{Send(30,0,new byte[0]);}catch{Dispose();}},null,5000,5000));
    if(mode=="LAN")Background(Capture);
    while(true){var f=Wire.Read(stream);Interlocked.Add(ref Received,f.Data.Length);
     if(f.Id==0&&f.Data.Length==0&&(f.Type==30||f.Type==31)){if(f.Type==30)Send(31,0,new byte[0]);continue;}
     if(mode=="LAN"){if(f.Type!=10||f.Id!=0||f.Data.Length<14||f.Data.Length>65535)throw new InvalidDataException("Błędna ramka LAN.");ethernet.Write(f.Data);}else HandleUsb(f);
    }
   }finally{Dispose();}
  }
  static void Setup(TcpClient c){c.NoDelay=true;c.SendTimeout=10000;c.ReceiveTimeout=10000;}
  void Connect(Invitation i){
   var peer=Own(new TcpClient());Setup(peer);if(!peer.ConnectAsync(i.Host,i.Port).Wait(10000))throw new IOException("Brak połączenia. Sprawdź RadminVPN, adres i uruchomienie sesji przy samochodzie.");Check();
   stream=Own(new SslStream(peer.GetStream(),false,(sender,cert,chain,errors)=>cert!=null&&Wire.Equal(Wire.Pin(cert.GetRawCertData()),i.Pin)));
   stream.ReadTimeout=10000;stream.WriteTimeout=10000;stream.AuthenticateAsClient("AutoTunel-MB",null,SslProtocols.Tls12,false);
   Send(1,0,Encoding.UTF8.GetBytes("AMB4|"+mode+"|"+i.UsbBusId+"|"+i.Key));var f=Wire.Read(stream);if(f.Type!=2||f.Id!=0||Encoding.UTF8.GetString(f.Data)!="OK")throw new AuthenticationException("Kod sesji lub wersja programu nie pasuje.");
  }
  void Accept(Invitation i,X509Certificate2 certificate){
   var listener=new TcpListener(listenAddress,i.Port);var lease=Own(new ListenerLease(listener));listener.Start(4);log("OCZEKIWANIE • TCP "+i.Port+" • "+mode);
   while(true){Check();var peer=Own(listener.AcceptTcpClient());Setup(peer);SslStream candidate=null;
    try{candidate=Own(new SslStream(peer.GetStream(),false));candidate.ReadTimeout=10000;candidate.WriteTimeout=10000;candidate.AuthenticateAsServer(certificate,false,SslProtocols.Tls12,false);
     var f=Wire.Read(candidate);if(f.Type!=1||f.Id!=0||!Wire.Equal(Encoding.UTF8.GetString(f.Data),"AMB4|"+mode+"|"+i.UsbBusId+"|"+i.Key))throw new AuthenticationException();
     stream=candidate;Send(2,0,Encoding.UTF8.GetBytes("OK"));Drop(lease);return;
    }catch{Drop(candidate);Drop(peer);Check();log("Odrzucono nieudane uwierzytelnienie. Oczekuję dalej.");}
   }
  }
  // These loops block on I/O for the session lifetime; keep them off the worker pool.
  static void Blocking(Action action){Task.Factory.StartNew(action,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);}
  void Background(Action action){Blocking(()=>{try{action();}catch(Exception e){if(!stopped)log("Przerwano: "+e.GetBaseException().Message);Dispose();}});}
  void Send(byte type,int id,byte[] data){lock(sendGate){Check();Wire.Write(stream,type,id,data);Interlocked.Add(ref Sent,data.Length);}}
  void Capture(){while(true){Check();var b=ethernet.Read();if(b!=null)Send(10,0,b);else Thread.Sleep(3);}}
  void AcceptUsb(TcpListener listener){while(true){Check();var c=listener.AcceptTcpClient();if(channels.Count>=16){c.Close();continue;}Own(c);c.NoDelay=true;c.SendTimeout=5000;int id=Interlocked.Increment(ref nextId);if(id<=0){Drop(c);throw new IOException("Limit kanałów.");}channels[id]=c;Send(20,id,new byte[0]);Pump(id,c);}}
  void Pump(int id,TcpClient c){Blocking(()=>{try{var buffer=new byte[32768];var s=c.GetStream();int n;while((n=s.Read(buffer,0,buffer.Length))>0){var b=new byte[n];Buffer.BlockCopy(buffer,0,b,0,n);Send(21,id,b);}}catch{}finally{CloseChannel(id);try{Send(22,id,new byte[0]);}catch{}}});}
  void HandleUsb(Frame f){
   if(f.Id<=0)throw new InvalidDataException("Błędny kanał USB.");
   if(f.Type==20&&server&&f.Data.Length==0){if(channels.Count>=16||channels.ContainsKey(f.Id))throw new InvalidDataException("Limit/duplikat kanału USB.");var c=Own(new TcpClient());c.NoDelay=true;c.SendTimeout=5000;
    try{if(!c.ConnectAsync(IPAddress.Loopback,remoteUsbPort).Wait(5000))throw new IOException("USB timeout.");Check();channels[f.Id]=c;usbGuards[f.Id]=new UsbImportGuard(allowedUsb);Pump(f.Id,c);}catch{Drop(c);Send(22,f.Id,new byte[0]);log("Brak silnika USB na 127.0.0.1:"+remoteUsbPort);}
   }else if(f.Type==21&&f.Data.Length>0){TcpClient c;if(channels.TryGetValue(f.Id,out c)){byte[] data=f.Data;if(server){UsbImportGuard guard;if(!usbGuards.TryGetValue(f.Id,out guard))throw new InvalidDataException("Brak kontroli kanału USB.");data=guard.Feed(data);}try{if(data.Length>0)c.GetStream().Write(data,0,data.Length);}catch{CloseChannel(f.Id);Send(22,f.Id,new byte[0]);}}}
   else if(f.Type==22&&f.Data.Length==0)CloseChannel(f.Id);else throw new InvalidDataException("Błędny komunikat USB.");
  }
  void CloseChannel(int id){UsbImportGuard guard;usbGuards.TryRemove(id,out guard);TcpClient c;if(channels.TryRemove(id,out c))Drop(c);}
  public void Dispose(){IDisposable[] items;lock(life){if(stopped)return;stopped=true;connected=false;items=new IDisposable[resources.Count];resources.CopyTo(items);resources.Clear();}foreach(var item in items)try{item.Dispose();}catch{}channels.Clear();usbGuards.Clear();}
  sealed class ListenerLease:IDisposable{readonly TcpListener listener;public ListenerLease(TcpListener l){listener=l;}public void Dispose(){listener.Stop();}}
 }
}
