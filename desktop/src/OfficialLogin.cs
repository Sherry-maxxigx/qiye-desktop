using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SuperAutoDesktop {
    internal static class LocalServices {
        private static Dictionary<string,object> Read(string root) {
            string file=Path.Combine(root,"data","service-endpoint.json");
            if(!File.Exists(file))throw new IOException("请先从安装目录中的 Qiye.exe 启动，完成运行环境准备。");
            return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file, System.Text.Encoding.UTF8));
        }
        internal static Uri Address(string root) {
            var config=Read(root);int port;
            if(!Int32.TryParse(Convert.ToString(config["apiPort"]),out port)||port<1024||port>65535)throw new IOException("本机服务配置无效，请重新运行 Qiye.exe。");
            return new Uri("http://127.0.0.1:"+port);
        }
        internal static string InstallationId(string root){return Convert.ToString(Read(root)["installationId"]);}
    }

    internal static class DesktopBrowserRuntime {
        private static readonly Dictionary<string,Task<CoreWebView2Environment>> environments=new Dictionary<string,Task<CoreWebView2Environment>>(StringComparer.OrdinalIgnoreCase);
        internal static Task<CoreWebView2Environment> Get(string root,bool test) {
            string profile=Path.Combine(root,"data",test?"browser-test-profile":"browser-profile");
            Task<CoreWebView2Environment> current;
            if(!environments.TryGetValue(profile,out current)||current.IsFaulted||current.IsCanceled) {
                current=CoreWebView2Environment.CreateAsync(null,profile,null);environments[profile]=current;
            }
            return current;
        }
        internal static async void Warm(string root) {try{await Get(root,false);}catch(Exception){}}
        internal static void LogTiming(string root,string stage,DateTime started) {
            try {File.AppendAllText(Path.Combine(root,"logs","desktop-performance.log"),DateTime.Now.ToString("s")+" "+stage+" elapsedMs="+(int)DateTime.UtcNow.Subtract(started).TotalMilliseconds+Environment.NewLine);}
            catch(IOException){}
        }
    }

    internal sealed class OfficialLoginView : UserControl {
        internal readonly WebView2 Browser = new WebView2 {Dock=DockStyle.Fill};
        private const string LoginUrl="https://passport2.chaoxing.com/login?newversion=true&refer=https%3A%2F%2Fi.chaoxing.com%2F";
        private const string QrCss="body{background:#faf9f6!important;min-width:0!important} .lg-container{width:430px!important;max-width:95vw!important;margin:36px auto!important;position:relative!important;top:auto!important;left:auto!important;transform:none!important;min-height:390px!important} #leftdiv{display:none!important} #rightdiv{display:block!important;float:none!important;position:relative!important;left:auto!important;right:auto!important;margin:24px auto!important;border:0!important} .lg-top{display:none!important}";
        private readonly string root;
        private readonly Func<Dictionary<string,string>,Task> synchronize;
        private readonly Timer timer=new Timer {Interval=1000};
        private readonly Label status;
        private readonly Button refresh, retry, password, back;
        private bool ready, closing, syncing, completed, allowProbe=true, passwordMode;
        private Task initialization;
        private DateTime syncStarted;
        private string syncStatus="正在读取登录状态";
        internal bool Ready {get {return ready;}}
        internal string StatusText {get {return status.Text;}}
        internal OfficialLoginView(string installRoot,Func<Dictionary<string,string>,Task> sync,Action leave) {
            root=installRoot;synchronize=sync;Dock=DockStyle.Fill;Name="OfficialQrLogin";BackColor=DesktopTheme.Pale;
            var header=new Panel {Dock=DockStyle.Top,Height=145,Padding=new Padding(26,16,26,10),BackColor=DesktopTheme.Sidebar};
            var title=new Label {Text="扫码登录学习通",Dock=DockStyle.Top,Height=36,Font=new Font("Microsoft YaHei UI",17,FontStyle.Bold),ForeColor=DesktopTheme.Ink};
            status=new Label {Text="正在加载官方二维码…",Dock=DockStyle.Top,Height=29,AutoEllipsis=true,ForeColor=DesktopTheme.Muted};
            var actions=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=41,WrapContents=false};
            refresh=Button("刷新二维码",async()=>{passwordMode=false;password.Text="账号密码登录";allowProbe=true;if(ready)Browser.CoreWebView2.Navigate(LoginUrl);else await Initialize();});
            retry=Button("重试同步",async()=>{allowProbe=true;await Probe(true);});
            password=Button("账号密码登录",async()=>{passwordMode=!passwordMode;password.Text=passwordMode?"返回扫码":"账号密码登录";await StyleLogin();});
            back=Button("返回课程",()=>{leave();return Task.FromResult(0);});
            actions.Controls.AddRange(new Control[]{refresh,retry,password,back});
            header.Controls.Add(status);header.Controls.Add(title);header.Controls.Add(actions);
            Controls.Add(Browser);Controls.Add(header);Browser.BringToFront();
            timer.Tick+=async(sender,args)=>{
                if(syncing){status.Text=syncStatus+"… 已用 "+(int)DateTime.UtcNow.Subtract(syncStarted).TotalSeconds+" 秒";return;}
                await Probe(false);
            };
            VisibleChanged+=(sender,args)=>{if(Visible&&ready&&!completed)timer.Start();else timer.Stop();};
            Load+=async(sender,args)=>await Initialize();
        }
        private Button Button(string text,Func<Task> action) {
            var button=new ModernButton {Text=text,AutoSize=false,Width=120,Height=35,Margin=new Padding(0,0,8,0),BackColor=DesktopTheme.VioletPale,ForeColor=DesktopTheme.Violet};
            button.Click+=async(sender,args)=>{try{await action();}catch(Exception){if(!closing)status.Text="操作未完成，请刷新二维码重试。";}};
            return button;
        }
        private Task Initialize() {
            if(initialization==null||initialization.IsCompleted&&!ready)initialization=InitializeCore();
            return initialization;
        }
        private async Task InitializeCore() {
            if(ready||closing)return;
            var started=DateTime.UtcNow;
            refresh.Enabled=false;
            try {
                var environment=await DesktopBrowserRuntime.Get(root,false);
                if(closing)return;
                await Browser.EnsureCoreWebView2Async(environment);
                if(closing)return;
                var core=Browser.CoreWebView2;
                core.IsMuted=true;core.Settings.AreDevToolsEnabled=false;core.Settings.IsStatusBarEnabled=false;
                core.Settings.IsWebMessageEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;
                await core.AddScriptToExecuteOnDocumentCreatedAsync(LayoutScript(QrCss));
                core.NavigationStarting+=(sender,args)=>{
                    Uri target;
                    if(Uri.TryCreate(args.Uri,UriKind.Absolute,out target)&&target.Scheme=="http"&&OfficialPlayer.Trusted("https"+args.Uri.Substring(4),false)) {
                        args.Cancel=true;BeginInvoke(new Action(()=>{if(!closing)core.Navigate("https"+args.Uri.Substring(4));}));return;
                    }
                    if(!OfficialPlayer.Trusted(args.Uri,false)){args.Cancel=true;status.Text="请使用当前官方登录页面完成扫码。";}
                };
                core.NewWindowRequested+=(sender,args)=>{args.Handled=true;if(OfficialPlayer.Trusted(args.Uri,false))core.Navigate(args.Uri);};
                core.SourceChanged+=async(sender,args)=>await Probe(false);
                core.DOMContentLoaded+=async(sender,args)=>{await StyleLogin();await Probe(false);};
                core.NavigationCompleted+=async(sender,args)=>{
                    if(closing)return;
                    if(!args.IsSuccess){status.Text="官方登录页面加载失败，请点击刷新二维码。";return;}
                    await StyleLogin();await Probe(false);
                };
                core.ProcessFailed+=(sender,args)=>{if(!closing)status.Text="登录页面暂时无响应，请返回课程后重新打开扫码登录。";};
                ready=true;status.Text="打开手机学习通 → 右上角扫一扫 → 扫码后在手机上确认登录。";core.Navigate(LoginUrl);timer.Start();
            } catch(Exception) {if(!closing)status.Text="二维码加载失败，请点击刷新二维码重试。";}
            finally {DesktopBrowserRuntime.LogTiming(root,"qr-browser-ready",started);if(!closing)refresh.Enabled=true;}
        }
        private static string LayoutScript(string css) {
            var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
            return "(()=>{if(location.hostname!=='passport2.chaoxing.com'||location.pathname!=='/login')return;const apply=()=>{if(!document.documentElement)return false;let s=document.getElementById('desktop-qr-layout');if(!s){s=document.createElement('style');s.id='desktop-qr-layout';(document.head||document.documentElement).appendChild(s);}s.textContent="+serializer.Serialize(css)+";return true;};if(!apply()){const observer=new MutationObserver(()=>{if(apply())observer.disconnect();});observer.observe(document,{childList:true});}})()";
        }
        private async Task StyleLogin() {
            if(!ready||closing)return;
            Uri address;
            if(!Uri.TryCreate(Browser.Source==null?"":Browser.Source.AbsoluteUri,UriKind.Absolute,out address)||address.Host!="passport2.chaoxing.com"||address.AbsolutePath!="/login")return;
            // Keep the official QR image, polling, expiry handling and confirmation page intact.
            try {await Browser.CoreWebView2.ExecuteScriptAsync(LayoutScript(passwordMode?"":QrCss));}
            catch(Exception){}
            if(!closing&&!syncing)status.Text="打开手机学习通 → 右上角扫一扫 → 扫码后在手机上确认登录。";
        }
        internal async Task Probe(bool manual) {
            if(!ready||closing||syncing||completed||(!allowProbe&&!manual)||!Visible)return;
            Uri address=Browser.Source;
            bool landing=address!=null&&OfficialPlayer.Trusted(address.AbsoluteUri,false)
                &&(address.Host=="i.chaoxing.com"||address.Host=="i.mooc.chaoxing.com"||address.Host=="mooc2-ans.chaoxing.com");
            if(!landing){if(manual)status.Text="请先扫码，并在手机学习通中确认登录。";return;}
            syncing=true;syncStarted=DateTime.UtcNow;syncStatus="正在读取登录状态";SetButtons(false);
            Dictionary<string,string> body=null;
            try {
                var passportRead=Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://passport2.chaoxing.com/");
                var courseRead=Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://mooc2-ans.chaoxing.com/");
                await Task.WhenAll(passportRead,courseRead);
                var passport=await passportRead;var courses=await courseRead;
                if(!courses.Any(cookie=>cookie.Name=="_uid"&&!String.IsNullOrEmpty(cookie.Value))){if(!closing)status.Text="请先扫码，并在手机学习通中确认登录。";return;}
                body=new Dictionary<string,string>{{"passportCookies",Header(passport)},{"courseCookies",Header(courses)}};
                syncStatus="已收到登录，正在验证并读取课程";status.Text=syncStatus+"…";
                await synchronize(body);
                completed=true;
                if(!closing)status.Text="登录成功，课程已更新。";
            } catch(Exception error) {
                allowProbe=false;
                if(!closing)status.Text="同步未完成："+error.Message+"；可点击重试同步。";
            } finally {
                if(body!=null)body.Clear();syncing=false;DesktopBrowserRuntime.LogTiming(root,"qr-login-sync",syncStarted);
                if(!closing){SetButtons(true);if(!completed&&Visible&&allowProbe)timer.Start();}
            }
        }
        private static string Header(IReadOnlyList<CoreWebView2Cookie> cookies) {
            return String.Join("; ",cookies.GroupBy(cookie=>cookie.Name).Select(group=>group.OrderByDescending(cookie=>cookie.Path.Length).First()).Select(cookie=>cookie.Name+"="+cookie.Value));
        }
        private void SetButtons(bool enabled){refresh.Enabled=retry.Enabled=password.Enabled=back.Enabled=enabled;}
        protected override void Dispose(bool disposing) {
            closing=true;
            if(disposing){timer.Stop();timer.Dispose();}
            base.Dispose(disposing);
        }
    }
}
