using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using CodexDragon.Official;
internal static class OfficialIdentityTests {
    static int checks;
    static void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;}
    static void Reject(Action action,string label){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,label);}
    static OfficialIdentityResolver.PackageRecord Package(string version,string root){string name="OpenAI.Codex_"+version+"_x64__2p2nqsd0c76g0";return new OfficialIdentityResolver.PackageRecord{FullName=name,Version=OfficialIdentityResolver.PackageVersion(name),Root=root,Executable=Path.Combine(root,"app","ChatGPT.exe")};}
    static string Manifest(string version,string executable,string extra){return "<Package><Identity Name='OpenAI.Codex' ProcessorArchitecture='x64' Version='"+version+"'/><Applications><Application Id='App' Executable='"+executable+"'/>"+extra+"</Applications></Package>";}
    static int Main(string[] args){
        try{
            if(args.Length==1&&args[0]=="--read-only-live")return Live();
            if(args.Length!=1)return 2;string root=Path.GetFullPath(args[0]);
            if(root!=Path.Combine(Path.GetDirectoryName(typeof(OfficialIdentityTests).Assembly.Location),"official-identity-fixtures"))return 3;
            Directory.CreateDirectory(root);
            var old=Package("26.1013.4894.0",Path.Combine(root,"old"));var newer=Package("26.1016.6573.0",Path.Combine(root,"new"));
            Check(OfficialIdentityResolver.PackageVersion(old.FullName).ToString()=="26.1013.4894.0","version-parsed");
            foreach(string invalid in new[]{"Other.Codex_26.1.1.0_x64__2p2nqsd0c76g0","OpenAI.Codex_26.1.1.0_arm64__2p2nqsd0c76g0","OpenAI.Codex_26.1.1.0_x64__attacker","OpenAI.Codex_999999.1.1.0_x64__2p2nqsd0c76g0","OpenAI.Codex_26.1.1_x64__2p2nqsd0c76g0"})Check(OfficialIdentityResolver.PackageVersion(invalid)==null,"invalid-package-ignored");
            Check(OfficialIdentityResolver.SelectCurrent(new[]{old,newer})==newer,"new-version-selected");
            Check(OfficialIdentityResolver.SelectCurrent(new[]{newer,old})==newer,"registration-order-independent");
            Check(OfficialIdentityResolver.SelectCurrent(new[]{old})==old,"older-only-still-supported");
            Reject(()=>OfficialIdentityResolver.SelectCurrent(new OfficialIdentityResolver.PackageRecord[0]),"no-registration-rejected");
            Reject(()=>OfficialIdentityResolver.SelectCurrent(new[]{newer,newer}),"duplicate-registration-rejected");
            OfficialIdentityResolver.ValidateInventoryCapacity(2,200,2,200);Check(true,"enumeration-buffer-capacity-matches");
            OfficialIdentityResolver.ValidateInventoryCapacity(0,0,2,200);Check(true,"enumeration-shrinking-to-empty-safe");
            Reject(()=>OfficialIdentityResolver.ValidateInventoryCapacity(3,200,2,200),"enumeration-count-growth-rejected-before-read");
            Reject(()=>OfficialIdentityResolver.ValidateInventoryCapacity(2,201,2,200),"enumeration-character-growth-rejected-before-read");
            Reject(()=>OfficialIdentityResolver.ValidateInventoryCapacity(2,1,2,200),"enumeration-inconsistent-count-rejected");
            Check(OfficialIdentityResolver.ManifestExecutable(newer.Root,"app/ChatGPT.exe")==newer.Executable,"relative-manifest-entry");
            foreach(string invalid in new[]{"../ChatGPT.exe","app/../ChatGPT.exe","C:\\evil.exe","\\\\server\\evil.exe","app\\x.exe:stream","app\\.\\ChatGPT.exe","app\\\\ChatGPT.exe","app\\ChatGPT.exe.","app\\ChatGPT.exe ","%TEMP%\\ChatGPT.exe","$variable$\\ChatGPT.exe","app\\x.dll"})Reject(()=>OfficialIdentityResolver.ManifestExecutable(newer.Root,invalid),"unsafe-relative-path");
            var process=new OfficialIdentityResolver.ProcessRecord{Pid=123,Session=4,Created=100,Package=old.FullName,Executable=old.Executable};
            Check(OfficialIdentityResolver.MatchProcess(process,new[]{newer,old},4,100)==old,"running-old-package-not-replaced-by-latest");
            Reject(()=>OfficialIdentityResolver.MatchProcess(process,new[]{newer},4,100),"old-live-registration-disappeared-fails-closed");
            Reject(()=>OfficialIdentityResolver.MatchProcess(process,new[]{newer,old},4,101),"pid-reuse-generation-rejected");
            Reject(()=>OfficialIdentityResolver.MatchProcess(process,new[]{newer,old},5,100),"other-session-rejected");
            process.Package=null;Check(OfficialIdentityResolver.MatchProcess(process,new[]{old,newer},4,100)==old,"direct-createprocess-no-package-token-supported");
            process.Package="Other.App_1.0.0.0_x64__foreign";Reject(()=>OfficialIdentityResolver.MatchProcess(process,new[]{old,newer},4,100),"foreign-nonempty-package-token-rejected");
            process.Package=old.FullName;process.Executable=Path.Combine(old.Root,"app","resources","codex.exe");Reject(()=>OfficialIdentityResolver.MatchProcess(process,new[]{old},4,100),"cli-is-not-manifest-gui-entry");
            process.Package=null;process.Executable=Path.Combine(root,"impostor","ChatGPT.exe");Reject(()=>OfficialIdentityResolver.MatchProcess(process,new[]{old},4,100),"same-name-outside-registered-path-rejected");
            Check(!OfficialIdentityResolver.IsEntryCandidate(process,new[]{old,newer}),"ordinary-unpackaged-chatgpt-coexists");
            process.Package="Other.ChatGPT_1.0.0.0_x64__other";Check(!OfficialIdentityResolver.IsEntryCandidate(process,new[]{old,newer}),"other-packaged-chatgpt-coexists");
            process.Package=old.FullName;process.Executable=old.Executable;Reject(()=>OfficialIdentityResolver.IsEntryCandidate(process,new[]{newer}),"known-family-unregistered-old-process-not-ignored");
            Check(OfficialIdentityResolver.IsEntryCandidate(process,new[]{old,newer}),"registered-old-entry-candidate");
            process.Executable=Path.Combine(old.Root,"app","resources","codex.exe");Check(!OfficialIdentityResolver.IsEntryCandidate(process,new[]{old,newer}),"registered-cli-not-gui-entry-candidate");
            var renderer=new OfficialIdentityResolver.ProcessRecord{Pid=2,Created=20,Executable=old.Executable};var main=new OfficialIdentityResolver.ProcessRecord{Pid=3,Created=30,Executable=old.Executable,Window=true};var latest=new OfficialIdentityResolver.ProcessRecord{Pid=4,Created=40,Executable=newer.Executable};
            var representatives=OfficialIdentityResolver.Representatives(new[]{renderer,main,latest});Check(representatives.Count==2&&representatives.Contains(main)&&representatives.Contains(latest),"old-new-live-groups-main-window-preferred");
            main.Window=false;Check(OfficialIdentityResolver.Representatives(new[]{main,renderer})[0]==renderer,"earliest-process-when-no-window");
            OfficialIdentityResolver.AuthorizeSignature(true,"OpenAI OpCo, LLC");Check(true,"valid-OpenAI-signer");
            Reject(()=>OfficialIdentityResolver.AuthorizeSignature(false,"OpenAI OpCo, LLC"),"publisher-name-cannot-bypass-invalid-signature");
            Reject(()=>OfficialIdentityResolver.AuthorizeSignature(true,"OpenAI Evil Company"),"prefix-impersonation-rejected");
            Reject(()=>OfficialIdentityResolver.AuthorizeSignature(true,"Other Corporation"),"other-valid-publisher-rejected");
            Directory.CreateDirectory(Path.Combine(newer.Root,"app"));File.WriteAllBytes(newer.Executable,new byte[]{1,2,3});
            string manifest=Path.Combine(newer.Root,"AppxManifest.xml");File.WriteAllText(manifest,Manifest("26.1016.6573.0","app/ChatGPT.exe",""));
            Check(OfficialIdentityResolver.ReadPackage(newer.FullName,newer.Root).Executable==newer.Executable,"manifest-app-entry-read");
            File.WriteAllText(manifest,Manifest("26.1016.6573.0","app/ChatGPT.exe","<Application Id='App' Executable='app/ChatGPT.exe'/>"));Reject(()=>OfficialIdentityResolver.ReadPackage(newer.FullName,newer.Root),"duplicate-app-entry-rejected");
            File.WriteAllText(manifest,Manifest("26.1013.4894.0","app/ChatGPT.exe",""));Reject(()=>OfficialIdentityResolver.ReadPackage(newer.FullName,newer.Root),"manifest-registration-version-disagrees");
            File.WriteAllText(manifest,Manifest("26.1016.6573.0","../outside.exe",""));Reject(()=>OfficialIdentityResolver.ReadPackage(newer.FullName,newer.Root),"manifest-traversal-rejected");
            File.WriteAllText(manifest,"<!DOCTYPE Package [<!ENTITY x SYSTEM 'file:///C:/unrelated'>]><Package>&x;</Package>");Reject(()=>OfficialIdentityResolver.ReadPackage(newer.FullName,newer.Root),"manifest-external-entities-rejected");
            File.WriteAllText(manifest,Manifest("26.1016.6573.0","app/ChatGPT.exe",""));
            string link=Path.Combine(root,"linked-package");if(Directory.Exists(link))Reject(()=>OfficialIdentityResolver.ReadPackage(newer.FullName,link),"reparse-parent-rejected");else throw new Exception("junction-fixture-required");
            var lease=new OfficialIdentityResolver.IdentityLease{Key="fixture",Package=newer.FullName,Executable=newer.Executable,Hash=new string('A',64),File=new FileStream(newer.Executable,FileMode.Open,FileAccess.Read,FileShare.Read),Manifest=new FileStream(manifest,FileMode.Open,FileAccess.Read,FileShare.Read),References=2};
            var first=new OfficialIdentitySnapshot(lease,1,2);var second=new OfficialIdentitySnapshot(lease,3,4);
            first.Dispose();first.Dispose();Reject(()=>{using(var write=File.OpenWrite(newer.Executable)){}},"shared-lease-stays-locked-after-one-dispose");
            Check(second.PackageFullName==newer.FullName&&second.Pid==3&&second.Created==4,"snapshot-exact-generation");second.Dispose();using(var write=File.OpenWrite(newer.Executable)){Check(write.CanWrite,"last-dispose-releases-file");}
            foreach(string name in new[]{"PackageFullName","Executable","Sha256","Created","Pid"})Check(typeof(OfficialIdentitySnapshot).GetProperty(name).GetSetMethod()==null,"snapshot-public-read-only");
            Check(OfficialIdentityResolver.DiagnosticCode(new InvalidOperationException("official-registration-changing"))==10,"registration-diagnostic-bounded");
            Check(OfficialIdentityResolver.DiagnosticCode(new InvalidOperationException("official-manifest-path-invalid"))==11,"manifest-diagnostic-bounded");
            Check(OfficialIdentityResolver.DiagnosticCode(new InvalidOperationException("official-signature-verification-timeout"))==12,"signature-diagnostic-bounded");
            Check(OfficialIdentityResolver.DiagnosticCode(new InvalidOperationException("official-running-unverifiable"))==13,"process-diagnostic-bounded");
            Check(OfficialIdentityResolver.DiagnosticCode(new InvalidOperationException("private arbitrary error"))==15,"unknown-message-not-exported");
            Check(OfficialIdentityResolver.DiagnosticCode(new IOException("official-signature-invalid"))==15,"foreign-error-type-not-interpreted");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(root),"official-identity-contract-result.json"),"{\"Passed\":true,\"Checks\":"+checks+",\"RealApplicationsLaunched\":false}");Console.WriteLine("PASS "+checks+" official identity contracts");return 0;
        }catch(Exception e){Console.WriteLine("FAIL "+e.GetType().Name+" "+e.Message);return 1;}
    }
    static int Live(){var clock=Stopwatch.StartNew();using(var current=OfficialIdentityResolver.ResolveCurrent()){
        Check(current.Pid==0&&current.Created==0,"current-not-process-proof");Check(current.Sha256.Length==64,"current-hash-present");
        var running=OfficialIdentityResolver.FindRunning();try{foreach(var item in running){using(var exact=OfficialIdentityResolver.VerifyProcess(item.Pid,item.Created)){Check(exact.Executable==item.Executable&&exact.Sha256==item.Sha256,"actual-process-generation-verified");}Reject(()=>OfficialIdentityResolver.VerifyProcess(item.Pid,item.Created+1),"actual-wrong-generation-rejected");}
            string json="{\"Passed\":true,\"Checks\":"+checks+",\"ElapsedMs\":"+clock.ElapsedMilliseconds+",\"RunningGroups\":"+running.Length+",\"PackageFullName\":\""+current.PackageFullName+"\",\"Sha256\":\""+current.Sha256+"\",\"ApplicationsLaunched\":false}";
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(typeof(OfficialIdentityTests).Assembly.Location),"official-identity-live-result.json"),json);Console.WriteLine(json);
        }finally{foreach(var item in running)item.Dispose();}}
        return 0;
    }
}
