using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace DragonLauncher
{
    /// <summary>
    /// Bounded native helpers for revealing an already activated application.
    /// This class never starts, closes, reparents or changes the opacity of a target.
    /// All entry points are intended for the WPF dispatcher thread.
    /// </summary>
    internal static class NativeHandoff
    {
        private const uint QueryLimitedInformation = 0x1000;
        private const uint GwOwner = 4;
        private const int GwlStyle = -16, GwlExStyle = -20;
        private const long WsChild = 0x40000000L, WsExToolWindow = 0x80L;
        private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002,
            SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010,
            SwpNoOwnerZOrder = 0x0200, SwpAsyncWindowPos = 0x4000;
        private const int SwRestore = 9, TransitionsForceDisabled = 3, ExtendedFrameBounds = 9, Cloaked = 14;
        private const uint SmtoBlock = 0x0001, SmtoAbortIfHung = 0x0002,
            SmtoErrorOnExit = 0x0020;
        private static readonly uint OwnPid = (uint)Process.GetCurrentProcess().Id;
        private static readonly Stopwatch CacheClock = Stopwatch.StartNew();
        private static readonly Dictionary<uint, ProcessIdentity> ProcessCache = new Dictionary<uint, ProcessIdentity>();
        private static readonly Dictionary<IntPtr, TargetIdentity> VerifiedTargets = new Dictionary<IntPtr, TargetIdentity>();
        private static BridgeClientIdentity bridgeTarget;
        private static readonly object ProbeGate = new object();
        private static readonly AutoResetEvent ProbeSignal = new AutoResetEvent(false);
        private static bool probePending;
        private static bool probeWorkerRunning;
        private static IntPtr requestedWindow;
        private static uint requestedPid;
        private static ulong requestedCreated;
        private static IntPtr probedWindow;
        private static uint probedPid;
        private static ulong probedCreated;
        private static long probedAt = -1000;
        private static bool probedResponsive;
        private static int compositorFencePending;

        private sealed class TargetIdentity
        {
            public string Exe, AppId, Family, Key;
            public BridgeClientIdentity Bound;
        }

        private sealed class ProcessIdentity
        {
            public ulong Created;
            public string Key;
            public bool Matches;
            public long CheckedAt;
        }

        // A lost post-placement sample is recoverable; a permanently hidden or
        // stalled client must not strand the loading movie for its full budget.
        // Once the finale is armed, the existing handoff probe lease owns it.
        internal sealed class ReadinessLossPolicy
        {
            internal const long GraceMilliseconds=5000;
            private long lostAt=-1;
            internal long LostMilliseconds(long now){return lostAt<0?0:Math.Max(0,now-lostAt);}
            internal bool Expired(bool ready,bool handoffArmed,long now)
            {
                if(ready||handoffArmed){lostAt=-1;return false;}
                if(lostAt<0||now<lostAt)lostAt=now;
                return now-lostAt>=GraceMilliseconds;
            }
            internal static bool PreservePresentation(bool wasReady,bool visible,bool minimized,bool cloaked)
            {return wasReady&&(!visible||minimized||cloaked);}
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePlacement
        {
            public uint Length, Flags, ShowCommand;
            public NativePoint MinPosition, MaxPosition;
            public NativeRect NormalPosition, Device;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMonitorInfo
        {
            public int Size;
            public NativeRect Monitor, Work;
            public uint Flags;
        }

        /// <summary>
        /// Temporarily fits a verified target under the film. Poll TryPrepare
        /// without blocking the UI. Dispose restores the original placement;
        /// Commit deliberately leaves the successfully revealed compact window.
        /// An HWND that an external application has already shown may have been
        /// visible before this scope was created; no first-frame interception
        /// is claimed for a cold application launch.
        /// </summary>
        internal sealed class CompactPlacement : IDisposable
        {
            private readonly IntPtr target;
            private readonly Window film;
            private TargetIdentity identity;
            private NativePlacement original;
            private NativeRect desired;
            private uint pid;
            private ulong created;
            private long started;
            private long placementQueuedAt=-1;
            private long handoffArmedAt=-1;
            private readonly int timeoutMilliseconds;
            private int phase;
            private bool changed, captured, committed, disposed, restored, wasReady, transitionSuppressionAttempted;
            private readonly ReadinessLossPolicy readinessLoss=new ReadinessLossPolicy();
            private NativeRestoreGuard.Lease recovery;
            internal bool RecoveryArmed {get{return !disposed&&!Failed&&recovery!=null&&recovery.Armed&&recovery.Available&&recovery.Owned;}}
            internal bool CanSuppressTransitions {get{return !transitionSuppressionAttempted&&RecoveryArmed&&recovery.Record.Transition==0;}}
            internal bool Ready { get; private set; }
            internal bool Failed { get; private set; }
            internal string FailureReason { get; private set; }
            internal int PreparationPhase { get { return phase; } }
            internal long PreparationElapsedMilliseconds { get { return Math.Max(0,CacheClock.ElapsedMilliseconds-started); } }
            internal string ReadinessDiagnostic {get{return DescribeReadiness(target)+"; postReadyLossMs="+readinessLoss.LostMilliseconds(CacheClock.ElapsedMilliseconds);}}

            internal CompactPlacement(IntPtr target, Window film, int timeoutMilliseconds = 2000)
            {
                this.target=target; this.film=film;
                // Cold Electron windows can exist before their UI thread is
                // responsive. Living startup owns a longer overall deadline;
                // do not replace it with the legacy two-second placement limit.
                this.timeoutMilliseconds=Math.Max(1,Math.Min(90000,timeoutMilliseconds));
                IntPtr previous=IntPtr.Zero;
                try
                {
                    if(film==null || !film.CheckAccess() || !VerifiedTargets.TryGetValue(target,out identity) || !WindowMatches(target,identity))
                    { Fail("Target identity or overlay thread is invalid.");return; }
                    GetWindowThreadProcessId(target,out pid);
                    if(!TryGetCreationTime(pid,out created)){Fail("Target process ended.");return;}
                    previous=EnterPhysicalCoordinates();
                    original.Length=(uint)Marshal.SizeOf(typeof(NativePlacement));
                    if(!GetWindowPlacement(target,ref original))
                    {
                        // Classic Windows SDKs use the 44-byte structure. Newer
                        // SDKs expose a trailing device rectangle (60 bytes).
                        original.Length=44;
                        if(!GetWindowPlacement(target,ref original)){Fail("Cannot save target placement.");return;}
                    }
                    captured=true;
                    if(!ReadFilmBounds(out desired)){Fail("Cannot read overlay bounds.");return;}
                    // Keep visible chrome inside the movie. The real frame is
                    // checked again after the application processes the request.
                    desired.Left+=6;desired.Top+=6;desired.Right-=6;desired.Bottom-=6;
                    if(desired.Width<128 || desired.Height<128){Fail("Overlay is too small.");return;}
                    started=CacheClock.ElapsedMilliseconds;phase=-1;
                    NativeRect initial;bool hasRect=GetWindowRect(target,out initial);
                    LauncherDiagnostics.Write("handoff-latest.log","Native placement begin; visible="+IsWindowVisible(target)+
                        "; initialHiddenLease="+(identity.Bound!=null&&HasInitialHiddenLease(target,identity))+
                        "; rectValid="+hasRect+"; target="+initial.Left+","+initial.Top+","+initial.Right+","+initial.Bottom+
                        "; desired="+desired.Left+","+desired.Top+","+desired.Right+","+desired.Bottom);
                }
                catch(Exception error){Fail(error.Message);}
                finally {LeavePhysicalCoordinates(previous);}
            }

            private bool SameTarget()
            {
                uint currentPid;ulong currentCreated;
                return (recovery==null||!recovery.Armed||recovery.Owned)&&IsWindow(target) && GetWindowThreadProcessId(target,out currentPid)!=0 && currentPid==pid &&
                    TryGetCreationTime(currentPid,out currentCreated) && currentCreated==created && WindowMatches(target,identity);
            }

            private bool ReadFilmBounds(out NativeRect rect)
            {
                rect=new NativeRect();
                IntPtr handle=new WindowInteropHelper(film).Handle;
                // The movie may already be hidden behind a same-sized pulse.
                return IsOwnWindow(handle) && GetWindowRect(handle,out rect) && rect.Width>0 && rect.Height>0;
            }

            private bool FitsFilm()
            {
                NativeRect bounds, cover;
                if(!ReadFilmBounds(out cover))return false;
                if(DwmGetRect(target,ExtendedFrameBounds,out bounds,Marshal.SizeOf(typeof(NativeRect)))!=0 || bounds.Width<=0 || bounds.Height<=0)
                    if(!GetWindowRect(target,out bounds))return false;
                return bounds.Width>0 && bounds.Height>0 && bounds.Left>=cover.Left && bounds.Top>=cover.Top && bounds.Right<=cover.Right && bounds.Bottom<=cover.Bottom;
            }

            private void SuppressTransitionsBeforeHide()
            {
                if(transitionSuppressionAttempted)return;
                transitionSuppressionAttempted=true;
                int current=-1,readResult=-1,setResult=-1,confirmed=-1,confirmResult=-1;
                // The durable guardian already captured the original DWM value.
                // Mark compensation as required BEFORE any possible mutation;
                // exceptions or a failed hide must not abandon that lease.
                if(RecoveryArmed&&recovery.Record.Transition==0)
                {
                    readResult=DwmGetInt(target,TransitionsForceDisabled,out current,sizeof(int));
                    if(readResult==0&&current==0)
                    {
                        changed=true;
                        int disabled=1;
                        setResult=DwmSetInt(target,TransitionsForceDisabled,ref disabled,sizeof(int));
                        confirmResult=DwmGetInt(target,TransitionsForceDisabled,out confirmed,sizeof(int));
                    }
                }
                LauncherDiagnostics.Write("handoff-latest.log","Native pre-hide transition; original="+recovery.Record.Transition+
                    "; readResult="+readResult+"; before="+current+"; setResult="+setResult+"; confirmResult="+confirmResult+"; confirmed="+confirmed);
            }

            private bool HiddenPlacementApplied(out NativeRect bounds,out NativeRect cover)
            {
                bounds=new NativeRect();cover=new NativeRect();
                // DWM extended-frame bounds may lag while hidden. Confirm the
                // actual physical HWND rectangle before asking it to show;
                // the existing visible FitsFilm check still follows afterward.
                if(!GetWindowRect(target,out bounds)||!ReadFilmBounds(out cover))return false;
                // Match the guardian's four-sided ownership rectangle exactly.
                // A minimum-size clamp or DPI-adjusted offset is not that owned
                // value, even if it still fits beneath the cover; never show it
                // as a successfully prepared/rollback-owned placement.
                return bounds.Width>=128&&bounds.Height>=128&&bounds.Left>=cover.Left&&bounds.Top>=cover.Top&&
                    bounds.Right<=cover.Right&&bounds.Bottom<=cover.Bottom&&
                    bounds.Left==desired.Left&&bounds.Top==desired.Top&&bounds.Right==desired.Right&&bounds.Bottom==desired.Bottom;
            }

            private static bool PlacementConfirmationExpired(long queuedAt,long now)
            {
                // This deadline starts once, only after the placement request.
                // It supplements rather than resets the overall startup budget.
                return queuedAt<0||now<queuedAt||now-queuedAt>=3000;
            }

            internal bool TryPrepare()
            {
                if(disposed || Failed)return false;
                IntPtr previous=IntPtr.Zero;
                try
                {
                    if(recovery!=null&&recovery.Armed&&!recovery.Available){Fail("Native recovery guardian is no longer available.");return false;}
                    if(!SameTarget()){Fail("Target window ended or changed identity.");return false;}
                    previous=EnterPhysicalCoordinates();
                    if(Ready)
                    {
                        // After the owned hide/show completed, a later hide or
                        // minimize may be intentional. Wait briefly, never show
                        // it again here, and preserve that state during cleanup.
                        bool ready=IsReady(target);
                        if(readinessLoss.Expired(ready,handoffArmedAt>=0,CacheClock.ElapsedMilliseconds))
                        {Fail("Native readiness lost after placement; "+ReadinessDiagnostic);return false;}
                        if(!ready)return false;
                        if(!FitsFilm()){Fail("Target moved outside the overlay.");return false;}
                        return true;
                    }
                    if(PreparationElapsedMilliseconds>=timeoutMilliseconds){Fail("Compact placement timed out; phase="+phase+"; elapsedMs="+PreparationElapsedMilliseconds+"; budgetMs="+timeoutMilliseconds);return false;}
                    if(phase==-1){
                        var guard=NativeRestoreGuard.Current;
                        if(guard==null||guard.Faulted){Fail("Native recovery guard unavailable.");return false;}
                        if(recovery==null){if(!guard.Ready)return false;recovery=guard.Arm(target,desired.Left,desired.Top,desired.Right,desired.Bottom);if(recovery==null){Fail("Native recovery record refused.");return false;}}
                        if(recovery.Failed){Fail("Native recovery acknowledgement failed.");return false;}
                        if(!recovery.Armed)return false;
                        if(!RecoveryArmed){Fail("Native recovery lease ownership lost before hide.");return false;}
                        SuppressTransitionsBeforeHide();
                        // A cold client can activate itself while the durable
                        // guard is arming. Retain only its existing foreground
                        // in our cover; never steal from an unrelated app.
                        if(IsForegroundTarget(target)){film.Activate();film.Focus();}
                        changed=true;if(!ShowWindowAsync(target,0)){Fail("Cannot request target hide.");return false;}
                        phase=0;return false;
                    }
                    if(phase==0)
                    {
                        if(IsWindowVisible(target))return false;
                        NativePlacement compact=original;
                        compact.Flags=4;compact.ShowCommand=0;compact.NormalPosition=desired;
                        if((WindowStyle(target,GwlExStyle)&WsExToolWindow)==0)
                        {
                            NativeMonitorInfo monitor=new NativeMonitorInfo();monitor.Size=Marshal.SizeOf(typeof(NativeMonitorInfo));
                            IntPtr screen=MonitorFromRect(ref desired,2);
                            if(screen!=IntPtr.Zero && GetMonitorInfo(screen,ref monitor))
                            {
                                int dx=monitor.Monitor.Left-monitor.Work.Left,dy=monitor.Monitor.Top-monitor.Work.Top;
                                compact.NormalPosition.Left+=dx;compact.NormalPosition.Right+=dx;
                                compact.NormalPosition.Top+=dy;compact.NormalPosition.Bottom+=dy;
                            }
                        }
                        if(!SetWindowPlacement(target,ref compact) ||
                            !SetWindowPos(target,IntPtr.Zero,desired.Left,desired.Top,desired.Width,desired.Height,
                                SwpNoActivate|SwpNoZOrder|SwpNoOwnerZOrder|SwpAsyncWindowPos))
                        {Fail("Cannot apply compact placement.");return false;}
                        // Successful async requests are only queued, not an
                        // acknowledgement that the old rectangle disappeared.
                        placementQueuedAt=CacheClock.ElapsedMilliseconds;phase=2;return false;
                    }
                    if(phase==2)
                    {
                        if(PlacementConfirmationExpired(placementQueuedAt,CacheClock.ElapsedMilliseconds))
                        {Fail("Compact placement confirmation timed out; phaseMs="+Math.Max(0,CacheClock.ElapsedMilliseconds-placementQueuedAt)+"; budgetMs=3000");return false;}
                        bool alreadyVisible=IsWindowVisible(target);
                        NativeRect applied,cover;
                        if(!HiddenPlacementApplied(out applied,out cover))
                        {
                            // Even an already matching anchor can belong to the
                            // old rectangle while the resize remains queued.
                            // Hidden mismatches get only the fixed 3s budget;
                            // never infer request completion from the anchor.
                            if(alreadyVisible)Fail("Target became visible outside confirmed compact placement.");
                            return false;
                        }
                        LauncherDiagnostics.Write("handoff-latest.log","Native hidden placement confirmed; elapsedMs="+PreparationElapsedMilliseconds+
                            "; alreadyVisible="+alreadyVisible+
                            "; target="+applied.Left+","+applied.Top+","+applied.Right+","+applied.Bottom+
                            "; cover="+cover.Left+","+cover.Top+","+cover.Right+","+cover.Bottom);
                        // Electron may show itself after its hidden placement
                        // has settled. Accept a safely covered window as-is.
                        if(!alreadyVisible&&!ShowWindowAsync(target,4)){Fail("Cannot reveal confirmed compact placement.");return false;}
                        if(!CompleteInitialHiddenLease(target)){Fail("Cannot release initial hidden-window lease.");return false;}
                        phase=1;return false;
                    }
                    if(!IsReady(target))return false;
                    if(!FitsFilm()){Fail("Application minimum size does not fit the overlay.");return false;}
                    Ready=true;wasReady=true;return true;
                }
                catch(Exception error){Fail(error.Message);return false;}
                finally {LeavePhysicalCoordinates(previous);}
            }

            private bool HandoffPlacementIntact()
            {
                if(!Ready||Failed||disposed)return false;
                IntPtr previous=IntPtr.Zero;
                try {
                    if(recovery!=null&&recovery.Armed&&!recovery.Available){Fail("Native recovery guardian is no longer available.");return false;}
                    if(!SameTarget()){Fail("Target window ended or changed identity.");return false;}
                    previous=EnterPhysicalCoordinates();
                    if(!FitsFilm()){Fail("Target moved outside the overlay.");return false;}
                    return IsMainCandidate(target,false);
                }catch(Exception error){Fail(error.Message);return false;}
                finally {LeavePhysicalCoordinates(previous);}
            }
            internal bool ArmHandoff()
            {
                if(!HandoffPlacementIntact()||!IsReady(target))return false;
                lock(ProbeGate){
                    long now=CacheClock.ElapsedMilliseconds;
                    if(probedWindow!=target||probedPid!=pid||probedCreated!=created||!probedResponsive||now-probedAt>500)return false;
                    handoffArmedAt=now;return true;
                }
            }
            internal void DisarmHandoff(){handoffArmedAt=-1;}
            internal bool HandoffReady()
            {
                if(!HandoffPlacementIntact())return false;
                // Queue a refresh without blocking this frame. A genuine negative
                // response after arming cancels the lease; mere cache expiry does not.
                IsReady(target);
                lock(ProbeGate){return HandoffProbeLease(handoffArmedAt,CacheClock.ElapsedMilliseconds,
                    probedWindow==target&&probedPid==pid&&probedCreated==created,probedResponsive,probedAt);}
            }
            internal bool Commit()
            {
                // The pulse deliberately stops preparation polling. Its reveal
                // can outlast the responsiveness cache, so final placement
                // ownership must not depend on that expired startup sample.
                // Revalidate the identity, guard and geometry before releasing
                // compensation; a user move or changed target still refuses.
                if(!Ready || Failed || disposed)return false;
                IntPtr previous=IntPtr.Zero;
                try
                {
                    if(recovery!=null&&recovery.Armed&&!recovery.Available){Fail("Native recovery guardian is no longer available.");return false;}
                    if(!SameTarget()){Fail("Target window ended or changed identity.");return false;}
                    previous=EnterPhysicalCoordinates();
                    if(!FitsFilm()){Fail("Target moved outside the overlay.");return false;}
                    if(!IsMainCandidate(target,false))return false;
                    if(recovery!=null){if(!NativeRestoreGuard.RestoreTransition(recovery.Record))return false;recovery.Release();}
                    committed=true;
                    return true;
                }
                catch(Exception error){Fail(error.Message);return false;}
                finally {LeavePhysicalCoordinates(previous);}
            }

            private void Fail(string reason)
            { Failed=true;Ready=false;FailureReason=reason;RestoreCore(); }

            private void RestoreCore()
            {
                if(restored || committed || !captured || !changed)return;
                IntPtr previous=IntPtr.Zero;
                try
                {
                    if(!SameTarget())return;
                    previous=EnterPhysicalCoordinates();
                    int cloaked;
                    bool isCloaked=DwmGetInt(target,Cloaked,out cloaked,sizeof(int))==0&&cloaked!=0;
                    if(ReadinessLossPolicy.PreservePresentation(wasReady,IsWindowVisible(target),IsIconic(target),isCloaked))
                    {
                        // This waiver is evaluated only during terminal cleanup,
                        // never during the recovery grace. A transient hide that
                        // has already ended retains normal placement restoration.
                        if(recovery!=null&&NativeRestoreGuard.RestoreTransition(recovery.Record))
                        {recovery.Release();restored=true;return;}
                        // Do not abandon durable compensation on a DWM failure.
                        // The existing bounded guardian remains authoritative;
                        // this rare fallback may restore the hidden client.
                        LauncherDiagnostics.Write("handoff-latest.log","Native presentation preserve unavailable; transition restoration unconfirmed; retaining compensation");
                    }
                    // Compensation checks its per-window lease and each owned
                    // value. Do not overwrite a later user move/resize.
                    if(recovery!=null)restored=NativeRestoreGuard.Restore(recovery.Record);
                }
                catch(Exception error){LauncherDiagnostics.Write("handoff-latest.log","Native placement recovery failed: "+error.GetType().Name);}
                finally {LeavePhysicalCoordinates(previous);}
            }

            internal void Restore(){RestoreCore();Ready=false;}
            public void Dispose(){if(disposed)return;RestoreCore();if(!changed&&recovery!=null)recovery.Release();disposed=true;}
        }

        private sealed class EmptyScope : IDisposable
        {
            public void Dispose() { }
        }

        private sealed class TransitionScope : IDisposable
        {
            private IntPtr target;
            private readonly TargetIdentity identity;
            private readonly uint pid;
            private readonly ulong created;
            private readonly int original;
            internal TransitionScope(IntPtr target, TargetIdentity identity, uint pid, ulong created, int original)
            {
                this.target = target; this.identity = identity; this.pid = pid;
                this.created = created; this.original = original;
            }
            public void Dispose()
            {
                IntPtr window = target;
                target = IntPtr.Zero;
                if (window == IntPtr.Zero) return;
                try
                {
                    uint currentPid;
                    ulong currentCreated;
                    if (!IsWindow(window) || GetWindowThreadProcessId(window, out currentPid) == 0 ||
                        currentPid != pid || !TryGetCreationTime(currentPid, out currentCreated) ||
                        currentCreated != created || !WindowMatches(window, identity)) return;
                    int current;
                    // Do not overwrite a different later change made by the app.
                    if (DwmGetInt(window, TransitionsForceDisabled, out current, sizeof(int)) == 0 && current == 1)
                    {
                        int restore = original;
                        DwmSetInt(window, TransitionsForceDisabled, ref restore, sizeof(int));
                    }
                }
                catch { }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFileTime
        {
            public uint Low, High;
            public ulong Value { get { return ((ulong)High << 32) | Low; } }
        }

        private delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr parameter);
        private delegate IntPtr KeyboardHookCallback(int code,IntPtr message,IntPtr data);
        [DllImport("user32.dll",SetLastError=true)]
        private static extern IntPtr SetWindowsHookEx(int type,KeyboardHookCallback callback,IntPtr module,uint thread);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string module);

        // A short-lived input shield for the final covered activation interval.
        // Never logs or stores typed text, never touches another foreground app.
        internal sealed class KeyboardGuard : IDisposable
        {
            private readonly IntPtr target;
            private readonly Func<bool> covering;
            private readonly Action skip;
            private readonly KeyboardHookCallback callback;
            private IntPtr hook;
            private bool skipQueued;
            private readonly HashSet<int> suppressedKeys=new HashSet<int>();
            internal bool Installed { get { return hook!=IntPtr.Zero; } }
            internal KeyboardGuard(IntPtr verifiedTarget,Func<bool> isCovered,Action requestSkip)
            {
                target=verifiedTarget;covering=isCovered;skip=requestSkip;callback=OnKey;
                if(IsVerified(target))hook=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
            }
            // Kept separate for deterministic tests without injecting keystrokes
            // into a real desktop or conversation.
            internal bool HandleCoveredKey(IntPtr foreground,int message,int key,int flags,bool windowsHeld=false,bool controlHeld=false)
            {
                if(!Installed || foreground!=target || !covering())return false;
                if(message!=0x100 && message!=0x101 && message!=0x104 && message!=0x105)return false;
                // Preserve Alt-based window switching and the Windows shell.
                if(windowsHeld || (flags&0x20)!=0 || key==0x12 || key==0xA4 || key==0xA5 || key==0x5B || key==0x5C)return false;
                if(key==0x10 || key==0xA0 || key==0xA1 || key==0x11 || key==0xA2 || key==0xA3 || (controlHeld && key==0x1B))return false;
                if(message==0x101 || message==0x105)return suppressedKeys.Remove(key);
                suppressedKeys.Add(key);
                if((key==0x1B || key==0x0D || key==0x20) && message==0x100 && !skipQueued){skipQueued=true;skip();}
                return true;
            }
            private IntPtr OnKey(int code,IntPtr message,IntPtr data)
            {
                try {
                    if(code>=0 && data!=IntPtr.Zero && HandleCoveredKey(GetForegroundWindow(),message.ToInt32(),Marshal.ReadInt32(data),Marshal.ReadInt32(data,8),GetAsyncKeyState(0x5B)<0 || GetAsyncKeyState(0x5C)<0,GetAsyncKeyState(0x11)<0))return new IntPtr(1);
                } catch { }
                return CallNextHookEx(hook,code,message,data);
            }
            public void Dispose()
            { suppressedKeys.Clear();if(hook==IntPtr.Zero)return;IntPtr old=hook;hook=IntPtr.Zero;UnhookWindowsHookEx(old); }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowPlacement(IntPtr hwnd, ref NativePlacement placement);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPlacement(IntPtr hwnd, ref NativePlacement placement);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref NativeRect rect,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor,ref NativeMonitorInfo info);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int DwmGetRect(IntPtr hwnd, int attribute, out NativeRect rect, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int DwmGetInt(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
        private static extern int DwmSetInt(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();
        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(IntPtr hwnd, int command);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message,
            IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(IntPtr process, out NativeFileTime created,
            out NativeFileTime exited, out NativeFileTime kernel, out NativeFileTime user);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags,
            StringBuilder path, ref uint size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetApplicationUserModelId(IntPtr process, ref uint size, StringBuilder appId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetPackageFamilyName(IntPtr process, ref uint size, StringBuilder family);

        private static long WindowStyle(IntPtr hwnd, int index)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index).ToInt64() : GetWindowLong32(hwnd, index);
        }

        private static TargetIdentity FromSettings(Settings settings)
        {
            if (settings == null) return null;
            var identity = new TargetIdentity();
            if (!string.IsNullOrWhiteSpace(settings.Exe))
            {
                identity.Exe = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.Exe.Trim()));
                identity.Key = "exe:" + identity.Exe;
            }
            else
            {
                identity.AppId = (settings.AppId ?? "").Trim();
                if (identity.AppId.Length == 0) return null;
                int separator = identity.AppId.IndexOf('!');
                identity.Family = separator > 0 ? identity.AppId.Substring(0, separator) : "";
                identity.Key = "app:" + identity.AppId;
                if(bridgeTarget!=null && string.Equals(identity.AppId,BridgeClientIdentity.OfficialAppId,StringComparison.OrdinalIgnoreCase))
                {
                    identity.Bound=bridgeTarget;
                    identity.Key += ";owned:"+bridgeTarget.Pid+":"+bridgeTarget.Created;
                }
            }
            return identity;
        }

        private static bool IsMainCandidate(IntPtr hwnd, bool allowMinimized)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd)) return false;
            if (GetWindow(hwnd, GwOwner) != IntPtr.Zero) return false;
            if ((WindowStyle(hwnd, GwlStyle) & WsChild) != 0 ||
                (WindowStyle(hwnd, GwlExStyle) & WsExToolWindow) != 0) return false;
            int cloaked;
            if (DwmGetInt(hwnd, Cloaked, out cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
            if (IsIconic(hwnd)) return allowMinimized;
            NativeRect rect;
            return GetWindowRect(hwnd, out rect) && rect.Width >= 64 && rect.Height >= 64;
        }

        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd,StringBuilder text,int capacity);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr hwnd,string name);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr RemoveProp(IntPtr hwnd,string name);
        private static bool IsInitialHiddenCandidate(IntPtr hwnd,TargetIdentity expected)
        {
            if(expected==null||expected.Bound==null||hwnd==IntPtr.Zero||!IsWindow(hwnd)||IsWindowVisible(hwnd)||GetWindow(hwnd,GwOwner)!=IntPtr.Zero)return false;
            if((WindowStyle(hwnd,GwlStyle)&WsChild)!=0||(WindowStyle(hwnd,GwlExStyle)&WsExToolWindow)!=0)return false;
            var type=new StringBuilder(64);if(GetClassName(hwnd,type,type.Capacity)==0||type.ToString()!="Chrome_WidgetWin_1")return false;
            int cloaked;NativeRect rect;
            return DwmGetInt(hwnd,Cloaked,out cloaked,sizeof(int))==0&&cloaked==0&&GetWindowRect(hwnd,out rect)&&rect.Width>=128&&rect.Height>=128;
        }
        private static bool HasInitialHiddenLease(IntPtr hwnd,TargetIdentity expected)
        {
            return GetProp(hwnd,InitialHiddenMarker(expected))==new IntPtr(1);
        }
        private static string InitialHiddenMarker(TargetIdentity expected)
        {
            return "CodexDragon.InitialHidden.v25."+expected.Bound.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture)+"."+expected.Bound.Created.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        internal static bool CompleteInitialHiddenLease(IntPtr hwnd)
        {
            TargetIdentity expected;
            if(!VerifiedTargets.TryGetValue(hwnd,out expected)||!WindowMatches(hwnd,expected))return false;
            if(expected.Bound==null)return true;
            string marker=InitialHiddenMarker(expected);
            if(GetProp(hwnd,marker)!=new IntPtr(1))return true;
            // Only the initial-start capability is retired. The independently
            // armed NativeRestoreGuard lease remains until commit/rollback.
            RemoveProp(hwnd,marker);
            return GetProp(hwnd,marker)!=new IntPtr(1);
        }

        private static bool ProcessMatches(uint pid, TargetIdentity expected)
        {
            if (pid == 0 || pid == OwnPid || expected == null) return false;
            IntPtr process = OpenProcess(QueryLimitedInformation, false, pid);
            if (process == IntPtr.Zero) return false;
            try
            {
                NativeFileTime created, exited, kernel, user;
                uint exitCode;
                if (!GetExitCodeProcess(process, out exitCode) || exitCode != 259 ||
                    !GetProcessTimes(process, out created, out exited, out kernel, out user)) return false;
                // Direct CreateProcess launches of the signed Store executable
                // have no AppModel identity. Only the authenticated bridge's
                // exact client instance may use this fallback. Re-read its path
                // and generation on every operation; never cache authorization.
                if(expected.Bound!=null)
                {
                    if(pid!=expected.Bound.Pid)return false;
                    string actualPath=BridgeNative.ImagePath(process);
                    return actualPath.Length>0 && expected.Bound.Matches(pid,unchecked((long)created.Value),actualPath);
                }
                long now = CacheClock.ElapsedMilliseconds;
                ProcessIdentity cached;
                // Creation time is checked even on a cache hit, so a recycled PID
                // cannot inherit permission from a different application.
                if (ProcessCache.TryGetValue(pid, out cached) && cached.Created == created.Value &&
                    string.Equals(cached.Key, expected.Key, StringComparison.OrdinalIgnoreCase) &&
                    now - cached.CheckedAt < 1500) return cached.Matches;

                bool matches = false;
                if (expected.Exe != null)
                {
                    string path=BridgeNative.ImagePath(process);
                    if(path.Length>0)matches=string.Equals(Path.GetFullPath(path),expected.Exe,StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    uint size = 512;
                    var appId = new StringBuilder((int)size);
                    if (GetApplicationUserModelId(process, ref size, appId) == 0)
                        matches = string.Equals(appId.ToString(), expected.AppId, StringComparison.OrdinalIgnoreCase);
                    if (!matches && expected.Family.Length > 0)
                    {
                        size = 512;
                        var family = new StringBuilder((int)size);
                        if (GetPackageFamilyName(process, ref size, family) == 0)
                            matches = string.Equals(family.ToString(), expected.Family, StringComparison.OrdinalIgnoreCase);
                    }
                }
                // No process handles remain cached. Bound memory even if many
                // unrelated visible processes appear during a slow startup.
                if (ProcessCache.Count > 256) ProcessCache.Clear();
                ProcessCache[pid] = new ProcessIdentity
                {
                    Created = created.Value, Key = expected.Key, Matches = matches, CheckedAt = now
                };
                return matches;
            }
            finally { CloseHandle(process); }
        }

        private static bool WindowMatches(IntPtr hwnd, TargetIdentity expected)
        {
            uint pid;
            return IsWindow(hwnd) && GetWindowThreadProcessId(hwnd, out pid) != 0 && ProcessMatches(pid, expected);
        }

        private static bool IsVerified(IntPtr hwnd)
        {
            TargetIdentity expected;
            return VerifiedTargets.TryGetValue(hwnd, out expected) && WindowMatches(hwnd, expected);
        }

        private static bool IsOwnWindow(IntPtr hwnd)
        {
            uint pid;
            return hwnd != IntPtr.Zero && IsWindow(hwnd) &&
                GetWindowThreadProcessId(hwnd, out pid) != 0 && pid == OwnPid;
        }

        private static IntPtr EnterPhysicalCoordinates()
        { try{return SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){return IntPtr.Zero;} }
        private static void LeavePhysicalCoordinates(IntPtr previous)
        { if(previous!=IntPtr.Zero)try{SetThreadDpiAwarenessContext(previous);}catch(EntryPointNotFoundException){} }

        internal static bool FindCurrentStillValid(IntPtr target)
        { try{return IsVerified(target);}catch{return false;} }

        internal static void RegisterBridgeTarget(BridgeClientIdentity identity)
        {if(identity==null)throw new ArgumentNullException("identity");bridgeTarget=identity;}

        internal static CompactPlacement PrepareTargetInOverlay(IntPtr target,Window film,int timeoutMilliseconds = 2000)
        { return new CompactPlacement(target,film,timeoutMilliseconds); }

        private static bool TryGetCreationTime(uint pid, out ulong created)
        {
            created = 0;
            IntPtr process = OpenProcess(QueryLimitedInformation, false, pid);
            if (process == IntPtr.Zero) return false;
            try
            {
                NativeFileTime started, exited, kernel, user;
                uint exitCode;
                if (!GetExitCodeProcess(process, out exitCode) || exitCode != 259 ||
                    !GetProcessTimes(process, out started, out exited, out kernel, out user)) return false;
                created = started.Value;
                return true;
            }
            finally { CloseHandle(process); }
        }

        /// <summary>Returns only a window whose executable or package identity matches the configuration.</summary>
        internal static IntPtr FindTarget(Settings settings, uint activatedPid)
        {
            try
            {
                TargetIdentity expected = FromSettings(settings);
                if (expected == null) return IntPtr.Zero;
                IntPtr foreground = GetForegroundWindow(), best = IntPtr.Zero;
                IntPtr initialHidden=IntPtr.Zero;int hiddenCount=0;
                long bestScore = long.MinValue;
                var passMatches = new Dictionary<uint, bool>();
                EnumWindows(delegate(IntPtr hwnd, IntPtr parameter)
                {
                    bool visibleMain=IsMainCandidate(hwnd,true);
                    if (!visibleMain&&!IsInitialHiddenCandidate(hwnd,expected)) return true;
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    bool matches;
                    if (!passMatches.TryGetValue(pid, out matches))
                    {
                        matches = ProcessMatches(pid, expected);
                        passMatches[pid] = matches;
                    }
                    if (!matches) return true;
                    if(!visibleMain){hiddenCount++;if(HasInitialHiddenLease(hwnd,expected))initialHidden=hwnd;return true;}
                    NativeRect rect;
                    long area = GetWindowRect(hwnd, out rect) ? Math.Max(1L, (long)rect.Width * rect.Height) : 1;
                    // Foreground wins; otherwise prefer an actual large app UI.
                    // PID is a tie-breaker, not permission to trust an unrelated HWND.
                    long score = Math.Min(area, 100000000L) * 4 + (pid == activatedPid && activatedPid != 0 ? 1 : 0);
                    if (hwnd == foreground) score += 1000000000L;
                    if (!IsIconic(hwnd)) score += 500000000L;
                    if (score > bestScore) { bestScore = score; best = hwnd; }
                    return true;
                }, IntPtr.Zero);
                // Only the Host's initial-hide lease can authorize a hidden
                // target. Warm discovery and arbitrary hidden Electron utility
                // windows never gain this capability. Ambiguity fails closed.
                if(best==IntPtr.Zero&&hiddenCount==1&&initialHidden!=IntPtr.Zero&&IsInitialHiddenCandidate(initialHidden,expected)&&WindowMatches(initialHidden,expected)&&HasInitialHiddenLease(initialHidden,expected))best=initialHidden;
                if (best != IntPtr.Zero)
                {
                    if (VerifiedTargets.Count > 64) VerifiedTargets.Clear();
                    VerifiedTargets[best] = expected;
                }
                return best;
            }
            catch { return IntPtr.Zero; }
        }

        // A single bounded diagnostic at a failed handoff. Do not turn hidden
        // Electron windows into candidates: a normal client also owns hidden
        // background and tray windows. This records only counts, never titles
        // or page contents, and never changes another window's state.
        internal static string DescribeTargetAvailability(Settings settings)
        {
            try
            {
                TargetIdentity expected=FromSettings(settings);
                if(expected==null)return "targetIdentity=unavailable";
                int verified=0,visible=0,hidden=0,cloaked=0,eligible=0;
                var passMatches=new Dictionary<uint,bool>();
                EnumWindows(delegate(IntPtr hwnd,IntPtr parameter)
                {
                    uint pid;GetWindowThreadProcessId(hwnd,out pid);
                    bool matches;
                    if(!passMatches.TryGetValue(pid,out matches))
                    {matches=ProcessMatches(pid,expected);passMatches[pid]=matches;}
                    if(!matches)return true;
                    verified++;
                    if(IsWindowVisible(hwnd))visible++;else hidden++;
                    int cloak;
                    if(DwmGetInt(hwnd,Cloaked,out cloak,sizeof(int))==0 && cloak!=0)cloaked++;
                    if(IsMainCandidate(hwnd,true))eligible++;
                    return true;
                },IntPtr.Zero);
                return "verifiedWindows="+verified+"; visible="+visible+"; hidden="+hidden+
                    "; cloaked="+cloaked+"; eligible="+eligible;
            }
            catch {return "targetAvailability=unavailable";}
        }

        // Fixed native state only: no window title, path, page data or secret.
        // Called only by throttled wait/failure logs, never probes synchronously.
        internal static string DescribeReadiness(IntPtr target)
        {
            try {
                bool exists=IsWindow(target),verified=IsVerified(target);
                int cloak;bool cloaked=exists&&DwmGetInt(target,Cloaked,out cloak,sizeof(int))==0&&cloak!=0;
                uint pid=0;ulong created=0;
                bool identity=exists&&GetWindowThreadProcessId(target,out pid)!=0&&TryGetCreationTime(pid,out created);
                lock(ProbeGate){
                    bool same=identity&&probedWindow==target&&probedPid==pid&&probedCreated==created;
                    long age=same?Math.Max(0,CacheClock.ElapsedMilliseconds-probedAt):-1;
                    return "exists="+exists+"; verified="+verified+"; visible="+(exists&&IsWindowVisible(target))+
                        "; minimized="+(exists&&IsIconic(target))+"; cloaked="+cloaked+"; mainCandidate="+IsMainCandidate(target,false)+
                        "; sameProbe="+same+"; probeAgeMs="+age+"; probeResponsive="+(same&&probedResponsive)+"; probePending="+probePending;
                }
            }catch{return "nativeReadiness=unavailable";}
        }

        /// <summary>
        /// Checks native visibility and responsiveness only. This cannot prove
        /// an Electron page has finished its own application-level initialization.
        /// </summary>
        internal static bool IsReady(IntPtr target)
        {
            try
            {
                if (!IsVerified(target) || !IsMainCandidate(target, false)) return false;
                uint pid;ulong created;
                if(GetWindowThreadProcessId(target,out pid)==0 || !TryGetCreationTime(pid,out created))return false;
                bool ready;long now=CacheClock.ElapsedMilliseconds;
                // Responsiveness is only a short-lived signal. Authorization is
                // rechecked above on every call and again at each native action.
                // A hung target must never spend its bounded probe on our dispatcher.
                lock(ProbeGate) {
                    bool same=probedWindow==target && probedPid==pid && probedCreated==created;
                    // The caller polls at 250ms. Give a completed response
                    // one polling interval of scheduling slack; identity never
                    // shares this cache. This avoids resetting readiness forever.
                    ready=same && probedResponsive && now-probedAt<=500;
                    if(!probePending && (!same || now-probedAt>=100)) {
                        probePending=true;requestedWindow=target;requestedPid=pid;requestedCreated=created;
                        try {
                            if(!probeWorkerRunning) {
                                probeWorkerRunning=true;
                                new Thread(DrainResponsivenessProbes){IsBackground=true,Name="Single native readiness probe"}.Start();
                            }
                            ProbeSignal.Set();
                        }catch{probePending=false;probeWorkerRunning=false;return false;}
                    }
                }
                return ready;
            }
            catch { return false; }
        }

        internal static bool HandoffProbeLease(long armedAt,long now,bool sameProbe,bool responsive,long observedAt)
        {return armedAt>=0&&now>=armedAt&&now-armedAt<=6000&&!(sameProbe&&!responsive&&observedAt>=armedAt);}

        private static bool SameProbeTarget(IntPtr target,uint pid,ulong created)
        {
            uint actualPid;ulong actualCreated;
            return IsWindow(target) && GetWindowThreadProcessId(target,out actualPid)!=0 && actualPid==pid &&
                TryGetCreationTime(actualPid,out actualCreated) && actualCreated==created;
        }
        private static void DrainResponsivenessProbes()
        {
            while(true) {
                // A dedicated, single worker is independent of blocked pipe
                // ThreadPool work. It exits after one idle second and never
                // keeps the launcher process alive or queues a second probe.
                ProbeSignal.WaitOne(1000);
                IntPtr window;uint pid;ulong created;
                lock(ProbeGate) {
                    if(!probePending){probeWorkerRunning=false;return;}
                    window=requestedWindow;pid=requestedPid;created=requestedCreated;
                }
                ProbeResponsiveness(window,pid,created);
            }
        }
        private static void ProbeResponsiveness(IntPtr target,uint pid,ulong created)
        {
            bool responsive=false;
            try {
                UIntPtr result;
                responsive=SameProbeTarget(target,pid,created) &&
                    SendMessageTimeout(target,0,IntPtr.Zero,IntPtr.Zero,SmtoBlock|SmtoAbortIfHung|SmtoErrorOnExit,150,out result)!=IntPtr.Zero &&
                    SameProbeTarget(target,pid,created);
            }catch { }
            finally {lock(ProbeGate){probedWindow=target;probedPid=pid;probedCreated=created;probedResponsive=responsive;probedAt=CacheClock.ElapsedMilliseconds;probePending=false;}}
        }

        /// <summary>
        /// Places only our overlay. Native coordinates remain in the same DPI
        /// context; WPF receives normal HWND size notifications after placement.
        /// </summary>
        internal static bool PlaceOverTarget(Window splash, IntPtr target)
        {
            if (splash == null || !splash.CheckAccess()) return false;
            IntPtr previousDpi = IntPtr.Zero;
            try
            {
                // An owned launcher overlay can be used as a geometry reference.
                // Other processes still require a previously verified app identity.
                if ((!IsOwnWindow(target) && !IsVerified(target)) || !IsWindow(target) || IsIconic(target)) return false;
                IntPtr overlay = new WindowInteropHelper(splash).Handle;
                if (overlay == IntPtr.Zero || overlay == target) return false;
                // DWM bounds are physical pixels; explicitly opt this native
                // operation into physical per-monitor coordinates when supported.
                try { previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4)); }
                catch (EntryPointNotFoundException) { }
                NativeRect rect;
                if (previousDpi == IntPtr.Zero || DwmGetRect(target, ExtendedFrameBounds, out rect, Marshal.SizeOf(typeof(NativeRect))) != 0)
                {
                    // Both GetWindowRect and SetWindowPos use this thread's
                    // current coordinate context on the compatibility path.
                    if (!GetWindowRect(target, out rect)) return false;
                }
                if (rect.Width < 64 || rect.Height < 64) return false;
                return SetWindowPos(overlay, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height,
                    SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
            }
            catch { return false; }
            finally
            {
                if (previousDpi != IntPtr.Zero)
                {
                    try { SetThreadDpiAwarenessContext(previousDpi); }
                    catch (EntryPointNotFoundException) { }
                }
            }
        }

        /// <summary>
        /// Raises/restores a verified target without changing its topmost style.
        /// Foreground focus is best effort and may legitimately be denied by Windows.
        /// The caller must already have its own topmost overlay visible.
        /// </summary>
        internal static bool RaiseTargetBehindOverlay(IntPtr target, bool finalFocus = false)
        {
            try
            {
                if (!IsVerified(target) || !IsMainCandidate(target, true)) return false;
                if (IsIconic(target)) ShowWindowAsync(target, SwRestore);
                bool positioned = SetWindowPos(target, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder | SwpAsyncWindowPos);
                bool focused = finalFocus && SetForegroundWindow(target);
                return positioned || focused;
            }
            catch { return false; }
        }

        internal static bool IsForegroundTarget(IntPtr target)
        {
            return target != IntPtr.Zero && GetForegroundWindow() == target && IsVerified(target);
        }
        internal static bool IsForegroundOwnWindow(Window window)
        {
            if(window==null||!window.CheckAccess()||!window.IsVisible)return false;
            IntPtr handle=new WindowInteropHelper(window).Handle;
            return IsOwnWindow(handle)&&GetForegroundWindow()==handle;
        }
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        internal static string ForegroundCategory(Window film,Window pulse,IntPtr target)
        {
            // No titles, process paths or input content enter diagnostics.
            IntPtr foreground=GetForegroundWindow();
            if(foreground==IntPtr.Zero)return "none";
            if(film!=null&&film.CheckAccess()&&new WindowInteropHelper(film).Handle==foreground)return "film";
            if(pulse!=null&&pulse.CheckAccess()&&new WindowInteropHelper(pulse).Handle==foreground)return "pulse";
            if(foreground==target&&IsVerified(target))return "target";
            if(foreground==GetShellWindow()||foreground==GetDesktopWindow())return "shell";
            return IsOwnWindow(foreground)?"own-other":"other";
        }

        // Pre-render the replacement below the still-playing movie. Both HWNDs
        // must belong to this process; never reorders unrelated applications.
        internal static bool PlaceBehindOwnWindow(Window replacement, Window movie)
        {
            try {
                if(replacement==null || movie==null || !replacement.CheckAccess() || !movie.CheckAccess())return false;
                IntPtr back=new WindowInteropHelper(replacement).Handle, front=new WindowInteropHelper(movie).Handle;
                if(back==front || !IsOwnWindow(back) || !IsOwnWindow(front))return false;
                return SetWindowPos(back,front,0,0,0,0,SwpNoMove|SwpNoSize|SwpNoActivate|SwpNoOwnerZOrder);
            } catch { return false; }
        }

        internal static bool RaiseOwnCover(Window cover)
        {
            try {
                if(cover==null || !cover.CheckAccess())return false;
                IntPtr handle=new WindowInteropHelper(cover).Handle;
                if(!IsOwnWindow(handle))return false;
                return SetWindowPos(handle,new IntPtr(-1),0,0,0,0,SwpNoMove|SwpNoSize|SwpNoActivate|SwpNoOwnerZOrder);
            } catch { return false; }
        }

        // The launcher owns these windows from creation to disposal; suppress
        // Windows show/hide effects only for them. Never touches global settings.
        internal static bool DisableOwnTransitions(Window window)
        {
            try {
                if(window==null || !window.CheckAccess())return false;
                IntPtr handle=new WindowInteropHelper(window).Handle;
                if(!IsOwnWindow(handle))return false;
                int disabled=1;
                return DwmSetInt(handle,TransitionsForceDisabled,ref disabled,sizeof(int))==0;
            } catch { return false; }
        }

        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
        [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr result,IntPtr first,IntPtr second,int mode);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region,int x,int y);
        [DllImport("gdi32.dll")] private static extern int GetRgnBox(IntPtr region,out NativeRect bounds);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window,IntPtr region,bool redraw);
        [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window,IntPtr region);

        // Chromium does not count non-rectangular native covers as occluders.
        // Remove only our film's top-left physical pixel, outside the target's
        // six-pixel inset. This keeps the opaque WPF video pipeline and avoids
        // emulating focus or changing the official client's window/settings.
        // Reapply only on the film's actual size/DPI change, never per frame.
        internal static bool PrepareOwnMediaCover(Window window)
        {
            IntPtr previous=IntPtr.Zero,region=IntPtr.Zero,corner=IntPtr.Zero,observed=IntPtr.Zero;
            try
            {
                if(window==null||!window.CheckAccess()||window.AllowsTransparency)return false;
                IntPtr handle=new WindowInteropHelper(window).Handle;
                if(!IsOwnWindow(handle))return false;
                previous=EnterPhysicalCoordinates();
                NativeRect bounds;
                if(!GetWindowRect(handle,out bounds)||bounds.Width<=128||bounds.Height<=128)return false;
                region=CreateRectRgn(0,0,bounds.Width,bounds.Height);
                corner=CreateRectRgn(0,0,1,1);
                observed=CreateRectRgn(0,0,0,0);
                if(region==IntPtr.Zero||corner==IntPtr.Zero||observed==IntPtr.Zero)return false;
                if(CombineRgn(region,region,corner,4)!=3)return false; // RGN_DIFF -> COMPLEXREGION
                if(SetWindowRgn(handle,region,true)==0)return false;
                // Successful SetWindowRgn transfers ownership to USER32. Never
                // delete or reuse the adopted HRGN; replacing/closing the HWND
                // releases it. Only our temporary query/corner handles remain.
                region=IntPtr.Zero;
                NativeRect confirmed;
                return GetWindowRgn(handle,observed)==3&&GetRgnBox(observed,out confirmed)==3&&
                    confirmed.Left==0&&confirmed.Top==0&&confirmed.Right==bounds.Width&&confirmed.Bottom==bounds.Height&&
                    !PtInRegion(observed,0,0)&&PtInRegion(observed,1,0)&&PtInRegion(observed,0,1)&&PtInRegion(observed,6,6);
            }
            catch { return false; }
            finally
            {
                if(region!=IntPtr.Zero)DeleteObject(region);
                if(corner!=IntPtr.Zero)DeleteObject(corner);
                if(observed!=IntPtr.Zero)DeleteObject(observed);
                LeavePhysicalCoordinates(previous);
            }
        }

        // Prepare focus while still covered. Deliberately never calls ShowWindow
        // or restores/minimizes a target; geometry was settled earlier.
        internal static bool FocusReadyTarget(IntPtr target)
        {
            try {
                if(!IsReady(target))return false;
                SetWindowPos(target,IntPtr.Zero,0,0,0,0,
                    SwpNoMove|SwpNoSize|SwpNoActivate|SwpNoOwnerZOrder|SwpAsyncWindowPos);
                return SetForegroundWindow(target);
            } catch { return false; }
        }

        internal static void FlushCompositor()
        {
            // OpaqueCoverReady already requires rendered frames. This optional
            // driver fence must never turn impact/cleanup into an unbounded UI
            // wait. At most one background call may be outstanding, even if a
            // display driver stops answering. No thread is joined on shutdown.
            if(Interlocked.CompareExchange(ref compositorFencePending,1,0)!=0)return;
            try {new Thread(delegate(){try{DwmFlush();}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}finally{Interlocked.Exchange(ref compositorFencePending,0);}}){IsBackground=true,Name="Optional compositor fence"}.Start();}
            catch(Exception error){Interlocked.Exchange(ref compositorFencePending,0);LauncherDiagnostics.Write("handoff-latest.log","Compositor fence unavailable: "+error.GetType().Name);}
        }

        internal static bool IsCoveringVirtualDesktop(Window splash)
        {
            if(splash==null || !splash.CheckAccess())return false;
            IntPtr previous=IntPtr.Zero;
            try {
                IntPtr window=new WindowInteropHelper(splash).Handle;
                if(!IsOwnWindow(window) || !IsWindowVisible(window))return false;
                try { previous=SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch(EntryPointNotFoundException) { }
                NativeRect rect;
                if(!GetWindowRect(window,out rect))return false;
                int left=GetSystemMetrics(76), top=GetSystemMetrics(77);
                return rect.Left<=left && rect.Top<=top && rect.Right>=left+GetSystemMetrics(78) && rect.Bottom>=top+GetSystemMetrics(79);
            } catch { return false; }
            finally {
                if(previous!=IntPtr.Zero)try{SetThreadDpiAwarenessContext(previous);}catch(EntryPointNotFoundException){}
            }
        }

        /// <summary>
        /// Covers the entire virtual desktop, including negative monitor origins.
        /// Call once the launcher's HWND exists and before activating the target.
        /// Does not change any other application's placement or window styles.
        /// </summary>
        internal static bool CoverVirtualDesktop(Window splash)
        {
            if (splash == null || !splash.CheckAccess()) return false;
            IntPtr previousDpi = IntPtr.Zero;
            try
            {
                IntPtr overlay = new WindowInteropHelper(splash).Handle;
                if (overlay == IntPtr.Zero || !IsOwnWindow(overlay)) return false;
                try { previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4)); }
                catch (EntryPointNotFoundException) { }
                // SM_*VIRTUALSCREEN. The native getter and setter execute under
                // one DPI context, including on systems lacking per-monitor v2.
                int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
                int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
                if (width <= 0 || height <= 0) return false;
                splash.WindowStartupLocation = WindowStartupLocation.Manual;
                return SetWindowPos(overlay, IntPtr.Zero, left, top, width, height,
                    SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
            }
            catch { return false; }
            finally
            {
                if (previousDpi != IntPtr.Zero)
                {
                    try { SetThreadDpiAwarenessContext(previousDpi); }
                    catch (EntryPointNotFoundException) { }
                }
            }
        }

        /// <summary>
        /// Temporarily suppresses DWM transitions for this verified target alone.
        /// Dispose on every exit path to restore its previous setting. This does
        /// not suppress app-internal animation or transitions already in progress.
        /// </summary>
        internal static IDisposable SuppressTransitions(IntPtr target)
        {
            try
            {
                TargetIdentity expected;
                uint pid;
                ulong created;
                if (!VerifiedTargets.TryGetValue(target, out expected) || !WindowMatches(target, expected) ||
                    GetWindowThreadProcessId(target, out pid) == 0 || !TryGetCreationTime(pid, out created)) return new EmptyScope();
                int original;
                if (DwmGetInt(target, TransitionsForceDisabled, out original, sizeof(int)) != 0 || original != 0) return new EmptyScope();
                int disabled = 1;
                if (DwmSetInt(target, TransitionsForceDisabled, ref disabled, sizeof(int)) != 0) return new EmptyScope();
                return new TransitionScope(target, expected, pid, created, original);
            }
            catch { return new EmptyScope(); }
        }
    }
}
