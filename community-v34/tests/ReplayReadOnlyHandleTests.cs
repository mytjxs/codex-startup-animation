using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace DragonLauncher {
internal static class ReplayReadOnlyHandleTests {
 static int checks;
 static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 static void Until(Func<bool> predicate,int timeout,string message){var clock=Stopwatch.StartNew();while(!predicate()&&clock.ElapsedMilliseconds<timeout)Thread.Sleep(10);Check(predicate(),message);}
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool SetKernelObjectSecurity(IntPtr handle,uint information,byte[] descriptor);
 [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
 [DllImport("ntdll.dll")] static extern int NtQueryObject(IntPtr handle,int information,IntPtr buffer,uint length,out uint needed);
 static uint GrantedAccess(IntPtr handle){IntPtr data=Marshal.AllocHGlobal(56);try{uint needed;if(NtQueryObject(handle,0,data,56,out needed)!=0)throw new Exception("query owned handle access");return (uint)Marshal.ReadInt32(data,4);}finally{Marshal.FreeHGlobal(data);}}
 static void WriteJson<T>(string file,T data){using(var stream=File.Create(file))new DataContractJsonSerializer(typeof(T)).WriteObject(stream,data);}
 static void CandidateCountCases(){
  const string baseline="verifiedWindows=22; visible=1; hidden=21; cloaked=0; eligible=0";
  const string extra="; minimized=0; ownedRejected=1; childRejected=0; toolRejected=0; cloakedRejected=0; rectUnavailable=0; sizeRejected=0; stateChanged=0";
  Check(ExistingReplayTarget.CandidateCountsValid(baseline),"legacy five-counter report remains valid");
  Check(ExistingReplayTarget.CandidateCountsValid(baseline+extra),"complete extended candidate counts pass allowlist");
  Check(ExistingReplayTarget.CandidateCountsValid((baseline+extra).Replace("=0","=99999").Replace("=1;","=99999;")),"counter upper bound accepted");
  foreach(string value in new[]{null,"",baseline+"\n",baseline+" title=PRIVATE",baseline+"; minimized=0",baseline+extra+"; stateChanged=1",baseline+extra+"\r\nPRIVATE",
   baseline.Replace("visible=1","visible=-1"),baseline.Replace("visible=1","visible=1.0"),baseline.Replace("visible=1","visible=100000"),baseline.Replace("visible=1","visible=\u0661"),
   (baseline+extra).Replace("; ownedRejected=1; childRejected=0","; childRejected=0; ownedRejected=1"),(baseline+extra).Replace("stateChanged=0","stateChanged=PRIVATE"),new string('0',513)})
   Check(!ExistingReplayTarget.CandidateCountsValid(value),"malformed private partial reordered or oversized count record refused");
 }
 static int Child(string root,string token){
  File.WriteAllText(Path.Combine(root,"child-stage"),"entered");
  using(var own=Process.GetCurrentProcess()){
   long created;BridgeNative.CreationTime(own.Handle,out created);
   var status=new BridgeStatus{Version=1,Disposition=2,HostPid=own.Id,HostCreated=created,CodexPid=own.Id,CodexCreated=created,MediaCode=4,Ready=true};
   // This ACL change applies only to this disposable test child. The parent
   // keeps its original Process.Start handle and can always reap its fixture.
   using(var identity=WindowsIdentity.GetCurrent()){
    var descriptor=new RawSecurityDescriptor("D:P(A;;0x00101000;;;"+identity.User.Value+")");
    byte[] bytes=new byte[descriptor.BinaryLength];descriptor.GetBinaryForm(bytes,0);
    if(!SetKernelObjectSecurity(GetCurrentProcess(),4,bytes)){File.WriteAllText(Path.Combine(root,"child-stage"),"acl-error-"+Marshal.GetLastWin32Error());return 10;}
   }
   WriteJson(Path.Combine(root,"wallpaper-host-session.json"),new BridgeDiscovery{Version=1,Session=token,HostPid=own.Id,HostCreated=created});
   var lifetime=Stopwatch.StartNew();
   while(lifetime.ElapsedMilliseconds<15000&&!File.Exists(Path.Combine(root,"stop"))){
    using(var pipe=new NamedPipeServerStream(BridgeActivation.PipeName(token),PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous)){
     File.WriteAllText(Path.Combine(root,"ready"),"ready");
     var waiting=pipe.BeginWaitForConnection(null,null);try{if(!waiting.AsyncWaitHandle.WaitOne(200)){continue;}pipe.EndWaitForConnection(waiting);}finally{waiting.AsyncWaitHandle.Close();}
     var input=new MemoryStream();for(int i=0;i<128;i++){int next=pipe.ReadByte();if(next<0)return 11;if(next==10)break;input.WriteByte((byte)next);}
     string command=Encoding.UTF8.GetString(input.ToArray());if(command!="status")return 12;
     if(File.Exists(Path.Combine(root,"hold"))){File.WriteAllText(Path.Combine(root,"seen"),"status");var delay=Stopwatch.StartNew();while(!File.Exists(Path.Combine(root,"release"))&&delay.ElapsedMilliseconds<2000)Thread.Sleep(10);}
     using(var data=new MemoryStream()){
      if(File.Exists(Path.Combine(root,"bad-reply")))status.HostCreated=created+1;else status.HostCreated=created;
      new DataContractJsonSerializer(typeof(BridgeStatus)).WriteObject(data,status);byte[] reply=data.ToArray();pipe.Write(reply,0,reply.Length);pipe.WriteByte(10);pipe.Flush();
     }
    }
   }
  }
  return 0;
 }
 static void Run(string root){
  string token=Guid.NewGuid().ToString("N");var logs=new List<string>();
  string executable=Assembly.GetExecutingAssembly().Location;
  using(var child=Process.Start(new ProcessStartInfo(executable,"--owned-child \""+root+"\" "+token){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){
   BridgeSession cold=null;
   try{
    Until(()=>File.Exists(Path.Combine(root,"ready")),2500,"owned restricted fixture ready");
    bool fullDenied=false;try{using(var reopened=Process.GetProcessById(child.Id)){var ignored=reopened.Handle;}}catch(Win32Exception error){fullDenied=error.NativeErrorCode==5;}
    Check(fullDenied,"fixture denies excess Process.Handle rights");
    cold=new BridgeSession(child,token,null);
    Check(cold.Status(700).Disposition==2,"cold constructor retains original Process.Start handle despite later restrictive ACL");
    var session=BridgeActivation.Discover(root,0,line=>logs.Add(line));
    Check(session!=null,"read-only discovery must succeed without ALL_ACCESS");
    try{
     Check(session.LastStatus!=null&&session.LastStatus.Disposition==2,"actual authenticated pipe status received");
     Check(session.Status(700).Disposition==2,"subsequent read-only status succeeds");
     Check(!session.HostExited,"retained original restricted host alive");
     Check(session.MatchesHost(session.LastStatus.HostPid,session.LastStatus.HostCreated),"retained identity matches authenticated status");
     Check(!session.MatchesHost(session.LastStatus.HostPid,session.LastStatus.HostCreated+1),"different generation never matches retained process");
     byte[] discovery=File.ReadAllBytes(Path.Combine(root,"wallpaper-host-session.json"));
     var original=BridgeActivation.Decode<BridgeDiscovery>(discovery,discovery.Length);original.HostCreated++;
     WriteJson(Path.Combine(root,"wallpaper-host-session.json"),original);
     Check(BridgeActivation.Discover(root,0,line=>logs.Add(line))==null,"discovery rejects mismatched process creation before IPC");
     File.WriteAllBytes(Path.Combine(root,"wallpaper-host-session.json"),discovery);
     File.WriteAllText(Path.Combine(root,"bad-reply"),"bad");bool badReply=false;
     try{session.Status(700);}catch(InvalidDataException){badReply=true;}
     Check(badReply,"authenticated server cannot change retained host generation");File.Delete(Path.Combine(root,"bad-reply"));
     Check(session.Status(700).Disposition==2,"valid subsequent status recovers after rejected reply");
     File.WriteAllText(Path.Combine(root,"wallpaper-host-config.json"),"{\"version\":0,\"packageFullName\":\"invalid\",\"executableSha256\":\""+new string('a',64)+"\"}");
     var replayLogs=new List<string>();Check(BridgeClientIdentity.Attest(new Settings(),root,session,line=>replayLogs.Add(line))==null,"restricted host still cannot authorize invalid official pin");
     Check(replayLogs.Exists(line=>line.StartsWith("replay-diagnostic operation=attest phase=4 ",StringComparison.Ordinal)&&line.Contains(" reason=9 ")),"invalid pin diagnosis identifies exact bounded stage and reason");
     Check(ExistingReplayTarget.Find(new Settings{AppId="",Exe=executable},root,line=>replayLogs.Add(line))==null,"no main window remains unavailable without activation");
     Check(replayLogs.Exists(line=>line.StartsWith("replay-native-candidates phase=1 ",StringComparison.Ordinal))&&replayLogs.Exists(line=>line.StartsWith("replay-native-candidates phase=4 ",StringComparison.Ordinal)),"both native selection attempts have fixed candidate counts");
     foreach(string line in replayLogs)if(line.StartsWith("replay-native-candidates ",StringComparison.Ordinal)){
      Check(!line.Contains("countsUnavailable=true")&&line.Contains("; minimized=")&&line.Contains("; stateChanged="),"real native availability extension survives LogCandidates allowlist");
      int start=line.IndexOf("verifiedWindows=",StringComparison.Ordinal);Check(start>=0&&ExistingReplayTarget.CandidateCountsValid(line.Substring(start)),"actual emitted native count block stays within fixed schema");
     }
     Check(replayLogs.Exists(line=>line.StartsWith("replay-diagnostic operation=find phase=4 ",StringComparison.Ordinal)&&line.Contains(" reason=1 ")),"find unavailable has a distinct reason");
     foreach(string line in replayLogs)if(line.StartsWith("replay-",StringComparison.Ordinal))Check(line.IndexOf(root,StringComparison.OrdinalIgnoreCase)<0&&!line.Contains("pid=")&&!line.Contains("session="),"new replay diagnostics exclude path process and session identity");
     Check(BridgeClientIdentity.Attest(new Settings(),root,session,delegate(string ignored){throw new InvalidOperationException("sink");})==null,"failing diagnostic sink cannot authorize invalid pin");
     var method=typeof(BridgeNative).GetMethod("OpenReadProcess",BindingFlags.Static|BindingFlags.NonPublic);
     Check(method!=null,"minimum-access query helper exists");
     using(var handle=(SafeProcessHandle)method.Invoke(null,new object[]{(uint)child.Id})){
      uint access=GrantedAccess(handle.DangerousGetHandle());Check((access&0x101000)==0x101000,"query handle includes required read and synchronize rights");
      Check((access&0x000F0FEF)==0,"query handle has no terminate VM-write create-thread ACL or owner rights");
      long created;Check(BridgeNative.CreationTime(handle.DangerousGetHandle(),out created)&&created==session.LastStatus.HostCreated,"query handle preserves exact native generation");
     }
     File.WriteAllText(Path.Combine(root,"hold"),"hold");session.Queue("status");
     Until(()=>File.Exists(Path.Combine(root,"seen")),1200,"asynchronous status reaches real owned pipe");session.Dispose();
     var disposed=typeof(BridgeSession).GetField("disposed",BindingFlags.Instance|BindingFlags.NonPublic);
     var retainedQuery=(SafeProcessHandle)typeof(BridgeSession).GetField("queriedHost",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);
     Check(!(bool)disposed.GetValue(session),"dispose retains queried handle while IPC is in flight");
     Check(!retainedQuery.IsClosed,"minimum-rights SafeHandle stays open during asynchronous IPC");
     Check(!session.HostExited,"inflight queried handle remains live after dispose requested");
     File.WriteAllText(Path.Combine(root,"release"),"release");
     Until(()=>(bool)disposed.GetValue(session),2000,"handle released after asynchronous command completes");
     Check(retainedQuery.IsClosed,"asynchronous completion closes owned SafeHandle exactly once");
     bool rejected=false;try{session.Status(200);}catch(ObjectDisposedException){rejected=true;}Check(rejected,"disposed session rejects further reads");
    }finally{session.Dispose();}
    File.Delete(Path.Combine(root,"hold"));
    using(var next=BridgeActivation.Discover(root,0,line=>logs.Add(line))){
     Check(next!=null,"discovery can reopen a new read session without mutating host");
     File.WriteAllText(Path.Combine(root,"stop"),"stop");Check(child.WaitForExit(2500),"owned fixture exits normally");
     Check(next.HostExited,"retained handle proves exact host generation exited");
    }
    Check(BridgeActivation.Discover(root,0,line=>logs.Add(line))==null,"stale discovery is rejected after host exit");
   }finally{File.WriteAllText(Path.Combine(root,"stop"),"stop");if(!child.WaitForExit(2500)){child.Kill();child.WaitForExit(1000);}if(cold!=null)cold.Dispose();}
  }
 }
 [STAThread] static int Main(string[] args){
  if(args.Length==3&&args[0]=="--owned-child"){try{return Child(args[1],args[2]);}catch(Exception error){File.WriteAllText(Path.Combine(args[1],"child-stage"),"exception-"+error.GetType().Name+"-"+(error.HResult&65535));return 13;}}
  try{CandidateCountCases();Run(args[0]);Console.WriteLine("{\"passed\":true,\"checks\":"+checks+",\"actualCodexStarted\":false,\"ownedRestrictedChildAndRealPipe\":true}");return 0;}
  catch(Exception error){Console.WriteLine("FAIL: "+error.Message);return 1;}
 }
}
}
