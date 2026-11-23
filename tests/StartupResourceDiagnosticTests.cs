using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace DragonLauncher
{
    internal static class StartupResourceDiagnosticTests
    {
        static int passed;
        static Type diagnosticType;
        static readonly BindingFlags AnyInstance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);passed++;}
        static object NewDiagnostics(){return Activator.CreateInstance(diagnosticType,true);}
        static string Record(object value,long now,double cpu,long bytes,long available,long total,long commitAvailable,long commit,bool low)
        {return (string)diagnosticType.GetMethod("Record",AnyInstance).Invoke(value,new object[]{now,cpu,bytes,available,total,commitAvailable,commit,4,low});}
        static Dictionary<string,long> Parse(string text)
        {
            Check(text!=null&&text.Length<512,"Bounded diagnostic line");
            Check(Regex.IsMatch(text,@"\AStartup resource sample; elapsedMs=\d+; intervalMs=\d+; cpuPermille=-?\d+; privateBytes=-?\d+; availablePhysicalBytes=-?\d+; totalPhysicalBytes=-?\d+; availableCommitBytes=-?\d+; totalCommitBytes=-?\d+; logicalProcessors=\d+; lowMemory=[01]\z"),"Fixed numeric-only diagnostic fields");
            var result=new Dictionary<string,long>();foreach(Match item in Regex.Matches(text,@"(?<name>[A-Za-z]+)=(?<value>-?\d+)"))result[item.Groups["name"].Value]=long.Parse(item.Groups["value"].Value,CultureInfo.InvariantCulture);return result;
        }
        static StartupResourceBreaker Breaker(Action<string> trip,Action<string> output)
        {
            var ctor=typeof(StartupResourceBreaker).GetConstructor(AnyInstance,null,new[]{typeof(Action<string>),typeof(Action<string>)},null);
            Check(ctor!=null,"Background sampler diagnostic sink available");return (StartupResourceBreaker)ctor.Invoke(new object[]{trip,output});
        }
        static void TestPolicy()
        {
            object value=NewDiagnostics();var first=Parse(Record(value,0,10,100,200,300,400,500,false));
            Check(first["cpuPermille"]==-1&&first["intervalMs"]==0,"First CPU observation remains unknown");
            Check(Record(value,1999,11,100,200,300,400,500,false)==null,"No report earlier than two seconds");
            var second=Parse(Record(value,2000,14,101,201,301,401,501,true));
            Check(second["cpuPermille"]==500&&second["intervalMs"]==2000,"CPU delta normalized across logical processors");
            Check(second["lowMemory"]==1&&second["privateBytes"]==101&&second["availablePhysicalBytes"]==201&&second["totalPhysicalBytes"]==301,"Sample preserves observed counters");
            Check(Record(value,1000,15,0,0,0,0,0,false)==null,"Clock reversal cannot burst diagnostics");
            var missing=Parse(Record(value,4000,double.NaN,-1,-1,-1,-1,-1,false));
            Check(missing["cpuPermille"]==-1&&missing["availablePhysicalBytes"]==-1,"Unknown counters retained as minus one");
            var resumed=Parse(Record(value,6000,18,100,200,300,400,500,false));Check(resumed["cpuPermille"]==-1,"Invalid CPU baseline never becomes a fabricated rate");
            var reset=Parse(Record(value,8000,1,100,200,300,400,500,false));Check(reset["cpuPermille"]==-1,"CPU reset is unknown");
            var ceiling=Parse(Record(value,10000,100,100,200,300,400,500,false));Check(ceiling["cpuPermille"]==1000,"CPU rounding limited to per-mille range");
            var bounded=Parse(Record(NewDiagnostics(),0,0,long.MaxValue,long.MaxValue,long.MaxValue,long.MaxValue,long.MaxValue,false));Check(bounded["privateBytes"]==-1&&bounded["totalPhysicalBytes"]==-1&&bounded["totalCommitBytes"]==-1,"Oversized memory counters are unknown");
            value=NewDiagnostics();int count=0;for(int i=0;i<100;i++)if(Record(value,i*2000L,i,100,200,300,400,500,false)!=null)count++;
            Check(count==64,"Lifetime record count limited to64");
            var oldCulture=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=new CultureInfo("fr-FR");Parse(Record(NewDiagnostics(),0,1,100,200,300,400,500,false));}finally{CultureInfo.CurrentCulture=oldCulture;}
        }
        static void TestBackgroundSampler()
        {
            var lines=new List<string>();var threads=new List<int>();int current=Thread.CurrentThread.ManagedThreadId;int trips=0;
            using(var ready=new AutoResetEvent(false)){
                var breaker=Breaker(delegate{Interlocked.Increment(ref trips);},delegate(string value){lock(lines){lines.Add(value);threads.Add(Thread.CurrentThread.ManagedThreadId);}ready.Set();});
                breaker.Start();Check(ready.WaitOne(1500),"Existing background sampler emits first observation");Check(ready.WaitOne(3500),"Existing background sampler emits bounded second observation");
                breaker.Dispose();int count;lock(lines){count=lines.Count;Check(count==2,"Only two samples within test interval");var first=Parse(lines[0]);var second=Parse(lines[1]);Check(second["elapsedMs"]-first["elapsedMs"]>=2000,"Actual timer obeys two-second output bound");Check(first["totalPhysicalBytes"]>0&&first["availablePhysicalBytes"]>=0,"Real memory counters are observed");foreach(int thread in threads)Check(thread!=current,"Log output is off caller/UI thread");}
                Thread.Sleep(600);lock(lines)Check(count==lines.Count,"Disposed sampler emits no subsequent records");Check(trips==0,"Healthy diagnostics do not trip resource protection");
            }
            int attempts=0;
            using(var breaker=Breaker(delegate{throw new Exception("Unexpected protection trip");},delegate{attempts++;throw new System.IO.IOException("synthetic output failure");})){
                typeof(StartupResourceBreaker).GetMethod("Sample",AnyInstance).Invoke(breaker,new object[]{null});
                Check(attempts==1,"Diagnostic output failure is contained");
                var health=(StartupSamplingHealth)typeof(StartupResourceBreaker).GetField("samplingHealth",AnyInstance).GetValue(breaker);
                Check(!health.Tripped,"Output failure does not change sampling health");
            }
            var policy=new StartupResourcePolicy(4);policy.Evaluate(0,0,0,false);policy.Evaluate(0.5,0,StartupResourcePolicy.PrivateLimit,false);Check(policy.Evaluate(1,0,StartupResourcePolicy.PrivateLimit,false)&&policy.Reason=="startup-private-memory-limit","Original memory breaker threshold preserved");
            policy=new StartupResourcePolicy(4);policy.Evaluate(0,0,0,false);for(int i=1;i<=11;i++)Check(!policy.Evaluate(i*0.5,i*1.2,0,false),"Original CPU hold interval retained");Check(policy.Evaluate(6,14.4,0,false)&&policy.Reason=="startup-sustained-cpu-limit","Original six-second CPU threshold preserved");
        }
        static int Main()
        {
            try{diagnosticType=typeof(StartupResourceBreaker).Assembly.GetType("DragonLauncher.StartupResourceDiagnostics");Check(diagnosticType!=null,"Missing bounded startup resource diagnostics");TestPolicy();TestBackgroundSampler();Console.WriteLine("PASS "+passed+" assertions; owned synthetic test process sampled; no application entry invoked.");return 0;}
            catch(Exception error){Console.WriteLine("FAIL "+error.GetBaseException().Message);return 1;}
        }
    }
}
