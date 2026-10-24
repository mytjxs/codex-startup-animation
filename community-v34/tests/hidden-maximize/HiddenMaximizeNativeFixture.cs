using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;

// Loads production methods without invoking the launcher entry point. Every HWND
// is created by this process on a separate non-input desktop; SwitchDesktop is
// deliberately absent. No installed process/window is enumerated or mutated.
internal static class HiddenMaximizeNativeFixture {
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    [StructLayout(LayoutKind.Sequential)] struct Pt {public int X,Y;}
    [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct Place {public uint Length,Flags,Show;public Pt Min,Max;public Rect Normal;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
    [StructLayout(LayoutKind.Sequential)] struct Message {public IntPtr Hwnd;public uint Code;public IntPtr WParam,LParam;public uint Time;public Pt Point;public uint Private;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr mode,uint flags,uint access,IntPtr attrs);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateWindowEx(uint ex,string cls,string text,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr w);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr w,out Rect r);
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr w,ref Place p);
    [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr w,ref Place p);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr w,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr w,int command);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr w,int command);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr w);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr w);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref Rect r,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SetProp(IntPtr w,string name,IntPtr value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr w,string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr w,string name);
    [DllImport("user32.dll")] static extern bool PeekMessage(out Message m,IntPtr w,uint first,uint last,uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref Message m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref Message m);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    static Type guard,recordType,placeType,compactType,rectType,nativePlaceType,leaseType;
    static readonly List<object> checks=new List<object>();
    static readonly List<object> samples=new List<object>();
    static int failed;
    static void Check(string name,bool result){checks.Add(new{name=name,passed=result});if(!result)failed++;}
    static void Set(object obj,string name,object value){var f=obj.GetType().GetField(name,Members);if(f!=null)f.SetValue(obj,value);else obj.GetType().GetProperty(name,Members).SetValue(obj,value,null);}
    static object Get(object obj,string name){var f=obj.GetType().GetField(name,Members);return f!=null?f.GetValue(obj):obj.GetType().GetProperty(name,Members).GetValue(obj,null);}
    static object Call(Type type,string name,object instance,params object[] args){try{return type.GetMethod(name,instance==null?Static:Members).Invoke(instance,args);}catch(TargetInvocationException e){throw e.InnerException??e;}}
    static Place Placement(IntPtr w){var p=new Place{Length=44};if(!GetWindowPlacement(w,ref p))throw new InvalidOperationException("fixture-read-placement");return p;}
    static Rect Bounds(IntPtr w){Rect r;if(!GetWindowRect(w,out r))throw new InvalidOperationException("fixture-read-bounds");return r;}
    static bool Same(Rect a,Rect b){return a.Left==b.Left&&a.Top==b.Top&&a.Right==b.Right&&a.Bottom==b.Bottom;}
    static object BoxRect(Type type,Rect r){object o=Activator.CreateInstance(type);Set(o,"Left",r.Left);Set(o,"Top",r.Top);Set(o,"Right",r.Right);Set(o,"Bottom",r.Bottom);return o;}
    static object BoxPlacement(Place p){object o=Activator.CreateInstance(placeType);Set(o,"Length",p.Length);Set(o,"Flags",p.Flags);Set(o,"Show",p.Show);Set(o,"Normal",BoxRect(placeType.GetField("Normal",Members).FieldType,p.Normal));return o;}
    static void Pump(int milliseconds){var until=Stopwatch.StartNew();do{Message m;while(PeekMessage(out m,IntPtr.Zero,0,0,1)){TranslateMessage(ref m);DispatchMessage(ref m);}Thread.Sleep(1);}while(until.ElapsedMilliseconds<milliseconds);}
    static IntPtr NewWindow(bool initiallyMaximized,bool toolWindow=false){
        IntPtr w=CreateWindowEx(0x08000000u|(toolWindow?0x80u:0u),"STATIC","Owned isolated geometry fixture",0x00cf0000u,274,70,1268,708,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        uint pid;if(w==IntPtr.Zero||GetWindowThreadProcessId(w,out pid)==0||pid!=(uint)Process.GetCurrentProcess().Id)throw new InvalidOperationException("fixture-hwnd-ownership");
        if(initiallyMaximized){ShowWindow(w,3);ShowWindow(w,0);Pump(20);}return w;
    }
    static object RecordFor(IntPtr w,Place before,Rect bounds,Rect desired){
        object r=Activator.CreateInstance(recordType,true);using(var own=Process.GetCurrentProcess()){
            Set(r,"Token",Guid.NewGuid().ToString("N"));Set(r,"Window",w.ToInt64());Set(r,"Pid",own.Id);Set(r,"Created",own.StartTime.ToUniversalTime().ToFileTimeUtc());Set(r,"Exe",own.MainModule.FileName);
        }
        Set(r,"Show",before.Show);Set(r,"Left",before.Normal.Left);Set(r,"Top",before.Normal.Top);Set(r,"Right",before.Normal.Right);Set(r,"Bottom",before.Normal.Bottom);
        Set(r,"ScreenLeft",bounds.Left);Set(r,"ScreenTop",bounds.Top);Set(r,"ScreenRight",bounds.Right);Set(r,"ScreenBottom",bounds.Bottom);
        Set(r,"ExpectedLeft",desired.Left);Set(r,"ExpectedTop",desired.Top);Set(r,"ExpectedRight",desired.Right);Set(r,"ExpectedBottom",desired.Bottom);
        if(recordType.GetProperty("ExpectedNormalCaptured",Members)!=null){Set(r,"ExpectedNormalCaptured",true);Set(r,"ExpectedNormalLeft",desired.Left);Set(r,"ExpectedNormalTop",desired.Top);Set(r,"ExpectedNormalRight",desired.Right);Set(r,"ExpectedNormalBottom",desired.Bottom);}Set(r,"Transition",-1);
        if(!SetProp(w,"CodexDragon.Restore."+(string)Get(r,"Token"),new IntPtr(1)))throw new InvalidOperationException("fixture-set-owned-marker");return r;
    }
    static object CompactFor(IntPtr w,Place original,Rect desired,object r){
        object owner=FormatterServices.GetUninitializedObject(guard);Set(owner,"ready",true);
        object lease=Activator.CreateInstance(leaseType,Members,null,new[]{r,owner},null);Set(lease,"Armed",true);
        object scope=FormatterServices.GetUninitializedObject(compactType);Set(scope,"target",w);Set(scope,"desired",BoxRect(rectType,desired));Set(scope,"recovery",lease);
        object before=Activator.CreateInstance(nativePlaceType);Set(before,"ShowCommand",original.Show);Set(scope,"original",before);return scope;
    }
    static void QueueThenLateMaximize(IntPtr w,Rect desired){
        Place p=Placement(w);p.Flags=4;p.Show=0;p.Normal=desired;
        if(!SetWindowPlacement(w,ref p)||!SetWindowPos(w,IntPtr.Zero,desired.Left,desired.Top,desired.Right-desired.Left,desired.Bottom-desired.Top,0x4214))throw new InvalidOperationException("fixture-queue-placement");
        // Emulate the client's own persisted-window-state restoration AFTER the
        // launcher's normal snapshot/queue. This touches only our new desktop.
        ShowWindow(w,3);ShowWindow(w,0);Pump(30);
    }
    static object Snapshot(IntPtr w,string name){var p=Placement(w);return new{name=name,visible=IsWindowVisible(w),maximized=IsZoomed(w),actual=Bounds(w),placement=p};}
    static void Scenario(bool newBehavior,bool originalMax){
        string prefix=originalMax?"original-max":"late-max-from-normal";IntPtr w=IntPtr.Zero;object r=null;
        var desired=new Rect{Left=326,Top=162,Right=1594,Bottom=870};var cover=new Rect{Left=320,Top=156,Right=1600,Bottom=876};
        try{
            w=NewWindow(originalMax);Place before=Placement(w);Rect initial=Bounds(w);Check(prefix+": initial show",before.Show==(originalMax?3u:1u));
            r=RecordFor(w,before,initial,desired);object compact=CompactFor(w,before,desired,r);
            samples.Add(Snapshot(w,prefix+": before"));QueueThenLateMaximize(w,desired);Place after=Placement(w);samples.Add(Snapshot(w,prefix+": hidden maximum"));
            Check(prefix+": exact failure state reproduced",!IsWindowVisible(w)&&IsZoomed(w)&&after.Show==3&&Same(after.Normal,desired)&&!Same(Bounds(w),desired));
            bool expected=newBehavior;
            bool guardAdmission=newBehavior&&(bool)Call(guard,"OwnsPendingMaximizedPlacement",null,r,BoxPlacement(after),true,true);
            bool handoffAdmission=newBehavior&&(bool)Call(compactType,"HiddenMaximizedPlacementApplied",compact,BoxRect(rectType,cover));
            samples.Add(new{name=prefix+": actual production admission",guardAdmission=guardAdmission,handoffAdmission=handoffAdmission,expected=expected});
            Check(prefix+": pending guard ownership available",guardAdmission==expected);
            Check(prefix+": pending handoff admission available",handoffAdmission==expected);if(!newBehavior){Check(prefix+": old build has no late-max acknowledgement",guard.GetMethod("OwnsPendingMaximizedPlacement",Static)==null&&compactType.GetMethod("HiddenMaximizedPlacementApplied",Members)==null);}
            // Prove the existing one-shot no-activation restore is sufficient.
            Check(prefix+": show4 accepted",ShowWindowAsync(w,4));Pump(60);samples.Add(Snapshot(w,prefix+": shown without activation"));
            Check(prefix+": show4 reaches desired physical rectangle",IsWindowVisible(w)&&!IsZoomed(w)&&Same(Bounds(w),desired));
            // Recreate the hidden pending state to test actual compensator,
            // not only a Boolean ownership helper.
            ShowWindow(w,0);QueueThenLateMaximize(w,desired);
            samples.Add(Snapshot(w,prefix+": before actual guardian rollback"));
            bool restored=false;int attempts=0;var timer=Stopwatch.StartNew();do{attempts++;restored=(bool)Call(guard,"Restore",null,r);Pump(10);}while(!restored&&timer.ElapsedMilliseconds<600);
            samples.Add(Snapshot(w,prefix+": after actual guardian rollback"));Place final=Placement(w);
            bool exact=final.Show==before.Show&&Same(final.Normal,before.Normal)&&Same(Bounds(w),initial)&&IsWindowVisible(w)&&IsZoomed(w)==originalMax;
            Check(prefix+": public rollback conservatively retains pending lease",!restored&&(bool)Get(r,"VisibilityRestoreRequested"));
            Check(prefix+": exact rollback geometry matches expected version",exact==newBehavior);
            Check(prefix+": rollback position ownership matches expected version",(bool)Get(r,"PositionRestoreRequested")==newBehavior);
            samples.Add(new{name=prefix+": rollback attempts",attempts=attempts,elapsedMilliseconds=timer.ElapsedMilliseconds});
            Check(prefix+": public async compensation marker retained",GetProp(w,"CodexDragon.Restore."+(string)Get(r,"Token"))==new IntPtr(1));
        }finally{if(w!=IntPtr.Zero){if(r!=null)RemoveProp(w,"CodexDragon.Restore."+(string)Get(r,"Token"));DestroyWindow(w);}}
    }
    static void RejectBoundary(string kind){
        IntPtr w=IntPtr.Zero;object r=null;var desired=new Rect{Left=326,Top=162,Right=1594,Bottom=870};var cover=new Rect{Left=320,Top=156,Right=1600,Bottom=876};
        try{
            w=NewWindow(false);Place original=Placement(w);r=RecordFor(w,original,Bounds(w),desired);object compact=CompactFor(w,original,desired,r);QueueThenLateMaximize(w,desired);
            if(kind=="one-edge-mismatch")Set(r,"ExpectedNormalRight",desired.Right+1);
            if(kind=="uncaptured-normal")Set(r,"ExpectedNormalCaptured",false);
            if(kind=="original-minimized"){Set(r,"Show",2u);object op=Get(compact,"original");Set(op,"ShowCommand",2u);Set(compact,"original",op);}
            if(kind=="visible")ShowWindow(w,3);
            if(kind=="not-maximized")ShowWindow(w,4);
            if(kind=="current-minimized"){ShowWindow(w,2);ShowWindow(w,0);Pump(20);}
            if(kind=="foreign-normal"){
                Place foreign=Placement(w);foreign.Normal.Right++;foreign.Flags=4;foreign.Show=3;
                if(!SetWindowPlacement(w,ref foreign))throw new InvalidOperationException("fixture-foreign-placement");ShowWindow(w,0);Pump(20);
                Check(kind+": actual foreign normal reproduced",Placement(w).Normal.Right==desired.Right+1&&!IsWindowVisible(w)&&IsZoomed(w));
            }
            Place current=Placement(w);
            Check(kind+": guard rejects",!(bool)Call(guard,"OwnsPendingMaximizedPlacement",null,r,BoxPlacement(current),!IsWindowVisible(w),IsZoomed(w)));
            Check(kind+": handoff rejects",!(bool)Call(compactType,"HiddenMaximizedPlacementApplied",compact,BoxRect(rectType,cover)));
            if(kind=="foreign-normal"){
                bool restored=false;var watch=Stopwatch.StartNew();do{restored=(bool)Call(guard,"Restore",null,r);Pump(10);}while(!restored&&watch.ElapsedMilliseconds<600);
                Rect foreign=desired;foreign.Right++;
                Check(kind+": rollback preserves foreign rectangle",!restored&&IsWindowVisible(w)&&Same(Placement(w).Normal,foreign)&&!(bool)Get(r,"PositionRestoreRequested"));
            }
        }finally{if(w!=IntPtr.Zero){if(r!=null)RemoveProp(w,"CodexDragon.Restore."+(string)Get(r,"Token"));DestroyWindow(w);}}
    }
    static void IdentityBoundary(string kind){
        IntPtr w=IntPtr.Zero;object r=null;var desired=new Rect{Left=326,Top=162,Right=1594,Bottom=870};
        try{
            w=NewWindow(false);r=RecordFor(w,Placement(w),Bounds(w),desired);QueueThenLateMaximize(w,desired);Place prior=Placement(w);Rect bounds=Bounds(w);
            if(kind=="wrong-generation")Set(r,"Created",(long)Get(r,"Created")+1);
            if(kind=="missing-token")RemoveProp(w,"CodexDragon.Restore."+(string)Get(r,"Token"));
            Check(kind+": actual rollback rejected",!(bool)Call(guard,"Restore",null,r));Pump(20);
            Place final=Placement(w);Check(kind+": native state unchanged",!IsWindowVisible(w)&&IsZoomed(w)&&prior.Show==final.Show&&Same(prior.Normal,final.Normal)&&Same(bounds,Bounds(w)));
        }finally{if(w!=IntPtr.Zero){if(r!=null)RemoveProp(w,"CodexDragon.Restore."+(string)Get(r,"Token"));DestroyWindow(w);}}
    }
    static void CapturedCoordinates(bool toolWindow){
        IntPtr w=IntPtr.Zero;object r=null;var desired=new Rect{Left=326,Top=162,Right=1594,Bottom=870};
        try{
            w=NewWindow(false,toolWindow);Rect originalNormal=Placement(w).Normal;
            object owner=FormatterServices.GetUninitializedObject(guard);Set(owner,"ready",true);Set(owner,"gate",new object());
            Set(owner,"pending",Activator.CreateInstance(guard.GetField("pending",Members).FieldType));
            object lease=Call(guard,"Arm",owner,w,desired.Left,desired.Top,desired.Right,desired.Bottom);
            Check("capture: actual guard lease returned "+toolWindow,lease!=null);
            if(lease==null)return;r=Get(lease,"Record");Rect expected=desired;
            if(!toolWindow){
                var monitor=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};IntPtr display=MonitorFromRect(ref expected,2);
                if(display==IntPtr.Zero||!GetMonitorInfo(display,ref monitor))throw new InvalidOperationException("fixture-monitor-info");
                int dx=monitor.Monitor.Left-monitor.Work.Left,dy=monitor.Monitor.Top-monitor.Work.Top;
                expected.Left+=dx;expected.Right+=dx;expected.Top+=dy;expected.Bottom+=dy;
            }
            Check("capture: real workspace rectangle "+toolWindow,(bool)Get(r,"ExpectedNormalCaptured")&&
                (int)Get(r,"ExpectedNormalLeft")==expected.Left&&(int)Get(r,"ExpectedNormalTop")==expected.Top&&
                (int)Get(r,"ExpectedNormalRight")==expected.Right&&(int)Get(r,"ExpectedNormalBottom")==expected.Bottom);
            object copy=Call(guard,"Deserialize",null,Call(guard,"Serialize",null,r));
            Check("capture: journal round trip "+toolWindow,(bool)Get(copy,"ExpectedNormalCaptured")&&
                (int)Get(copy,"ExpectedNormalLeft")==expected.Left&&(int)Get(copy,"ExpectedNormalTop")==expected.Top&&
                (int)Get(copy,"ExpectedNormalRight")==expected.Right&&(int)Get(copy,"ExpectedNormalBottom")==expected.Bottom);
            Check("capture: no application placement mutation "+toolWindow,Same(Placement(w).Normal,originalNormal));
        }finally{if(w!=IntPtr.Zero){if(r!=null)RemoveProp(w,"CodexDragon.Restore."+(string)Get(r,"Token"));DestroyWindow(w);}}
    }
    static void OwnershipMatrix(){
        object r=Activator.CreateInstance(recordType,true);Set(r,"ExpectedNormalCaptured",true);
        Set(r,"ExpectedNormalLeft",-1000);Set(r,"ExpectedNormalTop",50);Set(r,"ExpectedNormalRight",500);Set(r,"ExpectedNormalBottom",750);
        Place p=new Place{Length=44,Show=3,Normal=new Rect{Left=-1000,Top=50,Right=500,Bottom=750}};
        foreach(uint show in new uint[]{0,1,2,3,4,5,7,8,9}){
            Set(r,"Show",show);Check("ownership: original show "+show,(bool)Call(guard,"OwnsPendingMaximizedPlacement",null,r,BoxPlacement(p),true,true)==(show==1||show==3));
        }
        Set(r,"Show",1u);
        foreach(uint show in new uint[]{0,1,2,4,5,7,8,9}){Place changed=p;changed.Show=show;Check("ownership: rejects current show "+show,!(bool)Call(guard,"OwnsPendingMaximizedPlacement",null,r,BoxPlacement(changed),true,true));}
        foreach(string edge in new[]{"Left","Top","Right","Bottom"})foreach(int shift in new[]{-1,1}){
            object current=BoxPlacement(p);object bounds=Get(current,"Normal");Set(bounds,edge,(int)Get(bounds,edge)+shift);Set(current,"Normal",bounds);
            Check("ownership: rejects "+edge+" "+shift,!(bool)Call(guard,"OwnsPendingMaximizedPlacement",null,r,current,true,true));
        }
        Check("ownership: rejects visible",!(bool)Call(guard,"OwnsPendingMaximizedPlacement",null,r,BoxPlacement(p),false,true));
        Check("ownership: rejects normal surface",!(bool)Call(guard,"OwnsPendingMaximizedPlacement",null,r,BoxPlacement(p),true,false));
        Check("ownership: rejects missing record",!(bool)Call(guard,"OwnsPendingMaximizedPlacement",null,null,BoxPlacement(p),true,true));
        Set(r,"ExpectedNormalCaptured",false);Check("ownership: rejects unknown workspace",!(bool)Call(guard,"OwnsPendingMaximizedPlacement",null,r,BoxPlacement(p),true,true));
    }
    [MTAThread] static int Main(string[] args){
        if(args.Length!=3)return 2;string report=Path.GetFullPath(args[2]);IntPtr desktop=IntPtr.Zero,originalDesktop=GetThreadDesktop(GetCurrentThreadId()),dpi=IntPtr.Zero;string error=null,assemblySha256=null;var elapsed=Stopwatch.StartNew();
        try{
            desktop=CreateDesktop("CodexDragonFixture-"+Guid.NewGuid().ToString("N"),IntPtr.Zero,IntPtr.Zero,0,0x01ff,IntPtr.Zero);
            if(desktop==IntPtr.Zero||!SetThreadDesktop(desktop))throw new InvalidOperationException("isolated-desktop-unavailable:"+Marshal.GetLastWin32Error());
            dpi=SetThreadDpiAwarenessContext(new IntPtr(-4));
            using(var stream=File.OpenRead(args[0]))using(var sha=SHA256.Create())assemblySha256=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
            Assembly assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));guard=assembly.GetType("DragonLauncher.NativeRestoreGuard",true);recordType=guard.GetNestedType("Record",Members);placeType=guard.GetNestedType("Placement",Members);leaseType=guard.GetNestedType("Lease",Members);
            Type native=assembly.GetType("DragonLauncher.NativeHandoff",true);compactType=native.GetNestedType("CompactPlacement",Members);rectType=native.GetNestedType("NativeRect",Members);nativePlaceType=native.GetNestedType("NativePlacement",Members);
            bool newBehavior=args[1]=="green";if(!newBehavior&&args[1]!="red")throw new InvalidOperationException("invalid-mode");
            Scenario(newBehavior,false);Scenario(newBehavior,true);
            if(newBehavior){CapturedCoordinates(false);CapturedCoordinates(true);OwnershipMatrix();}
            if(newBehavior)foreach(string boundary in new[]{"one-edge-mismatch","uncaptured-normal","original-minimized","visible","not-maximized","current-minimized","foreign-normal"})RejectBoundary(boundary);
            IdentityBoundary("wrong-generation");IdentityBoundary("missing-token");
        }catch(Exception ex){error=ex.ToString();failed++;}
        finally{if(dpi!=IntPtr.Zero)SetThreadDpiAwarenessContext(dpi);if(desktop!=IntPtr.Zero){SetThreadDesktop(originalDesktop);CloseDesktop(desktop);}File.WriteAllText(report,new JavaScriptSerializer{MaxJsonLength=1024*1024}.Serialize(new{mode=args[1],loadedAssembly=Path.GetFullPath(args[0]),assemblySha256=assemblySha256,passed=failed==0,failed=failed,elapsedMilliseconds=elapsed.ElapsedMilliseconds,isolatedNonInputDesktop=true,actualLauncherStarted=false,checks=checks,samples=samples,error=error}));}
        return failed==0?0:1;
    }
}
