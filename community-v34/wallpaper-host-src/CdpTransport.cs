using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Diagnostics;

namespace CodexDragonWallpaper {
internal static class Deadline {
    internal static int Remaining(int budget,long elapsed){return elapsed>=budget?0:elapsed<0?0:budget-(int)elapsed;}
}
internal static class Json {
    internal static JavaScriptSerializer Serializer() {return new JavaScriptSerializer{MaxJsonLength=1048576,RecursionLimit=40};}
    internal static Dictionary<string,object> Map(object value){var m=value as Dictionary<string,object>;if(m==null)throw new HostFailure(30);return m;}
    internal static object Get(Dictionary<string,object> m,string key){object value;return m.TryGetValue(key,out value)?value:null;}
    internal static string Text(Dictionary<string,object> m,string key){return Get(m,key) as string;}
    internal static bool Bool(Dictionary<string,object> m,string key){object v=Get(m,key);return v is bool&&(bool)v;}
    internal static long Number(Dictionary<string,object> m,string key){object v=Get(m,key);if(v is int)return (int)v;if(v is long)return (long)v;throw new HostFailure(30);}
    internal static Dictionary<string,object> Obj(params object[] pairs){var m=new Dictionary<string,object>();for(int i=0;i<pairs.Length;i+=2)m.Add((string)pairs[i],pairs[i+1]);return m;}
}
internal sealed class CdpTransport {
    // In-process rendezvous only. Zero spin avoids burning CPU during pipe IO;
    // never access WaitHandle, which would allocate an underlying kernel event.
    sealed class Pending { internal readonly ManualResetEventSlim Done=new ManualResetEventSlim(false,0);internal object Result;internal int Error;internal string Session; }
    readonly OwnedClient client;readonly bool isolated;readonly object gate=new object(),writeGate=new object();
    readonly Dictionary<int,Pending> pending=new Dictionary<int,Pending>();int nextId;int fault;volatile bool ended;
    internal event Action<string,string> Signal;
    internal bool Faulted {get{return Interlocked.CompareExchange(ref fault,0,0)!=0||ended;}}
    static readonly HashSet<string> allowed=new HashSet<string>{"Target.getTargets","Target.attachToTarget","Target.detachFromTarget","Runtime.evaluate","Runtime.addBinding","Runtime.removeBinding","DOM.getDocument","DOM.querySelector","DOM.describeNode","DOM.setFileInputFiles"};
    internal CdpTransport(OwnedClient owned,bool test){client=owned;isolated=test;var thread=new Thread(ReadLoop){IsBackground=true,Name="Wallpaper CDP receive"};thread.Start();}
    void Notify(string kind,string session){var handler=Signal;if(handler!=null)try{handler(kind,session);}catch{}}
    void Fail(int code){bool first=Interlocked.CompareExchange(ref fault,code,0)==0;lock(gate){foreach(var request in pending.Values){request.Error=code;request.Done.Set();}}if(first)Notify("transport-fault",null);}
    void Frame(byte[] buffer,int count){
        try {var m=Json.Map(Json.Serializer().DeserializeObject(new UTF8Encoding(false,true).GetString(buffer,0,count)));
            object idObject=Json.Get(m,"id");if(!(idObject is int)){
                string method=Json.Text(m,"method"),eventSession=Json.Text(m,"sessionId");var parameters=Json.Get(m,"params") as Dictionary<string,object>;
                if(parameters!=null&&method=="Runtime.bindingCalled"&&Json.Text(parameters,"name")=="__codexDragonNotifyV2"&&Json.Text(parameters,"payload")=="state")Notify("renderer-state",eventSession);
                else if(parameters!=null&&method=="Target.detachedFromTarget")Notify("target-lost",Json.Text(parameters,"sessionId"));
                else if(method=="Inspector.targetCrashed"||method=="Inspector.detached")Notify("target-lost",eventSession);
                return;
            }int id=(int)idObject;Pending request;
            lock(gate){if(!pending.TryGetValue(id,out request))return;
                string session=Json.Text(m,"sessionId");if(session!=request.Session){request.Error=31;request.Done.Set();return;}
                if(Json.Get(m,"error")!=null)request.Error=32;else request.Result=Json.Get(m,"result");request.Done.Set();
            }
        }catch{Fail(33);} // No payload, exceptionDetails, URL or text logging.
    }
    void ReadLoop(){
        const int initialFrame=8192,maxFrame=1048576,retainedFrame=65536;
        var chunk=new byte[8192];var frame=new byte[initialFrame];int used=0;
        try {for(;;){int n=client.Read.Read(chunk,0,chunk.Length);if(n==0)break;
            // A fault is terminal for commands, but the owned child's pipe must
            // stay open and be drained until process exit. Do not deserialize
            // its later events or keep a large frame alive after terminal failure.
            if(fault!=0){used=0;if(frame.Length>initialFrame)frame=new byte[initialFrame];continue;}
            int offset=0;while(offset<n&&fault==0){
                int zero=Array.IndexOf(chunk,(byte)0,offset,n-offset);
                int length=(zero<0?n:zero)-offset;
                if(length>maxFrame-used){Fail(34);used=0;if(frame.Length>initialFrame)frame=new byte[initialFrame];break;}
                int required=used+length;
                if(required>frame.Length){int capacity=frame.Length;while(capacity<required)capacity=Math.Min(maxFrame,capacity*2);Array.Resize(ref frame,capacity);}
                Buffer.BlockCopy(chunk,offset,frame,used,length);used=required;
                if(zero<0)break;
                if(used>0)Frame(frame,used);else Fail(33);used=0;offset=zero+1;
                // Keep ordinary <=64 KiB bursts to avoid reallocating per reply.
                // Rare larger replies may grow to 1 MiB but do not pin that LOH
                // allocation throughout hours of otherwise idle wallpaper use.
                if(frame.Length>retainedFrame)frame=new byte[initialFrame];
            }
        }}catch{Fail(35);}finally{ended=true;Fail(35);}
        // Failure stops commands but does NOT close handles. Main keeps them
        // alive until the official process exits; a bad frame is drained above.
    }
    internal Dictionary<string,object> Send(string method,Dictionary<string,object> parameters,string session,int timeout){
        if(!allowed.Contains(method)&&!(isolated&&method=="Browser.close"))throw new HostFailure(36);
        if(fault!=0||ended)throw new HostFailure(fault==0?35:fault);
        if(timeout<1||timeout>30000)throw new HostFailure(36);
        var deadline=Stopwatch.StartNew();int id=Interlocked.Increment(ref nextId);if(id<=0)throw new HostFailure(36);
        var packet=Json.Obj("id",id,"method",method,"params",parameters??Json.Obj());if(session!=null)packet["sessionId"]=session;
        byte[] bytes=Encoding.UTF8.GetBytes(Json.Serializer().Serialize(packet)+"\0");if(bytes.Length>1048576)throw new HostFailure(34);
        var request=new Pending{Session=session};
        lock(gate){if(pending.Count>=4){request.Done.Dispose();throw new HostFailure(38);}pending.Add(id,request);}
        bool writer=false;
        try {
            int remaining=Deadline.Remaining(timeout,deadline.ElapsedMilliseconds);if(remaining<=0||!Monitor.TryEnter(writeGate,remaining))throw new HostFailure(37);writer=true;
            try{
                if(fault!=0)throw new HostFailure(fault);
                remaining=Deadline.Remaining(timeout,deadline.ElapsedMilliseconds);if(remaining<=0)throw new HostFailure(37);
                Task written=Task.Factory.FromAsync(client.Write.BeginWrite,client.Write.EndWrite,bytes,0,bytes.Length,null);
                if(!written.Wait(remaining)){written.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);Fail(37);throw new HostFailure(37);}
            }finally{if(writer){Monitor.Exit(writeGate);writer=false;}}
            remaining=Deadline.Remaining(timeout,deadline.ElapsedMilliseconds);
            if(remaining<=0||!request.Done.Wait(remaining))throw new HostFailure(37);
            if(request.Error!=0)throw new HostFailure(request.Error);return Json.Map(request.Result);
        }finally{if(writer)Monitor.Exit(writeGate);lock(gate)pending.Remove(id);request.Done.Dispose();}
    }
}
}
