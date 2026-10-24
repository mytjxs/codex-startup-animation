using System;
using System.Collections.Generic;
using System.IO;
using Process = CodexProcessSessionTests.FakeProcess;

namespace CodexProcessSessionTests {
// Only this fixture owns fake processes. The production method is inserted
// verbatim by the runner; no Codex process is launched, inspected or stopped.
internal sealed class HostFailure : Exception {
    internal readonly int Code;
    internal HostFailure(int code) { Code=code; }
}
internal static class Native {
    internal static long Creation(IntPtr handle) { return handle.ToInt64()*10; }
}
internal sealed class FakeModule { internal string FileName; }
internal sealed class FakeProcess : IDisposable {
    internal static FakeProcess[] Items=new FakeProcess[0];
    internal static FakeProcess Self;
    internal static string RequestedName;
    internal int Id,SessionId,ModuleReads,DisposeCount;
    internal bool HasExited,DenyModule;
    internal string Image;
    internal IntPtr MainWindowHandle;
    internal IntPtr Handle { get { return new IntPtr(Id); } }
    internal FakeModule MainModule { get { ModuleReads++; if(DenyModule)throw new UnauthorizedAccessException(); return new FakeModule { FileName=Image }; } }
    internal static FakeProcess GetCurrentProcess() { return Self; }
    internal static FakeProcess[] GetProcessesByName(string name) { RequestedName=name; return Items; }
    public void Dispose() { DisposeCount++; }
}
internal static class Subject {
/* INSERT_EXACT_EXISTING_METHOD */
}
internal static class ProcessSessionTests {
    static int checks;
    const string Exe=@"C:\FixtureOnly\app\ChatGPT.exe";
    static void Check(bool value,string name) { if(!value)throw new Exception(name);checks++; }
    static FakeProcess Item(int id,int session,bool window) { return new FakeProcess { Id=id,SessionId=session,Image=Exe,MainWindowHandle=window?new IntPtr(id):IntPtr.Zero }; }
    static void Set(params FakeProcess[] items) { FakeProcess.Items=items;FakeProcess.Self=Item(1,7,false);FakeProcess.RequestedName=null; }
    static void Disposed() { foreach(var item in FakeProcess.Items)Check(item.DisposeCount==1,"enumerated process disposed");Check(FakeProcess.Self.DisposeCount==1,"current process disposed"); }
    static bool Existing(out int pid,out long created) { bool found=Subject.Existing(Exe,out pid,out created);Check(FakeProcess.RequestedName=="ChatGPT","name-scoped enumeration");return found; }
    public static int Main() {
        try {
            int pid;long created;
            var other=Item(20,8,true);Set(other);
            Check(!Existing(out pid,out created)&&pid==0&&created==0,"other session must not block a launch");
            Check(other.ModuleReads==0,"other session image is not inspected");Disposed();

            other=Item(21,8,true);other.DenyModule=true;Set(other);
            Check(!Existing(out pid,out created),"inaccessible other session must not become unknown local process");
            Check(other.ModuleReads==0,"other session denied module never opened");Disposed();

            var current=Item(31,7,false);other=Item(32,8,true);Set(current,other);
            Check(Existing(out pid,out created)&&pid==31&&created==310,"cross-session window cannot replace local process hint");
            Check(other.ModuleReads==0,"cross-session preferred window not inspected");Disposed();

            current=Item(41,7,true);current.Image=Exe.ToLowerInvariant();Set(current);
            Check(Existing(out pid,out created)&&pid==41,"local executable comparison remains case insensitive");Disposed();

            current=Item(51,7,false);var window=Item(52,7,true);Set(current,window);
            Check(Existing(out pid,out created)&&pid==52,"local main window remains preferred");Disposed();

            current=Item(61,7,true);current.Image=@"C:\FixtureOnly\Unrelated\ChatGPT.exe";Set(current);
            Check(!Existing(out pid,out created),"same basename with different image remains unrelated");Disposed();

            current=Item(71,7,true);current.HasExited=true;Set(current);
            Check(!Existing(out pid,out created)&&current.ModuleReads==0,"exited local processes remain ignored");Disposed();

            current=Item(81,7,true);current.DenyModule=true;Set(current);bool rejected=false;
            try { Existing(out pid,out created); }catch(HostFailure error) { rejected=error.Code==53; }
            Check(rejected,"same-session unreadable process still fails closed");Disposed();

            current=Item(91,7,true);var denied=Item(92,7,false);denied.DenyModule=true;Set(denied,current);
            Check(Existing(out pid,out created)&&pid==91,"verified local existing process remains authoritative");Disposed();

            Set();Check(!Existing(out pid,out created)&&pid==0&&created==0,"empty process set remains false");Disposed();
            Console.WriteLine("{\"passed\":true,\"checks\":"+checks+",\"scope\":\"production-method-with-synthetic-processes\"}");return 0;
        }catch(Exception error) {
            // Errors are fixed fixture labels, not raw process or user data.
            Console.WriteLine("{\"passed\":false,\"checks\":"+checks+",\"failure\":\""+error.Message.Replace("\\","\\\\").Replace("\"","\\\"")+"\"}");return 1;
        }
    }
}
}
