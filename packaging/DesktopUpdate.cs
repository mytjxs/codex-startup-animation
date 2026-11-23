using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using CodexDragonWallpaper;

namespace CodexDragonPublic {
    internal static class DesktopUpdate {
        const string DownloadUrl="https://persistent.oaistatic.com/codex-app-prod/ChatGPT-x64.msix";
        const string StoreUrl="ms-windows-store://pdp/?ProductId=9PLM9XGG6VKS";
        const string Publisher="CN=50BDFD77-8903-4850-9FFE-6E8522F64D5B";
        const string Title="ChatGPT 更新";
        const long MaxBytes=4L*1024*1024*1024;

        internal static int Run(bool checkOnly) {
            using(var gate=new Mutex(false,@"Local\CodexDragon.ChatGPT.Update")) {
                bool held;
                try{held=gate.WaitOne(0);}catch(AbandonedMutexException){held=true;}
                if(!held)return 75;
                try {
                    if(checkOnly){CheckVersion();return 0;}
                    int result=1;
                    using(var cancelled=new CancellationTokenSource())
                    using(var form=new Form{Text=Title,Width=420,Height=155,StartPosition=FormStartPosition.CenterScreen,
                        FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false})
                    using(var status=new Label{Dock=DockStyle.Top,Height=44,TextAlign=ContentAlignment.MiddleCenter,Text="正在检查官方版本…"})
                    using(var bar=new ProgressBar{Dock=DockStyle.Top,Style=ProgressBarStyle.Marquee})
                    using(var cancel=new Button{Dock=DockStyle.Bottom,Text="取消"}) {
                        form.Controls.Add(bar);
                        form.Controls.Add(status);
                        form.Controls.Add(cancel);
                        bool finished=false;
                        form.FormClosing+=(sender,args)=>{if(!finished){args.Cancel=true;cancel.PerformClick();}};
                        cancel.Click+=(sender,args)=>{cancel.Enabled=false;status.Text="正在取消…";cancelled.Cancel();};
                        form.Shown+=async (sender,args)=>{
                            try {
                                var versions=await Task.Run(()=>CheckVersion(cancelled.Token));
                                cancelled.Token.ThrowIfCancellationRequested();
                                if(versions.Item2<=versions.Item1) {
                                    MessageBox.Show(form,"当前已安装 "+versions.Item1+"，官方安装包当前为 "+versions.Item2+"。",Title);
                                } else if(MessageBox.Show(form,"当前版本 "+versions.Item1+"。下载 "+versions.Item2+" 并打开 Windows 安装程序？",
                                    Title,MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes) {
                                    bar.Style=ProgressBarStyle.Continuous;
                                    status.Text="正在下载官方安装包…";
                                    var progress=new Progress<int>(value=>{if(!finished)bar.Value=value;});
                                    string file=await Task.Run(()=>Download(versions.Item2,progress,cancelled.Token));
                                    cancelled.Token.ThrowIfCancellationRequested();
                                    Open(file);
                                    PortableLaunch.Record("UPDATE_INSTALLER_OPENED "+versions.Item2);
                                }
                                result=0;
                            }catch(OperationCanceledException){result=0;}
                            catch(Exception error) {
                                PortableLaunch.Record("UPDATE_FAILED "+error.GetType().Name);
                                if(!cancelled.IsCancellationRequested &&
                                    MessageBox.Show(form,"更新检查、下载或安装程序打开失败。要打开 Microsoft Store 吗？",
                                        Title,MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes) {
                                    try{Open(StoreUrl);}catch(Exception){MessageBox.Show(form,"无法打开 Microsoft Store。",Title);}
                                }
                            }finally{finished=true;form.Close();}
                        };
                        Application.Run(form);
                    }
                    return result;
                }catch(Exception error){PortableLaunch.Record("UPDATE_FAILED "+error.GetType().Name);return 1;}
                finally{gate.ReleaseMutex();}
            }
        }

        static void Open(string target) {
            using(Process.Start(new ProcessStartInfo(target){UseShellExecute=true})){}
        }

        static HttpWebRequest Request(string method) {
            var request=(HttpWebRequest)WebRequest.Create(DownloadUrl);
            request.Method=method;
            request.AllowAutoRedirect=false;
            request.Timeout=15000;
            request.ReadWriteTimeout=30000;
            return request;
        }

        static Tuple<Version,Version> CheckVersion(CancellationToken token=default(CancellationToken)) {
            var installed=new Version(PortableLaunch.FindOfficialPackage(false).Split('_')[1]);
            var request=Request("HEAD");
            using(token.Register(request.Abort))
            using(var response=(HttpWebResponse)request.GetResponse()) {
                Version latest;
                if(response.StatusCode!=HttpStatusCode.OK ||
                    response.Headers["x-ms-meta-package_identity"]!="OpenAI.Codex" ||
                    response.Headers["x-ms-meta-architecture"]!="x64" ||
                    !Version.TryParse(response.Headers["x-ms-meta-package_version"],out latest) || latest.Revision<0)
                    throw new InvalidDataException("Unexpected official package metadata.");
                PortableLaunch.Record((latest>installed?"UPDATE_AVAILABLE ":"UPDATE_CURRENT ")+installed+" "+latest);
                return Tuple.Create(installed,latest);
            }
        }

        static string Download(Version version,IProgress<int> progress,CancellationToken token) {
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexDragon","Updates",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            for(string path=folder;path!=null;path=Path.GetDirectoryName(path))
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked update path.");
            string file=Path.Combine(folder,"ChatGPT-"+version+"-x64.msix");
            var request=Request("GET");
            try {
                using(token.Register(request.Abort))
                using(var response=(HttpWebResponse)request.GetResponse())
                using(var source=response.GetResponseStream())
                using(var target=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    long length=response.ContentLength;
                    if(response.StatusCode!=HttpStatusCode.OK || length<=0 || length>MaxBytes)
                        throw new InvalidDataException("Unexpected package length.");
                    byte[] buffer=new byte[128*1024];
                    long received=0;
                    int previous=-1;
                    for(int count;(count=source.Read(buffer,0,buffer.Length))>0;) {
                        token.ThrowIfCancellationRequested();
                        received+=count;
                        if(received>length)throw new InvalidDataException("Package exceeds declared length.");
                        target.Write(buffer,0,count);
                        int percent=(int)(received*100/length);
                        if(percent!=previous){progress.Report(percent);previous=percent;}
                    }
                    if(received!=length)throw new EndOfStreamException();
                    target.Flush(true);
                }
                token.ThrowIfCancellationRequested();
                VerifyPackage(file,version);
                if(new Version(PortableLaunch.FindOfficialPackage(false).Split('_')[1])>=version)
                    throw new InvalidOperationException("This version has already been installed.");
                return file;
            }catch {
                if(File.Exists(file))File.Delete(file);
                token.ThrowIfCancellationRequested();
                throw;
            }
        }

        internal static void VerifyPackage(string file,Version version) {
            Native.VerifySignature(file,cert=>String.Equals(cert.Subject,Publisher,StringComparison.OrdinalIgnoreCase));
            using(var archive=ZipFile.OpenRead(file)) {
                var manifest=archive.GetEntry("AppxManifest.xml");
                if(manifest==null || manifest.Length>1024*1024)throw new InvalidDataException("Invalid package manifest.");
                using(var reader=XmlReader.Create(manifest.Open(),new XmlReaderSettings{
                    XmlResolver=null,DtdProcessing=DtdProcessing.Prohibit,MaxCharactersInDocument=1024*1024,CloseInput=true})) {
                    XNamespace ns="http://schemas.microsoft.com/appx/manifest/foundation/windows10";
                    var identity=XDocument.Load(reader).Root.Element(ns+"Identity");
                    if(identity==null || (string)identity.Attribute("Name")!="OpenAI.Codex" ||
                        (string)identity.Attribute("Publisher")!=Publisher ||
                        (string)identity.Attribute("ProcessorArchitecture")!="x64" ||
                        (string)identity.Attribute("Version")!=version.ToString())
                        throw new InvalidDataException("Unexpected signed package identity.");
                }
            }
        }
    }
}
