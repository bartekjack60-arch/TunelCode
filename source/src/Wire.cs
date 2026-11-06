using System;
using System.IO;
using System.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Globalization;
namespace AutoTunel {
 public sealed class Frame { public byte Type; public int Id; public byte[] Data; public Frame(byte t,int i,byte[] d){Type=t;Id=i;Data=d;} }
 public static class Wire {
  public const int MaxPayload=65536;
  public static byte[] Exact(Stream s,int n) { var b=new byte[n]; int p=0; while(p<n){int k=s.Read(b,p,n-p);if(k==0)throw new EndOfStreamException("Połączenie zamknięte.");p+=k;}return b; }
  public static Frame Read(Stream s) { var h=Exact(s,9);int id=IPAddress.NetworkToHostOrder(BitConverter.ToInt32(h,1)),n=IPAddress.NetworkToHostOrder(BitConverter.ToInt32(h,5));if(id<0||n<0||n>MaxPayload)throw new InvalidDataException("Błędna ramka.");return new Frame(h[0],id,Exact(s,n)); }
  public static void Write(Stream s,byte type,int id,byte[] data) { if(id<0||data==null||data.Length>MaxPayload)throw new InvalidDataException("Błędna ramka.");var h=new byte[9];h[0]=type;Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(id)),0,h,1,4);Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length)),0,h,5,4);s.Write(h,0,9);s.Write(data,0,data.Length);s.Flush(); }
  public static string Hex(byte[] b){return BitConverter.ToString(b).Replace("-","");}
  public static string Pin(byte[] cert){using(var sha=SHA256.Create())return Hex(sha.ComputeHash(cert));}
  public static string Secret(){var b=new byte[32];using(var r=RandomNumberGenerator.Create())r.GetBytes(b);return Hex(b);}
  public static bool Equal(string a,string b){if(a==null||b==null)return false;byte[] x,y;using(var h=SHA256.Create()){x=h.ComputeHash(Encoding.UTF8.GetBytes(a));y=h.ComputeHash(Encoding.UTF8.GetBytes(b));}int d=0;for(int k=0;k<x.Length;k++)d|=x[k]^y[k];return d==0;}
 }
 public sealed class Invitation {
  public string Host,Pin,Key;public int Port;
  public string Mode="LAN";public string UsbBusId="";
  public void Validate(){if(string.IsNullOrWhiteSpace(Host)||Uri.CheckHostName(Host)==UriHostNameType.Unknown||Host.Contains("|")||Port<1||Port>65535||(Mode!="LAN"&&Mode!="USB")||(Mode=="USB"&&!UsbIpBackend.ValidBusId(UsbBusId))||(Mode=="LAN"&&!string.IsNullOrEmpty(UsbBusId))||!Regex.IsMatch(Pin??"",@"\A[A-F0-9]{64}\z")||!Regex.IsMatch(Key??"",@"\A[A-F0-9]{64}\z"))throw new FormatException("Nieprawidłowy adres, port lub kod połączenia.");}
  public string Encode(){Validate();return "AMB4-"+Convert.ToBase64String(Encoding.UTF8.GetBytes(Host+"|"+Port.ToString(CultureInfo.InvariantCulture)+"|"+Pin+"|"+Key+"|"+Mode+"|"+(UsbBusId??"")));}
  public static Invitation Parse(string text){
   if(text==null||text.Length>2048||!text.Trim().StartsWith("AMB4-",StringComparison.Ordinal))throw new FormatException("Wklej pełny kod AMB4 z programu Server przy samochodzie. Użyj tej samej wersji AutoTunel na obu komputerach.");
   var p=Encoding.UTF8.GetString(Convert.FromBase64String(text.Trim().Substring(5))).Split('|');int port;
   if(p.Length!=6||!int.TryParse(p[1],NumberStyles.None,CultureInfo.InvariantCulture,out port))throw new FormatException("Uszkodzony kod połączenia. Skopiuj go ponownie z programu Server.");
   var v=new Invitation{Host=p[0],Port=port,Pin=p[2],Key=p[3],Mode=p[4],UsbBusId=p[5]};v.Validate();return v;
  }
 }
 public interface IEthernet:IDisposable {byte[] Read();void Write(byte[] data);}
}
