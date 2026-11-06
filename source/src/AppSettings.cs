using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AutoTunel {
 [DataContract]
 sealed class SavedSettings:IExtensibleDataObject {
  [DataMember(Name="version",IsRequired=true,Order=0)] public int Version=1;
  [DataMember(Name="serverTool",Order=1)] public string ServerTool="";
  [DataMember(Name="clientTool",Order=2)] public string ClientTool="";
  public ExtensionDataObject ExtensionData{get;set;}
 }

 // User preferences only. Session secrets and USB ownership journals never belong here.
 public sealed class AppSettingsStore {
  public const string FileName="settings.json";
  const int MaxBytes=65536,MaxPathCharacters=4096;
  readonly string directory,mutexName;
  public string FilePath{get{return Path.Combine(directory,FileName);}}
  public AppSettingsStore(string dataDirectory){
   if(string.IsNullOrWhiteSpace(dataDirectory))throw new ArgumentException("Brak folderu ustawień.");
   directory=Path.GetFullPath(dataDirectory);
   using(var hash=SHA256.Create())mutexName="AutoTunel-Settings-"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(directory.ToUpperInvariant()))).Replace("-","");
  }
  public string GetTool(bool server){return Locked(()=>{var settings=LoadAndMigrate();return server?settings.ServerTool:settings.ClientTool;});}
  public void SetTool(bool server,string path){
   ValidatePath(path,server,false);
   Locked(()=>{var settings=LoadAndMigrate();if(server)settings.ServerTool=path;else settings.ClientTool=path;Save(settings);return true;});
  }
  T Locked<T>(Func<T> action){
   using(var mutex=new Mutex(false,mutexName)){
    bool held=false;
    try{
     try{held=mutex.WaitOne(TimeSpan.FromSeconds(5));}catch(AbandonedMutexException){held=true;}
     if(!held)throw new IOException("Ustawienia są aktualizowane przez drugą aplikację. Spróbuj ponownie.");
     return action();
    }finally{if(held)mutex.ReleaseMutex();}
   }
  }
  SavedSettings LoadAndMigrate(){
   SavedSettings settings=new SavedSettings();
   if(File.Exists(FilePath)){
    try{
     using(var stream=new MemoryStream(ReadBounded(FilePath)))settings=(SavedSettings)new DataContractJsonSerializer(typeof(SavedSettings)).ReadObject(stream);
     if(settings==null||settings.Version!=1)throw new SerializationException("Nieobsługiwana wersja ustawień.");
     ValidatePath(settings.ServerTool,true,true);ValidatePath(settings.ClientTool,false,true);
    }catch(Exception e)when(e is SerializationException||e is ArgumentException||e is InvalidDataException){
     throw new IOException("Nie można odczytać ustawień: "+FilePath+". Zachowaj kopię pliku i zmień jego nazwę, aby ustawić programy USB ponownie. Oryginał nie został nadpisany.",e);
    }
   }
   string serverLegacy=Path.Combine(directory,"usbipd-path.txt"),clientLegacy=Path.Combine(directory,"usbip-path.txt");
   bool serverExists=File.Exists(serverLegacy),clientExists=File.Exists(clientLegacy);
   if(serverExists||clientExists){
    // Read and validate both before writing anything. An incomplete migration must
    // not discard the other role's previously saved executable path.
    if(serverExists&&string.IsNullOrEmpty(settings.ServerTool))settings.ServerTool=ReadLegacy(serverLegacy,true);
    if(clientExists&&string.IsNullOrEmpty(settings.ClientTool))settings.ClientTool=ReadLegacy(clientLegacy,false);
    Save(settings);
    if(serverExists)File.Delete(serverLegacy);
    if(clientExists)File.Delete(clientLegacy);
   }
   return settings;
  }
  string ReadLegacy(string file,bool server){
   try{
    string value;using(var reader=new StreamReader(new MemoryStream(ReadBounded(file)),Encoding.UTF8,true))value=reader.ReadToEnd().Trim();
    ValidatePath(value,server,false);return value;
   }catch(Exception e)when(e is ArgumentException||e is InvalidDataException){throw new IOException("Nieprawidłowe stare ustawienia: "+file+". Popraw ścieżkę lub zachowaj kopię i zmień nazwę pliku. Ustawienia nie zostały usunięte.",e);}
  }
  static byte[] ReadBounded(string path){
   using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
    var bytes=new byte[MaxBytes+1];int used=0,read;
    while(used<bytes.Length&&(read=file.Read(bytes,used,bytes.Length-used))>0)used+=read;
    if(used>MaxBytes)throw new InvalidDataException("Plik ustawień jest zbyt duży.");
    Array.Resize(ref bytes,used);return bytes;
   }
  }
  static void ValidatePath(string path,bool server,bool allowEmpty){
   if(allowEmpty&&string.IsNullOrEmpty(path))return;
   if(string.IsNullOrWhiteSpace(path)||path.Length>MaxPathCharacters||!Path.IsPathRooted(path))throw new ArgumentException("Wskaż pełną ścieżkę do programu USB/IP.");
   foreach(char c in path)if(char.IsControl(c))throw new ArgumentException("Ścieżka programu zawiera niedozwolony znak.");
   if(path.IndexOfAny(Path.GetInvalidPathChars())>=0||!Path.GetFileName(path).Equals(server?"usbipd.exe":"usbip.exe",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Wskaż program "+(server?"usbipd.exe":"usbip.exe")+" dla wybranej roli.");
  }
  void Save(SavedSettings settings){
   Directory.CreateDirectory(directory);string temporary=Path.Combine(directory,".settings-"+Guid.NewGuid().ToString("N")+".tmp");
   try{
    using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
     new DataContractJsonSerializer(typeof(SavedSettings)).WriteObject(file,settings);file.Flush(true);
    }
    if(File.Exists(FilePath))File.Replace(temporary,FilePath,null);else File.Move(temporary,FilePath);
   }finally{if(File.Exists(temporary))File.Delete(temporary);}
  }
 }
}
