using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using CodexDragonWallpaper;

namespace CodexDragonPublic {
    // This public entry reuses the production host's registration, path and
    // WinVerifyTrust checks. It neither patches Codex nor uses a TCP debug port.
    internal static class PortableLaunch {
        const string Family="OpenAI.Codex_2p2nqsd0c76g0";
        const string AppId=Family+"!App";
        static readonly string Root=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);

        [STAThread] static int Main(string[] args) {
            bool quiet=args.Length==1 && (args[0]=="--configure" || args[0]=="--check");
            if(args.Length>1 || (args.Length==1 && args[0]!="--configure" && args[0]!="--check" && args[0]!="--preview"))return 64;
            try {
                bool taken=false;
                using(var gate=new Mutex(false,"Local\\CodexDragon.PublicLaunch."+Files.HashText(Root.ToUpperInvariant()).Substring(0,24))) {
                    try {
                        try {taken=gate.WaitOne(0);}catch(AbandonedMutexException){taken=true;}
                        if(!taken) {
                            if(quiet){Record("PUBLIC_ENTRY_BUSY");return 75;}
                            return 0; // A repeated click must not queue a second startup.
                        }
                        bool preview=args.Length==1 && args[0]=="--preview";
                        bool check=args.Length==1 && args[0]=="--check";
                        var prepare=Task.Run(()=>Prepare(check,preview,quiet&&!check));
                        if(!prepare.Wait(20000))throw new InvalidOperationException("PUBLIC_PREPARATION_TIMEOUT");
                        bool replay=prepare.GetAwaiter().GetResult();
                        if(quiet)return 0;
                        var start=new ProcessStartInfo {
                            FileName=Files.Regular(Path.Combine(Root,"Start-Codex-Dragon.exe")),
                            WorkingDirectory=Root,Arguments=preview?"--preview":(replay?"--replay-existing":""),
                            UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden
                        };
                        start.EnvironmentVariables.Clear();
                        foreach(var item in HostEnvironment.Build(null))start.EnvironmentVariables[item.Key]=item.Value;
                        // Keep the duplicate-click gate for the bounded startup lifetime.
                        // We never kill the child if it outlives this entry's wait.
                        using(var process=Process.Start(start)) {
                            if(process==null)throw new InvalidOperationException("PUBLIC_LAUNCH_FAILED");
                            if(!process.WaitForExit(120000)){Record("PUBLIC_CHILD_TIMEOUT");return 124;}
                            int result=process.ExitCode;
                            if(result!=0)Record("PUBLIC_CHILD_EXIT_"+result.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            return result;
                        }
                    }finally {if(taken)gate.ReleaseMutex();}
                }
            }catch(Exception error) {
                Exception cause=error is AggregateException?((AggregateException)error).GetBaseException():error;
                string code=cause is InvalidOperationException && cause.Message.StartsWith("PUBLIC_",StringComparison.Ordinal)?cause.Message:
                    (cause is HostFailure?"PUBLIC_COMPONENT_OR_SIGNATURE_REJECTED":"PUBLIC_PREPARATION_FAILED");
                Record(code);
                if(!quiet)MessageBox.Show("The wallpaper launcher could not start safely.\n\n"+
                    "Install the official Microsoft Store Codex app and .NET Framework 4.8. Extract the complete release to a local writable folder.\n"+
                    "If files are missing or damaged, extract a fresh copy; do not mix releases.\n\n"+
                    "This launcher does not close or restart an existing Codex client. Code: "+code,
                    "Codex Dragon startup",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                return 1;
            }
        }
        static void Record(string code) {
            try {
                for(string cursor=Root;!String.IsNullOrEmpty(cursor);cursor=Path.GetDirectoryName(cursor))
                    if((File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)return;
                string folder=Path.Combine(Root,"diagnostics");
                if(IsLink(folder))return;
                Directory.CreateDirectory(folder);
                if(IsLink(folder))return;
                string file=Path.Combine(folder,"portable-launch.log");
                if(IsLink(file))return;
                if(File.Exists(file))Files.Regular(file);
                if(File.Exists(file) && new FileInfo(file).Length>65536)File.WriteAllText(file,"");
                // Never write exception messages, environment variables or account data.
                File.AppendAllText(file,DateTime.UtcNow.ToString("O")+" "+code+Environment.NewLine,Encoding.UTF8);
            }catch{}
        }
        static bool IsLink(string path) {
            // GetAttributes examines the link itself; File.Exists alone misses
            // a dangling link and must not authorize creating its external target.
            try {return (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0;}
            catch(FileNotFoundException){return false;}
            catch(DirectoryNotFoundException){return false;}
        }
        internal static bool Prepare(bool readOnly,bool preview,bool configure=false) {
            if(!Environment.Is64BitOperatingSystem || Environment.OSVersion.Version.Major<10)
                throw new InvalidOperationException("PUBLIC_UNSUPPORTED_WINDOWS");
            foreach(var pin in BootstrapPins.Code) {
                string file=Files.Regular(Path.Combine(Root,pin.Key));
                if(!String.Equals(Files.Hash(file),pin.Value,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("PUBLIC_CODE_INTEGRITY_FAILED");
            }
            foreach(var pin in BootstrapPins.Media) {
                // The distribution/installer requires both videos, but a damaged
                // optional clip after installation must retain the host's ambient fallback.
                if(pin.Path.EndsWith("/dragon-wallpaper-particles-1440p48.mp4",StringComparison.Ordinal))continue;
                string file=Files.Regular(Path.Combine(Root,pin.Path));
                if(new FileInfo(file).Length!=pin.Bytes)throw new InvalidOperationException("PUBLIC_MEDIA_INCOMPLETE");
            }
            ValidateLauncher();
            if(preview)return false; // Preview never discovers, starts or controls Codex.
            bool replay;
            try {
                replay=CodexDragon.Official.OfficialIdentityResolver.IsOfficialRunning();
                // Validation/install modes remain read-only with respect to the
                // official client. A real cold launch selects its identity only
                // in Host, after taking the launch lock, avoiding stale pins.
                if(readOnly||configure)using(var identity=CodexDragon.Official.OfficialIdentityResolver.ResolveCurrent()){
                    string app=Path.GetDirectoryName(identity.Executable);
                    if(!File.Exists(Path.Combine(app,"chrome.dll"))||!File.Exists(Path.Combine(app,"resources","app.asar")))
                        throw new InvalidOperationException("PUBLIC_OFFICIAL_RUNTIME_UNSUPPORTED");
                }
            }catch(Exception error){
                if(error is InvalidOperationException&&error.Message=="PUBLIC_OFFICIAL_RUNTIME_UNSUPPORTED")throw;
                int diagnostic=CodexDragon.Official.OfficialIdentityResolver.DiagnosticCode(error);
                throw new InvalidOperationException(diagnostic==10?"PUBLIC_OFFICIAL_DISCOVERY_FAILED":"PUBLIC_OFFICIAL_IDENTITY_REJECTED");
            }
            if(readOnly)return replay;
            var ambient=BootstrapPins.Media.Single(p=>p.Path.EndsWith("/dragon-wallpaper-ambient-2k-144fps.mp4",StringComparison.Ordinal));
            var alternate=BootstrapPins.Media.Single(p=>p.Path.EndsWith("/dragon-wallpaper-particles-1440p48.mp4",StringComparison.Ordinal));
            var config=new Dictionary<string,object> {
                {"version",2},{"officialFamilyName",Family},{"officialIdentityMode","registered-signed"},
                {"rendererSha256",BootstrapPins.Code["renderer.js"]},
                {"mediaPath",Path.Combine(Root,ambient.Path)},{"mediaBytes",ambient.Bytes},{"mediaSha256",ambient.Sha256},
                {"wallpaperEnabled",true},
                {"alternateMediaPath",Path.Combine(Root,alternate.Path)},{"alternateMediaBytes",alternate.Bytes},{"alternateMediaSha256",alternate.Sha256}
            };
            WriteConfig(Json.Serializer().Serialize(config));
            return replay;
        }
        static void ValidateLauncher() {
            string config=Files.Regular(Path.Combine(Root,"launcher.config.xml"));
            var options=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=65536};
            XDocument document;using(var reader=XmlReader.Create(config,options))document=XDocument.Load(reader);
            XElement launcher=document.Root;
            if(launcher==null || launcher.Name!="launcher" || (string)launcher.Element("appUserModelId")!=AppId ||
                !String.IsNullOrWhiteSpace((string)launcher.Element("executablePath")) ||
                !String.IsNullOrWhiteSpace((string)launcher.Element("executableArguments")))
                throw new InvalidOperationException("PUBLIC_LAUNCHER_CONFIG_INVALID");
            string[] names={"videoFile","characterImage","closedEyeImage","startupIntroFile","startupIdleFile","startupFinaleFile"};
            foreach(string name in names) {
                string relative=(string)launcher.Element(name);
                if(!BootstrapPins.Media.Any(p=>p.Path==relative))throw new InvalidOperationException("PUBLIC_LAUNCHER_ASSET_INVALID");
            }
        }
        static void WriteConfig(string value) {
            string path=Path.Combine(Root,"wallpaper-host-config.json");
            if(File.Exists(path)) {
                Files.Regular(path);
                if(new FileInfo(path).Length<=16384 && File.ReadAllText(path,Encoding.UTF8)==value)return;
            }
            string pending=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                using(var stream=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    byte[] bytes=new UTF8Encoding(false).GetBytes(value);stream.Write(bytes,0,bytes.Length);stream.Flush(true);
                }
                if(File.Exists(path))File.Replace(pending,path,null);else File.Move(pending,path);
            }finally {if(File.Exists(pending))File.Delete(pending);}
        }
    }
}
