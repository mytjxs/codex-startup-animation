using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace DragonLauncher {
internal static class WallpaperControls {
    static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
    static TextBlock message;
    static readonly List<Button> buttons=new List<Button>();
    static bool busy;
    static bool closeRequested;
    static Window window;
    [STAThread] static void Main(string[] args) {
        var app=new Application();
        var panel=new StackPanel { Margin=new Thickness(24) };
        panel.Children.Add(new TextBlock { Text="Codex 动态壁纸", FontSize=22, Margin=new Thickness(0,0,0,14) });
        message=new TextBlock { Text="只控制龙娘启动器接入的壁纸\n不会关闭或重启 Codex", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,16) };
        panel.Children.Add(message);
        Add(panel,"查看当前状态","refresh"); Add(panel,"暂停动画","pause"); Add(panel,"继续播放","reveal");
        Add(panel,"关闭壁纸并恢复界面","off"); Add(panel,"下次启动启用壁纸","on");
        panel.Children.Add(new TextBlock { Text="重新启用后：保存工作并正常退出 Codex，再使用桌面的龙娘启动图标。\n保持这个窗口打开并不是运行壁纸的必要条件。", TextWrapping=TextWrapping.Wrap, FontSize=12, Margin=new Thickness(0,16,0,0) });
        window=new Window { Title="Codex 壁纸控制", Content=panel, Width=410, SizeToContent=SizeToContent.Height, ResizeMode=ResizeMode.NoResize, WindowStartupLocation=WindowStartupLocation.CenterScreen };
        window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(busy){closeRequested=true;e.Cancel=true;message.Text="正在完成当前操作 随后自动关闭";}};
        app.Run(window);
    }
    static void Add(Panel panel,string title,string command) {
        var button=new Button {Content=title,Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,3,0,3)};
        button.Click+=delegate {Run(command);}; buttons.Add(button); panel.Children.Add(button);
    }
    internal static void SetEnabled(string root,bool enabled) {
        using(var maintenance=MaintenanceLaunchLease.Acquire(root,2000)) {
        MaintenanceLaunchLease.AssertInstallationReady(root);
        string path=Path.Combine(root,"launcher.config.xml");
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Configuration link rejected");
        var settings=new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=65536};
        XDocument config;using(var reader=XmlReader.Create(path,settings)){config=XDocument.Load(reader);}
        if(config.Root==null || config.Root.Name!="launcher")throw new InvalidDataException("Invalid configuration");
        config.Root.SetElementValue("wallpaperEnabled",enabled?"true":"false");
        string pending=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {using(var stream=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None)){config.Save(stream);stream.Flush(true);} File.Replace(pending,path,null);}
        finally {if(File.Exists(pending))File.Delete(pending);}
        }
    }
    static async void Run(string command) {
        if(busy)return;
        busy=true;foreach(var button in buttons)button.IsEnabled=false;
        message.Text="正在处理壁纸操作";
        try {
            message.Text=await Task.Run(()=>Execute(command));
        } catch {message.Text="未能完成操作\nCodex 没有被关闭或重启 请稍后重试";}
        finally {busy=false;foreach(var button in buttons)button.IsEnabled=true;if(closeRequested)window.Close();}
    }
    internal static string Describe(BridgeStatus state) {
        return "上次确认状态\n"+DescribeSnapshot(state);
    }
    internal static string DescribeRefresh(BridgeStatus state) {
        if(state==null)return "暂时无法取得新的播放状态\n尚不能确认壁纸已挂载或播放";
        if(state.RefreshCode==1)return "刚刚确认\n"+DescribeSnapshot(state);
        if(state.RefreshCode==3)return "状态刷新超时\n"+Describe(state);
        if(state.RefreshCode==4)return "状态刷新失败\n"+Describe(state);
        return "暂时无法取得新的播放状态\n"+Describe(state);
    }
    static string DescribeSnapshot(BridgeStatus state) {
        if(state==null)return "暂时没有已确认的壁纸状态";
        if(state.Disabled)return "本次壁纸已关闭\n重新启用需在下次正常启动 Codex 后生效";
        if(state.MediaCode>=100)return "壁纸接入未成功\nCodex 仍可正常使用 壁纸控制不会重启客户端";
        if(state.MediaCode==7)return "当前客户端布局不适合接入壁纸\n已保留原有界面";
        if(state.MediaCode==8)return "壁纸状态异常\n可关闭壁纸后在下次启动时重试";
        if(state.MediaCode==6)return "壁纸已释放\n重新接入需在下次启动 Codex 后生效";
        // A ready/revealed command snapshot cannot prove that video actually
        // attached to the visible workspace or advanced after its first frame.
        if(state.RefreshCode==1){
            WallpaperReadinessState readiness=WallpaperReadiness.Inspect(state);
            return readiness==WallpaperReadinessState.Unknown && state.MediaCode==5?"壁纸已暂停":WallpaperReadiness.Describe(readiness);
        }
        if(state.MediaCode==5)return "壁纸已暂停";
        if(state.Ready && state.Revealed)return "上次已发送壁纸显示指令\n尚不能确认当前挂载或播放状态";
        if(state.Ready)return "上次确认媒体已准备\n尚不能确认当前播放状态";
        return "壁纸正在准备";
    }
    static string Execute(string command) {
        bool savedDisabled=false;
        try {
            if(command=="on") {SetEnabled(Root,true);return "已设置下次启动启用壁纸\n当前 Codex 保持原状";}
            if(command=="off"){SetEnabled(Root,false);savedDisabled=true;}
            using(BridgeSession session=BridgeActivation.Discover(Root,0,null)) {
                if(session==null)return command=="off"?"已关闭下次启动的壁纸\n当前没有可连接的壁纸会话":"当前没有可连接的壁纸会话\n请在下次正常退出 Codex 后通过龙娘图标启动";
                BridgeStatus state=command=="refresh"?session.Refresh(1800):session.Control(command=="off"?"disable":command,1800);
                if(command=="off")return "关闭指令已发送\n界面将恢复 壁纸将释放";
                if(command=="refresh")return DescribeRefresh(state);
                if(state.Disabled||state.MediaCode>=100||state.MediaCode==7)return Describe(state);
                if(command=="pause")return "暂停指令已发送";
                if(command=="reveal")return "继续播放指令已发送";
                return Describe(state);
            }
        } catch {return savedDisabled?"已关闭下次启动的壁纸\n本次关闭指令未能确认 请稍后重试":"未能完成操作\nCodex 没有被关闭或重启 请稍后重试";}
    }
}
}
