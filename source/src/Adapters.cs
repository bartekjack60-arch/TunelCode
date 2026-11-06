using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
namespace AutoTunel {
 public sealed class AdapterInfo {
  public string Id,Name,Alias;public bool Tap;
  public override string ToString(){return Name;}
  public static List<AdapterInfo> List(bool tap){
   var list=new List<AdapterInfo>();var taps=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   using(var root=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002BE10318}"))if(root!=null)foreach(string key in root.GetSubKeyNames())using(var sub=root.OpenSubKey(key)){if(sub!=null&&Convert.ToString(sub.GetValue("ComponentId"))=="tap0901")taps.Add(Convert.ToString(sub.GetValue("NetCfgInstanceId")));}
   foreach(var n in NetworkInterface.GetAllNetworkInterfaces()){bool t=taps.Contains(n.Id);if((tap&&t)||(!tap&&!t&&n.NetworkInterfaceType==NetworkInterfaceType.Ethernet))list.Add(new AdapterInfo{Id=n.Id,Alias=n.Name,Name=n.Name+" — "+n.Description,Tap=t});}return list;
  }
 }
 public sealed class TapAdapter:IEthernet {
  readonly FileStream stream; readonly SafeFileHandle handle;
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string p,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool DeviceIoControl(SafeFileHandle h,uint code,IntPtr input,int inlen,IntPtr output,int outlen,out int returned,IntPtr overlapped);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetOverlappedResult(SafeFileHandle h,IntPtr overlapped,out int returned,bool wait);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool CancelIoEx(SafeFileHandle h,IntPtr overlapped);
  [StructLayout(LayoutKind.Sequential)]struct Overlap {public IntPtr Internal,InternalHigh;public uint Offset,OffsetHigh;public IntPtr Event;}
  static void Activate(SafeFileHandle h){
   using(var done=new System.Threading.ManualResetEvent(false)){
    IntPtr data=Marshal.AllocHGlobal(4), ov=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Overlap)));
    try{Marshal.WriteInt32(data,1);Marshal.StructureToPtr(new Overlap{Event=done.SafeWaitHandle.DangerousGetHandle()},ov,false);int count;
     if(!DeviceIoControl(h,0x00220018,data,4,data,4,out count,ov)){
      int error=Marshal.GetLastWin32Error();if(error!=997)throw new Win32Exception(error,"Błąd aktywacji TAP.");
      if(!done.WaitOne(5000)){CancelIoEx(h,ov);GetOverlappedResult(h,ov,out count,true);throw new IOException("TAP nie odpowiada.");}
      if(!GetOverlappedResult(h,ov,out count,false))throw new Win32Exception(Marshal.GetLastWin32Error(),"Błąd aktywacji TAP.");
     }
    }finally{Marshal.FreeHGlobal(ov);Marshal.FreeHGlobal(data);}
   }
  }
  public TapAdapter(string id){
   var h=CreateFile(@"\\.\Global\"+id+".tap",0xC0000000,0,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
   if(h.IsInvalid){h.Dispose();throw new Win32Exception(Marshal.GetLastWin32Error(),"Nie można otworzyć TAP. Zamknij inne VPN.");}
   try{Activate(h);handle=h;stream=new FileStream(h,FileAccess.ReadWrite,65536,true);}catch{h.Dispose();throw;}
  }
  public byte[] Read(){var b=new byte[65536];int n=stream.ReadAsync(b,0,b.Length).GetAwaiter().GetResult();if(n==0)throw new EndOfStreamException("TAP zamknięty.");Array.Resize(ref b,n);return b;}
  public void Write(byte[] b){if(b.Length<14||b.Length>65535)throw new InvalidDataException("Błędna ramka Ethernet.");stream.WriteAsync(b,0,b.Length).GetAwaiter().GetResult();}
  public void Dispose(){if(!handle.IsClosed)try{CancelIoEx(handle,IntPtr.Zero);}catch(ObjectDisposedException){}stream.Dispose();}
 }
 public sealed class PcapAdapter:IEthernet {
  IntPtr handle;readonly object gate=new object();
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string p,IntPtr file,uint flags);
  [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr pcap_open(string name,int snap,int flags,int timeout,IntPtr auth,StringBuilder err);
  [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)]static extern int pcap_setnonblock(IntPtr p,int value,StringBuilder err);
  [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)]static extern int pcap_datalink(IntPtr p);
  [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)]static extern int pcap_next_ex(IntPtr p,out IntPtr h,out IntPtr d);
  [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)]static extern int pcap_sendpacket(IntPtr p,byte[] b,int n);
  [DllImport("wpcap.dll",CallingConvention=CallingConvention.Cdecl)]static extern void pcap_close(IntPtr p);
  public PcapAdapter(string id){string dll=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"Npcap\wpcap.dll");if(LoadLibraryEx(dll,IntPtr.Zero,0x1100)==IntPtr.Zero)throw new IOException("Brak Npcap x64. Zainstaluj Npcap z npcap.com.");var err=new StringBuilder(512);
   // NOCAPTURE_LOCAL prevents a transmitted frame re-entering the tunnel.
   handle=pcap_open(@"rpcap://\Device\NPF_"+id,65536,1|8|16,50,IntPtr.Zero,err);if(handle==IntPtr.Zero)throw new IOException("Npcap: "+err);if(pcap_datalink(handle)!=1||pcap_setnonblock(handle,1,err)!=0){Dispose();throw new IOException("Npcap: nieobsługiwany interfejs. "+err);}}
  public byte[] Read(){lock(gate){if(handle==IntPtr.Zero)throw new ObjectDisposedException("Npcap");IntPtr h,d;int r=pcap_next_ex(handle,out h,out d);if(r==0)return null;if(r<0)throw new IOException("Błąd odbioru Npcap.");int n=Marshal.ReadInt32(h,8);if(n<14||n>65535)return null;var b=new byte[n];Marshal.Copy(d,b,0,n);return b;}}
  public void Write(byte[] b){lock(gate){if(handle==IntPtr.Zero)throw new ObjectDisposedException("Npcap");if(b.Length<14||pcap_sendpacket(handle,b,b.Length)!=0)throw new IOException("Błąd wysyłania Npcap.");}}
  public void Dispose(){lock(gate){if(handle!=IntPtr.Zero){pcap_close(handle);handle=IntPtr.Zero;}}}
 }
}
