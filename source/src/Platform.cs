using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
namespace AutoTunel {
 public static class Platform {
  public static string DataDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AutoTunel-MB");
  public static string Quote(string s){if(s.IndexOf('"')>=0||s.IndexOf('\r')>=0||s.IndexOf('\n')>=0)throw new ArgumentException("Niedozwolony znak.");return "\""+s+"\"";}
  public static string Run(string path,string args,int timeout=15000){using(var p=Process.Start(new ProcessStartInfo(path,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){var o=p.StandardOutput.ReadToEndAsync();var e=p.StandardError.ReadToEndAsync();if(!p.WaitForExit(timeout)){try{p.Kill();}catch{}throw new IOException("Przekroczono czas wykonania polecenia.");}string output=o.GetAwaiter().GetResult(),error=e.GetAwaiter().GetResult();if(p.ExitCode!=0)throw new IOException(string.IsNullOrWhiteSpace(error)?output:error);return output.Trim();}}
  public static string Powershell(string script,int timeout=30000){return Run("powershell.exe","-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),timeout);}
  public static X509Certificate2 Certificate(){
   string thumb=Powershell("$ErrorActionPreference='Stop'; $c=Get-ChildItem Cert:\\CurrentUser\\My | Where-Object {$_.FriendlyName -eq 'AutoTunel-MB TLS' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(7)} | Sort-Object NotAfter -Descending | Select-Object -First 1; if (!$c) {$c=New-SelfSignedCertificate -Subject 'CN=AutoTunel-MB' -FriendlyName 'AutoTunel-MB TLS' -CertStoreLocation 'Cert:\\CurrentUser\\My' -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(2)}; Write-Output $c.Thumbprint",60000);
   using(var store=new X509Store(StoreName.My,StoreLocation.CurrentUser)){store.Open(OpenFlags.ReadOnly);var found=store.Certificates.Find(X509FindType.FindByThumbprint,thumb,false);if(found.Count==0)throw new IOException("Brak certyfikatu TLS.");return found[0];}
  }
 }
}
