using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutoTunel;

static class AppSettingsTests {
 static void Assert(bool value,string message){if(!value)throw new Exception(message);}
 static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
 sealed class Folder:IDisposable {
  public readonly string Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"autotunel-settings-"+Guid.NewGuid().ToString("N"));
  public Folder(){Directory.CreateDirectory(Path);}
  public string Tool(bool server,string child="Programy z polską nazwą"){return System.IO.Path.Combine(Path,child,server?"usbipd.exe":"usbip.exe");}
  public void Dispose(){Directory.Delete(Path,true);}
 }
 public static void Run(Action<string,Action> test){
  test("Settings keep both roles in one JSON through repeated path changes",()=>{
   using(var folder=new Folder()){
    var store=new AppSettingsStore(folder.Path);Assert(string.IsNullOrEmpty(store.GetTool(true)),"new profile must be empty");
    store.SetTool(true,folder.Tool(true));store.SetTool(false,folder.Tool(false));store.SetTool(true,folder.Tool(true,"Nowsza wersja"));
    var fresh=new AppSettingsStore(folder.Path);
    Assert(fresh.GetTool(true)==folder.Tool(true,"Nowsza wersja")&&fresh.GetTool(false)==folder.Tool(false),"one role overwrote the other");
    using(var json=JsonDocument.Parse(File.ReadAllBytes(store.FilePath))){Assert(json.RootElement.GetProperty("version").GetInt32()==1,"missing format version");Assert(json.RootElement.EnumerateObject().Count()==3,"unexpected persisted fields");}
    Assert(Directory.GetFiles(folder.Path).Select(System.IO.Path.GetFileName).SequenceEqual(new[]{AppSettingsStore.FileName}),"settings left sidecar files");
   }
  });
  test("Settings migrate both legacy paths including UTF8 BOM without changing recovery journals",()=>{
   using(var folder=new Folder()){
    File.WriteAllText(System.IO.Path.Combine(folder.Path,"usbipd-path.txt"),"  "+folder.Tool(true)+"\r\n",new UTF8Encoding(true));
    File.WriteAllText(System.IO.Path.Combine(folder.Path,"usbip-path.txt"),folder.Tool(false));
    string journal=System.IO.Path.Combine(folder.Path,"owned-usb-share.txt");File.WriteAllText(journal,"test ownership marker");
    var store=new AppSettingsStore(folder.Path);Assert(store.GetTool(true)==folder.Tool(true)&&store.GetTool(false)==folder.Tool(false),"migration lost a role");
    Assert(!File.Exists(System.IO.Path.Combine(folder.Path,"usbipd-path.txt"))&&!File.Exists(System.IO.Path.Combine(folder.Path,"usbip-path.txt")),"legacy paths were not consolidated");
    Assert(File.ReadAllText(journal)=="test ownership marker","settings migration changed session recovery state");
   }
  });
  test("Settings migration fills missing roles while preserving current values and unknown fields",()=>{
   using(var folder=new Folder()){
    var store=new AppSettingsStore(folder.Path);
    File.WriteAllText(store.FilePath,JsonSerializer.Serialize(new{version=1,serverTool=folder.Tool(true,"Current"),futurePreference="keep"}));
    File.WriteAllText(System.IO.Path.Combine(folder.Path,"usbipd-path.txt"),folder.Tool(true,"Legacy"));
    File.WriteAllText(System.IO.Path.Combine(folder.Path,"usbip-path.txt"),folder.Tool(false));
    Assert(store.GetTool(true)==folder.Tool(true,"Current")&&store.GetTool(false)==folder.Tool(false),"migration overwrote newer settings");
    store.SetTool(false,folder.Tool(false,"Updated"));
    using(var json=JsonDocument.Parse(File.ReadAllBytes(store.FilePath)))Assert(json.RootElement.GetProperty("futurePreference").GetString()=="keep","unknown fields were discarded");
   }
  });
  test("Settings reject corrupt oversized and unsupported JSON without destroying originals",()=>{
   using(var folder=new Folder()){
    var store=new AppSettingsStore(folder.Path);string legacy=System.IO.Path.Combine(folder.Path,"usbipd-path.txt");File.WriteAllText(legacy,folder.Tool(true));
    foreach(var data in new[]{"{broken","{}","null","{\"version\":2}",new string(' ',65537),"{\"version\":1,\"clientTool\":\"relative.exe\"}"}){
     File.WriteAllText(store.FilePath,data);Throws<IOException>(()=>store.GetTool(true));Throws<IOException>(()=>store.SetTool(false,folder.Tool(false)));
     Assert(File.ReadAllText(store.FilePath)==data&&File.Exists(legacy),"invalid config was overwritten or migration data removed");
    }
   }
  });
  test("Settings reject invalid executable paths and incomplete migration atomically",()=>{
   using(var folder=new Folder()){
    var store=new AppSettingsStore(folder.Path);store.SetTool(true,folder.Tool(true));string original=File.ReadAllText(store.FilePath);
    foreach(var invalid in new[]{"usbip.exe",folder.Tool(true),folder.Tool(false)+"\n",new string('x',4097)}){
     Throws<ArgumentException>(()=>store.SetTool(false,invalid));Assert(File.ReadAllText(store.FilePath)==original,"bad path changed settings");
    }
    string legacy=System.IO.Path.Combine(folder.Path,"usbip-path.txt");File.WriteAllText(legacy,"not-a-tool");
    Throws<IOException>(()=>store.GetTool(true));Assert(File.ReadAllText(store.FilePath)==original&&File.Exists(legacy),"failed migration changed data");
   }
  });
  test("Settings serialize concurrent updates from separate store instances",()=>{
   using(var folder=new Folder()){
    var a=Task.Run(()=>{for(int i=0;i<12;i++)new AppSettingsStore(folder.Path).SetTool(true,folder.Tool(true,i.ToString()));});
    var b=Task.Run(()=>{for(int i=0;i<12;i++)new AppSettingsStore(folder.Path).SetTool(false,folder.Tool(false,i.ToString()));});
    Task.WaitAll(a,b);var store=new AppSettingsStore(folder.Path);
    Assert(store.GetTool(true)==folder.Tool(true,"11")&&store.GetTool(false)==folder.Tool(false,"11"),"concurrent writes lost an update");
    Assert(Directory.GetFiles(folder.Path).Length==1,"temporary files leaked");
   }
  });
 }
}
