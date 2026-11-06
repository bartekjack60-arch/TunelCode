using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AutoTunel {
 // Native OS and CPU details are independent of the application's manifest.
 public sealed class WindowsHostInfo {
  public int Major {get;private set;}
  public int Minor {get;private set;}
  public int Build {get;private set;}
  public ushort NativeArchitecture {get;private set;}
  public string Edition {get;private set;}
  public string ProductName {get;private set;}
  public bool NativeX64 {get{return NativeArchitecture==9;}}
  public string ArchitectureName {get{return NativeArchitecture==9?"x64":NativeArchitecture==12?"ARM64":NativeArchitecture==0?"x86":"architecture "+NativeArchitecture.ToString(CultureInfo.InvariantCulture);}}
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
  struct OsVersionInfo {
   public uint Size,Major,Minor,Build,PlatformId;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string ServicePack;
  }
  [StructLayout(LayoutKind.Sequential)]
  struct SystemInfo {
   public ushort Architecture,Reserved;
   public uint PageSize;
   public IntPtr MinimumAddress,MaximumAddress;
   public UIntPtr ProcessorMask;
   public uint ProcessorCount,ProcessorType,AllocationGranularity;
   public ushort ProcessorLevel,ProcessorRevision;
  }
  [DllImport("ntdll.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern int RtlGetVersion(ref OsVersionInfo version);
  [DllImport("kernel32.dll",ExactSpelling=true)] static extern void GetNativeSystemInfo(out SystemInfo info);
  public static WindowsHostInfo Read(){
   var version=new OsVersionInfo{Size=(uint)Marshal.SizeOf(typeof(OsVersionInfo))};
   int status=RtlGetVersion(ref version);
   if(status!=0)throw new Win32Exception("RtlGetVersion nie potwierdził wersji Windows (NTSTATUS 0x"+unchecked((uint)status).ToString("X8",CultureInfo.InvariantCulture)+").");
   SystemInfo system;GetNativeSystemInfo(out system);
   var result=new WindowsHostInfo{Major=checked((int)version.Major),Minor=checked((int)version.Minor),Build=checked((int)version.Build),NativeArchitecture=system.Architecture};
   try{
    using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
    using(var key=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")){
     result.Edition=key?.GetValue("EditionID") as string;result.ProductName=key?.GetValue("ProductName") as string;
    }
   }catch(System.Security.SecurityException){}catch(UnauthorizedAccessException){}catch(System.IO.IOException){}
   return result;
  }
  public override string ToString(){return "Windows "+Major+"."+Minor+" build "+Build+"; native="+ArchitectureName+"; process="+(Environment.Is64BitProcess?"64-bit":"32-bit")+"; edition="+(Edition??"unknown")+"; product="+(ProductName??"unknown");}
 }
}
