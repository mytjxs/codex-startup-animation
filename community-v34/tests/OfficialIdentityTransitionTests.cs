using System;
using System.Collections.Generic;
using System.IO;
using CodexDragon.Official;

internal static class OfficialIdentityTransitionTests {
    static int checks;
    static void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
    sealed class Fixture:IDisposable {
        internal readonly List<OfficialIdentityResolver.IdentityLease> Leases=new List<OfficialIdentityResolver.IdentityLease>();
        readonly string root;
        internal int Reads,Acquires;
        internal string AcquireFailure,InventoryFailure;
        internal int InventoryFailureAt,AcquireFailureAt=1;
        internal OfficialIdentityResolver.PackageRecord[][] Inventories;
        internal Fixture(string root){this.root=root;Directory.CreateDirectory(root);}
        internal OfficialIdentityResolver.PackageRecord Package(string version,string folder){return new OfficialIdentityResolver.PackageRecord{FullName="OpenAI.Codex_"+version+"_x64__2p2nqsd0c76g0",Version=new Version(version),Root=Path.Combine(root,folder),Executable=Path.Combine(root,folder,"ui","RenamedDesktop.exe")};}
        internal IList<OfficialIdentityResolver.PackageRecord> Inventory(){
            Reads++;
            if(InventoryFailure!=null&&Reads==InventoryFailureAt)throw new InvalidOperationException(InventoryFailure);
            Check(Reads<=Inventories.Length,"inventory must stay within configured bounded calls");
            return Inventories[Reads-1];
        }
        internal OfficialIdentityResolver.IdentityLease Acquire(OfficialIdentityResolver.PackageRecord package){
            Acquires++;
            foreach(var prior in Leases)Check(!prior.File.CanRead&&!prior.Manifest.CanRead,"prior rejected lease is released before reacquisition");
            if(AcquireFailure!=null&&Acquires==AcquireFailureAt)throw new InvalidOperationException(AcquireFailure);
            string path=Path.Combine(root,"lease-"+Acquires);File.WriteAllText(path,"isolated mock trust lease");
            var lease=new OfficialIdentityResolver.IdentityLease{Key=Guid.NewGuid().ToString("N"),Package=package.FullName,Executable=package.Executable,Hash=new string((char)('A'+Acquires),64),References=1,File=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read),Manifest=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)};
            Leases.Add(lease);return lease;
        }
        internal OfficialIdentitySnapshot Resolve(){return OfficialIdentityResolver.ResolveCurrent(Inventory,Acquire);}
        internal void AllReleased(){foreach(var lease in Leases)Check(!lease.File.CanRead&&!lease.Manifest.CanRead,"every rejected or disposed identity lease released");}
        public void Dispose(){foreach(var lease in Leases){lease.File.Dispose();lease.Manifest.Dispose();}}
    }
    static void Failure(Action operation,string code){try{operation();throw new Exception("expected failure was accepted: "+code);}catch(InvalidOperationException error){Check(error.Message==code,"failure category remains exact: "+code);}}
    static void Stable(string root){using(var f=new Fixture(Path.Combine(root,"stable"))){
        var old=f.Package("26.1016.1.0","old");var current=f.Package("26.2001.2.0","current");
        f.Inventories=new[]{new[]{old,current},new[]{current,old}};
        using(var identity=f.Resolve()){Check(identity.Executable==current.Executable&&identity.PackageFullName==current.FullName,"newest registration selected regardless of inventory order");Check(identity.Pid==0&&identity.Created==0,"cold identity never claims a running process");Check(f.Leases[0].File.CanRead&&f.Leases[0].Manifest.CanRead,"successful identity holds both lease files");}
        Check(f.Reads==2&&f.Acquires==1,"unchanged startup keeps original two reads and one verification");f.AllReleased();
    }}
    static void Changed(string root,bool sameVersion){using(var f=new Fixture(Path.Combine(root,sameVersion?"new-path":"new-version"))){
        var old=f.Package("26.1016.1.0","old");var current=f.Package(sameVersion?"26.1016.1.0":"26.2001.2.0","current");
        f.Inventories=new[]{new[]{old},new[]{current},new[]{current},new[]{current}};
        using(var identity=f.Resolve()){Check(identity.Executable==current.Executable&&identity.PackageFullName==current.FullName,"changed registration is fully reacquired before success");Check(identity.Sha256==new string('C',64),"returned hash belongs to newly acquired identity");Check(!f.Leases[0].File.CanRead&&f.Leases[1].File.CanRead,"only current verified lease remains held");}
        Check(f.Reads==4&&f.Acquires==2,"one change is limited to four inventories and two verifications");f.AllReleased();
    }}
    static void StillChanging(string root){using(var f=new Fixture(Path.Combine(root,"still-changing"))){
        var one=f.Package("26.1.1.0","one");var two=f.Package("26.1.2.0","two");var three=f.Package("26.1.3.0","three");
        f.Inventories=new[]{new[]{one},new[]{two},new[]{two},new[]{three}};
        Failure(()=>{using(var identity=f.Resolve()){}},"official-registration-changing");Check(f.Reads==4&&f.Acquires==2,"continuous change never retries a third time");f.AllReleased();
    }}
    static void NeverRetryTrustErrors(string root){foreach(string code in new[]{"official-signature-invalid","official-signer-mismatch","official-signature-verifier-busy","official-signature-verification-timeout","official-manifest-path-invalid","official-linked-path-rejected","official-registration-changing","unknown-runtime-error"})using(var f=new Fixture(Path.Combine(root,code))){
        var one=f.Package("26.1.1.0","one");f.Inventories=new[]{new[]{one}};f.AcquireFailure=code;
        Failure(()=>{using(var identity=f.Resolve()){}},code);Check(f.Reads==1&&f.Acquires==1,"trust/path failures are not treated as a benign inventory transition");f.AllReleased();
    }}
    static void NeverRetryInvalidInventory(string root){foreach(string code in new[]{"official-manifest-identity-invalid","official-application-ambiguous","official-manifest-path-invalid","official-package-not-registered","official-registration-query-failed"})using(var f=new Fixture(Path.Combine(root,"inventory-"+code))){
        var one=f.Package("26.1.1.0","one");f.Inventories=new[]{new[]{one},new[]{one}};f.InventoryFailure=code;f.InventoryFailureAt=2;
        Failure(()=>{using(var identity=f.Resolve()){}},code);Check(f.Reads==2&&f.Acquires==1,"malformed or unavailable inventory is not reinterpreted as a safe identity switch");f.AllReleased();
    }}
    static void ChangedThenUntrusted(string root){using(var f=new Fixture(Path.Combine(root,"changed-then-untrusted"))){
        var old=f.Package("26.1.1.0","old");var current=f.Package("26.1.2.0","new");f.Inventories=new[]{new[]{old},new[]{current},new[]{current}};f.AcquireFailure="official-signature-invalid";f.AcquireFailureAt=2;
        Failure(()=>{using(var identity=f.Resolve()){}},"official-signature-invalid");Check(f.Reads==3&&f.Acquires==2,"new version must pass its own trust check and never falls back to old version");f.AllReleased();
    }}
    static int Main(string[] args){try{
        if(args.Length!=1)return 2;string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
        Stable(root);Changed(root,false);Changed(root,true);StillChanging(root);NeverRetryTrustErrors(root);NeverRetryInvalidInventory(root);ChangedThenUntrusted(root);
        Console.WriteLine("{\"passed\":true,\"checks\":"+checks+",\"mockInventoryOnly\":true,\"mockTrustOnly\":true,\"realCodexStarted\":false,\"maximumInventoryCalls\":4,\"maximumAcquireCalls\":2}");return 0;
    }catch(Exception error){Console.WriteLine("FAIL "+error.Message);return 1;}}
}
