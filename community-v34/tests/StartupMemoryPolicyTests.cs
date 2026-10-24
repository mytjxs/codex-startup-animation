using System;
using System.IO;

namespace DragonLauncher {
    internal static class StartupMemoryPolicyTests {
        const ulong MiB=1024UL*1024;
        static int checks;
        static void Check(bool value,string description){if(!value)throw new Exception(description);checks++;}
        static bool Pressure(ulong physical,ulong totalCommit,ulong availableCommit,int notificationLow,bool observed){
#if LEGACY_PHYSICAL_POLICY
            // Frozen r8 LowMemory's exact snapshot-to-policy adapter. Native
            // counters are injected: no machine pressure is created by tests.
            if(notificationLow==1)return true;
            if(observed)return physical<1024*MiB||availableCommit<512*MiB;
            if(notificationLow==0)return false;
            throw new IOException("startup-memory-observation-unavailable");
#else
            // Notification and physical headroom are diagnostic evidence only.
            // The production sampler uses this same commit-only classifier.
            return StartupMemoryPolicy.IsCriticalCommit(observed,totalCommit,availableCommit);
#endif
        }
        static void ReportScenario(){
            // r8 final full sample was 1132.1 MiB physical / 12104 MiB
            // available commit. Inject the subsequent low-physical indication.
            bool low=Pressure(1132*MiB,30648*MiB,12104*MiB,1,true);
            var p=new StartupResourcePolicy(20);bool trip=false;
            for(int i=0;i<=4;i++)trip=p.Evaluate(i*0.5,i*0.59,986*1024L*1024,low)||trip;
            Console.WriteLine("report-scenario: notificationLow=1 physicalMiB=1132 availableCommitMiB=12104 trip="+trip+" reason="+(p.Reason??"none"));
            Check(!trip&&p.Reason==null,"Physical pressure with ample commit must not cancel startup");
        }
        static void PolicyCases(){
            Check(!Pressure(1*MiB,32768*MiB,12000*MiB,1,true),"Very low physical headroom alone is not an OOM diagnosis");
            Check(!Pressure(500*MiB,32768*MiB,12000*MiB,0,true),"Removed one-GiB physical threshold does not overrule healthy commit");
            Check(!Pressure(0,32768*MiB,512*MiB,-1,true),"Exactly512MiB commit preserves original strict lower bound");
            Check(Pressure(8192*MiB,32768*MiB,512*MiB-1,0,true),"Critical commit protects even with ample physical memory");
            Check(Pressure(8192*MiB,32768*MiB,0,-1,true),"Zero commit headroom is critical");
            foreach(var missing in new[]{new ulong[]{0,0},new ulong[]{100,101},new ulong[]{ulong.MaxValue,ulong.MaxValue}}){bool rejected=false;try{Pressure(1024*MiB,missing[0],missing[1],1,true);}catch(IOException){rejected=true;}Check(rejected,"Invalid commit counters are unknown, never fabricated healthy");}
            for(int signal=-1;signal<=1;signal++){bool rejected=false;try{Pressure(1024*MiB,32768*MiB,12000*MiB,signal,false);}catch(IOException){rejected=true;}Check(rejected,"Missing commit counters cannot use physical notification as substitute");}
            var critical=new StartupResourcePolicy(4);critical.Evaluate(0,0,0,true);
            Check(!critical.Evaluate(.5,0,0,true),"One critical sample alone does not trip");
            Check(critical.Evaluate(1,0,0,true)&&critical.Reason=="system-commit-pressure","Existing one-second hold applies to critical commit");
            var transient=new StartupResourcePolicy(4);transient.Evaluate(0,0,0,false);transient.Evaluate(.5,0,0,true);Check(!transient.Evaluate(1,0,0,false)&&transient.Reason==null,"Transient critical commit resets after recovery");
            var own=new StartupResourcePolicy(4);own.Evaluate(0,0,0,false);own.Evaluate(.5,0,StartupResourcePolicy.PrivateLimit,false);Check(own.Evaluate(1,0,StartupResourcePolicy.PrivateLimit,false)&&own.Reason=="startup-private-memory-limit","Own1536MiB sustained limit is unchanged");
            var cpu=new StartupResourcePolicy(4);cpu.Evaluate(0,0,0,false);for(int i=1;i<12;i++)Check(!cpu.Evaluate(i*.5,i*1.2,0,false),"CPU hold remains six seconds");Check(cpu.Evaluate(6,14.4,0,false)&&cpu.Reason=="startup-sustained-cpu-limit","Sustained CPU safety limit remains unchanged");
            var health=new StartupSamplingHealth();Check(!health.Record(false)&&!health.Record(false)&&health.Record(false)&&health.Tripped,"Three unknown samples retain existing protective stop");
        }
        static int Main(){try{ReportScenario();PolicyCases();Console.WriteLine("PASS "+checks+" assertions; synthetic counters only; no pressure allocation or app launch.");return 0;}catch(Exception e){Console.WriteLine("FAIL: "+e.Message);return 1;}}
    }
}
