using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CodexDragonWallpaper {
internal sealed class HostFailure : Exception { internal readonly int Code; internal HostFailure(int code) : base("HOST_" + code) { Code = code; } }
internal static class Native {
    internal const uint INFINITE = 0xffffffff;
    [StructLayout(LayoutKind.Sequential)] internal struct SecurityAttributes { internal int Size; internal IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] internal bool Inherit; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] internal struct StartupInfo {
        internal int Size; internal IntPtr Reserved, Desktop, Title; internal int X,Y,XSize,YSize,XCount,YCount,Fill,Flags;
        internal short Show,ReservedBytes; internal IntPtr Reserved2,StdIn,StdOut,StdErr;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct StartupInfoEx { internal StartupInfo Info; internal IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] internal struct ProcessInformation { internal IntPtr Process,Thread; internal uint Pid,Tid; }
    [StructLayout(LayoutKind.Sequential)] internal struct FileTime { internal uint Low,High; internal long Value { get { return unchecked((long)(((ulong)High<<32)|Low)); } } }
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool CreatePipe(out IntPtr read,out IntPtr write,ref SecurityAttributes attrs,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool SetHandleInformation(IntPtr handle,uint mask,uint flags);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,uint flags,ref IntPtr size);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,IntPtr attribute,IntPtr value,IntPtr size,IntPtr previous,IntPtr returned);
    [DllImport("kernel32.dll")] internal static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern bool CreateProcessW(string application,StringBuilder command,IntPtr processAttrs,IntPtr threadAttrs,bool inherit,uint flags,IntPtr environment,string cwd,ref StartupInfoEx startup,out ProcessInformation info);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool GetProcessTimes(IntPtr process,out FileTime created,out FileTime exited,out FileTime kernel,out FileTime user);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern int GetPackagePathByFullName(string fullName,ref uint length,StringBuilder path);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern int GetPackagesByPackageFamily(string family,ref uint count,IntPtr names,ref uint bufferLength,IntPtr buffer);

    internal static string PackagePath(string fullName) {
        if(String.IsNullOrWhiteSpace(fullName)||!fullName.StartsWith("OpenAI.Codex_",StringComparison.Ordinal)||fullName.Length>240)throw new HostFailure(10);
        string[] parts=fullName.Split('_'); if(parts.Length<5)throw new HostFailure(10);
        string family=parts[0]+"_"+parts[parts.Length-1]; if(family!="OpenAI.Codex_2p2nqsd0c76g0")throw new HostFailure(10);uint count=0,chars=0;
        int rc=GetPackagesByPackageFamily(family,ref count,IntPtr.Zero,ref chars,IntPtr.Zero);
        if(rc!=122||count==0||count>32||chars>32768)throw new HostFailure(10);
        IntPtr names=Marshal.AllocHGlobal(checked((int)count*IntPtr.Size)),buffer=Marshal.AllocHGlobal(checked((int)chars*2));
        bool registered=false;
        try { rc=GetPackagesByPackageFamily(family,ref count,names,ref chars,buffer);if(rc!=0)throw new HostFailure(10);
            for(int i=0;i<count;i++)if(String.Equals(Marshal.PtrToStringUni(Marshal.ReadIntPtr(names,i*IntPtr.Size)),fullName,StringComparison.Ordinal)){registered=true;break;}
        }finally{Marshal.FreeHGlobal(names);Marshal.FreeHGlobal(buffer);}
        if(!registered)throw new HostFailure(10);
        uint length=0;if(GetPackagePathByFullName(fullName,ref length,null)!=122||length<2||length>32768)throw new HostFailure(10);
        var output=new StringBuilder((int)length);if(GetPackagePathByFullName(fullName,ref length,output)!=0)throw new HostFailure(10);
        return Path.GetFullPath(output.ToString());
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct TrustFile { internal uint Size; [MarshalAs(UnmanagedType.LPWStr)] internal string Path; internal IntPtr File,Subject; }
    [StructLayout(LayoutKind.Sequential)] struct TrustData { internal uint Size; internal IntPtr Policy,Sip; internal uint UI,Revocation,Union; internal IntPtr File; internal uint StateAction; internal IntPtr State,Url; internal uint Flags,Context; internal IntPtr Signature; }
    [DllImport("wintrust.dll",ExactSpelling=true,PreserveSig=true)] static extern int WinVerifyTrust(IntPtr hwnd,ref Guid action,ref TrustData data);
    internal static void VerifyOfficial(string exe,string expectedHash) {
        Files.Regular(exe); if(Files.Hash(exe)!=expectedHash.ToLowerInvariant())throw new HostFailure(11);
        var f=new TrustFile{Size=(uint)Marshal.SizeOf(typeof(TrustFile)),Path=exe};
        IntPtr pf=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));Marshal.StructureToPtr(f,pf,false);
        var data=new TrustData{Size=(uint)Marshal.SizeOf(typeof(TrustData)),UI=2,Union=1,File=pf,StateAction=1,Flags=0x1000};
        var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try { if(WinVerifyTrust(new IntPtr(-1),ref action,ref data)!=0)throw new HostFailure(12);
            using(var cert=new X509Certificate2(X509Certificate.CreateFromSignedFile(exe))){
                string signer=cert.GetNameInfo(X509NameType.SimpleName,false);
                if(!System.Text.RegularExpressions.Regex.IsMatch(signer,@"^OpenAI(?:[ ,.]|$)",System.Text.RegularExpressions.RegexOptions.CultureInvariant))throw new HostFailure(12);
            }
        }finally{data.StateAction=2;WinVerifyTrust(new IntPtr(-1),ref action,ref data);Marshal.DestroyStructure(pf,typeof(TrustFile));Marshal.FreeHGlobal(pf);}
    }
    internal static long Creation(IntPtr handle) { FileTime a,b,c,d;if(!GetProcessTimes(handle,out a,out b,out c,out d))throw new HostFailure(13);return a.Value; }
    internal static string Quote(string text) { if(text.IndexOf('"')>=0||text.IndexOf('\0')>=0)throw new HostFailure(14);return "\""+text+"\""; }
    internal static string PipeHandle(IntPtr handle) { return unchecked((ulong)handle.ToInt64()).ToString(System.Globalization.CultureInfo.InvariantCulture); }
    internal static void Close(ref IntPtr handle) { if(handle!=IntPtr.Zero&&handle!=new IntPtr(-1)){CloseHandle(handle);handle=IntPtr.Zero;} }
}
internal static class Files {
    internal static string Regular(string file) {
        string full=Path.GetFullPath(file);if(!Path.IsPathRooted(file)||full.StartsWith(@"\\",StringComparison.Ordinal))throw new HostFailure(15);
        if(!File.Exists(full))throw new HostFailure(15);
        string cursor=full;while(!String.IsNullOrEmpty(cursor)) { if((File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)throw new HostFailure(15);cursor=Path.GetDirectoryName(cursor); }
        return full;
    }
    // Keep transient verification buffers below the large-object threshold.
    // The complete file is still hashed; trust checks and sharing are unchanged.
    internal static string Hash(string file) { using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read,64*1024,FileOptions.SequentialScan))using(var sha=SHA256.Create()){return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();} }
    internal static string HashText(string text) { using(var sha=SHA256.Create()){return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();} }
}
internal sealed class OwnedClient : IDisposable {
    internal IntPtr ProcessHandle; internal int Pid; internal long Created; internal readonly FileStream Read,Write;
    readonly object visibilityGate=new object();
    readonly string executable;
    AutoResetEvent visibilityWake;
    bool visibilityStopped,disposed;
    IntPtr markedWindow;
    IntPtr launcherParent;
    string marker;
    [StructLayout(LayoutKind.Sequential)] struct WindowRect {internal int Left,Top,Right,Bottom;}
    delegate bool WindowEnum(IntPtr window,IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowEnum callback,IntPtr state);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int capacity);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out WindowRect rect);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int GetCloaked(IntPtr window,int attribute,out int cloaked,int size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SetProp(IntPtr window,string name,IntPtr value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window,string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr window,string name);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window,int command);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder name,ref uint size);
    [DllImport("kernel32.dll")] static extern uint GetProcessId(IntPtr process);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ProcessEntry {
        internal uint Size,Usage,Pid;internal UIntPtr Heap;internal uint Module,Threads,ParentPid;internal int Priority;internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] internal string Exe;
    }
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32First(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32Next(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    private OwnedClient(FileStream read,FileStream write,string exe) {Read=read;Write=write;executable=Path.GetFullPath(exe);}
    static string InitialMarker(int pid,long created){return "CodexDragon.InitialHidden.v25."+pid.ToString(System.Globalization.CultureInfo.InvariantCulture)+"."+created.ToString(System.Globalization.CultureInfo.InvariantCulture);}
    static IntPtr CaptureLauncherParent(){
        IntPtr snapshot=IntPtr.Zero,parent=IntPtr.Zero;
        try{using(var own=Process.GetCurrentProcess()){
            snapshot=CreateToolhelp32Snapshot(2,0);if(snapshot==IntPtr.Zero||snapshot==new IntPtr(-1))return IntPtr.Zero;
            var entry=new ProcessEntry{Size=(uint)Marshal.SizeOf(typeof(ProcessEntry))};uint parentPid=0;
            if(Process32First(snapshot,ref entry))do{if(entry.Pid==(uint)own.Id){parentPid=entry.ParentPid;break;}}while(Process32Next(snapshot,ref entry));
            if(parentPid==0)return IntPtr.Zero;parent=OpenProcess(0x00101000,false,parentPid);if(parent==IntPtr.Zero)return IntPtr.Zero;
            uint size=1024;var name=new StringBuilder((int)size);string expected=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Start-Codex-Dragon.exe");
            if(Native.WaitForSingleObject(parent,0)!=258||Native.Creation(parent)>Native.Creation(own.Handle)||!QueryFullProcessImageName(parent,0,name,ref size)||!String.Equals(name.ToString(),expected,StringComparison.OrdinalIgnoreCase))return IntPtr.Zero;
            IntPtr retained=parent;parent=IntPtr.Zero;return retained;
        }}catch{return IntPtr.Zero;}finally{Native.Close(ref parent);Native.Close(ref snapshot);}
    }
    bool OwnLiveProcess(){return ProcessHandle!=IntPtr.Zero&&Pid>0&&Native.WaitForSingleObject(ProcessHandle,0)==258&&GetProcessId(ProcessHandle)==(uint)Pid;}
    bool SameProcess(){
        try{
        if(!OwnLiveProcess())return false;
        long actualCreated=Native.Creation(ProcessHandle);
        if(Created<=0||actualCreated!=Created)return false;
        uint capacity=(uint)Math.Max(1024,executable.Length+1);var path=new StringBuilder((int)capacity);
        return QueryFullProcessImageName(ProcessHandle,0,path,ref capacity)&&String.Equals(path.ToString(),executable,StringComparison.OrdinalIgnoreCase);
        }catch{return false;}
    }
    bool MainWindow(IntPtr window){
        uint pid;WindowRect rect;int cloaked;
        if(window==IntPtr.Zero||!IsWindow(window)||GetWindowThreadProcessId(window,out pid)==0||pid!=(uint)Pid||GetWindow(window,4)!=IntPtr.Zero||
            (GetWindowLongPtr(window,-16).ToInt64()&0x40000000L)!=0||(GetWindowLongPtr(window,-20).ToInt64()&0x80L)!=0)return false;
        var type=new StringBuilder(64);if(GetClassName(window,type,type.Capacity)==0||type.ToString()!="Chrome_WidgetWin_1")return false;
        if(IsWindowVisible(window)&&IsIconic(window))return true;
        return GetCloaked(window,14,out cloaked,4)==0&&cloaked==0&&GetWindowRect(window,out rect)&&rect.Right-rect.Left>=128&&rect.Bottom-rect.Top>=128;
    }
    void ClearMarker(){if(markedWindow!=IntPtr.Zero&&marker!=null){uint pid;if(IsWindow(markedWindow)&&GetWindowThreadProcessId(markedWindow,out pid)!=0&&pid==(uint)Pid&&GetProp(markedWindow,marker)==new IntPtr(1))RemoveProp(markedWindow,marker);}markedWindow=IntPtr.Zero;}
    // Install the recovery worker BEFORE CreateProcess. If its construction or
    // start fails, use an ordinary visible launch instead of creating an ownerless
    // hidden client. This worker is separate from renderer/CDP/media failures.
    bool BeginInitialVisibility(Func<int> request,Action<int,long> diagnostic){
        try{
            visibilityWake=new AutoResetEvent(false);
            launcherParent=CaptureLauncherParent();
            var thread=new Thread(()=>InitialVisibilityLoop(request,diagnostic),262144){IsBackground=true,Name="Initial Codex visibility"};
            thread.Start();return true;
        }catch{visibilityStopped=true;if(visibilityWake!=null)visibilityWake.Dispose();visibilityWake=null;Native.Close(ref launcherParent);return false;}
    }
    void InitialVisibilityLoop(Func<int> request,Action<int,long> diagnostic){
        var clock=Stopwatch.StartNew();int release=0,lastReport=-1;
        try{for(;;){
            bool done=false;
            try{lock(visibilityGate){
                if(disposed)break;
                if(ProcessHandle!=IntPtr.Zero){
                    if(Native.WaitForSingleObject(ProcessHandle,0)==0)break;
                    if(release==0){try{release=request==null?0:request();}catch{release=4;}if(release==0&&Created<=0)release=5;if(release==0&&launcherParent!=IntPtr.Zero&&Native.WaitForSingleObject(launcherParent,0)==0)release=6;if(release==0&&clock.ElapsedMilliseconds>=90000)release=3;}
                    if(OwnLiveProcess()){
                        uint markedPid;
                        if(markedWindow!=IntPtr.Zero&&IsWindow(markedWindow)&&GetWindowThreadProcessId(markedWindow,out markedPid)!=0&&markedPid==(uint)Pid&&GetProp(markedWindow,marker)!=new IntPtr(1)){
                            // The guarded native placement explicitly accepted
                            // the first show. Do not depend on sampling a very
                            // short visible interval before a user hides it.
                            markedWindow=IntPtr.Zero;done=true;
                        }
                        if(!done){
                        IntPtr candidate=IntPtr.Zero;int count=0;bool anyVisible=false;
                        EnumWindows((window,unused)=>{if(MainWindow(window)){if(IsWindowVisible(window))anyVisible=true;else{count++;candidate=window;}}return true;},IntPtr.Zero);
                        // Any visible main (including a minimized one) means our
                        // initial hide no longer owns presentation. Never pull a
                        // later user hide/minimize back onto the desktop.
                        if(anyVisible){ClearMarker();done=true;}
                        else if(count==1&&OwnLiveProcess()&&MainWindow(candidate)&&!IsWindowVisible(candidate)){
                            if(markedWindow!=candidate&&SameProcess()){ClearMarker();marker=InitialMarker(Pid,Created);if(SetProp(candidate,marker,new IntPtr(1))&&GetProp(candidate,marker)==new IntPtr(1))markedWindow=candidate;}
                            // For fail-open only, the still-live retained
                            // CreateProcess handle proves the PID generation
                            // even if optional image/creation metadata failed.
                            // One successfully queued ordinary show transfers
                            // ownership back to the app; never queue it again.
                            if(release!=0&&OwnLiveProcess()&&MainWindow(candidate)&&!IsWindowVisible(candidate)&&ShowWindowAsync(candidate,4)){ClearMarker();done=true;}
                        }
                        }
                    }
                }
            }}catch{if(release==0)release=4;}
            if(diagnostic!=null&&release!=lastReport&&release!=0){lastReport=release;try{diagnostic(release,clock.ElapsedMilliseconds);}catch{}}
            if(done)break;
            // A missing/ambiguous window after the startup deadline keeps only
            // a low-frequency release obligation, never an infinite film wait.
            try{visibilityWake.WaitOne(clock.ElapsedMilliseconds>=90000?1000:250);}
            catch{lock(visibilityGate){if(disposed)break;}if(release==0)release=4;Thread.Sleep(1000);}
        }}
        finally{lock(visibilityGate){try{if(OwnLiveProcess())ClearMarker();}catch{}Native.Close(ref launcherParent);visibilityStopped=true;}try{visibilityWake.Dispose();}catch{}}
    }
    internal static OwnedClient Start(string exe,IDictionary<string,string> environment,string isolatedProfile,Action<int,long> created,Func<int> releaseRequested=null,Action<int,long> initialDiagnostic=null) {
        IntPtr childRead=IntPtr.Zero,parentWrite=IntPtr.Zero,parentRead=IntPtr.Zero,childWrite=IntPtr.Zero,list=IntPtr.Zero,handles=IntPtr.Zero,env=IntPtr.Zero;
        var sa=new Native.SecurityAttributes{Size=Marshal.SizeOf(typeof(Native.SecurityAttributes)),Inherit=true};
        Native.ProcessInformation pi=new Native.ProcessInformation();bool transferred=false,attributesInitialized=false;OwnedClient client=null;
        try {
            if(!Native.CreatePipe(out childRead,out parentWrite,ref sa,65536)||!Native.CreatePipe(out parentRead,out childWrite,ref sa,65536))throw new HostFailure(20);
            if(!Native.SetHandleInformation(parentWrite,1,0)||!Native.SetHandleInformation(parentRead,1,0))throw new HostFailure(20);
            IntPtr size=IntPtr.Zero;Native.InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref size);list=Marshal.AllocHGlobal(size);
            if(!Native.InitializeProcThreadAttributeList(list,1,0,ref size))throw new HostFailure(20);attributesInitialized=true;
            handles=Marshal.AllocHGlobal(IntPtr.Size*2);Marshal.WriteIntPtr(handles,0,childRead);Marshal.WriteIntPtr(handles,IntPtr.Size,childWrite);
            if(!Native.UpdateProcThreadAttribute(list,0,new IntPtr(0x20002),handles,new IntPtr(IntPtr.Size*2),IntPtr.Zero,IntPtr.Zero))throw new HostFailure(20);
            var block=new StringBuilder();foreach(var item in new SortedDictionary<string,string>(environment,StringComparer.OrdinalIgnoreCase))block.Append(item.Key).Append('=').Append(item.Value).Append('\0');block.Append('\0');
            env=Marshal.StringToHGlobalUni(block.ToString());
            var startup=new Native.StartupInfoEx();startup.Info.Size=Marshal.SizeOf(typeof(Native.StartupInfoEx));startup.Attributes=list;
            string args=Native.Quote(exe)+" --remote-debugging-pipe --remote-debugging-io-pipes="+Native.PipeHandle(childRead)+","+Native.PipeHandle(childWrite);
            if(isolatedProfile!=null)args+=" --user-data-dir="+Native.Quote(isolatedProfile);
            var read=new FileStream(new SafeFileHandle(parentRead,true),FileAccess.Read,4096,false);parentRead=IntPtr.Zero;
            // A one-byte managed buffer prevents short NUL-framed CDP requests
            // remaining buffered while the caller is already awaiting a reply.
            FileStream write=null;try{write=new FileStream(new SafeFileHandle(parentWrite,true),FileAccess.Write,1,false);parentWrite=IntPtr.Zero;}catch{read.Dispose();throw;}
            client=new OwnedClient(read,write,exe);
            if(client.BeginInitialVisibility(releaseRequested,initialDiagnostic)){startup.Info.Flags=1;startup.Info.Show=0;}
            // No Job creation, breakaway flag, shell, TCP port, or inherited
            // miscellaneous handle. Child endpoints are the only handle list.
            if(!Native.CreateProcessW(exe,new StringBuilder(args),IntPtr.Zero,IntPtr.Zero,true,0x00080000|0x00000400,env,Path.GetDirectoryName(exe),ref startup,out pi))throw new HostFailure(21);
            lock(client.visibilityGate){client.ProcessHandle=pi.Process;client.Pid=(int)pi.Pid;transferred=true;
                try{client.Created=Native.Creation(pi.Process);}catch{client.Created=0;}
                if(!client.visibilityStopped&&client.visibilityWake!=null)try{client.visibilityWake.Set();}catch{}
            }
            // Sticky STARTED is reported immediately after CreateProcess,
            // before CDP, media IO, renderer work, or optional metadata writes.
            // Ownership must still reach Main even if optional state reporting
            // fails. Main repeats this idempotent commit before any CDP work.
            try{created(client.Pid,client.Created);}catch{}
            return client;
        }finally{
            Native.Close(ref childRead);Native.Close(ref childWrite);Native.Close(ref parentRead);Native.Close(ref parentWrite);Native.Close(ref pi.Thread);
            if(!transferred){if(client!=null)client.Dispose();Native.Close(ref pi.Process);}
            if(list!=IntPtr.Zero){if(attributesInitialized)Native.DeleteProcThreadAttributeList(list);Marshal.FreeHGlobal(list);}if(handles!=IntPtr.Zero)Marshal.FreeHGlobal(handles);if(env!=IntPtr.Zero)Marshal.FreeHGlobal(env);
        }
    }
    internal bool HasExited { get { return Native.WaitForSingleObject(ProcessHandle,0)==0; } }
    internal void Wait() {
        if(Native.WaitForSingleObject(ProcessHandle,Native.INFINITE)!=0){
            // A native wait error is not evidence of process exit. Preserve
            // pipe ownership rather than triggering Electron Quit through EOF.
            using(var hold=new System.Threading.ManualResetEvent(false))hold.WaitOne();
        }
    }
    public void Dispose() {lock(visibilityGate){if(disposed)return;disposed=true;if(!visibilityStopped&&visibilityWake!=null)try{visibilityWake.Set();}catch{}try{if(OwnLiveProcess())ClearMarker();}catch{}Read.Dispose();Write.Dispose();Native.Close(ref ProcessHandle);}}
}
}
