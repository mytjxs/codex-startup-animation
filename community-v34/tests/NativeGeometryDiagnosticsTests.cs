using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DragonLauncher
{
    internal static class NativeGeometryDiagnosticsTests
    {
        static int passed;
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);passed++;}
        static NativeHandoff.GeometryRect Rect(int l,int t,int r,int b){return new NativeHandoff.GeometryRect(l,t,r,b);}
        static NativeHandoff.GeometryObservation Sample(int dx=0,int dy=0)
        {
            return new NativeHandoff.GeometryObservation{
                Cover=Rect(320+dx,156+dy,1600+dx,876+dy),Target=Rect(326+dx,162+dy,1594+dx,870+dy),
                Frame=Rect(334+dx,162+dy,1586+dx,862+dy),TargetVisible=1,TargetMinimized=0,TargetCloaked=0,TargetDpi=144,CoverDpi=144};
        }
        static Dictionary<string,long> Parse(string line)
        {
            Check(line!=null&&line.Length<800,"Numeric geometry line has a small fixed upper bound");
            Check(Regex.IsMatch(line,@"\ANative geometry(?:; [A-Za-z]+=-?\d+){21}\z"),"Only fixed numeric fields are emitted");
            var values=new Dictionary<string,long>();foreach(Match m in Regex.Matches(line,@"(?<name>[A-Za-z]+)=(?<value>-?\d+)"))values.Add(m.Groups["name"].Value,Int64.Parse(m.Groups["value"].Value,CultureInfo.InvariantCulture));
            Check(values.Count==21,"Geometry field count stays fixed");return values;
        }
        static string Record(NativeHandoff.GeometryObservation sample,int phase=1)
        {return new NativeHandoff.GeometryDiagnostics().Record(0,phase,sample);}
        static void GeometryCases()
        {
            var v=Parse(Record(Sample()));
            Check(v["coverRectKnown"]==1&&v["targetRectKnown"]==1&&v["frameKnown"]==1,"Known geometry is explicit");
            Check(v["coverWidth"]==1280&&v["coverHeight"]==720&&v["targetWidth"]==1268&&v["targetHeight"]==708,"Physical dimensions describe the actual HWND rectangles");
            Check(v["deltaLeft"]==6&&v["deltaTop"]==6&&v["deltaRight"]==-6&&v["deltaBottom"]==-6,"All offsets use target edge minus cover edge");
            Check(v["frameInsetLeft"]==8&&v["frameInsetTop"]==0&&v["frameInsetRight"]==8&&v["frameInsetBottom"]==8,"DWM insets distinguish invisible resize borders from placement");
            Check(v["targetDpi"]==144&&v["coverDpi"]==144,"DPI observation is retained without scaling geometry");
            Check(Record(Sample())==Record(Sample(-24000,-16000)),"Negative-monitor translation never exports desktop position");
            Check(Record(Sample())==Record(Sample(900000,700000)),"Large absolute coordinate translation remains private");
            var none=Parse(Record(new NativeHandoff.GeometryObservation()));
            Check(none["coverRectKnown"]==0&&none["targetRectKnown"]==0&&none["frameKnown"]==0,"Unavailable rectangles remain unknown");
            Check(none["coverWidth"]==-1&&none["targetHeight"]==-1&&none["targetVisible"]==-1&&none["targetDpi"]==-1,"Unknown dimensions, states and DPI have explicit sentinels");
            foreach(string name in new[]{"deltaLeft","deltaTop","deltaRight","deltaBottom","frameInsetLeft","frameInsetTop","frameInsetRight","frameInsetBottom"})Check(none[name]==Int32.MinValue,"Unknown "+name+" differs from a valid negative pixel");
            var sample=Sample();sample.Target=Rect(319,155,1599,875);sample.Frame=null;v=Parse(Record(sample));
            Check(v["deltaLeft"]==-1&&v["deltaTop"]==-1&&v["deltaRight"]==-1&&v["deltaBottom"]==-1,"A real minus-one offset is never mistaken for unknown");
            sample=Sample();sample.Cover=Rect(0,0,0,1);v=Parse(Record(sample));
            Check(v["coverRectKnown"]==0&&v["coverWidth"]==-1&&v["targetRectKnown"]==1&&v["deltaLeft"]==Int32.MinValue,"Zero area does not fabricate valid geometry");
            sample=Sample();sample.Target=Rect(Int32.MinValue,0,Int32.MaxValue,10);v=Parse(Record(sample));
            Check(v["targetRectKnown"]==0&&v["targetWidth"]==-1&&v["frameKnown"]==0,"Native integer overflow cannot become a valid size");
            sample=Sample();sample.Target=Rect(0,10,10,0);v=Parse(Record(sample));
            Check(v["targetRectKnown"]==0&&v["targetHeight"]==-1,"Reversed rectangle is unknown");
            sample=Sample();sample.Cover=Rect(0,0,65536,65536);sample.Target=Rect(65536,-65536,131072,0);sample.Frame=null;v=Parse(Record(sample));
            Check(v["coverRectKnown"]==1&&v["targetRectKnown"]==1&&v["deltaLeft"]==65536&&v["deltaTop"]==-65536,"Accepted dimensions and deltas include their exact bounds");
            sample.Target=Rect(65537,-65536,131073,0);v=Parse(Record(sample));
            Check(v["targetRectKnown"]==1&&v["deltaLeft"]==Int32.MinValue&&v["deltaBottom"]==Int32.MinValue,"An outlier edge withholds all relative offsets");
            sample=Sample();sample.Frame=Rect(1351,162,1594,870);v=Parse(Record(sample));
            Check(v["frameKnown"]==0&&v["frameInsetLeft"]==Int32.MinValue&&v["frameInsetBottom"]==Int32.MinValue,"Outlier DWM frame is not presented as reliable evidence");
            sample=Sample();sample.TargetDpi=0;sample.CoverDpi=769;sample.TargetVisible=3;sample.TargetMinimized=-2;sample.TargetCloaked=2;v=Parse(Record(sample));
            Check(v["targetDpi"]==-1&&v["coverDpi"]==-1&&v["targetVisible"]==-1&&v["targetMinimized"]==-1&&v["targetCloaked"]==-1,"Invalid DPI and state values cannot escape numeric bounds");
            foreach(int dpi in new[]{48,96,120,144,192,384,768}){sample=Sample();sample.TargetDpi=dpi;sample.CoverDpi=dpi;v=Parse(Record(sample));Check(v["targetDpi"]==dpi&&v["coverDpi"]==dpi,"Accepted DPI "+dpi);}
            var culture=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=new CultureInfo("ar-SA");Check(Record(Sample())=="Native geometry; phase=1; coverRectKnown=1; targetRectKnown=1; frameKnown=1; targetVisible=1; targetMinimized=0; targetCloaked=0; targetDpi=144; coverDpi=144; coverWidth=1280; coverHeight=720; targetWidth=1268; targetHeight=708; deltaLeft=6; deltaTop=6; deltaRight=-6; deltaBottom=-6; frameInsetLeft=8; frameInsetTop=0; frameInsetRight=8; frameInsetBottom=8","Locale never changes invariant field syntax");}finally{CultureInfo.CurrentCulture=culture;}
        }
        static void RateCases()
        {
            var log=new NativeHandoff.GeometryDiagnostics();Check(!log.Due(-1),"Negative monotonic time is rejected");
            Check(log.Record(0,-2,Sample())==null&&log.Record(0,3,Sample())==null&&log.Record(0,1,null)==null,"Invalid samples do not consume the logging budget");
            Check(log.Record(0,-1,Sample())!=null,"Initial native preparation can be observed immediately");
            Check(log.Record(1999,0,Sample())==null&&!log.Due(1999),"Native getters need not run before the two-second cadence");
            Check(log.Record(2000,0,Sample())!=null,"Next phase observes at the two-second boundary");
            Check(log.Record(1000,1,Sample())==null,"Clock reversal cannot create a diagnostic burst");
            Check(log.Record(4000,2,Sample())!=null,"Hidden placement phase can be observed");
            int count=3;for(int i=3;i<100;i++)if(log.Record(i*2000L,1,Sample())!=null)count++;
            Check(count==48&&!log.Due(Int64.MaxValue),"One launcher has a lifetime maximum of 48 geometry lines");
        }
        [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateWindowEx(uint exStyle,string type,string name,uint style,int x,int y,int width,int height,IntPtr owner,IntPtr menu,IntPtr instance,IntPtr parameter);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("dwmapi.dll",EntryPoint="DwmSetWindowAttribute")] static extern int SetCloak(IntPtr window,int attribute,ref int value,int size);
        [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int ReadCloak(IntPtr window,int attribute,out int value,int size);
        static readonly List<IntPtr> fixtureWindows=new List<IntPtr>();
        static IntPtr Fixture(uint exStyle,uint style,int width,int height,IntPtr owner)
        {
            // Owned fixture windows are invisible to interaction: no activation,
            // no taskbar buttons and far outside the visible desktop.
            IntPtr result=CreateWindowEx(exStyle|0x08000000u,"STATIC","diagnostic fixture",style,-30000,-30000,width,height,owner,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
            Check(result!=IntPtr.Zero,"Owned fixture window created");fixtureWindows.Add(result);return result;
        }
        static void Admission(IntPtr window,NativeHandoff.CandidateExclusion expected,string name)
        {
            var actual=NativeHandoff.DescribeCandidateExclusion(window);Check(actual==expected,name+" has an explicit diagnostic reason");
            bool selected=(bool)typeof(NativeHandoff).GetMethod("IsMainCandidate",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{window,true});
            Check(selected==(actual==NativeHandoff.CandidateExclusion.None),name+" exactly agrees with the unchanged selection predicate");
        }
        static void NativeCandidateCases()
        {
            const uint popupVisible=0x90000000u;IntPtr foreground=GetForegroundWindow();
            try{
                Admission(IntPtr.Zero,NativeHandoff.CandidateExclusion.StateChanged,"Missing handle");
                IntPtr normal=Fixture(0,popupVisible,180,140,IntPtr.Zero);Admission(normal,NativeHandoff.CandidateExclusion.None,"Visible top-level STATIC-class window");
                IntPtr hidden=Fixture(0,0x80000000u,180,140,IntPtr.Zero);Admission(hidden,NativeHandoff.CandidateExclusion.StateChanged,"Hidden window");
                IntPtr small=Fixture(0,popupVisible,32,32,IntPtr.Zero);Admission(small,NativeHandoff.CandidateExclusion.TooSmall,"Small utility window");
                IntPtr tool=Fixture(0x80,popupVisible,32,32,IntPtr.Zero);Admission(tool,NativeHandoff.CandidateExclusion.Tool,"Tool window wins over size rejection");
                IntPtr owned=Fixture(0x80,popupVisible,32,32,normal);Admission(owned,NativeHandoff.CandidateExclusion.Owned,"Owned popup wins over tool and size rejection");
                IntPtr child=Fixture(0x80,0x50000000u,32,32,normal);Admission(child,NativeHandoff.CandidateExclusion.Child,"Child window wins over tool and size rejection");
                ShowWindow(normal,7);Check(IsIconic(normal),"Owned fixture minimized without activation");Admission(normal,NativeHandoff.CandidateExclusion.None,"Minimized main window remains eligible");
                IntPtr cloaked=Fixture(0,popupVisible,180,140,IntPtr.Zero);int on=1,observed;
                if(SetCloak(cloaked,13,ref on,4)==0&&ReadCloak(cloaked,14,out observed,4)==0&&observed!=0)
                    Admission(cloaked,NativeHandoff.CandidateExclusion.Cloaked,"DWM-cloaked window");
                else Console.WriteLine("SKIP cloak predicate: DWM cloak is unavailable for this owned fixture");
                IntPtr ended=Fixture(0,popupVisible,180,140,IntPtr.Zero);Check(DestroyWindow(ended),"Owned fixture ended");fixtureWindows.Remove(ended);Admission(ended,NativeHandoff.CandidateExclusion.StateChanged,"Window ended during observation");
            }finally{for(int i=fixtureWindows.Count-1;i>=0;i--)DestroyWindow(fixtureWindows[i]);fixtureWindows.Clear();}
            Check(GetForegroundWindow()==foreground,"Native diagnostic fixtures never activate or change the foreground application");
        }
        static int Main()
        {
            try{GeometryCases();RateCases();NativeCandidateCases();Console.WriteLine("PASS "+passed+" assertions; synthetic geometry and owned offscreen no-activate windows only; no application entry or mutation of another application.");return 0;}
            catch(Exception e){Console.WriteLine("FAIL "+e.GetBaseException().Message);return 1;}
        }
    }
}
