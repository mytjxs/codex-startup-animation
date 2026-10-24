using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace CodexDragon.Official {
    public sealed class OfficialIdentitySnapshot : IDisposable {
        private OfficialIdentityResolver.IdentityLease lease;
        public string PackageFullName { get; private set; }
        public string Executable { get; private set; }
        public string Sha256 { get; private set; }
        public long Created { get; private set; }
        public int Pid { get; private set; }
        internal OfficialIdentitySnapshot(OfficialIdentityResolver.IdentityLease owned,int pid,long created) {
            lease=owned;PackageFullName=owned.Package;Executable=owned.Executable;Sha256=owned.Hash;Pid=pid;Created=created;
        }
        public void Dispose() { var owned=Interlocked.Exchange(ref lease,null);if(owned!=null)OfficialIdentityResolver.Release(owned); }
    }

    public static class OfficialIdentityResolver {
        public const string PackageFamily="OpenAI.Codex_2p2nqsd0c76g0";
        private const int MaxPackages=32,MaxProcesses=4096,SignatureTimeoutMs=5000;
        private static readonly object CacheGate=new object();
        private static readonly Dictionary<string,IdentityLease> Leases=new Dictionary<string,IdentityLease>(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim TrustGate=new SemaphoreSlim(1,1);
        internal sealed class PackageRecord { internal string FullName,Root,Executable;internal Version Version; }
        internal sealed class ProcessRecord { internal int Pid,Session;internal long Created;internal string Package,Executable;internal bool Window; }
        internal sealed class IdentityLease { internal string Key,Package,Executable,Hash;internal FileStream File,Manifest;internal int References; }
        private static InvalidOperationException Failure(string code) { return new InvalidOperationException(code); }

        // Stable, allowlisted diagnostics. Never return paths, signer details,
        // process command lines, exception messages or package versions.
        public static int DiagnosticCode(Exception error) {
            if(!(error is InvalidOperationException))return 15;
            switch(error.Message){
                case "official-package-not-registered":case "official-registration-ambiguous":
                case "official-registration-query-failed":case "official-registration-buffer-invalid":
                case "official-registration-capacity-changed":case "official-package-path-unavailable":
                case "official-registration-changing":return 10;
                case "official-manifest-path-invalid":case "official-file-missing":case "official-linked-path-rejected":
                case "official-package-identity-invalid":case "official-manifest-too-large":case "official-manifest-identity-invalid":
                case "official-application-ambiguous":case "official-executable-size-invalid":return 11;
                case "official-signature-invalid":case "official-signer-mismatch":case "official-signature-verifier-busy":
                case "official-signature-verification-timeout":return 12;
                case "official-running-unverifiable":case "official-process-generation-mismatch":
                case "official-process-family-mismatch":case "official-process-registration-ambiguous":
                case "official-session-query-failed":case "official-process-query-denied":case "official-process-not-alive":
                case "official-process-package-query-failed":case "official-process-inventory-limit":return 13;
                default:return 15;
            }
        }

        internal static Version PackageVersion(string fullName) {
            if(fullName==null||fullName.Length>240)return null;
            var match=Regex.Match(fullName,@"^OpenAI\.Codex_([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)_x64__2p2nqsd0c76g0$",RegexOptions.CultureInvariant);
            Version version;
            if(!match.Success||!Version.TryParse(match.Groups[1].Value,out version)||version.Major>65535||version.Minor>65535||version.Build>65535||version.Revision>65535)return null;
            return version;
        }
        internal static bool SameFamily(string fullName) {
            if(String.IsNullOrEmpty(fullName))return false;
            string[] parts=fullName.Split('_');return parts.Length==5&&parts[0]=="OpenAI.Codex"&&parts[4]=="2p2nqsd0c76g0";
        }
        internal static PackageRecord SelectCurrent(IList<PackageRecord> records) {
            PackageRecord best=null;var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var record in records){
                Version version=PackageVersion(record.FullName);if(version==null)continue;
                if(!seen.Add(record.FullName))throw Failure("official-registration-ambiguous");
                if(best==null||version>best.Version)best=record;
                else if(version==best.Version&&!String.Equals(best.FullName,record.FullName,StringComparison.Ordinal))throw Failure("official-registration-ambiguous");
            }
            if(best==null)throw Failure("official-package-not-registered");return best;
        }
        internal static string ManifestExecutable(string root,string relative) {
            if(String.IsNullOrWhiteSpace(root)||!Path.IsPathRooted(root)||root.StartsWith(@"\\",StringComparison.Ordinal)||String.IsNullOrWhiteSpace(relative)||relative.Length>512||Path.IsPathRooted(relative)||relative.IndexOfAny(new[]{':','\0','%','$','"'})>=0)throw Failure("official-manifest-path-invalid");
            relative=relative.Replace('/','\\');
            foreach(string component in relative.Split('\\'))if(component.Length==0||component=="."||component==".."||component.EndsWith(".",StringComparison.Ordinal)||component.EndsWith(" ",StringComparison.Ordinal)||component.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw Failure("official-manifest-path-invalid");
            string boundary=Path.GetFullPath(root).TrimEnd('\\')+"\\",path=Path.GetFullPath(Path.Combine(boundary,relative));
            if(!path.StartsWith(boundary,StringComparison.OrdinalIgnoreCase)||!String.Equals(Path.GetExtension(path),".exe",StringComparison.OrdinalIgnoreCase))throw Failure("official-manifest-path-invalid");return path;
        }
        private static void Regular(string path) {
            if(!Path.IsPathRooted(path)||path.StartsWith(@"\\",StringComparison.Ordinal)||!File.Exists(path))throw Failure("official-file-missing");
            for(string part=Path.GetFullPath(path);!String.IsNullOrEmpty(part);part=Path.GetDirectoryName(part))if((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw Failure("official-linked-path-rejected");
        }
        internal static PackageRecord ReadPackage(string fullName,string root) {
            Version version=PackageVersion(fullName);if(version==null)throw Failure("official-package-identity-invalid");
            string manifest=Path.Combine(root,"AppxManifest.xml");Regular(manifest);
            using(var lease=new FileStream(manifest,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                if(lease.Length>2097152)throw Failure("official-manifest-too-large");
                var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2097152};
                var document=new XmlDocument{XmlResolver=null};using(var reader=XmlReader.Create(lease,settings))document.Load(reader);
                var identities=document.SelectNodes("/*[local-name()='Package']/*[local-name()='Identity']");
                if(identities.Count!=1)throw Failure("official-manifest-identity-invalid");
                var identity=(XmlElement)identities[0];
                if(identity.GetAttribute("Name")!="OpenAI.Codex"||identity.GetAttribute("ProcessorArchitecture")!="x64"||identity.GetAttribute("Version")!=version.ToString())throw Failure("official-manifest-identity-invalid");
                var apps=document.SelectNodes("/*[local-name()='Package']/*[local-name()='Applications']/*[local-name()='Application'][@Id='App']");
                if(apps.Count!=1)throw Failure("official-application-ambiguous");
                string executable=ManifestExecutable(root,((XmlElement)apps[0]).GetAttribute("Executable"));Regular(executable);
                return new PackageRecord{FullName=fullName,Root=Path.GetFullPath(root),Executable=executable,Version=version};
            }
        }
        private static List<PackageRecord> Registered() {
            for(int attempt=0;attempt<2;attempt++) {
                uint count=0,chars=0;int result=GetPackagesByPackageFamily(PackageFamily,ref count,IntPtr.Zero,ref chars,IntPtr.Zero);
                if(result==0&&count==0)return new List<PackageRecord>();
                if(result!=122||count==0||count>MaxPackages||chars<2||chars>32768)throw Failure("official-registration-query-failed");
                uint allocatedCount=count,allocatedChars=chars;
                IntPtr names=Marshal.AllocHGlobal(checked((int)count*IntPtr.Size)),buffer=Marshal.AllocHGlobal(checked((int)chars*2));
                try {
                    result=GetPackagesByPackageFamily(PackageFamily,ref count,names,ref chars,buffer);
                    if(result==122)continue;if(result!=0)throw Failure("official-registration-query-failed");
                    ValidateInventoryCapacity(count,chars,allocatedCount,allocatedChars);
                    var records=new List<PackageRecord>();
                    for(int i=0;i<count;i++){
                        IntPtr value=Marshal.ReadIntPtr(names,i*IntPtr.Size);long offset=value.ToInt64()-buffer.ToInt64();
                        if(offset<0||(offset&1)!=0||offset>=checked((long)chars*2))throw Failure("official-registration-buffer-invalid");
                        int available=checked((int)chars-(int)(offset/2)),lengthInBuffer=0;
                        while(lengthInBuffer<available&&lengthInBuffer<=240&&Marshal.ReadInt16(value,lengthInBuffer*2)!=0)lengthInBuffer++;
                        if(lengthInBuffer>=available||lengthInBuffer>240)throw Failure("official-registration-buffer-invalid");
                        string name=Marshal.PtrToStringUni(value,lengthInBuffer);if(PackageVersion(name)==null)continue;
                        uint length=0;if(GetPackagePathByFullName(name,ref length,null)!=122||length<2||length>32768)throw Failure("official-package-path-unavailable");
                        var path=new StringBuilder((int)length);if(GetPackagePathByFullName(name,ref length,path)!=0)throw Failure("official-package-path-unavailable");
                        records.Add(ReadPackage(name,path.ToString()));
                    }
                    return records;
                }finally{Marshal.FreeHGlobal(names);Marshal.FreeHGlobal(buffer);}
            }
            throw Failure("official-registration-changing");
        }
        internal static void ValidateInventoryCapacity(uint count,uint chars,uint allocatedCount,uint allocatedChars){
            if(allocatedCount>MaxPackages||allocatedChars>32768||count>allocatedCount||chars>allocatedChars||count>chars)throw Failure("official-registration-capacity-changed");
        }
        internal static bool IsEntryCandidate(ProcessRecord process,IList<PackageRecord> registered){
            foreach(var package in registered)if(String.Equals(package.Executable,process.Executable,StringComparison.OrdinalIgnoreCase))return true;
            if(SameFamily(process.Package)){
                foreach(var package in registered)if(package.FullName==process.Package)return false; // A registered package's CLI/utility is not its App entry.
                throw Failure("official-running-unverifiable"); // Known family still alive after its registration disappeared.
            }
            // A successfully queried ordinary ChatGPT.exe in another location is unrelated.
            return false;
        }
        internal static PackageRecord MatchProcess(ProcessRecord process,IList<PackageRecord> records,int session,long expectedCreated) {
            if(process.Pid<=0||process.Created<=0||process.Created!=expectedCreated||process.Session!=session)throw Failure("official-process-generation-mismatch");
            if(!String.IsNullOrEmpty(process.Package)&&!SameFamily(process.Package))throw Failure("official-process-family-mismatch");
            PackageRecord match=null;
            foreach(var record in records)if(String.Equals(process.Executable,record.Executable,StringComparison.OrdinalIgnoreCase)&&
                (String.IsNullOrEmpty(process.Package)||String.Equals(process.Package,record.FullName,StringComparison.Ordinal))){if(match!=null)throw Failure("official-process-registration-ambiguous");match=record;}
            if(match==null)throw Failure("official-running-unverifiable");return match;
        }
        internal static List<ProcessRecord> Representatives(IEnumerable<ProcessRecord> processes) {
            var groups=new Dictionary<string,ProcessRecord>(StringComparer.OrdinalIgnoreCase);
            foreach(var p in processes){ProcessRecord old;if(!groups.TryGetValue(p.Executable,out old)||p.Window&&!old.Window||p.Window==old.Window&&p.Created<old.Created)groups[p.Executable]=p;}
            return new List<ProcessRecord>(groups.Values);
        }
        private static int CurrentSession(){uint session;using(var own=Process.GetCurrentProcess())if(!ProcessIdToSessionId((uint)own.Id,out session))throw Failure("official-session-query-failed");return checked((int)session);}
        private static ProcessRecord Probe(int pid) {
            IntPtr handle=OpenProcess(0x00101000,false,pid);if(handle==IntPtr.Zero)throw Failure("official-process-query-denied");
            try{
                long created,exited,kernel,user;uint session;
                if(WaitForSingleObject(handle,0)!=258||!GetProcessTimes(handle,out created,out exited,out kernel,out user)||!ProcessIdToSessionId((uint)pid,out session))throw Failure("official-process-not-alive");
                var image=new StringBuilder(32768);uint size=32768;if(!QueryFullProcessImageName(handle,0,image,ref size))throw Failure("official-process-query-denied");
                uint length=0;string package=null;int rc=GetPackageFullName(handle,ref length,null);
                if(rc==122&&length>1&&length<=256){var text=new StringBuilder((int)length);if(GetPackageFullName(handle,ref length,text)!=0)throw Failure("official-process-package-query-failed");package=text.ToString();}
                else if(rc!=15700)throw Failure("official-process-package-query-failed");
                if(WaitForSingleObject(handle,0)!=258)throw Failure("official-process-not-alive");
                return new ProcessRecord{Pid=pid,Created=created,Session=checked((int)session),Package=package,Executable=Path.GetFullPath(image.ToString())};
            }finally{CloseHandle(handle);}
        }
        public static OfficialIdentitySnapshot ResolveCurrent() {
            return ResolveCurrent(Registered,Acquire);
        }
        internal static OfficialIdentitySnapshot ResolveCurrent(Func<IList<PackageRecord>> inventory,Func<PackageRecord,IdentityLease> acquire) {
            // A Store update can finish between discovery and trust checking.
            // Reacquire at most once, only after two valid inventories disagree.
            // Signature, path, manifest and inventory errors still fail closed;
            // this never reselects the identity of an already running process.
            for(int attempt=0;attempt<2;attempt++){
                var selected=SelectCurrent(inventory());var lease=acquire(selected);
                try{
                    var current=SelectCurrent(inventory());
                    if(current.FullName==selected.FullName&&String.Equals(current.Executable,selected.Executable,StringComparison.OrdinalIgnoreCase))return new OfficialIdentitySnapshot(lease,0,0);
                }catch{Release(lease);throw;}
                Release(lease); // Never hold the superseded version while retrying.
            }
            throw Failure("official-registration-changing");
        }
        public static OfficialIdentitySnapshot VerifyProcess(int pid,long creation) {
            int session=CurrentSession();var first=Probe(pid);var selected=MatchProcess(first,Registered(),session,creation);var lease=Acquire(selected);
            try{var after=Probe(pid);var selectedAfter=MatchProcess(after,Registered(),session,creation);if(selectedAfter.FullName!=selected.FullName||!String.Equals(selectedAfter.Executable,selected.Executable,StringComparison.OrdinalIgnoreCase))throw Failure("official-process-generation-mismatch");return new OfficialIdentitySnapshot(lease,pid,creation);}catch{Release(lease);throw;}
        }
        public static OfficialIdentitySnapshot[] FindRunning() {
            var registered=Registered();int session=CurrentSession();var candidates=new List<ProcessRecord>();
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"ChatGPT","Codex"};foreach(var p in registered)names.Add(Path.GetFileNameWithoutExtension(p.Executable));
            Process[] processes=Process.GetProcesses();var verified=new List<OfficialIdentitySnapshot>();
            try{
                if(processes.Length>MaxProcesses)throw Failure("official-process-inventory-limit");
                foreach(var process in processes){
                    uint processSession;if(!ProcessIdToSessionId((uint)process.Id,out processSession)||processSession!=session)continue;
                    bool named=false;try{named=names.Contains(process.ProcessName);}catch{continue;}
                    ProcessRecord probe;try{probe=Probe(process.Id);}catch{if(named){try{if(process.HasExited)continue;}catch{}throw Failure("official-running-unverifiable");}continue;}
                    bool family=SameFamily(probe.Package);if(!family&&!named)continue;
                    if(!IsEntryCandidate(probe,registered))continue;
                    MatchProcess(probe,registered,session,probe.Created);try{probe.Window=process.MainWindowHandle!=IntPtr.Zero;}catch{}candidates.Add(probe);
                }
                foreach(var representative in Representatives(candidates))verified.Add(VerifyProcess(representative.Pid,representative.Created));
                return verified.ToArray();
            }catch{foreach(var item in verified)item.Dispose();throw;}finally{foreach(var process in processes)process.Dispose();}
        }
        public static bool IsOfficialRunning(){var items=FindRunning();try{return items.Length!=0;}finally{foreach(var item in items)item.Dispose();}}
        private static IdentityLease Acquire(PackageRecord package) {
            string key=package.FullName+"\n"+package.Executable;
            lock(CacheGate){IdentityLease existing;if(Leases.TryGetValue(key,out existing)){existing.References++;return existing;}}
            Regular(package.Executable);Regular(Path.Combine(package.Root,"AppxManifest.xml"));FileStream file=null,manifest=null;
            try{
                manifest=new FileStream(Path.Combine(package.Root,"AppxManifest.xml"),FileMode.Open,FileAccess.Read,FileShare.Read);
                file=new FileStream(package.Executable,FileMode.Open,FileAccess.Read,FileShare.Read);
                // Reparse checks repeated while deletion/replacement of these files is denied.
                Regular(package.Executable);Regular(Path.Combine(package.Root,"AppxManifest.xml"));
                var lockedManifest=ReadPackage(package.FullName,package.Root);
                if(!String.Equals(lockedManifest.Executable,package.Executable,StringComparison.OrdinalIgnoreCase))throw Failure("official-registration-changing");
                if(file.Length<=0||file.Length>2147483648L)throw Failure("official-executable-size-invalid");
                VerifySignatureBounded(package.Executable);
                string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(file)).Replace("-","");
                var item=new IdentityLease{Key=key,Package=package.FullName,Executable=package.Executable,Hash=hash,File=file,Manifest=manifest,References=1};file=null;manifest=null;
                lock(CacheGate){IdentityLease existing;if(Leases.TryGetValue(key,out existing)){existing.References++;item.File.Dispose();item.Manifest.Dispose();return existing;}Leases.Add(key,item);}
                return item;
            }finally{if(file!=null)file.Dispose();if(manifest!=null)manifest.Dispose();}
        }
        internal static void Release(IdentityLease lease){lock(CacheGate){if(--lease.References==0){Leases.Remove(lease.Key);lease.File.Dispose();lease.Manifest.Dispose();}}}
        internal static bool AllowedSigner(string signer){return signer=="OpenAI OpCo, LLC"||signer=="OpenAI, L.L.C."||signer=="OpenAI";}
        internal static void AuthorizeSignature(bool valid,string signer){if(!valid)throw Failure("official-signature-invalid");if(!AllowedSigner(signer))throw Failure("official-signer-mismatch");}
        private static void VerifySignatureBounded(string executable){
            if(!TrustGate.Wait(SignatureTimeoutMs))throw Failure("official-signature-verifier-busy");
            Task<bool> work;
            try{work=Task.Factory.StartNew(()=>{try{using(var held=new FileStream(executable,FileMode.Open,FileAccess.Read,FileShare.Read)){VerifySignature(executable);return true;}}finally{TrustGate.Release();}},CancellationToken.None,TaskCreationOptions.DenyChildAttach,TaskScheduler.Default);}catch{TrustGate.Release();throw;}
            try{if(!work.Wait(SignatureTimeoutMs))throw Failure("official-signature-verification-timeout");if(!work.Result)throw Failure("official-signature-invalid");}catch(AggregateException){throw Failure("official-signature-invalid");}
        }
        private static void VerifySignature(string executable){
            var info=new TrustFile{Size=(uint)Marshal.SizeOf(typeof(TrustFile)),Path=executable};IntPtr pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));Marshal.StructureToPtr(info,pointer,false);
            var data=new TrustData{Size=(uint)Marshal.SizeOf(typeof(TrustData)),UI=2,Union=1,File=pointer,StateAction=1,Flags=0x1000};var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            try{if(WinVerifyTrust(new IntPtr(-1),ref action,ref data)!=0)throw Failure("official-signature-invalid");using(var cert=new X509Certificate2(X509Certificate.CreateFromSignedFile(executable)))AuthorizeSignature(true,cert.GetNameInfo(X509NameType.SimpleName,false));}
            finally{data.StateAction=2;WinVerifyTrust(new IntPtr(-1),ref action,ref data);Marshal.DestroyStructure(pointer,typeof(TrustFile));Marshal.FreeHGlobal(pointer);}
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct TrustFile{internal uint Size;[MarshalAs(UnmanagedType.LPWStr)]internal string Path;internal IntPtr File,Subject;}
        [StructLayout(LayoutKind.Sequential)] private struct TrustData{internal uint Size;internal IntPtr Policy,Sip;internal uint UI,Revocation,Union;internal IntPtr File;internal uint StateAction;internal IntPtr State,Url;internal uint Flags,Context;internal IntPtr Signature;}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern int GetPackagesByPackageFamily(string family,ref uint count,IntPtr names,ref uint chars,IntPtr buffer);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern int GetPackagePathByFullName(string name,ref uint length,StringBuilder path);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern int GetPackageFullName(IntPtr process,ref uint length,StringBuilder name);
        [DllImport("kernel32.dll",SetLastError=true)]private static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
        [DllImport("kernel32.dll")]private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")]private static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
        [DllImport("kernel32.dll")]private static extern bool ProcessIdToSessionId(uint pid,out uint session);
        [DllImport("kernel32.dll")]private static extern bool GetProcessTimes(IntPtr process,out long created,out long exited,out long kernel,out long user);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder image,ref uint size);
        [DllImport("wintrust.dll",ExactSpelling=true,PreserveSig=true)]private static extern int WinVerifyTrust(IntPtr window,ref Guid action,ref TrustData data);
    }
}
