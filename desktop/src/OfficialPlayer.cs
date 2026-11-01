using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SuperAutoDesktop {
    internal sealed class BrowserProgress {
        public string CourseId, FrameId = "", MediaId = "", JobId = "", ObjectId = "", Status = "正在打开官方课程", Detail = "", KnowledgeId = "";
        public double Position, Duration, Recorded;
        public int CardIndex = -1;
        public bool HasVideo, HasRecord, Passed, Paused = true, Ended;
        public bool ManualPause, PageVisible, HasSource;
        public int MediaError;
        public DateTime SampleAt = DateTime.MinValue, RecordedAt = DateTime.MinValue;
    }

    internal sealed class OfficialRecord {
        public string JobId, ObjectId;
        public double Position;
        public bool Passed;
        public static List<OfficialRecord> Parse(string html) {
            var match = Regex.Match(html, @"mArg\s*=\s*(\{.*?\})\s*;", RegexOptions.Singleline);
            if (!match.Success) throw new IOException("未读取到章节记录，请检查登录或页面提示。");
            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(match.Groups[1].Value);
            object raw;
            var rows = data.TryGetValue("attachments", out raw) ? raw as IEnumerable : null;
            var result = new List<OfficialRecord>();
            if (rows == null) return result;
            foreach (object item in rows) {
                var row = item as Dictionary<string, object>;
                if (row == null) continue;
                var properties = row.ContainsKey("property") ? row["property"] as Dictionary<string, object> : null;
                string type = Value(row, "type");
                if (type != "video" && (properties == null || Value(properties, "module") != "insertvideo")) continue;
                double milliseconds;
                if (!Double.TryParse(Value(row, "playTime"), out milliseconds)) Double.TryParse(Value(row, "headOffset"), out milliseconds);
                result.Add(new OfficialRecord { JobId = Value(row, "jobid"),
                    ObjectId = properties == null ? "" : Value(properties, "objectid"),
                    Position = Math.Max(0, milliseconds / 1000), Passed = Value(row, "isPassed").Equals("true", StringComparison.OrdinalIgnoreCase) });
            }
            return result;
        }
        internal static string Value(Dictionary<string, object> row, string key) {
            object value; return row != null && row.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
        }
    }

    internal sealed class OfficialCard {
        public bool HasVideo, HasQuiz, AllComplete, Known;
        public bool PendingVideo, PendingQuiz;
        internal readonly List<OfficialTask> Tasks=new List<OfficialTask>();
        internal static OfficialCard Parse(string html) {
            var match=Regex.Match(html,@"mArg\s*=\s*(\{.*?\})\s*;",RegexOptions.Singleline);
            if (!match.Success) throw new IOException("未读取到官方任务状态。");
            var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(match.Groups[1].Value);
            object raw;
            var rows=data.TryGetValue("attachments",out raw) ? raw as IEnumerable : null;
            var result=new OfficialCard {AllComplete=true};
            if (rows == null) { result.AllComplete=false; return result; }
            int count=0; bool unknown=false;
            foreach (object item in rows) {
                var row=item as Dictionary<string,object>;
                if (row == null) {unknown=true;continue;}
                string type=OfficialRecord.Value(row,"type");
                var properties=row.ContainsKey("property") ? row["property"] as Dictionary<string,object> : null;
                if(type != "video" && OfficialRecord.Value(properties,"module") == "insertvideo")type="video";
                bool passed=OfficialRecord.Value(row,"isPassed").Equals("true",StringComparison.OrdinalIgnoreCase);
                bool optional=OfficialRecord.Value(row,"job").Equals("false",StringComparison.OrdinalIgnoreCase)
                    && OfficialRecord.Value(row,"jobid").Length==0 && !row.ContainsKey("isPassed");
                var task=new OfficialTask {Index=result.Tasks.Count,Kind=type,JobId=OfficialRecord.Value(row,"jobid"),
                    ObjectId=OfficialRecord.Value(properties,"objectid"),Title=OfficialRecord.Value(properties,"name"),Passed=passed,Required=!optional};
                if(task.ObjectId.Length==0)task.ObjectId=OfficialRecord.Value(row,"objectId");
                result.Tasks.Add(task);
                if(optional)continue;
                if (type == "video") {
                    count++; result.HasVideo=true; result.PendingVideo|=!passed;
                } else if (type == "workid") {
                    count++; result.HasQuiz=true;
                    // Official cards remove `job` once this quiz is no longer outstanding.
                    // Require a task identity and reject an explicit incomplete flag.
                    bool cleared=!row.ContainsKey("isPassed") && OfficialRecord.Value(row,"jobid").Length > 0
                        && (!row.ContainsKey("job") || OfficialRecord.Value(row,"job").Equals("false",StringComparison.OrdinalIgnoreCase));
                    result.PendingQuiz|=!(passed || cleared);
                    task.Passed=passed || cleared;
                } else { unknown=true; }
            }
            result.Known=count > 0 && !unknown;
            result.AllComplete=result.Known && !result.PendingVideo && !result.PendingQuiz;
            return result;
        }
    }

    internal static class SavedBrowserLogin {
        // Read saved local login in memory; never log or export cookies to the UI or an HTTP API.
        public static Dictionary<string, string> Read(string root) {
            string account = File.ReadAllText(Path.Combine(root, "data", "desktop-account.txt")).Trim();
            if (!Regex.IsMatch(account, @"^\d{5,20}$")) return new Dictionary<string, string>();
            string mysql = Directory.GetDirectories(Path.Combine(root, "tools"), "mysql-*")
                .Select(path => Path.Combine(path, "bin", "mysql.exe")).First(File.Exists);
            string config = Path.Combine(root, "config", "mysql-admin.cnf");
            var info = new ProcessStartInfo(mysql) {
                Arguments = "--defaults-extra-file=\"" + config + "\" --batch --skip-column-names --raw --default-character-set=utf8mb4 --execute=\"SELECT cookies FROM tihai.th_wk_user WHERE account='" + account + "' LIMIT 1\"",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (Process process = Process.Start(info)) {
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(8000)) { process.Kill(); throw new IOException("读取已有登录超时，请在课程页登录。"); }
                Task.WaitAll(output, error);
                if (process.ExitCode != 0) throw new IOException("已有登录未能读取，请在课程页登录。");
                string raw = output.Result.Trim().Trim('[', ']');
                var result = new Dictionary<string, string>();
                foreach (string part in Regex.Split(raw, @",\s+(?=[^;,=\s]+=)")) {
                    string pair = part.Split(';')[0].Trim();
                    int equals = pair.IndexOf('=');
                    if (equals > 0) result[pair.Substring(0, equals).Trim()] = pair.Substring(equals + 1).Trim();
                }
                return result;
            }
        }
    }

    internal sealed partial class OfficialPlayer : Form {
        [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
        private readonly string root, entryUrl, token = Guid.NewGuid().ToString("N");
        private readonly CourseItem course;
        private readonly bool test;
        internal readonly WebView2 Browser = new WebView2 { Dock = DockStyle.Fill };
        internal readonly BrowserProgress Progress = new BrowserProgress();
        internal event Action Changed;
        internal event Action EndRequested;
        internal Task Initialization;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer {MaxJsonLength=4*1024*1024};
        private readonly HttpClient officialHttp;
        private readonly Timer timer = new Timer { Interval = 1000 };
        private readonly Dictionary<string, BrowserProgress> media = new Dictionary<string, BrowserProgress>();
        private readonly Dictionary<string, BrowserProgress> prompts = new Dictionary<string, BrowserProgress>();
        private Label status, position, recorded, hint, workflow;
        private Button play, pause, refresh, catalogue;
        private ToolStripMenuItem reload;
        private FlatProgress playbackBar;
        private readonly ToolTip detailTips=new ToolTip {AutoPopDelay=15000,InitialDelay=400};
        private bool catalogueExpanded;
        private CheckBox autoResume, autoAdvance, muteAudio;
        internal Func<string,int,Task<OfficialCard>> WorkflowRecordReader;
        internal bool AutomaticAdvanceEnabled { get { return autoAdvance.Checked; } }
        internal void SetAutomaticAdvance(bool enabled) { autoAdvance.Checked=enabled; }
        internal string WorkflowStatus { get { return workflow.Text; } }
        private string workflowKey="", nextKnowledgeId="", workflowRequest="", workflowBlock="", workflowAttemptTarget="";
        private int workflowCard=-1, workflowCardCount, workflowVersion, workflowAttempts;
        private bool workflowVisible, catalogueKnown, nextChapterLocked, nextChapterReady, workflowBusy, observedIncomplete;
        private DateTime workflowSample=DateTime.MinValue, nextWorkflowCheck=DateTime.MinValue, workflowSentAt=DateTime.MinValue;
        private bool recordBusy, userPaused, closing, alerted, initialized, initialChapterPending = true, keepAwake;
        private DateTime lastRecordAttempt = DateTime.MinValue, lastMoved = DateTime.UtcNow;
        private double lastPosition;
        private int navigationRetries;
        private ulong latestNavigationId,blockedNavigationId;
        private bool navigationInFlight,navigationFailed,navigationBlocked,navigationStopping;
        private DateTime navigationStartedAt=DateTime.MinValue,navigationRetryAt=DateTime.MinValue;
        private string navigationTarget="";
        private bool openingInitialChapter;
        private ulong initialChapterClickNavigation;
        private DateTime pausedSince = DateTime.MinValue, nextResumeAt = DateTime.MinValue;
        private int resumeAttempt, resumeSequence;
        private string recoveryFrame = "", recoveryBlock = "";
        private double recoveryPosition;
        private bool recoveryPending;
        internal int AutomaticResumeCount { get; private set; }
        internal bool AutomaticResumeEnabled { get { return autoResume.Checked; } }
        internal bool Ready { get { return initialized && !IsDisposed; } }
        private bool studyPaused;
        private int studyCommandVersion;
        internal bool StudyPaused { get { return studyPaused; } }
        private bool PlaybackWindowAvailable {
            get {var host=TopLevelControl as Form;return Visible&&host!=null&&!host.IsDisposed&&host.WindowState!=FormWindowState.Minimized;}
        }

        public OfficialPlayer(string installRoot, CourseItem selected, bool testing) {
            root = installRoot; course = selected; test = testing; Progress.CourseId = course.Id;
            officialHttp=new HttpClient(new HttpClientHandler {UseCookies=false,AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(12)};
            officialHttp.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36");
            entryUrl = CourseUrl(course);
            Text = "学习通官方播放 · " + course.Title;
            Name = "OfficialPlayer"; StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1200, 850); MinimumSize = new Size(960, 700);
            Font = new Font("Microsoft YaHei UI", 9.5F); BackColor = Color.White;ForeColor=DesktopTheme.Ink;
            var header = new Panel { Dock = DockStyle.Top, Height = 108, Padding = new Padding(20,4,20,0), Name="OfficialPlaybackHeader",BackColor=Color.White };
            Controls.Add(Browser); Controls.Add(header);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 43, WrapContents = true, Name="OfficialPlaybackToolbar" };
            header.Controls.Add(toolbar);
            play = AddButton(toolbar, "播放 / 继续", async () => await ControlPlayback("play"));
            play.BackColor=DesktopTheme.Blue;play.ForeColor=Color.White;
            pause = AddButton(toolbar, "暂停学习", async () => await ControlPlayback("pause"));
            pause.BackColor=DesktopTheme.CoralPale;pause.ForeColor=DesktopTheme.Coral;
            pause.Name="PauseStudy"; play.Name="ContinueStudy";
            catalogue=AddButton(toolbar, "展开章节", () => { SetCatalogueExpanded(!catalogueExpanded); return Task.FromResult(0); });
            catalogue.Name="ToggleChapterCatalogue";
            catalogue.BackColor=DesktopTheme.VioletPale;catalogue.ForeColor=DesktopTheme.Violet;
            refresh = AddButton(toolbar, "刷新进度", async () => await RefreshRecord(true));refresh.Name="RefreshPlayerProgress";
            refresh.BackColor=DesktopTheme.TealPale;refresh.ForeColor=DesktopTheme.Teal;
            var endCourse=AddButton(toolbar,"结束课程",()=>{if(EndRequested!=null)EndRequested();return Task.FromResult(0);});
            endCourse.Name="EndPlayerCourse";endCourse.BackColor=DesktopTheme.CoralPale;endCourse.ForeColor=DesktopTheme.Coral;
            var details=new SurfacePanel {Name="PlayerDetails",Visible=false,Height=90,BackColor=DesktopTheme.TealPale};
            bool detailsExpanded=false;
            header.Controls.Add(details);
            Action arrangeHeader=null;
            Button settingsButton=null;
            settingsButton=AddButton(toolbar,"设置",()=>{detailsExpanded=!detailsExpanded;details.Visible=detailsExpanded;settingsButton.Text=detailsExpanded?"收起设置":"设置";if(arrangeHeader!=null)arrangeHeader();return Task.FromResult(0);});
            settingsButton.Name="TogglePlayerDetails";
            settingsButton.BackColor=DesktopTheme.AmberPale;settingsButton.ForeColor=DesktopTheme.Amber;
            var moreMenu=new ContextMenuStrip {Name="PlayerMoreMenu",Font=Font,ShowImageMargin=false,BackColor=Color.White,Padding=new Padding(6)};
            reload=AddMenuItem(moreMenu,"重新加载页面",()=>{ReloadPage();return Task.FromResult(0);});
            AddMenuItem(moreMenu,"课程首页",()=>{if(Ready)Browser.CoreWebView2.Navigate(entryUrl);return Task.FromResult(0);});
            AddMenuItem(moreMenu,"官方登录",()=>{if(Ready)Browser.CoreWebView2.Navigate("https://passport2.chaoxing.com/login?newversion=true&refer="+Uri.EscapeDataString(entryUrl));return Task.FromResult(0);});
            Button more=null;
            more=AddButton(toolbar,"更多 ···",()=>{moreMenu.Show(more,new Point(0,more.Height+4));return Task.FromResult(0);});more.Name="PlayerMoreActions";more.ContextMenuStrip=moreMenu;
            status = NewLabel(header, 20, 55, 500, "正在准备课程…");status.Name="PlayerStatus";
            var options=new FlowLayoutPanel {Dock=DockStyle.Top,Height=30,WrapContents=true,Name="AutomaticPlaybackOptions",Padding=new Padding(10,0,0,0)};
            details.Controls.Add(options);
            autoResume = new CheckBox { Text="自动续播", Name="AutoResumePlayback", Checked=true, AutoSize=true,Margin=new Padding(0,3,20,0) };
            autoAdvance = new CheckBox { Text="自动切换章节", Name="AutoAdvanceChapters", Checked=true, AutoSize=true,Margin=new Padding(0,3,20,0) };
            if (!test) {
                try {
                    string preference=Path.Combine(root,"data","browser-player-settings.json");
                    if (File.Exists(preference)) {
                        var settings=json.Deserialize<Dictionary<string,bool>>(File.ReadAllText(preference,Encoding.UTF8));
                        if (settings.ContainsKey("autoResume")) autoResume.Checked=settings["autoResume"];
                        if (settings.ContainsKey("autoAdvance")) autoAdvance.Checked=settings["autoAdvance"];
                    }
                } catch (Exception) { }
            }
            autoResume.CheckedChanged += (sender,args) => {
                ResetRecovery();
                if (!test) {
                    try { File.WriteAllText(Path.Combine(root,"data","browser-player-settings.json"),json.Serialize(new { autoResume=autoResume.Checked, autoAdvance=autoAdvance.Checked, autoQuiz=autoQuiz.Checked }),Encoding.UTF8); }
                    catch (IOException) { }
                }
                Render();
            };
            options.Controls.Add(autoResume);
            autoAdvance.CheckedChanged += (sender,args) => {
                workflowVersion++; ResetWorkflowNavigation(); nextWorkflowCheck=DateTime.MinValue;
                if (!test) {
                    try { File.WriteAllText(Path.Combine(root,"data","browser-player-settings.json"),json.Serialize(new {autoResume=autoResume.Checked,autoAdvance=autoAdvance.Checked,autoQuiz=autoQuiz.Checked}),Encoding.UTF8); }
                    catch (IOException) { }
                }
                if (workflow != null) SetWorkflow(autoAdvance.Checked ? "等待核对当前任务" : "自动切换已关闭");
            };
            options.Controls.Add(autoAdvance);
            position = NewLabel(header, 520, 55, 420, "等待视频");position.Name="PlaybackPosition";position.TextAlign=ContentAlignment.MiddleRight;position.ForeColor=DesktopTheme.Teal;
            recorded = NewLabel(details, 10, 32, 700, "平台记录：等待读取");recorded.ForeColor=DesktopTheme.Muted;
            workflow=NewLabel(details,10,58,700,"章节衔接：等待打开章节"); workflow.Name="ChapterWorkflowStatus";workflow.ForeColor=DesktopTheme.Muted;
            playbackBar=new FlatProgress {Name="ActualPlaybackProgress",Location=new Point(20,90),Height=4,Fill=DesktopTheme.Teal};header.Controls.Add(playbackBar);
            InitializeQuizControls(options);
            InitializeAudioControls(options);
            arrangeHeader=()=>{
                int width=Math.Max(0,header.ClientSize.Width-40);
                int rowsHeight=Math.Max(43,toolbar.GetPreferredSize(new Size(Math.Max(1,width),0)).Height+9);
                if(toolbar.Height!=rowsHeight)toolbar.Height=rowsHeight;
                int optionsHeight=Math.Max(30,options.GetPreferredSize(new Size(Math.Max(1,width),0)).Height);
                options.Height=optionsHeight;
                int detailsHeight=optionsHeight+60;
                int wantedHeight=rowsHeight+57+(detailsExpanded?detailsHeight+8:0);
                if(header.Height!=wantedHeight)header.Height=wantedHeight;
                status.Top=position.Top=rowsHeight+8;
                status.Width=Math.Max(0,width-260);position.Left=status.Right+12;position.Width=Math.Max(0,width-status.Width-12);
                playbackBar.SetBounds(20,rowsHeight+41,width,4);
                details.SetBounds(20,rowsHeight+55,width,detailsHeight);
                recorded.Top=optionsHeight+2;workflow.Top=optionsHeight+28;
                recorded.Width=workflow.Width=Math.Max(0,width-20);
            };
            header.SizeChanged+=(sender,args)=>arrangeHeader();arrangeHeader();
            hint = new Label { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(20,5,20,3),ForeColor=DesktopTheme.Muted,Font=new Font("Microsoft YaHei UI",9),
                Text = "准备就绪", AutoEllipsis = true,BackColor=DesktopTheme.AmberPale };
            hint.Name="OfficialPlaybackHint";
            Controls.Add(hint); Browser.BringToFront();
            Browser.SizeChanged+=(sender,args)=>PublishLayout();
            timer.Tick += async (sender, args) => {
                RecoverNavigation();
                Render();
                if(TaskPlanCurrent)PickMedia();
                TryAutomaticResume();
                await RecognizeTasks();
                TryAutomaticResume();
                await AdvanceWorkflow();
                await ProcessAutomaticQuiz();
                if (DateTime.UtcNow.Subtract(lastRecordAttempt).TotalSeconds >= 30) await RefreshRecord(false);
            };
            Shown += (sender, args) => { Initialization = Initialize(); };
            FormClosing += (sender, args) => { InvalidateQuiz(true); closing = true; timer.Stop(); if (keepAwake) SetThreadExecutionState(0x80000000); };
            FormClosed += (sender, args) => { timer.Dispose(); moreMenu.Dispose();detailTips.Dispose();officialHttp.Dispose();Browser.Dispose(); };
        }

        private void InitializeAudioControls(Control options) {
            muteAudio=new CheckBox {Text="视频静音",Name="MutePlayerAudio",Checked=true,AutoSize=true,Margin=new Padding(20,3,0,0)};
            string preference=Path.Combine(root,"data","browser-audio-settings.json");
            if(!test) {
                try {
                    if(File.Exists(preference)) {
                        var settings=json.Deserialize<Dictionary<string,bool>>(File.ReadAllText(preference,Encoding.UTF8));
                        if(settings.ContainsKey("muted"))muteAudio.Checked=settings["muted"];
                    }
                }catch(Exception){}
            }
            detailTips.SetToolTip(muteAudio,"勾选后课程视频保持静音，切换视频、刷新和重启后仍生效。取消勾选恢复声音。");
            muteAudio.CheckedChanged+=(sender,args)=>{
                try {
                    if(Browser.CoreWebView2!=null)Browser.CoreWebView2.IsMuted=muteAudio.Checked;
                }catch(Exception error) {
                    hint.Text="声音设置未能应用，请重新打开课程后再试。";
                    WriteLog("audio-mute "+error.GetType().Name);return;
                }
                if(!test) {
                    try {
                        Directory.CreateDirectory(Path.GetDirectoryName(preference));
                        File.WriteAllText(preference,json.Serialize(new {muted=muteAudio.Checked}),Encoding.UTF8);
                    }catch(Exception error) {
                        hint.Text="声音设置本次已生效，但未能保存；下次打开请检查静音开关。";
                        WriteLog("audio-settings "+error.GetType().Name);
                    }
                }
            };
            options.Controls.Add(muteAudio);
        }

        private static Button AddButton(Control parent, string text, Func<Task> action) {
            var button = new ModernButton { Text = text, AutoSize = true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MinimumSize=new Size(80,36),Height=36,Padding=new Padding(12,3,12,3),BackColor=DesktopTheme.Pale, Margin = new Padding(0,0,8,0) };
            button.Click += async (sender, args) => { try { await action(); } catch (Exception) { MessageBox.Show("操作未完成，请查看课程页面或重新加载。", "官方播放"); } };
            parent.Controls.Add(button); return button;
        }
        private static ToolStripMenuItem AddMenuItem(ContextMenuStrip menu,string text,Func<Task> action) {
            var item=new ToolStripMenuItem(text) {Padding=new Padding(14,8,14,8)};
            item.Click+=async(sender,args)=>{try{await action();}catch(Exception){MessageBox.Show("操作未完成，请查看课程页面或重新加载。","课程播放");}};
            menu.Items.Add(item);return item;
        }
        private static Label NewLabel(Control parent, int x, int y, int width, string text) {
            var label = new Label { Location = new Point(x,y), Size = new Size(width,25), Text = text, AutoEllipsis = true };
            parent.Controls.Add(label); return label;
        }
        internal static bool Trusted(string url, bool testing) {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == "https" &&
                (uri.Host == "chaoxing.com" || uri.Host.EndsWith(".chaoxing.com", StringComparison.OrdinalIgnoreCase) || (testing && uri.Host == "player-test.invalid"));
        }
        internal static string CourseUrl(CourseItem value) {
            if (!Regex.IsMatch(value.Id ?? "", @"^\d+$") || !Regex.IsMatch(value.ClazzId ?? "", @"^\d+$") || !Regex.IsMatch(value.Cpi ?? "", @"^\d+$"))
                return "https://i.chaoxing.com/";
            return "https://mooc2-ans.chaoxing.com/mooc2-ans/mycourse/studentcourse?courseid=" + value.Id + "&clazzid=" + value.ClazzId + "&cpi=" + value.Cpi + "&ut=s";
        }
        private async Task Initialize() {
            try {
                var environment = await DesktopBrowserRuntime.Get(root,test);
                if (closing) return;
                await Browser.EnsureCoreWebView2Async(environment);
                var core = Browser.CoreWebView2;
                // Mute browser output before navigation; page volume changes and new frames cannot override it.
                core.IsMuted=muteAudio.Checked;
                core.Settings.AreDevToolsEnabled = test;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.AreDefaultScriptDialogsEnabled = true;
                core.Settings.IsWebMessageEnabled = true;
                core.WebMessageReceived += Receive;
                core.NavigationStarting += (sender, args) => {
                    if (!Trusted(args.Uri,test)) {
                        args.Cancel=true;blockedNavigationId=args.NavigationId;
                        if(latestNavigationId==args.NavigationId){navigationInFlight=false;navigationFailed=true;navigationBlocked=true;}
                        WriteLog("navigation blocked id="+args.NavigationId);
                        SetStatus("此页面跳转未打开", "请使用官方课程入口；点击播放 / 继续可重新打开课程。");return;
                    }
                    bool redirected=latestNavigationId==args.NavigationId;
                    if(initialChapterPending&&initialChapterClickNavigation!=0&&initialChapterClickNavigation!=args.NavigationId
                        &&!new Uri(args.Uri).AbsolutePath.EndsWith("/mycourse/studentcourse",StringComparison.OrdinalIgnoreCase))initialChapterPending=false;
                    latestNavigationId=args.NavigationId;navigationTarget=args.Uri;
                    navigationInFlight=true;navigationFailed=false;navigationBlocked=false;navigationStopping=false;
                    navigationStartedAt=DateTime.UtcNow;navigationRetryAt=DateTime.MinValue;
                    WriteLog("navigation start id="+args.NavigationId+" redirect="+args.IsRedirected+" "+NavigationRoute(args.Uri));
                    if(redirected)return;
                    media.Clear(); prompts.Clear(); ResetTaskRecognition(); ResetRecovery(); Progress.HasVideo = false; Progress.FrameId = ""; Progress.MediaId=""; Progress.HasRecord = false;
                    InvalidateQuiz(true);
                    workflowVersion++; workflowKey=""; ResetWorkflowNavigation(); workflowCard=-1; workflowSample=DateTime.MinValue;
                    SetWorkflow("正在加载章节");
                    Progress.KnowledgeId = ""; Progress.CardIndex = -1; Progress.Passed = false; userPaused = false; alerted = false;
                    SetStatus("正在加载官方页面", "登录失效时，请直接在页面内重新登录。");
                };
                core.DOMContentLoaded+=async(sender,args)=>{
                    if(closing||args.NavigationId!=latestNavigationId)return;
                    WriteLog("navigation interactive id="+args.NavigationId+" elapsedMs="+(int)DateTime.UtcNow.Subtract(navigationStartedAt).TotalMilliseconds);
                    await OpenInitialChapter();
                };
                core.NavigationCompleted += async (sender, args) => {
                    WriteLog("navigation complete id="+args.NavigationId+" ok="+args.IsSuccess+" error="+args.WebErrorStatus+" http="+args.HttpStatusCode);
                    if(closing||args.NavigationId!=latestNavigationId||args.NavigationId==blockedNavigationId)return;
                    navigationInFlight=false;
                    if (args.IsSuccess) {
                        navigationFailed=false;navigationRetryAt=DateTime.MinValue;
                        navigationRetries = 0; SetStatus("官方页面已打开", "在页面中选择视频，点击播放后即可显示真实位置。");
                        await OpenInitialChapter();
                    }
                    else {
                        navigationFailed=true;navigationRetryAt=DateTime.UtcNow.AddSeconds(1);
                        SetStatus(navigationRetries<2 ? "课程页面加载中断，正在重新连接" : "课程页面暂未载入，点击播放 / 继续重试",
                            "加载状态："+args.WebErrorStatus+"；最多自动重试两次，手动暂停状态会保留。");
                    }
                };
                core.NewWindowRequested += (sender, args) => {
                    args.Handled = true;
                    if (Trusted(args.Uri,test)) {if(!navigationInFlight||navigationTarget!=args.Uri)core.Navigate(args.Uri);}
                    else SetStatus("链接未在播放器中打开", "请使用课程内的播放和登录入口。");
                };
                core.ProcessFailed += (sender,args) => SetStatus("浏览器播放进程异常", "点击重新加载页面；如仍无响应，重新启动桌面助手。");
                string script;
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("SuperAutoDesktop.player-monitor.js"))
                using (var reader = new StreamReader(resource,Encoding.UTF8)) script=reader.ReadToEnd();
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("SuperAutoDesktop.player-layout.js"))
                using (var reader = new StreamReader(resource,Encoding.UTF8)) script=reader.ReadToEnd()+Environment.NewLine+script;
                script=script.Replace("__PLAYER_TOKEN__",json.Serialize(token)).Replace("__PLAYER_TEST__",test ? "true" : "false");
                await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
                core.FrameCreated+=(sender,args)=>{
                    var frame=args.Frame;
                    frame.DOMContentLoaded+=async(frameSender,loaded)=>{
                        if(closing)return;
                        try{await frame.ExecuteScriptAsync(script);}catch(Exception){ }
                    };
                };
                if (test) core.SetVirtualHostNameToFolderMapping("player-test.invalid",Path.Combine(root,"temp","player-fixture"),CoreWebView2HostResourceAccessKind.DenyCors);
                else await ImportSavedLogin();
                if (closing) return;
                initialized = true;
                string initial = test ? "https://player-test.invalid/index.html" : entryUrl;
                if (!closing) core.Navigate(initial);
                timer.Start();
            } catch (Exception error) {
                SetStatus("播放器未能初始化", "请重新启动桌面助手。浏览器组件错误：" + error.GetType().Name);
                WriteLog("initialize " + error.GetType().Name);
            }
        }
        private async Task OpenInitialChapter() {
            if(closing||!initialChapterPending||openingInitialChapter||Browser.CoreWebView2==null)return;
            var core=Browser.CoreWebView2;Uri source;
            if(!Uri.TryCreate(core.Source,UriKind.Absolute,out source)||!source.AbsolutePath.EndsWith("/mycourse/studentcourse",StringComparison.OrdinalIgnoreCase))return;
            openingInitialChapter=true;ulong navigation=latestNavigationId;
            try {
                int chapterIndex=course.Task==null?0:(int)Number(course.Task,"chapterIndex");string remembered="";
                if(!test)try {
                    string preference=Path.Combine(root,"data","browser-chapters.json");
                    if(File.Exists(preference)) {
                        var locations=json.Deserialize<Dictionary<string,string>>(File.ReadAllText(preference,Encoding.UTF8));
                        locations.TryGetValue(course.Id,out remembered);
                    }
                }catch(Exception){}
                // Click the official entry as soon as its DOM is ready. Unrelated
                // images or frames must not hold up obtaining a fresh signed URL.
                initialChapterClickNavigation=navigation;
                string result=await core.ExecuteScriptAsync("(()=>{const nodes=Array.from(document.querySelectorAll('.chapter_item[id^=cur]'));const node=document.getElementById('cur'+"+json.Serialize(remembered??"")+")||nodes[Math.min("+chapterIndex+",nodes.length-1)];if(!node)return false;node.click();return true;})()");
                if(result=="true") {
                    // A click can run before a site's asynchronous handler is ready.
                    // Consume the initial entry only after navigation actually starts;
                    // NavigationCompleted can otherwise retry the same entry.
                    if(latestNavigationId!=navigation)initialChapterPending=false;
                    WriteLog("chapter entry requested from interactive document");
                }
            }catch(Exception){if(!closing&&navigation==latestNavigationId)hint.Text="请在课程目录中选择章节。";}
            finally{openingInitialChapter=false;}
        }
        internal void SetCatalogueExpanded(bool expanded) {
            catalogueExpanded=expanded;
            catalogue.Text=expanded ? "收起章节" : "展开章节";
            PublishLayout();
        }
        private void PublishLayout() {
            if(!Ready || closing)return;
            Browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {channel="study-player-command",token=token,
                action="player-layout",expanded=catalogueExpanded,viewportHeight=Browser.ClientSize.Height}));
        }
        private async Task ImportSavedLogin() {
            var existing = await Browser.CoreWebView2.CookieManager.GetCookiesAsync("https://mooc1.chaoxing.com/");
            if (existing.Any(cookie => cookie.Name == "_uid")) return;
            try {
                var saved = await Task.Run(() => SavedBrowserLogin.Read(root));
                if (closing) return;
                foreach (var pair in saved) {
                    try {
                        var cookie = Browser.CoreWebView2.CookieManager.CreateCookie(pair.Key,pair.Value,".chaoxing.com","/");
                        cookie.IsSecure = true;
                        Browser.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
                    } catch (ArgumentException) { }
                }
            } catch (Exception) { hint.Text = "已有登录无法复用，请在官方课程页面内登录一次，后续会保存在 E 盘。"; }
        }
        private async Task<string> ReadOfficial(string url) {
            if (!Trusted(url,false)) throw new IOException("课程地址无效");
            var cookies=Browser.CoreWebView2.CookieManager.GetCookiesAsync(url);
            if(await Task.WhenAny(cookies,Task.Delay(4000))!=cookies)throw new IOException("读取登录状态超时");
            var saved = await cookies;
            if(closing)throw new OperationCanceledException();
            var address = new Uri(url);
            // Keep each request's session snapshot separate while reusing connections.
            var jar=new CookieContainer();
            foreach (var cookie in saved) {
                try { jar.Add(address,new Cookie(cookie.Name,cookie.Value,"/")); } catch (CookieException) { }
            }
            using(var request=new HttpRequestMessage(HttpMethod.Get,url)) {
                request.Headers.Referrer=new Uri(entryUrl);
                request.Headers.TryAddWithoutValidation("Cookie",jar.GetCookieHeader(address));
                using (var response = await officialHttp.SendAsync(request)) {
                    if (!response.IsSuccessStatusCode) {
                        WriteLog("official-read http="+(int)response.StatusCode);
                        throw new IOException("平台记录读取失败，请检查页面登录状态。");
                    }
                    return Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
                }
            }
        }
        private async void Receive(object sender, CoreWebView2WebMessageReceivedEventArgs args) {
            if (!Trusted(args.Source,test) || closing) return;
            try {
                var data = json.Deserialize<Dictionary<string,object>>(args.WebMessageAsJson);
                if (OfficialRecord.Value(data,"channel") != "study-player" || OfficialRecord.Value(data,"token") != token) return;
                string kind = OfficialRecord.Value(data,"kind");
                if(kind == "player-ready") { PublishStudyState(); PublishLayout(); return; }
                if(kind == "task-plan") {ReceiveTaskPlan(data);return;}
                if(kind == "card-read-started") {ReceiveCardReadStarted(data);return;}
                if(kind == "card-read-result") {ReceiveCardRead(data);return;}
                if(kind == "manual-play") {
                    int version;BrowserProgress source;
                    string frame=OfficialRecord.Value(data,"frameId");
                    if(!studyPaused || !Ready || !PlaybackWindowAvailable
                        || !Int32.TryParse(OfficialRecord.Value(data,"version"),out version) || version!=studyCommandVersion
                        || !media.TryGetValue(MediaKey(frame,OfficialRecord.Value(data,"mediaId")),out source) || !source.PageVisible || frame!=Progress.FrameId
                        || source.MediaId!=Progress.MediaId
                        || DateTime.UtcNow.Subtract(source.SampleAt).TotalSeconds>3 || CurrentPrompt().Length>0 || VideoQuizActive)return;
                    await ControlPlayback("play");
                    WriteLog("manual-play resumed from player control");
                    return;
                }
                if (kind == "quiz-page" || kind == "quiz-result" || kind == "video-quiz-page" || kind == "video-quiz-gone") { ReceiveQuiz(data); return; }
                if (kind == "navigation") {
                    string chapter=OfficialRecord.Value(data,"knowledgeId");
                    int card=(int)Number(data,"cardNum"),count=(int)Number(data,"cardCount");
                    if (!Regex.IsMatch(chapter,@"^\d+$") || card < 0 || count < 1 || count > 100 || card >= count) return;
                    string key=chapter+":"+card;
                    if (workflowKey != key) {
                        if (workflowAttemptTarget.Length > 0)
                            WriteLog("workflow "+(workflowAttemptTarget == key ? "arrived" : "cancelled")+" chapter="+chapter+" card="+card+" attempts="+workflowAttempts);
                        InvalidateQuiz(true);
                        workflowVersion++; workflowKey=key; ResetWorkflowNavigation(); observedIncomplete=false;
                        nextWorkflowCheck=DateTime.MinValue; media.Clear(); ResetTaskRecognition(); ResetRecovery();
                        Progress.HasVideo=false; Progress.FrameId=""; Progress.MediaId=""; Progress.HasRecord=false; Progress.Passed=false;
                        Progress.ManualPause=false; userPaused=false; Progress.KnowledgeId=chapter; Progress.CardIndex=card;
                        lastRecordAttempt=DateTime.MinValue;
                        if (!test) RememberChapter(chapter);
                        SetWorkflow("正在核对“"+OfficialRecord.Value(data,"cardTitle")+"”");
                    }
                    workflowCard=card; workflowCardCount=count; nextKnowledgeId=OfficialRecord.Value(data,"nextKnowledgeId");
                    if (!Regex.IsMatch(nextKnowledgeId,@"^\d*$")) nextKnowledgeId="";
                    nextChapterLocked=Boolean(data,"nextLocked"); nextChapterReady=Boolean(data,"nextReady"); catalogueKnown=Boolean(data,"catalogueKnown");
                    workflowVisible=Boolean(data,"pageVisible"); workflowSample=DateTime.UtcNow;
                    return;
                }
                if (kind == "flow-result") {
                    if (OfficialRecord.Value(data,"request") != workflowRequest) return;
                    if (!Boolean(data,"ok")) RetryWorkflowNavigation(OfficialRecord.Value(data,"reason"));
                    return;
                }
                if (kind == "prompt") {
                    string frame=OfficialRecord.Value(data,"frameId"), text=OfficialRecord.Value(data,"prompt");
                    if (text.Length == 0) prompts.Remove(frame);
                    else prompts[frame]=new BrowserProgress {Detail=text,SampleAt=DateTime.UtcNow};
                    if(text.Length>0 && quizMode == "video" && quizBusy)InvalidateQuiz(false);
                    return;
                }
                if (kind == "resume-result") {
                    if (OfficialRecord.Value(data,"targetFrame") != recoveryFrame || Number(data,"attempt") != resumeSequence) return;
                    if (!Boolean(data,"ok")) {
                        string reason=OfficialRecord.Value(data,"reason");
                        recoveryPending=false;
                        if (reason == "interaction-required") recoveryBlock="浏览器要求点击播放";
                        else if (reason == "confirmation") nextResumeAt=DateTime.UtcNow.AddSeconds(3);
                        else if (reason == "manual") userPaused=true;
                        WriteLog("auto-resume blocked reason="+reason);
                    }
                    return;
                }
                if (kind == "context") {
                    string knowledge = OfficialRecord.Value(data,"knowledgeId"), courseId = OfficialRecord.Value(data,"courseId");
                    if (workflowKey.Length > 0 && DateTime.UtcNow.Subtract(workflowSample).TotalSeconds < 4
                        && (knowledge != Progress.KnowledgeId || (Number(data,"cardNum") >= 0 && Number(data,"cardNum") != workflowCard))) return;
                    if (Regex.IsMatch(knowledge,@"^\d+$") && (courseId == "" || courseId == course.Id)) {
                        if (knowledge != Progress.KnowledgeId) {
                            Progress.KnowledgeId = knowledge; Progress.CardIndex = -1; Progress.HasRecord = false; Progress.Passed = false; lastRecordAttempt = DateTime.MinValue;
                            if (!test) RememberChapter(knowledge);
                        }
                        int card;
                        if (Int32.TryParse(OfficialRecord.Value(data,"cardNum"),out card) && card >= 0 && card <= 100 && card != Progress.CardIndex) {
                            Progress.CardIndex=card; Progress.HasRecord=false; Progress.Passed=false; lastRecordAttempt=DateTime.MinValue;
                        }
                    }
                    return;
                }
                if (kind == "play-error") { userPaused = false; recoveryBlock="浏览器要求点击播放"; SetStatus("需要在页面中操作",OfficialRecord.Value(data,"message")); return; }
                if (kind != "media") return;
                string id = OfficialRecord.Value(data,"frameId");
                int sampleCard;if(!Int32.TryParse(OfficialRecord.Value(data,"cardNum"),out sampleCard))sampleCard=-1;
                var sample = new BrowserProgress { FrameId=id, MediaId=OfficialRecord.Value(data,"mediaId"),JobId=OfficialRecord.Value(data,"jobId"),ObjectId=OfficialRecord.Value(data,"objectId"),
                    KnowledgeId=OfficialRecord.Value(data,"knowledgeId"),CardIndex=sampleCard,
                    HasVideo=true, Position=Number(data,"position"), Duration=Number(data,"duration"),
                    Paused=Boolean(data,"paused"),Ended=Boolean(data,"ended"),SampleAt=DateTime.UtcNow,
                    ManualPause=Boolean(data,"manualPause"),PageVisible=Boolean(data,"pageVisible"),HasSource=Boolean(data,"hasSource"),MediaError=(int)Number(data,"error"),
                    Detail=OfficialRecord.Value(data,"prompt"), Status=Number(data,"error") > 0 ? "视频加载错误" : Number(data,"readyState") < 3 ? "正在缓冲" : "" };
                media[MediaKey(sample)]=sample;
                var current = PickMedia();
                if (current == null) return;
                if (current.Ended && !Progress.Ended) nextWorkflowCheck=DateTime.MinValue;
                if (Progress.FrameId != current.FrameId || Progress.MediaId!=current.MediaId) ResetRecovery();
                if (current.Paused && (!Progress.Paused || pausedSince == DateTime.MinValue)) pausedSince=DateTime.UtcNow;
                if (!current.Paused) {
                    pausedSince=DateTime.MinValue;
                    if (recoveryPending && recoveryFrame == current.FrameId && current.Position > recoveryPosition+.2) {
                        AutomaticResumeCount++; recoveryPending=false;
                        WriteLog("auto-resume confirmed count="+AutomaticResumeCount);
                    }
                    if (!recoveryPending) { resumeAttempt=0; recoveryBlock=""; }
                }
                if (Progress.FrameId != current.FrameId || Progress.MediaId!=current.MediaId || Math.Abs(current.Position-lastPosition) > .1) {
                    lastMoved=DateTime.UtcNow; lastPosition=current.Position;
                }
                selectedMediaKey=MediaKey(current);
                Progress.FrameId=current.FrameId; Progress.MediaId=current.MediaId;Progress.JobId=current.JobId;Progress.ObjectId=current.ObjectId;
                Progress.HasVideo=true; Progress.Position=current.Position; Progress.Duration=current.Duration;
                Progress.Paused=current.Paused; Progress.Ended=current.Ended; Progress.SampleAt=current.SampleAt;
                Progress.ManualPause=current.ManualPause; Progress.PageVisible=current.PageVisible; Progress.HasSource=current.HasSource; Progress.MediaError=current.MediaError;
                string activePrompt=CurrentPrompt();
                if (activePrompt.Length > 0) SetStatus("等待本人确认",activePrompt);
                else if (VideoQuizActive && !userPaused && !current.ManualPause) SetStatus(quizStatus.Length>0 ? quizStatus : "视频弹题等待作答",
                    autoQuiz.Checked ? "正在处理官方视频题；通过后继续播放。" : "题库自动答题已关闭，请在官方页面作答。");
                else if (current.Ended) SetStatus("视频已播放到末尾", autoAdvance.Checked ? "正在核对官方任务状态，确认完成后自动进入下一任务。" : "自动切换已关闭，可在页面中选择章节测验或下一节。");
                else if (current.Paused) {
                    bool manual=userPaused || current.ManualPause;
                    if (manual) SetStatus("已手动暂停","自动续播不会恢复手动暂停；点击播放 / 继续可恢复。");
                    else if (!autoResume.Checked) SetStatus("视频已暂停","自动续播已关闭；点击播放 / 继续可恢复。");
                    else if (recoveryBlock.Length > 0 || resumeAttempt >= 3) SetStatus("自动续播未成功",recoveryBlock.Length > 0 ? recoveryBlock+"，请在官方播放器中点击一次播放。" : "已连续尝试 3 次，请检查网络或页面提示，再点击播放。");
                    else if (!current.PageVisible || !PlaybackWindowAvailable) SetStatus("等待返回播放页面","页面当前不可见，返回后将尝试自动续播。");
                    else if (current.MediaError > 0) SetStatus("视频加载错误","请检查网络或重新加载页面。");
                    else SetStatus(resumeAttempt > 0 ? "正在自动续播" : "暂停，准备自动续播","检测到非手动暂停，约 2 秒后尝试恢复；已恢复 "+AutomaticResumeCount+" 次。");
                }
                else if (current.Status.Length > 0) SetStatus(current.Status,"请检查网络或尝试官方播放器的其他线路；不会自动跳过当前视频。");
                else if (DateTime.UtcNow.Subtract(lastMoved).TotalSeconds > 20) SetStatus("播放位置暂未变化","请检查网络、缓冲和页面提示，必要时重新加载。");
                else { userPaused=false; alerted=false; SetStatus("真实视频播放中","平台断点约每 30 秒核对；如课程要求，请保持播放页面在前台，避免最小化。"); }
                if (current.Paused && !userPaused && !current.ManualPause && !current.Ended && !alerted && !test
                    && (activePrompt.Length > 0 || !autoResume.Checked || recoveryBlock.Length > 0 || resumeAttempt >= 3)) { alerted=true; System.Media.SystemSounds.Exclamation.Play(); }
                Render();
            } catch (Exception) { WriteLog("monitor message rejected"); }
        }
        internal async Task ControlPlayback(string action) {
            if(closing || (action != "play" && action != "pause"))return;
            studyPaused = action == "pause";
            studyCommandVersion++;
            userPaused = action == "pause";
            InvalidateQuiz(false);
            workflowVersion++; ResetWorkflowNavigation(); nextWorkflowCheck=DateTime.MinValue;
            ResetRecovery();
            if (action == "play") alerted = false;
            PublishStudyState();
            SetStatus(studyPaused ? "已暂停学习" : "已继续学习",studyPaused ? "视频、自动答题和章节切换已暂停；点击视频播放键或顶部“播放 / 继续”恢复。" : "正在恢复当前页面的学习流程。");
            Render();
            if(!Ready || studyPaused)return;
            if(navigationFailed) {RetryCourseNavigation();return;}
            if(NeedsTaskRecognition) {
                await RecognizeTasks();
                if(!Ready||closing||studyPaused)return;
                var target=PickMedia();
                if(target==null) {SetWorkflow(TaskPlanCurrent&&ChapterVideosReady(taskPlan)?"视频已核对，准备处理检测":"正在识别并匹配当前页任务");return;}
                SelectMediaTarget(target);
                Browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {channel="study-player-command",token=token,frameId=target.FrameId,mediaId=target.MediaId,action=action}));
                return;
            }
            if (!Progress.HasVideo || Progress.FrameId.Length == 0) { SetStatus("尚未找到视频","请在官方页面选择章节，点击播放器中的播放按钮。"); return; }
            if (action == "play") Browser.Focus();
            SelectMediaTarget(Progress);
            Browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {channel="study-player-command",token=token,frameId=Progress.FrameId,mediaId=Progress.MediaId,action=action}));
            await Task.FromResult(0);
        }
        private void PublishStudyState() {
            if(!Ready || closing)return;
            Browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {channel="study-player-command",token=token,
                action="study-state",paused=studyPaused,version=studyCommandVersion}));
        }
        internal void SetAutomaticResume(bool enabled) { autoResume.Checked=enabled; }
        private void ResetRecovery() {
            pausedSince=DateTime.MinValue; nextResumeAt=DateTime.MinValue; resumeAttempt=0;
            recoveryBlock=""; recoveryFrame=""; recoveryPending=false;
        }
        private string CurrentPrompt() {
            var prompt=prompts.Values.FirstOrDefault(value=>DateTime.UtcNow.Subtract(value.SampleAt).TotalSeconds < 3);
            return prompt == null ? "" : prompt.Detail;
        }
        private void TryAutomaticResume() {
            if(NeedsTaskRecognition&&(!TaskPlanCurrent||!taskPlan.Known||!taskPlan.Tasks.Any(value=>value.Key==activeTaskKey&&value.Kind=="video")))return;
            if (!Ready || closing || studyPaused || !autoResume.Checked || userPaused || !Progress.HasVideo || !Progress.Paused || Progress.Ended
                || Progress.ManualPause || !Progress.PageVisible || !Progress.HasSource || Progress.MediaError > 0
                || !PlaybackWindowAvailable || DateTime.UtcNow.Subtract(Progress.SampleAt).TotalSeconds > 3
                || CurrentPrompt().Length > 0 || VideoQuizActive || recoveryBlock.Length > 0 || resumeAttempt >= 3 || workflowRequest.Length > 0) return;
            if (pausedSince == DateTime.MinValue) { pausedSince=DateTime.UtcNow; return; }
            if (DateTime.UtcNow.Subtract(pausedSince).TotalSeconds < 2 || DateTime.UtcNow < nextResumeAt) return;
            resumeAttempt++; resumeSequence++; recoveryPending=true; recoveryFrame=Progress.FrameId; recoveryPosition=Progress.Position;
            nextResumeAt=DateTime.UtcNow.AddSeconds(5);
            try {
                SelectMediaTarget(Progress);
                Browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {channel="study-player-command",token=token,frameId=Progress.FrameId,mediaId=Progress.MediaId,action="resume",attempt=resumeSequence}));
            } catch (InvalidOperationException) {
                RecoveryUnavailable(); return;
            } catch (COMException) {
                RecoveryUnavailable(); return;
            }
            SetStatus("正在自动续播","第 "+resumeAttempt+" 次尝试；成功恢复后会继续显示真实播放位置。");
            WriteLog("auto-resume attempt="+resumeAttempt);
        }
        private void RecoveryUnavailable() {
            recoveryPending=false; recoveryBlock="浏览器播放进程不可用";
            SetStatus("自动续播未成功","请重新加载页面，或重新启动桌面助手。");
            WriteLog("auto-resume browser unavailable");
        }
        private void SetWorkflow(string text) {
            if (closing || workflow == null) return;
            if (quizSubmissionIssued && (text.StartsWith("本章任务已确认完成") || text.StartsWith("切换后未收到") || text.StartsWith("切换未完成") || text.StartsWith("已到课程目录末尾"))) quizStatus="";
            if (autoQuiz != null && autoQuiz.Checked && !userPaused && quizStatus.Length > 0 && quizAttemptedKey == QuizKey()) text=quizStatus;
            workflow.Text="章节衔接："+text;
            if (!Progress.HasVideo || Progress.Ended || VideoQuizActive) SetStatus(text,quizStatus.Length > 0 && text == quizStatus ? quizStatus : "章节衔接以官方任务标记为准；开启题库自动答题后，全部匹配成功会填写并提交。");
        }
        private bool WorkflowCurrent(string key,int version) {
            return !closing && Ready && !studyPaused && autoAdvance.Checked && !userPaused && !Progress.ManualPause
                && workflowKey == key && workflowVersion == version && workflowVisible
                && DateTime.UtcNow.Subtract(workflowSample).TotalSeconds < 4
                && PlaybackWindowAvailable && CurrentPrompt().Length == 0 && !VideoQuizActive;
        }
        private async Task<OfficialCard> ReadWorkflowCard(string chapter,int card) {
            if (test) {
                if (WorkflowRecordReader == null) throw new IOException("本地测试未设置任务记录。");
                return await WorkflowRecordReader(chapter,card);
            }
            return OfficialCard.Parse(await ReadCardHtml(chapter,card));
        }
        private void ResetWorkflowNavigation() {
            workflowRequest=""; workflowBlock=""; workflowAttemptTarget=""; workflowAttempts=0;
            workflowSentAt=DateTime.MinValue;
        }
        private void RetryWorkflowNavigation(string reason) {
            workflowRequest="";
            if (workflowAttempts >= 3) {
                workflowBlock=reason;
                SetWorkflow("已尝试切换 3 次，仍未进入目标任务；请检查页面后点击刷新平台记录");
            } else {
                nextWorkflowCheck=DateTime.UtcNow.AddSeconds(3);
                SetWorkflow("切换暂未生效，将重新核对任务后重试（已尝试 "+workflowAttempts+"/3）");
            }
            WriteLog("workflow "+(workflowBlock.Length > 0 ? "blocked" : "retry-pending")+" reason="+reason+" attempts="+workflowAttempts);
        }
        private async Task AdvanceWorkflow() {
            if (!Ready || closing || workflowBusy || workflowKey.Length == 0) return;
            if (!autoAdvance.Checked) {SetWorkflow("自动切换已关闭");return;}
            if (studyPaused || userPaused || Progress.ManualPause) {SetWorkflow("已暂停章节衔接，点击播放 / 继续可恢复");return;}
            if (workflowBlock.Length > 0) return;
            if (workflowRequest.Length > 0) {
                // A click acknowledgement is not arrival. Allow the official page to load before retrying.
                if (WorkflowCurrent(workflowKey,workflowVersion) && DateTime.UtcNow.Subtract(workflowSentAt).TotalSeconds > 20)
                    RetryWorkflowNavigation("navigation-timeout");
                return;
            }
            string key=workflowKey, chapter=Progress.KnowledgeId; int version=workflowVersion,card=workflowCard;
            if (!WorkflowCurrent(key,version) || DateTime.UtcNow < nextWorkflowCheck) return;
            // Completion checks can wait while the selected required video is still
            // running or paused. Playback recovery uses the already identified task.
            if(TaskPlanCurrent&&taskPlan.Known&&Progress.HasVideo&&ActiveVideoFinishing()) {
                SetWorkflow("先完成本页视频，再处理章节检测");return;
            }
            nextWorkflowCheck=DateTime.UtcNow.AddSeconds(10); workflowBusy=true;
            try {
                OfficialCard current=await ReadWorkflowCard(chapter,card);
                if (!WorkflowCurrent(key,version)) return;
                RememberTaskPlan(current,key);
                if (current == null || !current.Known) {SetWorkflow("任务类型暂不能确认，请在官方页面处理");return;}
                bool previouslyPending=observedIncomplete;
                if (!current.AllComplete) observedIncomplete=true;
                if (!current.AllComplete) {
                    if (current.PendingVideo) SetWorkflow(Progress.Ended ? "视频已结束，等待平台确认完成" : "先完成本页视频，再处理章节检测");
                    else if (current.PendingQuiz) SetWorkflow("章节测验待提交，平台确认后继续");
                    else SetWorkflow(Progress.Ended ? "视频已结束，等待平台确认完成" : "等待当前视频完成");
                    return;
                }
                if (current.HasVideo && previouslyPending && Progress.HasVideo && !Progress.Ended) {
                    SetWorkflow("平台已确认视频任务，等待当前视频播放结束");return;
                }
                if (card+1 < workflowCardCount) {
                    SendWorkflow("goto-card",chapter,card,card+1,""); return;
                }
                // Recheck every card before leaving a chapter, including any the user visited manually.
                for (int index=0;index<workflowCardCount;index++) {
                    if (index == card) continue;
                    OfficialCard previous=await ReadWorkflowCard(chapter,index);
                    if (!WorkflowCurrent(key,version)) return;
                    if (previous == null || !previous.Known || !previous.AllComplete) {
                        SendWorkflow("goto-card",chapter,card,index,""); return;
                    }
                }
                if (!catalogueKnown) {SetWorkflow("无法确认下一章，请在课程目录中选择");return;}
                if (nextKnowledgeId.Length == 0) {SetWorkflow("已到课程目录末尾，请核对课程总完成情况");return;}
                if (nextChapterLocked) {SetWorkflow("下一章尚未解锁，等待平台更新");return;}
                if (!nextChapterReady) {SetWorkflow("下一章目录入口尚未就绪，等待平台更新");return;}
                SendWorkflow("next-chapter",chapter,card,-1,nextKnowledgeId);
            } catch (Exception error) {
                if (WorkflowCurrent(key,version)) SetWorkflow("任务状态读取失败，稍后重试或点击刷新平台记录");
                WriteLog("workflow unavailable "+error.GetType().Name);
            } finally {workflowBusy=false;}
        }
        private void SendWorkflow(string action,string chapter,int card,int targetCard,string targetChapter) {
            string target=action == "goto-card" ? chapter+":"+targetCard : targetChapter+":0";
            if (workflowAttemptTarget != target) { workflowAttemptTarget=target; workflowAttempts=0; }
            workflowAttempts++;
            workflowRequest=Guid.NewGuid().ToString("N"); workflowSentAt=DateTime.UtcNow;
            Browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {channel="study-player-command",token=token,action=action,
                knowledgeId=chapter,card=card,targetCard=targetCard,targetKnowledgeId=targetChapter,request=workflowRequest}));
            SetWorkflow(action == "goto-card" ? "正在打开下一任务页" : "本章任务已确认完成，正在进入下一章");
            WriteLog("workflow "+action+" chapter="+chapter+" card="+card+" target="+(action == "goto-card" ? targetCard.ToString() : targetChapter)+" attempt="+workflowAttempts);
        }
        internal async Task<CourseProgressSnapshot> ReadCourseProgress(){return OfficialCourseProgress.Parse(await ReadOfficial(CourseUrl(course)));}
        internal async Task RefreshRecord(bool manual) {
            if (manual) RetryQuiz();
            if (manual) { workflowVersion++; ResetWorkflowNavigation(); nextWorkflowCheck=DateTime.MinValue;nextTaskRecognition=DateTime.MinValue;
                CardRead cached;if(cardReads.TryGetValue(workflowKey,out cached)){cached.Html="";cached.RetryAt=DateTime.MinValue;}
            }
            if (!Ready || recordBusy || test) return;
            lastRecordAttempt=DateTime.UtcNow;
            if (Progress.KnowledgeId.Length == 0 || Progress.CardIndex < 0) { if (manual) hint.Text="请先打开具体章节的视频，再刷新平台记录。"; return; }
            string knowledge=Progress.KnowledgeId;
            int card=Progress.CardIndex;
            recordBusy=true; refresh.Enabled=false;
            try {
                var records = new List<OfficialRecord>();
                string html=await ReadCardHtml(knowledge,card);
                records.AddRange(OfficialRecord.Parse(html));
                if (closing || Progress.KnowledgeId != knowledge || Progress.CardIndex != card) return;
                var matching=records.Where(value=>new OfficialTask {JobId=value.JobId??"",ObjectId=value.ObjectId??""}.Matches(Progress.JobId,Progress.ObjectId)).ToList();
                if(matching.Count==0&&records.Count==1&&Progress.JobId.Length==0&&Progress.ObjectId.Length==0)matching.Add(records[0]);
                if (matching.Count != 1) {
                    Progress.HasRecord=false;
                    OfficialCard cardState=OfficialCard.Parse(html);
                    recorded.Text=records.Count == 0 && cardState.HasQuiz ? (cardState.AllComplete ? "章节测验：平台已确认完成" : "章节测验：平台尚未确认完成") : "平台记录：当前页多视频或无视频，请在官方页面核对";
                    return;
                }
                Progress.Recorded=matching[0].Position; Progress.Passed=matching[0].Passed; Progress.HasRecord=true; Progress.RecordedAt=DateTime.Now;
                if (manual) hint.Text="平台记录已刷新；播放器位置与有效观看时长可能不同。";
                WriteLog("record chapter="+knowledge+" seconds="+Progress.Recorded+" passed="+Progress.Passed);
            } catch (Exception error) {
                if (!closing) { Progress.HasRecord=false; recorded.Text="平台记录暂不可读，请检查官方页面登录或确认提示"; }
                WriteLog("record unavailable " + error.GetType().Name);
            } finally { recordBusy=false; if (!closing) { refresh.Enabled=true; Render(); } }
        }
        private void ReloadPage() {
            if (!Ready) return;
            if (Progress.HasVideo && MessageBox.Show(this,"重新加载会中断当前播放，是否继续？","重新加载",MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            if(navigationFailed||!Trusted(Browser.CoreWebView2.Source,test))RetryCourseNavigation();
            else {navigationRetries=0;Browser.CoreWebView2.Reload();}
        }
        private static string NavigationRoute(string value) {
            Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri))return "internal";
            string kind=uri.AbsolutePath.EndsWith("/studentcourse")?"course":uri.AbsolutePath.EndsWith("/studentstudy")?"chapter":uri.AbsolutePath.EndsWith("/transfer")?"transfer":"page";
            return "host="+uri.Host+" kind="+kind;
        }
        private void RetryCourseNavigation() {
            if(!Ready||closing)return;
            string target=test&&Trusted(navigationTarget,true)?navigationTarget:entryUrl;
            navigationRetries=0;navigationFailed=false;navigationBlocked=false;
            if(!test){initialChapterPending=true;initialChapterClickNavigation=0;}
            Browser.CoreWebView2.Navigate(target);
        }
        private void RecoverNavigation() {
            if(!Ready||closing)return;
            if(navigationInFlight&&!navigationStopping&&DateTime.UtcNow.Subtract(navigationStartedAt).TotalSeconds>30) {
                navigationStopping=true;Browser.CoreWebView2.Stop();return;
            }
            if(!navigationFailed||navigationBlocked||navigationInFlight||navigationRetries>=2||DateTime.UtcNow<navigationRetryAt)return;
            // A replacement navigation or recovered media takes priority over a retry.
            if(Progress.HasVideo&&!Progress.Paused&&!Progress.Ended){navigationFailed=false;return;}
            navigationRetries++;navigationFailed=false;
            string target=navigationRetries==1&&Trusted(navigationTarget,test)?navigationTarget:(test&&Trusted(navigationTarget,true)?navigationTarget:entryUrl);
            if(!test&&target==entryUrl){initialChapterPending=true;initialChapterClickNavigation=0;}
            WriteLog("navigation retry attempt="+navigationRetries);
            SetStatus("正在重新载入课程（"+navigationRetries+"/2）","保留登录和上次章节；暂停状态不会解除。");
            Browser.CoreWebView2.Navigate(target);
        }
        private void SetStatus(string text, string detail) {
            if (closing) return;
            if(studyPaused){text="已暂停学习";detail="视频、自动答题和章节切换已暂停；点击视频播放键或顶部“播放 / 继续”恢复。";}
            bool changed=Progress.Status!=text||Progress.Detail!=detail;
            Progress.Status=text; Progress.Detail=detail; status.Text=text;
            string tasks=TaskSummary();
            hint.Text=studyPaused ? "学习已暂停 · 点击视频播放键或顶部“播放 / 继续”恢复" : (tasks.Length>0?tasks+" · ":"")+detail;
            detailTips.SetToolTip(status,text);detailTips.SetToolTip(hint,detail);
            if (changed && Changed != null) Changed();
        }
        private void Render() {
            if (closing) return;
            if (Progress.HasVideo && DateTime.UtcNow.Subtract(Progress.SampleAt).TotalSeconds > 5) {
                Progress.HasVideo=false; SetStatus("等待播放器状态","页面可能正在切换；不会估算或补写播放位置。");
            }
            position.Text=Progress.HasVideo ? Clock(Progress.Position)+" / "+(Progress.Duration > 0 ? Clock(Progress.Duration) : "待载入") : "等待视频";
            playbackBar.Value=Progress.HasVideo&&Progress.Duration>0 ? Progress.Position*100/Progress.Duration : 0;playbackBar.Invalidate();
            status.ForeColor=studyPaused ? DesktopTheme.Muted : DesktopTheme.Blue;
            if (Progress.HasRecord) recorded.Text="平台断点："+Clock(Progress.Recorded)+" · "+(Progress.Passed ? "已确认完成" : "尚未确认完成")+" · "+Progress.RecordedAt.ToString("HH:mm:ss");
            play.Enabled=Ready || studyPaused;
            pause.Enabled=!studyPaused;
            reload.Enabled=Ready;
            bool playing=!studyPaused && Progress.HasVideo && !Progress.Paused && !Progress.Ended;
            if (playing != keepAwake && !test) {
                SetThreadExecutionState(playing ? 0x80000003u : 0x80000000u); keepAwake=playing;
            }
            if (Changed != null) Changed();
        }
        private void RememberChapter(string knowledge) {
            try {
                string file=Path.Combine(root,"data","browser-chapters.json");
                var locations=File.Exists(file) ? json.Deserialize<Dictionary<string,string>>(File.ReadAllText(file,Encoding.UTF8)) : new Dictionary<string,string>();
                locations[course.Id]=knowledge;
                File.WriteAllText(file,json.Serialize(locations),Encoding.UTF8);
            } catch (Exception) { WriteLog("chapter preference unavailable"); }
        }
        private static double Number(Dictionary<string,object> data,string key) { double value; return Double.TryParse(OfficialRecord.Value(data,key),out value) ? Math.Max(0,value) : 0; }
        private static bool Boolean(Dictionary<string,object> data,string key) { return OfficialRecord.Value(data,key).Equals("true",StringComparison.OrdinalIgnoreCase); }
        private static string Clock(double seconds) { var time=TimeSpan.FromSeconds(Math.Max(0,seconds)); return ((int)time.TotalMinutes).ToString("00")+":"+time.Seconds.ToString("00"); }
        private void WriteLog(string message) { try { File.AppendAllText(Path.Combine(root,"logs",test ? "official-player-fixture.log" : "official-player.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine,Encoding.UTF8); } catch (IOException) { } }
    }
}
