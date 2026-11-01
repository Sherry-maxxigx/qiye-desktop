using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("栖页")]
[assembly: System.Reflection.AssemblyProduct("栖页")]
[assembly: System.Reflection.AssemblyDescription("栖页 · 学习，自有栖处")]

namespace SuperAutoDesktop
{
    internal static class AppIdentity
    {
        internal const string Name="栖页";
        internal const string Tagline="学习，自有栖处";
    }

    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);

        [STAThread]
        private static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = 8;
            try{TaskbarIdentity.ApplyProcess();}catch{}
            bool first;
            string installRoot=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..")).ToUpperInvariant();
            string instance;
            using(var hash=System.Security.Cryptography.SHA256.Create())instance=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(installRoot))).Replace("-","").Substring(0,16);
            using (Mutex mutex = new Mutex(true, @"Local\Qiye.Desktop."+instance, out first))
            {
                if (!first)
                {
                    IntPtr existing = FindWindow(null, AppIdentity.Name);
                    if (existing != IntPtr.Zero) { ShowWindow(existing, 9); SetForegroundWindow(existing); }
                    return;
                }
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (sender, error) => MessageBox.Show(error.Exception.Message, AppIdentity.Name);
                Application.Run(new MainWindow(args.Contains("--smoke-test")));
            }
        }
    }

    internal sealed class CourseItem
    {
        public string Id;
        public string Title;
        public string Teacher;
        public string ClazzId;
        public string Cpi;
        public Dictionary<string, object> Task;
        public override string ToString() { return Title; }
    }

    // A display clock; platform positions and completion flags are never changed by it.
    internal sealed class PlaybackClock
    {
        public readonly double Recorded, Duration, RecordAge;
        public readonly bool Running;
        private readonly double anchor;
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private double lastShown;
        internal const double MaximumRecordAge = 75;

        public PlaybackClock(double recorded, double reported, double duration, double recordAge, bool running)
        {
            Duration = Math.Max(0, duration);
            Recorded = Math.Min(Duration, Math.Max(0, recorded));
            RecordAge = Math.Max(0, recordAge);
            Running = running && Duration > 0;
            anchor = Running ? Math.Min(Duration, Math.Max(Recorded, reported) + Math.Min(RecordAge, MaximumRecordAge)) : Recorded;
            lastShown = anchor;
        }

        public double Read(bool connected) { return At(elapsed.Elapsed.TotalSeconds, connected); }
        internal double At(double seconds, bool connected)
        {
            if (connected)
                lastShown = Running ? Math.Min(Duration, anchor + Math.Min(Math.Max(0, seconds), Math.Max(0, MaximumRecordAge - RecordAge))) : Recorded;
            return lastShown;
        }
        public bool Stale { get { return Running && RecordAge + elapsed.Elapsed.TotalSeconds >= MaximumRecordAge; } }
    }

    internal static class DesktopTheme
    {
        internal static readonly Color Ink=Color.FromArgb(40,43,64), Muted=Color.FromArgb(103,114,133),
            Blue=Color.FromArgb(104,78,215), Pale=Color.FromArgb(250,249,246), Line=Color.FromArgb(231,229,239),
            Violet=Color.FromArgb(104,78,215), VioletPale=Color.FromArgb(240,235,255), Sidebar=Color.FromArgb(249,247,255),
            Teal=Color.FromArgb(16,121,112), TealPale=Color.FromArgb(228,246,240),
            Coral=Color.FromArgb(176,72,48), CoralPale=Color.FromArgb(255,237,227),
            Amber=Color.FromArgb(145,97,18), AmberPale=Color.FromArgb(255,245,220),
            Rose=Color.FromArgb(174,65,118), Sky=Color.FromArgb(45,108,166);
        internal static Color Blend(Color from,Color to,double amount) {
            return Color.FromArgb((int)(from.R+(to.R-from.R)*amount),(int)(from.G+(to.G-from.G)*amount),(int)(from.B+(to.B-from.B)*amount));
        }
        internal static Color CourseAccent(string id) {
            Color[] colors={Violet,Teal,Coral,Sky,Rose,Amber};
            int key=0;foreach(char c in id ?? "")key=(key*31+c)%997;
            return colors[key%colors.Length];
        }
        internal static GraphicsPath Rounded(RectangleF bounds,float radius) {
            float d=Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height));
            var path=new GraphicsPath();
            path.AddArc(bounds.X,bounds.Y,d,d,180,90);path.AddArc(bounds.Right-d,bounds.Y,d,d,270,90);
            path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);path.AddArc(bounds.X,bounds.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
    }

    internal sealed class ModernButton : Button
    {
        private bool hovered,pressed;
        public ModernButton() {
            FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;
            BackColor=Color.White;ForeColor=DesktopTheme.Ink;UseVisualStyleBackColor=false;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        }
        protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hovered=pressed=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent==null ? DesktopTheme.Pale : Parent.BackColor);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            bool primary=ForeColor.ToArgb()==Color.White.ToArgb();
            Color shade=primary ? Color.Black : ForeColor;
            Color fill=!Enabled ? Color.FromArgb(239,238,243) : pressed ? DesktopTheme.Blend(BackColor,shade,0.14)
                : hovered ? DesktopTheme.Blend(BackColor,shade,0.07) : BackColor;
            using(var path=DesktopTheme.Rounded(new RectangleF(0,0,Math.Max(1,Width-1),Math.Max(1,Height-1)),8))
            using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,path);
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,!Enabled ? Color.FromArgb(157,167,183) : ForeColor,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(5,5,Width-11,Height-11),ForeColor,fill);
        }
    }

    internal sealed class SurfacePanel : Panel
    {
        internal Color GradientEnd=Color.Empty;
        public SurfacePanel(){DoubleBuffered=true;BackColor=Color.White;}
        protected override void OnPaintBackground(PaintEventArgs e) {
            e.Graphics.Clear(Parent==null ? DesktopTheme.Pale : Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=DesktopTheme.Rounded(new RectangleF(0,0,Math.Max(1,Width-1),Math.Max(1,Height-1)),12))
            using(Brush brush=GradientEnd.IsEmpty ? (Brush)new SolidBrush(BackColor) : new LinearGradientBrush(ClientRectangle,BackColor,GradientEnd,45F))e.Graphics.FillPath(brush,path);
        }
    }

    internal sealed class FlatProgress : Control
    {
        public double Value;
        public Color Fill = DesktopTheme.Blue;
        public FlatProgress() { DoubleBuffered = true; Height = 9; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var track=DesktopTheme.Rounded(new RectangleF(0,0,Math.Max(1,Width),Math.Max(1,Height)),Height/2F))
            using(Brush background=new SolidBrush(DesktopTheme.Line))e.Graphics.FillPath(background,track);
            float filled=(float)(Width*Math.Max(0,Math.Min(100,Value))/100);
            if(filled>0)using(var bar=DesktopTheme.Rounded(new RectangleF(0,0,filled,Math.Max(1,Height)),Height/2F))
            using(Brush foreground=new SolidBrush(Fill))e.Graphics.FillPath(foreground,bar);
        }
    }

    internal sealed class CourseListBox : ListBox
    {
        protected override void OnDrawItem(DrawItemEventArgs e) {
            if(e.Bounds.Width<=0||e.Bounds.Height<=0){base.OnDrawItem(e);return;}
            // Compose each native owner-drawn row before presenting its background and text.
            using(var frame=BufferedGraphicsManager.Current.Allocate(e.Graphics,e.Bounds)) {
                base.OnDrawItem(new DrawItemEventArgs(frame.Graphics,e.Font,e.Bounds,e.Index,e.State,e.ForeColor,e.BackColor));
                frame.Render(e.Graphics);
            }
        }
    }

    internal sealed class MainWindow : Form
    {
        private static readonly Color Ink = DesktopTheme.Ink;
        private static readonly Color Muted = DesktopTheme.Muted;
        private static readonly Color Blue = DesktopTheme.Blue;
        private static readonly Color Pale = DesktopTheme.Pale;
        private readonly string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly HttpClient http = new HttpClient(new HttpClientHandler {UseProxy=false});
        private readonly ToolTip overviewTips=new ToolTip {AutoPopDelay=15000};
        private readonly System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer liveTimer = new System.Windows.Forms.Timer();
        private readonly Dictionary<string, PlaybackClock> playbackClocks = new Dictionary<string, PlaybackClock>();
        private readonly List<CourseItem> items = new List<CourseItem>();
        private readonly Dictionary<string,string> courseRowContent = new Dictionary<string,string>();
        private readonly bool smokeTest;
        private readonly CourseProgressStore courseProgress;
        private readonly Dictionary<string,Task> progressReads=new Dictionary<string,Task>();
        private readonly Dictionary<string,DateTime> progressAttempts=new Dictionary<string,DateTime>();
        private readonly Dictionary<string,string> progressErrors=new Dictionary<string,string>();
        internal Func<CourseItem,Task<CourseProgressSnapshot>> CourseProgressLoader {get;set;}
        private bool connected, busy, polling, rendering, shuttingDown;
        private int revision;
        private string selectedId = "";
        private string accountHint = "未登录";
        private DateTime noticeUntil = DateTime.MinValue;
        private DateTime lastWindowRefresh = DateTime.MinValue;
        private OfficialPlayer officialPlayer;
        private OfficialLoginView loginView;
        private OfficialHomeworkView homeworkView;
        private Task backendInitialization;
        private Panel main, studyPanel, studyHost;
        private Label studyTitle;
        internal Func<CourseItem,OfficialPlayer> PlayerFactory {get;set;}
        private ListBox courseList;
        private TextBox search;
        private Label serviceLabel, courseCount, accountLabel, courseTitle, stateLabel, chapterNumber, chapterCaption;
        private Label videoNumber, videoCaption, currentChapter, currentVideo, confirmation, updatedLabel;
        private Label videoHeading;
        private Label alertTitle, alertText, footer;
        private Panel alertPanel;
        private FlatProgress chapterBar, videoBar;
        private Button startButton, pauseButton, refreshButton, loginButton, retryButton, progressRefreshButton, endCourseButton, homeworkButton;
        private Icon taskbarSmall,taskbarLarge;

        public MainWindow(bool test)
        {
            smokeTest = test;
            courseProgress=new CourseProgressStore(root);
            Text = AppIdentity.Name;
            Name = "StudyDesktop";
            Font = new Font("Microsoft YaHei UI", 10F);
            ForeColor = Ink;
            BackColor = Pale;
            StartPosition = FormStartPosition.CenterScreen;
            Rectangle workArea = Screen.FromControl(this).WorkingArea;
            ClientSize = new Size(Math.Min(1360,workArea.Width-48),Math.Min(900,workArea.Height-72));
            MinimumSize = new Size(1060, 730);
            AutoScaleMode = AutoScaleMode.Dpi;
            try {Icon=new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"app.ico"),32,32);taskbarSmall=new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"app.ico"),16,16);taskbarLarge=new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"app.ico"),48,48);}catch{}
            http.BaseAddress = LocalServices.Address(root);
            http.Timeout = TimeSpan.FromSeconds(100);
            json.MaxJsonLength = 4 * 1024 * 1024;
            BuildLayout();
            refreshTimer.Interval = 3000;
            refreshTimer.Tick += async (sender, e) => await RefreshState();
            liveTimer.Interval = 1000;
            liveTimer.Tick += (sender, e) => RenderVideo();
            KeyPreview = true;
            KeyDown += async (sender, e) => {
                if (e.KeyCode == Keys.F5) {
                    e.Handled = true; e.SuppressKeyPress = true; await RefreshState(true);
                    if (CurrentBrowserProgress() != null) await officialPlayer.RefreshRecord(true);
                }
            };
            Shown += async (sender, e) => {ApplyTaskbarIcons();if(!smokeTest)DesktopBrowserRuntime.Warm(root);await InitializeBackend();};
            FormClosing += (sender,e) => { shuttingDown=true; CloseCoursePage(); if(loginView!=null)loginView.Dispose(); };
            FormClosed += (sender, e) => { refreshTimer.Stop(); liveTimer.Stop(); refreshTimer.Dispose(); liveTimer.Dispose();overviewTips.Dispose(); http.Dispose();if(taskbarSmall!=null)taskbarSmall.Dispose();if(taskbarLarge!=null)taskbarLarge.Dispose(); };
        }

        [DllImport("user32.dll",EntryPoint="SendMessage")] private static extern IntPtr SendIcon(IntPtr window,uint message,IntPtr kind,IntPtr icon);
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            ApplyTaskbarIcons();
        }
        private void ApplyTaskbarIcons() {
            try{TaskbarIdentity.ApplyWindow(Handle,Application.ExecutablePath,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","qiye-taskbar.ico"));}catch{}
            if(taskbarSmall!=null){SendIcon(Handle,0x80,IntPtr.Zero,taskbarSmall.Handle);SendIcon(Handle,0x80,new IntPtr(2),taskbarSmall.Handle);}
            if(taskbarLarge!=null)SendIcon(Handle,0x80,new IntPtr(1),taskbarLarge.Handle);
        }

        private Label LabelAt(Control parent, string text, int x, int y, int w, int h, float size, Color color, FontStyle style)
        {
            Label label = new Label { Text = text, Location = new Point(x, y), Size = new Size(w, h),
                Font = new Font("Microsoft YaHei UI", size, style), ForeColor = color,
                AutoEllipsis = true, BackColor = Color.Transparent };
            parent.Controls.Add(label);
            return label;
        }

        private Button ButtonAt(Control parent, string text, int x, int y, int w, bool primary)
        {
            Button button = new ModernButton { Text = text, Location = new Point(x, y), Size = new Size(w, 42),
                FlatStyle = FlatStyle.Flat, BackColor = primary ? Blue : Color.White,
                ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", 10F, primary ? FontStyle.Bold : FontStyle.Regular),
                UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(215, 224, 238);
            parent.Controls.Add(button);
            return button;
        }

        private Panel Card(int x, int y, int width, int height)
        {
            Panel panel = new SurfacePanel { Location = new Point(x, y), Size = new Size(width, height), BackColor = Color.White };
            main.Controls.Add(panel);
            return panel;
        }

        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,string text);
        private static void SetSearchPlaceholder(TextBox box) {
            box.HandleCreated+=(s,e)=>SendMessage(box.Handle,0x1501,new IntPtr(1),"搜索课程");
        }

        private void BuildLayout()
        {
            Panel sidebar = new Panel { Dock = DockStyle.Left, Width = 260, BackColor = DesktopTheme.Sidebar };
            Controls.Add(sidebar);
            var brand=new PictureBox {Name="BrandIcon",AccessibleName=AppIdentity.Name+"图标",Location=new Point(24,26),Size=new Size(44,44),SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.Transparent};
            try {using(var artwork=Image.FromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","qiye-icon.png")))brand.Image=new Bitmap(artwork);}
            catch {if(Icon!=null)brand.Image=Icon.ToBitmap();}
            sidebar.Controls.Add(brand);
            FormClosed+=(sender,args)=>{if(brand.Image!=null){brand.Image.Dispose();brand.Image=null;}};
            LabelAt(sidebar, AppIdentity.Name, 81, 20, 162, 36, 22, Ink, FontStyle.Bold).Name="BrandName";
            LabelAt(sidebar, AppIdentity.Tagline, 82, 59, 162, 22, 9, Muted, FontStyle.Regular);
            accountLabel = LabelAt(sidebar, "可直接点击下方扫码登录", 24, 99, 215, 24, 9, Muted, FontStyle.Regular);
            var searchBox=new SurfacePanel {Location=new Point(20,139),Size=new Size(220,40),BackColor=Color.White};sidebar.Controls.Add(searchBox);
            search = new TextBox { Name = "CourseSearch", Location = new Point(12,10), Size = new Size(196,24),
                Font = new Font("Microsoft YaHei UI", 10), BorderStyle = BorderStyle.None, BackColor=Color.White };
            searchBox.Controls.Add(search);SetSearchPlaceholder(search);
            search.TextChanged += (sender, e) => PopulateCourses();
            courseCount = LabelAt(sidebar, "我的课程", 24, 203, 215, 26, 10, DesktopTheme.Violet, FontStyle.Bold);
            courseList = new CourseListBox { Name = "CourseList", Location = new Point(16, 242), Size = new Size(228, 412),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom,
                BorderStyle = BorderStyle.None, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 64, BackColor = DesktopTheme.Sidebar };
            sidebar.Controls.Add(courseList);
            courseList.DrawItem += DrawCourse;
            courseList.SelectedIndexChanged += async (sender, e) => {
                if (rendering) return;
                CourseItem item = courseList.SelectedItem as CourseItem;
                selectedId = item == null ? "" : item.Id;
                noticeUntil = DateTime.MinValue;
                ReconcileCoursePage();
                UpdateDetails();
                await RefreshSelectedCourseProgress(false);
            };
            refreshButton = ButtonAt(sidebar, "刷新课程", 20, 680, 106, false);refreshButton.BackColor=DesktopTheme.TealPale;refreshButton.ForeColor=DesktopTheme.Teal;
            refreshButton.Name = "RefreshCourses";
            refreshButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            refreshButton.Click += async (sender, e) => await RefreshCourses(null, null);
            loginButton = ButtonAt(sidebar, "扫码登录", 136, 680, 104, false);loginButton.BackColor=DesktopTheme.VioletPale;loginButton.ForeColor=DesktopTheme.Violet;
            loginButton.Name="QrLogin";
            loginButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            loginButton.Click += async (sender, e) => await OpenQrLogin();
            Label sideNote = LabelAt(sidebar, "课程与学习记录", 24, 744, 215, 24, 9, Muted, FontStyle.Regular);
            sideNote.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

            var workspace = new Panel {Dock=DockStyle.Fill,Name="CourseWorkspace",BackColor=Pale};
            Controls.Add(workspace); workspace.BringToFront();
            main = new Panel { Dock = DockStyle.Fill, BackColor = Pale, AutoScroll = true, Name="CourseOverview" };
            workspace.Controls.Add(main);
            main.BringToFront();
            LabelAt(main, "课程概览", 32, 29, 300, 28, 11, Muted, FontStyle.Bold);
            serviceLabel = LabelAt(main, "● 正在连接后台", 527, 29, 290, 26, 10, Muted, FontStyle.Regular);
            serviceLabel.TextAlign = ContentAlignment.MiddleRight;
            courseTitle = LabelAt(main, "选择一门课程", 32, 80, 770, 69, 23, Ink, FontStyle.Bold);
            courseTitle.Name = "SelectedCourseTitle";
            stateLabel = LabelAt(main, "等待连接", 34, 157, 180, 29, 11, Muted, FontStyle.Bold);
            stateLabel.Name = "TaskState";
            LabelAt(main, "进度以学习通记录为准", 233, 159, 480, 25, 9, Muted, FontStyle.Regular);

            Panel chapterCard = Card(32, 208, 370, 165);
            chapterCard.BackColor=DesktopTheme.TealPale;
            LabelAt(chapterCard, "章节进度 · 学习通记录", 21, 18, 320, 25, 10, DesktopTheme.Teal, FontStyle.Regular);
            chapterNumber = LabelAt(chapterCard, "—", 20, 46, 325, 48, 28, DesktopTheme.Teal, FontStyle.Bold);
            chapterNumber.Name = "ChapterProgressText";
            chapterBar = new FlatProgress { Location = new Point(22, 107), Width = 324, Fill=DesktopTheme.Teal };
            chapterCard.Controls.Add(chapterBar);
            chapterCaption = LabelAt(chapterCard, "等待课程记录", 22, 128, 327, 25, 9, Muted, FontStyle.Regular);

            Panel videoCard = Card(420, 208, 397, 165);
            videoCard.BackColor=DesktopTheme.CoralPale;
            videoHeading = LabelAt(videoCard, "视频位置 · 历史平台断点", 21, 18, 350, 25, 10, DesktopTheme.Coral, FontStyle.Regular);
            videoNumber = LabelAt(videoCard, "— / —", 20, 48, 356, 45, 25, DesktopTheme.Coral, FontStyle.Bold);
            videoNumber.Name = "VideoPositionText";
            videoBar = new FlatProgress { Location = new Point(22, 107), Width = 350, Fill = DesktopTheme.Coral };
            videoCard.Controls.Add(videoBar);
            videoCaption = LabelAt(videoCard, "播放位置不等于有效观看时长", 22, 128, 355, 25, 9, Muted, FontStyle.Regular);
            videoCaption.Name = "RecordedVideoPosition";

            Panel detailCard = Card(32, 391, 785, 150);
            LabelAt(detailCard, "当前章节", 21, 17, 100, 24, 9, Muted, FontStyle.Regular);
            currentChapter = LabelAt(detailCard, "尚未读取", 129, 16, 625, 27, 11, Ink, FontStyle.Regular);
            LabelAt(detailCard, "当前视频", 21, 54, 100, 24, 9, Muted, FontStyle.Regular);
            currentVideo = LabelAt(detailCard, "尚未开始", 129, 53, 625, 27, 11, Ink, FontStyle.Regular);
            currentVideo.Name = "CurrentVideo";
            LabelAt(detailCard, "平台确认", 21, 91, 100, 24, 9, Muted, FontStyle.Regular);
            confirmation = LabelAt(detailCard, "待核验", 129, 90, 625, 25, 11, Muted, FontStyle.Bold);
            confirmation.Name = "PlatformConfirmation";
            updatedLabel = LabelAt(detailCard, "", 21, 124, 735, 21, 8, Muted, FontStyle.Regular);
            updatedLabel.Name = "ProgressUpdatedAt";

            alertPanel = Card(32, 558, 785, 88);
            alertPanel.BackColor = DesktopTheme.VioletPale;
            alertTitle = LabelAt(alertPanel, "准备就绪", 18, 11, 740, 25, 10, Blue, FontStyle.Bold);
            alertText = LabelAt(alertPanel, "请选择课程，然后点击开始。", 18, 39, 744, 45, 10, Ink, FontStyle.Regular);
            alertText.Name = "TaskMessage";

            startButton = ButtonAt(main, "打开视频 / 继续", 32, 668, 165, true);
            startButton.Name = "StartTask";
            startButton.Click += async (sender, e) => await OpenOfficialPlayer();
            pauseButton = ButtonAt(main, "暂停学习", 210, 668, 130, false);
            pauseButton.Width=110;
            pauseButton.BackColor=DesktopTheme.CoralPale;pauseButton.ForeColor=DesktopTheme.Coral;
            pauseButton.Name = "PauseTask";
            pauseButton.Click += async (sender, e) => {
                if (CurrentBrowserProgress() != null) await officialPlayer.ControlPlayback("pause");
                else await Command("pause");
            };
            Button official = ButtonAt(main, "打开学习通核对", 353, 668, 176, false);
            official.Width=155;
            official.BackColor=DesktopTheme.VioletPale;official.ForeColor=DesktopTheme.Violet;
            official.Click += (sender, e) => OpenPath("https://i.chaoxing.com/");
            homeworkButton=ButtonAt(main,"课程作业",353,668,110,false);
            homeworkButton.Name="OpenHomework";homeworkButton.BackColor=DesktopTheme.AmberPale;homeworkButton.ForeColor=DesktopTheme.Amber;
            homeworkButton.Click+=async(sender,e)=>await OpenHomework();
            progressRefreshButton = ButtonAt(main, "刷新进度", 543, 668, 145, false);
            progressRefreshButton.Width=110;
            progressRefreshButton.BackColor=DesktopTheme.TealPale;progressRefreshButton.ForeColor=DesktopTheme.Teal;
            progressRefreshButton.Name = "RefreshProgress";
            progressRefreshButton.Click += async (sender, e) => {
                await RefreshState(true);
                if (CurrentBrowserProgress() != null) await officialPlayer.RefreshRecord(true);
            };
            endCourseButton=ButtonAt(main,"结束课程",700,668,110,false);
            endCourseButton.Name="EndCourse";endCourseButton.BackColor=DesktopTheme.CoralPale;endCourseButton.ForeColor=DesktopTheme.Coral;
            endCourseButton.Click+=async(sender,e)=>await EndSelectedCourse();
            retryButton = ButtonAt(main, "重新连接", 700, 668, 117, false);
            retryButton.Visible = false;
            retryButton.Click += async (sender, e) => await InitializeBackend();
            footer = LabelAt(main, "打开课程，继续上次的学习。", 32, 727, 785, 25, 9, Muted, FontStyle.Regular);
            var actions=new FlowLayoutPanel {Name="OverviewActions",Location=new Point(32,668),Width=785,Height=48,WrapContents=true};main.Controls.Add(actions);
            actions.Controls.AddRange(new Control[]{startButton,pauseButton,homeworkButton,progressRefreshButton,endCourseButton,official,retryButton});
            foreach(Control action in actions.Controls)action.Margin=new Padding(0,0,10,8);
            foreach(Panel card in new[]{chapterCard,videoCard,detailCard,alertPanel})
                foreach(Control child in card.Controls)if(child.Width>150)child.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
            Action arrangeOverview=()=>{
                int width=Math.Max(600,main.ClientSize.Width-64);
                courseTitle.Width=width;serviceLabel.Left=Math.Max(340,main.ClientSize.Width-serviceLabel.Width-32);
                chapterCard.Width=(width-16)/2;videoCard.Left=chapterCard.Right+16;videoCard.Width=width-chapterCard.Width-16;
                detailCard.Width=alertPanel.Width=footer.Width=width;actions.Width=width;
                actions.Height=Math.Max(50,actions.GetPreferredSize(new Size(width,0)).Height);footer.Top=actions.Bottom+12;
            };
            main.SizeChanged+=(sender,args)=>arrangeOverview();retryButton.VisibleChanged+=(sender,args)=>arrangeOverview();arrangeOverview();
            studyPanel = new Panel {Dock=DockStyle.Fill,Name="CourseStudyView",BackColor=Color.White,Visible=false};
            workspace.Controls.Add(studyPanel);
            var studyHeader=new Panel {Dock=DockStyle.Top,Height=68,Padding=new Padding(20,16,20,12),BackColor=DesktopTheme.Sidebar};
            studyHost=new Panel {Dock=DockStyle.Fill,Name="OfficialCourseHost",BackColor=Color.White};
            studyPanel.Controls.Add(studyHost);studyPanel.Controls.Add(studyHeader);
            studyHost.BringToFront();
            var overviewButton=new ModernButton {Text="课程概览",Name="ShowCourseOverview",Dock=DockStyle.Right,Width=96,BackColor=DesktopTheme.VioletPale,ForeColor=DesktopTheme.Violet};
            overviewButton.FlatAppearance.BorderColor=Color.FromArgb(215,224,238);
            overviewButton.Click+=async(sender,e)=>{
                if(officialPlayer!=null&&!officialPlayer.IsDisposed)await officialPlayer.ControlPlayback("pause");
                ShowCourseOverview();
                await RefreshSelectedCourseProgress(true);
            };
            studyTitle=new Label {Dock=DockStyle.Fill,Text="课程页面",Name="EmbeddedCourseTitle",AutoEllipsis=true,
                TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Microsoft YaHei UI",14,FontStyle.Bold)};
            studyHeader.Controls.Add(studyTitle);studyHeader.Controls.Add(overviewButton);
            UpdateDetails();
        }

        private void DrawCourse(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= courseList.Items.Count) return;
            CourseItem item = (CourseItem)courseList.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color accent=DesktopTheme.CourseAccent(item.Id);
            using (Brush background = new SolidBrush(courseList.BackColor))
                e.Graphics.FillRectangle(background, e.Bounds);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            if(selected){using(var path=DesktopTheme.Rounded(new RectangleF(e.Bounds.X,e.Bounds.Y+2,e.Bounds.Width-2,e.Bounds.Height-4),9))using(var fill=new SolidBrush(DesktopTheme.Blend(accent,Color.White,0.88)))e.Graphics.FillPath(fill,path);}
            var badge=new Rectangle(e.Bounds.X+10,e.Bounds.Y+17,29,30);
            using(var path=DesktopTheme.Rounded(badge,8))using(var fill=new SolidBrush(DesktopTheme.Blend(accent,Color.White,selected?0.0:0.88)))e.Graphics.FillPath(fill,path);
            const TextFormatFlags bufferedText=TextFormatFlags.PreserveGraphicsTranslateTransform|TextFormatFlags.PreserveGraphicsClipping;
            using(var font=new Font("Microsoft YaHei UI",10F,FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics,String.IsNullOrWhiteSpace(item.Title)?"课":item.Title.Substring(0,1),font,badge,selected?Color.White:accent,bufferedText|TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            string title = item.Title;
            Rectangle titleBounds = new Rectangle(e.Bounds.X + 49, e.Bounds.Y + 10, e.Bounds.Width - 61, 24);
            using (Font titleFont = new Font("Microsoft YaHei UI", 10.5F, selected ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(e.Graphics, title, titleFont,
                    titleBounds, selected?accent:Ink, bufferedText | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            using (Font secondaryFont = new Font("Microsoft YaHei UI", 9))
                TextRenderer.DrawText(e.Graphics, CourseSecondary(item), secondaryFont,
                    new Rectangle(e.Bounds.X + 50, e.Bounds.Y + 36, e.Bounds.Width - 62, 21), Muted,
                    bufferedText | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        private string CourseSecondary(CourseItem item)
        {
            var record=courseProgress.Get(courseProgress.Key(item));
            if(record!=null&&record.Ended)return "本次学习已结束";
            if(record!=null&&record.Progress!=null&&record.Progress.AllCompleted)return "章节已全部完成";
            string state = TaskState(item.Task);
            if (officialPlayer != null && !officialPlayer.IsDisposed && officialPlayer.Progress.CourseId == item.Id)
                state = officialPlayer.Progress.Status;
            else if (NumberOf(item.Task,"status",-1) == 5) state = "待继续";
            return officialPlayer != null && !officialPlayer.IsDisposed && officialPlayer.Progress.CourseId == item.Id ? state
                : item.Task == null ? (String.IsNullOrEmpty(item.Teacher) ? "尚未创建任务" : item.Teacher) : state;
        }

        private void RefreshCourseRows(bool invalidateChanges = true)
        {
            if(courseList==null)return;
            for(int index=0;index<courseList.Items.Count;index++) {
                var item=(CourseItem)courseList.Items[index];
                string content=item.Title+"\n"+CourseSecondary(item),previous;
                bool changed=!courseRowContent.TryGetValue(item.Id,out previous)||previous!=content;
                courseRowContent[item.Id]=content;
                if(changed&&invalidateChanges) {
                    Rectangle row=courseList.GetItemRectangle(index);
                    if(row.IntersectsWith(courseList.ClientRectangle))courseList.Invalidate(row);
                }
            }
        }

        private Task InitializeBackend()
        {
            if(backendInitialization!=null&&!backendInitialization.IsCompleted)return backendInitialization;
            if(busy)return Task.FromResult(0);
            backendInitialization=InitializeBackendCore();return backendInitialization;
        }

        private async Task InitializeBackendCore()
        {
            if (busy) return;
            busy = true; connected = false; revision++;
            serviceLabel.Text = "● 正在连接本机后台";
            SetNotice("正在连接", "正在准备本机后台；你可以先点击左下方“扫码登录”。", false);
            UpdateDetails();
            try
            {
                bool ready = false;
                try { var health=await Request("GET", "/health", null, 3);ready=TextOf(health,"installationId")==LocalServices.InstallationId(root); } catch { }
                if (!ready)
                {
                    await Task.Run(() => {
                        ProcessStartInfo info = new ProcessStartInfo {
                            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                            Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "start-local.ps1") + "\"",
                            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                            RedirectStandardOutput = true, RedirectStandardError = true
                        };
                        var captured=new StringBuilder();
                        using (Process process = new Process {StartInfo=info})
                        {
                            process.OutputDataReceived+=(sender,args)=>{if(args.Data!=null)lock(captured)captured.AppendLine(args.Data);};
                            process.ErrorDataReceived+=(sender,args)=>{if(args.Data!=null)lock(captured)captured.AppendLine(args.Data);};
                            process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
                            if (!process.WaitForExit(90000)) {try{process.Kill();}catch{}throw new Exception("后台启动超时，请点击重新连接；扫码登录入口仍可使用。");}
                            // Child services may retain pipe handles; do not wait indefinitely for EOF.
                            try{process.CancelOutputRead();process.CancelErrorRead();}catch(InvalidOperationException){}
                            Directory.CreateDirectory(Path.Combine(root,"logs"));
                            lock(captured)File.AppendAllText(Path.Combine(root, "logs", "desktop-start.log"), DateTime.Now.ToString("s") + "\r\n" + captured.ToString(), Encoding.UTF8);
                            if (process.ExitCode != 0) throw new Exception("后台启动失败，详细记录在 logs\\desktop-start.log。");
                        }
                    });
                }
                Dictionary<string, object> state = await Request("GET", "/local/desktop/state", null, 10);
                connected = true;
                ApplyState(state);
                refreshTimer.Start();
                liveTimer.Start();
                busy = false;
                UpdateDetails();
                await RefreshSelectedCourseProgress(false);
                if (smokeTest)
                {
                    Directory.CreateDirectory(Path.Combine(root, "logs"));
                    using (Bitmap bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(Path.Combine(root, "logs", "desktop-smoke.png")); }
                    File.WriteAllText(Path.Combine(root, "logs", "desktop-smoke.json"), json.Serialize(new {
                        courseCount = items.Count, selectedCourse = courseTitle.Text, state = stateLabel.Text,
                        position = videoNumber.Text, confirmation = confirmation.Text,
                        startEnabled = startButton.Enabled, pauseEnabled = pauseButton.Enabled,
                        error = alertText.Text
                    }), Encoding.UTF8);
                    BeginInvoke(new Action(Close));
                }
            }
            catch (Exception error)
            {
                connected = false;
                SetNotice("连接失败", error.Message, true);
                if (smokeTest) { File.WriteAllText(Path.Combine(root, "logs", "desktop-smoke-error.txt"), error.ToString()); BeginInvoke(new Action(Close)); }
            }
            finally { busy = false;if(!IsDisposed&&!shuttingDown){UpdateConnection(); UpdateDetails();} }
        }

        private async Task RefreshState(bool manual = false)
        {
            if (busy || polling || IsDisposed) return;
            polling = true;
            progressRefreshButton.Enabled = false;
            if (manual) progressRefreshButton.Text = "刷新中…";
            int requestRevision = revision;
            try
            {
                Dictionary<string, object> state = await Request("GET", "/local/desktop/state", null, 7);
                if (revision == requestRevision) {
                    connected = true; ApplyState(state);
                    await RefreshSelectedCourseProgress(manual);
                    if (manual) {
                        string progressError;
                        if(progressErrors.TryGetValue(courseProgress.Key(Selected()),out progressError))SetNotice("章节进度暂未更新",progressError,true);
                        else SetNotice("已刷新进度", "已重新核对学习通官方章节目录。", false);
                        noticeUntil = DateTime.UtcNow.AddSeconds(3);
                    }
                }
            }
            catch (Exception error) { connected = false; SetNotice("暂时无法连接后台", error.Message, true); }
            finally { polling = false; if (!IsDisposed) { progressRefreshButton.Text = "刷新进度"; UpdateConnection(); UpdateDetails(); } }
        }

        private CourseProgressEntry SelectedCourseRecord(){return courseProgress.Get(courseProgress.Key(Selected()));}
        private async Task RefreshSelectedCourseProgress(bool force) {
            var selected=Selected();if(selected==null||shuttingDown||IsDisposed)return;
            string key=courseProgress.Key(selected);Task running;
            if(progressReads.TryGetValue(key,out running)){await running;return;}
            DateTime last;
            if(!force&&progressAttempts.TryGetValue(key,out last)&&DateTime.UtcNow.Subtract(last).TotalSeconds<30)return;
            progressAttempts[key]=DateTime.UtcNow;
            running=LoadCourseProgress(selected,key);progressReads[key]=running;
            try{await running;}finally{progressReads.Remove(key);}
        }
        private async Task LoadCourseProgress(CourseItem selected,string key) {
            try {
                CourseProgressSnapshot snapshot;
                if(CourseProgressLoader!=null)snapshot=await CourseProgressLoader(selected);
                else if(officialPlayer!=null&&!officialPlayer.IsDisposed&&officialPlayer.Progress.CourseId==selected.Id&&officialPlayer.Ready)snapshot=await officialPlayer.ReadCourseProgress();
                else snapshot=await OfficialCourseProgress.ReadSaved(root,selected);
                if(shuttingDown||IsDisposed)return;
                courseProgress.Update(key,snapshot);progressErrors.Remove(key);
            } catch(Exception error) {
                if(shuttingDown||IsDisposed)return;
                progressErrors[key]=error is IOException?error.Message:"读取章节进度暂未成功，请稍后刷新或重新扫码登录";
            }
            if(!shuttingDown&&!IsDisposed)UpdateDetails();
        }
        private async Task EndSelectedCourse() {
            var selected=Selected();if(selected==null||busy||shuttingDown)return;
            string key=courseProgress.Key(selected);busy=true;revision++;UpdateDetails();
            try {
                if(officialPlayer!=null&&!officialPlayer.IsDisposed&&officialPlayer.Progress.CourseId==selected.Id)await officialPlayer.ControlPlayback("pause");
                CloseCoursePage();ShowCourseOverview();
                if(selected.Task!=null&&BoolOf(selected.Task,"active")) {
                    ApplyState(await Request("POST","/local/desktop/pause",new Dictionary<string,string>{{"taskId",TextOf(selected.Task,"taskId")}},15));
                    var deadline=DateTime.UtcNow.AddSeconds(12);
                    while(items.Any(item=>item.Id==selected.Id&&BoolOf(item.Task,"active"))&&DateTime.UtcNow<deadline){await Task.Delay(300);ApplyState(await Request("GET","/local/desktop/state",null,5));}
                    if(items.Any(item=>item.Id==selected.Id&&BoolOf(item.Task,"active")))throw new IOException("网页已停止，后台任务仍在结束中，请稍后再次点击结束课程");
                }
                courseProgress.End(key,true);
                await RefreshSelectedCourseProgress(true);
                SetNotice("本次学习已结束","已停止视频和自动操作，保留课程进度；需要时点击打开视频 / 继续。",false);
                noticeUntil=DateTime.UtcNow.AddSeconds(5);
            }catch(Exception error){SetNotice("结束操作未完全完成",error.Message,true);noticeUntil=DateTime.UtcNow.AddSeconds(12);}
            finally{busy=false;if(!shuttingDown&&!IsDisposed)UpdateDetails();}
        }

        private async Task Command(string command)
        {
            CourseItem course = Selected();
            if (course == null || busy || !connected) return;
            Dictionary<string, string> body = new Dictionary<string, string>();
            if (command == "start") body["courseId"] = course.Id;
            else { if (course.Task == null) return; body["taskId"] = TextOf(course.Task, "taskId"); }
            busy = true; revision++; UpdateDetails();
            try
            {
                ApplyState(await Request("POST", "/local/desktop/" + command, body, 20));
            }
            catch (Exception error) { SetNotice("操作未完成", error.Message, true); noticeUntil = DateTime.UtcNow.AddSeconds(15); }
            finally { busy = false; UpdateDetails(); }
        }

        private async Task RefreshCourses(string account, string password)
        {
            if (busy || !connected) return;
            busy = true; revision++; UpdateDetails();
            SetNotice("正在读取课程", "正在连接学习通，窗口可正常移动，请稍候。", false);
            Dictionary<string, string> body = new Dictionary<string, string>();
            if (!String.IsNullOrEmpty(account)) { body["account"] = account; body["password"] = password; }
            try { ApplyState(await Request("POST", "/local/desktop/courses", body, 85)); }
            catch (Exception error) { SetNotice("课程刷新未成功", error.Message, true); noticeUntil = DateTime.UtcNow.AddSeconds(15); }
            finally { body.Clear(); password = null; busy = false; UpdateDetails(); }
        }

        private async Task<Dictionary<string, object>> Request(string method, string path, object body, int timeout)
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeout)))
            using (HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(method), path))
            {
                if (body != null) request.Content = new StringContent(json.Serialize(body), Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                try { response = await http.SendAsync(request, cancellation.Token); }
                catch (OperationCanceledException) { throw new Exception("请求超时，请检查后台状态或稍后重试。"); }
                catch (HttpRequestException) { throw new Exception("无法连接本机后台。请点击“重新连接”。"); }
                using (response)
                {
                    string text = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
                    Dictionary<string, object> data = null;
                    try { data = json.Deserialize<Dictionary<string, object>>(text); } catch { }
                    if (!response.IsSuccessStatusCode)
                        throw new Exception(data != null && !String.IsNullOrEmpty(TextOf(data, "message")) ? TextOf(data, "message") : "后台返回异常，请查看安装目录中的 logs 文件夹。");
                    return data ?? new Dictionary<string, object>();
                }
            }
        }

        private void ApplyState(Dictionary<string, object> state)
        {
            lastWindowRefresh = DateTime.Now;
            DateTime serverTime;
            if (!DateTime.TryParse(TextOf(state, "serverTime"), out serverTime)) serverTime = DateTime.Now;
            accountHint = TextOf(state, "accountHint");
            List<Dictionary<string, object>> tasks = Rows(state, "tasks");
            playbackClocks.Clear();
            foreach (Dictionary<string, object> task in tasks)
            {
                DateTime updated;
                bool hasTime = DateTime.TryParse(TextOf(task, "updatedAt"), out updated);
                bool running = hasTime && BoolOf(task, "active") && !BoolOf(task, "pauseRequested")
                    && NumberOf(task, "status", -1) == 1 && !BoolOf(task, "platformPassed")
                    && String.IsNullOrEmpty(TextOf(task, "message"))
                    && TextOf(task, "detail").Contains("学习通播放位置")
                    && !String.IsNullOrEmpty(TextOf(task, "videoName"))
                    && TextOf(task, "videoName") == TextOf(task, "currentVideo");
                playbackClocks[TextOf(task, "taskId")] = new PlaybackClock(NumberOf(task, "videoPosition", 0),
                    NumberOf(task, "reportedPosition", 0), NumberOf(task, "videoDuration", 0),
                    hasTime ? Math.Max(0, (serverTime - updated).TotalSeconds) : 0, running);
            }
            var refreshedItems=new List<CourseItem>();
            foreach (Dictionary<string, object> row in Rows(state, "courses"))
            {
                string id = TextOf(row, "courseId");
                var item=items.FirstOrDefault(existing=>existing.Id==id)??new CourseItem {Id=id};
                item.Title=TextOf(row,"title");item.Teacher=TextOf(row,"teacher");
                item.ClazzId=TextOf(row,"clazzId");item.Cpi=TextOf(row,"cpi");
                item.Task=tasks.FirstOrDefault(task=>TextOf(task,"courseId")==id);
                refreshedItems.Add(item);
            }
            foreach (Dictionary<string, object> task in tasks)
                if (!refreshedItems.Any(item => item.Id == TextOf(task, "courseId"))) {
                    string id=TextOf(task,"courseId");
                    var item=items.FirstOrDefault(existing=>existing.Id==id)??new CourseItem {Id=id};
                    item.Title=TextOf(task,"title");item.Teacher="";item.ClazzId="";item.Cpi="";item.Task=task;
                    refreshedItems.Add(item);
                }
            items.Clear();items.AddRange(refreshedItems);
            if (String.IsNullOrEmpty(selectedId) && items.Count > 0)
                selectedId = (items.FirstOrDefault(item => item.Task != null && BoolOf(item.Task, "active")) ?? items[0]).Id;
            accountLabel.Text = String.IsNullOrEmpty(accountHint) ? "尚未登录 · 点击下方登录账号" : "已有登录 · " + accountHint;
            PopulateCourses(); UpdateConnection();
        }

        private void PopulateCourses()
        {
            if (courseList == null) return;
            string query = search.Text.Trim();
            var visible=items.Where(item=>item.Title.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToList();
            bool rebuild=courseList.Items.Count!=visible.Count;
            if(!rebuild)for(int index=0;index<visible.Count;index++)
                if(!Object.ReferenceEquals(courseList.Items[index],visible[index])){rebuild=true;break;}
            int selected=visible.FindIndex(item=>item.Id==selectedId);
            if(selected<0&&visible.Count>0)selected=0;
            rendering=true;
            try {
                // Polling and playback timestamps must not reset the native list or its viewport.
                if(rebuild) {
                    int top=courseList.TopIndex;
                    courseList.BeginUpdate();
                    try {
                        courseList.Items.Clear();courseList.Items.AddRange(visible.Cast<object>().ToArray());
                        courseList.SelectedIndex=selected;
                        if(top>=0&&visible.Count>0)courseList.TopIndex=Math.Min(top,visible.Count-1);
                        courseRowContent.Clear();RefreshCourseRows(false);
                    } finally {courseList.EndUpdate();}
                } else if(courseList.SelectedIndex!=selected)courseList.SelectedIndex=selected;
                if(selected>=0)selectedId=visible[selected].Id;
            } finally {rendering=false;}
            courseCount.Text = "我的课程  " + courseList.Items.Count + (query.Length > 0 ? " / " + items.Count : "");
            ReconcileCoursePage();
            UpdateDetails();
        }

        private CourseItem Selected() { return courseList == null ? null : courseList.SelectedItem as CourseItem; }

        private void UpdateDetails()
        {
            if (startButton == null || shuttingDown || IsDisposed) return;
            RefreshCourseRows();
            CourseItem course = Selected();
            Dictionary<string, object> task = course == null ? null : course.Task;
            bool active = BoolOf(task, "active");
            bool pausing = BoolOf(task, "pauseRequested");
            int status = (int)NumberOf(task, "status", -1);
            BrowserProgress browserProgress = CurrentBrowserProgress();
            bool loggingIn=loginView!=null&&!loginView.IsDisposed&&loginView.Visible;
            startButton.Enabled = !busy && !loggingIn && course != null && !pausing;
            homeworkButton.Enabled = !busy && !loggingIn && course != null && !pausing;
            courseList.Enabled=search.Enabled=!busy&&!loggingIn;
            pauseButton.Enabled = !busy && (browserProgress != null ? !officialPlayer.StudyPaused : connected && task != null && active && !pausing);
            refreshButton.Enabled = connected && !busy && !loggingIn;
            loginButton.Enabled = !shuttingDown && !loggingIn;
            progressRefreshButton.Enabled = !busy && !polling;
            var courseRecord=SelectedCourseRecord();var officialProgress=courseRecord==null?null:courseRecord.Progress;
            bool courseEnded=courseRecord!=null&&courseRecord.Ended;
            endCourseButton.Enabled=!busy&&!loggingIn&&course!=null&&!courseEnded;
            courseTitle.Text = course == null ? "选择一门课程" : course.Title;
            stateLabel.Text = pausing ? "● 正在暂停…" : "● " + (status==5 ? "待继续" : TaskState(task));
            stateLabel.ForeColor = active ? Blue : Muted;
            chapterNumber.Text=officialProgress==null?"待读取":officialProgress.Percent.ToString("0.#")+"%";
            chapterBar.Value=officialProgress==null?0:officialProgress.Percent;chapterBar.Invalidate();
            string progressError;progressErrors.TryGetValue(courseProgress.Key(course),out progressError);
            chapterCaption.Text=officialProgress==null?(progressError??"正在核对官方章节目录"):
                String.Format("官方已完成 {0} / {1} 章 · {2:HH:mm:ss}",officialProgress.Completed,officialProgress.Total,officialProgress.CheckedUtc.ToLocalTime())
                +(officialProgress.Unknown>0?" · 部分状态待核验":"")+(String.IsNullOrEmpty(progressError)?"":" · 更新失败");
            overviewTips.SetToolTip(chapterCaption,progressError??chapterCaption.Text);
            int index = (int)NumberOf(task, "chapterIndex", 0);
            RenderVideo();
            currentChapter.Text = TextOf(task, "chapterTitle");
            if (String.IsNullOrEmpty(currentChapter.Text)) currentChapter.Text = task == null ? "尚未开始" : "第 " + (index + 1) + " 章 · 具体名称待读取";
            currentVideo.Text = TextOf(task, "videoName");
            if (String.IsNullOrEmpty(currentVideo.Text)) currentVideo.Text = TextOf(task, "currentVideo");
            if (String.IsNullOrEmpty(currentVideo.Text)) currentVideo.Text = "尚未读取视频";
            bool hasConfirmation = task != null && task.ContainsKey("platformPassed") && task["platformPassed"] != null;
            bool passed = BoolOf(task, "platformPassed");
            confirmation.Text = !hasConfirmation ? "待核验" : passed ? "已确认当前视频完成" : "尚未确认当前视频完成";
            confirmation.ForeColor = passed ? Color.FromArgb(28, 134, 101) : hasConfirmation ? Color.FromArgb(182, 110, 24) : Muted;
            string updated = TextOf(task, "updatedAt");
            updatedLabel.Text = (String.IsNullOrEmpty(updated) ? "平台记录待读取" : "平台记录时间：" + updated.Replace('T', ' '))
                + (lastWindowRefresh == DateTime.MinValue ? "" : "  ·  窗口刷新：" + lastWindowRefresh.ToString("HH:mm:ss"));
            if(browserProgress==null&&officialProgress!=null) {
                currentChapter.Text=officialProgress.AllCompleted?"官方目录中的章节均已完成":String.Format("官方已完成 {0} / {1} 章",officialProgress.Completed,officialProgress.Total);
                currentVideo.Text=courseEnded?"本次学习已结束":"打开视频后显示当前播放信息";
                confirmation.Text=officialProgress.AllCompleted?"学习通已确认全部章节完成":"以官方目录任务标记为准";
                confirmation.ForeColor=officialProgress.AllCompleted?DesktopTheme.Teal:Muted;
                updatedLabel.Text="章节记录同步时间："+officialProgress.CheckedUtc.ToLocalTime().ToString("HH:mm:ss");
                if(officialProgress.AllCompleted){stateLabel.Text="● 章节已完成";stateLabel.ForeColor=DesktopTheme.Teal;}
            }
            if(courseEnded){stateLabel.Text="● 本次学习已结束";stateLabel.ForeColor=DesktopTheme.Coral;}
            if (browserProgress != null) {
                stateLabel.Text = "● " + browserProgress.Status;
                stateLabel.ForeColor = Blue;
                currentChapter.Text = browserProgress.KnowledgeId.Length > 0 ? "官方页面章节 · " + browserProgress.KnowledgeId : "请在官方页面选择章节";
                currentVideo.Text = browserProgress.HasVideo ? "官方网页播放器" : "等待官方页面中的视频";
                confirmation.Text = !browserProgress.HasRecord ? "等待平台记录" : browserProgress.Passed ? "已确认当前章节视频完成" : "尚未确认当前视频完成";
                confirmation.ForeColor = browserProgress.HasRecord && browserProgress.Passed ? Color.FromArgb(28,134,101) : Muted;
                updatedLabel.Text = browserProgress.RecordedAt == DateTime.MinValue ? "网页播放模式；旧接口任务状态不代表当前播放状态" : "网页播放模式 · 平台记录读取时间：" + browserProgress.RecordedAt.ToString("HH:mm:ss");
                SetNotice(browserProgress.Status,browserProgress.Detail,false);
                return;
            }
            string error = TextOf(task, "message");
            if (!connected) return;
            if (DateTime.UtcNow < noticeUntil) return;
            if(courseEnded){SetNotice("本次学习已结束","视频和自动操作已停止，课程进度已保留。点击打开视频 / 继续可重新进入。",false);return;}
            if(officialProgress!=null&&officialProgress.AllCompleted){SetNotice("章节已全部完成","学习通官方目录已确认全部章节完成，可点击结束课程收起本次学习。",false);return;}
            if (pausing) SetNotice("正在暂停", "正在结束当前请求并保存进度；显示“已暂停”后即可继续。", false);
            else if (status == 5) {SetNotice("继续上次的学习", "点击“打开视频 / 继续”进入课程。", false);overviewTips.SetToolTip(alertText,error);}
            else if (status == 6) SetNotice("已暂停", "当前任务没有执行。点击“开始 / 继续”可从保存的章节和平台播放位置重新核验。", false);
            else if (active) SetNotice("任务正在执行", "大号数字为每秒变化的本机估算位置，下方显示学习通已记录位置；完成仍以平台确认为准。", false);
            else if (status == 4) SetNotice("任务已结束", "请在学习通核对最终课程完成情况。", false);
            else SetNotice("准备就绪", "选择课程后点击“打开视频 / 继续”，在 App 内使用官方播放器。", false);
        }

        private void RenderVideo()
        {
            if (videoNumber == null) return;
            CourseItem course = Selected();
            Dictionary<string, object> task = course == null ? null : course.Task;
            BrowserProgress browserProgress = CurrentBrowserProgress();
            if (browserProgress != null) {
                videoHeading.Text = "视频位置 · 真实播放器";
                videoNumber.Text = browserProgress.HasVideo ? Clock(browserProgress.Position) + " / " + (browserProgress.Duration > 0 ? Clock(browserProgress.Duration) : "待载入") : "— / —";
                videoBar.Value = browserProgress.HasVideo && browserProgress.Duration > 0 ? browserProgress.Position * 100 / browserProgress.Duration : 0;
                videoBar.Invalidate();
                videoCaption.Text = browserProgress.HasRecord ? "平台断点 " + Clock(browserProgress.Recorded) + " · " + browserProgress.Status : "平台记录待读取 · " + browserProgress.Status;
                return;
            }
            var record=SelectedCourseRecord();
            if(record!=null&&(record.Progress!=null||record.Ended)) {
                videoHeading.Text="视频位置 · 当前未播放";videoNumber.Text="— / —";videoBar.Value=0;videoBar.Invalidate();
                videoCaption.Text=record.Ended?"本次学习已结束，已保留官方章节进度":"打开视频后显示真实播放位置";
                return;
            }
            videoHeading.Text = "视频位置 · 历史平台断点";
            PlaybackClock clock;
            playbackClocks.TryGetValue(TextOf(task, "taskId"), out clock);
            double duration = clock == null ? 0 : clock.Duration;
            double position = clock == null ? 0 : clock.Recorded;
            videoNumber.Text = duration > 0 ? Clock(position) + " / " + Clock(duration) : "— / —";
            videoBar.Value = duration <= 0 ? 0 : position * 100 / duration; videoBar.Invalidate();
            string mode = "历史记录；打开视频后读取真实位置";
            videoCaption.Text = duration > 0 ? "学习通已记录 " + Clock(clock.Recorded) + " · " + mode : "等待视频记录";
        }

        private void UpdateConnection()
        {
            serviceLabel.Text = connected ? "● 已连接" : "● 未连接";
            serviceLabel.ForeColor = connected ? Color.FromArgb(28, 134, 101) : Color.FromArgb(187, 61, 63);
            retryButton.Visible = !connected && !busy;
        }

        private BrowserProgress CurrentBrowserProgress() {
            return officialPlayer != null && !officialPlayer.IsDisposed && officialPlayer.Progress.CourseId == selectedId ? officialPlayer.Progress : null;
        }

        private void ShowCourseOverview() {
            if(shuttingDown)return;
            if(loginView!=null&&!loginView.IsDisposed)loginView.Visible=false;
            studyPanel.Visible=false;main.Visible=true;main.BringToFront();
            UpdateDetails();
        }
        private async Task OpenQrLogin() {
            if(shuttingDown)return;
            if(officialPlayer!=null&&!officialPlayer.IsDisposed)await officialPlayer.ControlPlayback("pause");
            CloseCoursePage();
            if(loginView!=null)loginView.Dispose();
            loginView=new OfficialLoginView(root,SynchronizeBrowserLogin,ShowCourseOverview);
            main.Parent.Controls.Add(loginView);main.Visible=false;studyPanel.Visible=false;
            loginView.Visible=true;loginView.BringToFront();UpdateDetails();
        }
        private async Task SynchronizeBrowserLogin(Dictionary<string,string> session) {
            if(!connected)await InitializeBackend();
            if(!connected)throw new Exception("本机后台尚未连接，请稍后重试");
            if(busy)throw new Exception("后台正在处理其他操作，请稍后重试");
            busy=true;revision++;UpdateDetails();
            try {
                var syncStarted=DateTime.UtcNow;
                var state=await Request("POST","/local/desktop/browser-login",session,45);
                DesktopBrowserRuntime.LogTiming(root,"verified-course-list",syncStarted);
                if(shuttingDown||IsDisposed)return;
                ApplyState(state);ShowCourseOverview();
                SetNotice("扫码登录成功","已更新课程列表，选择课程后点击打开视频 / 继续。",false);
                noticeUntil=DateTime.UtcNow.AddSeconds(6);
            } finally {busy=false;if(!shuttingDown&&!IsDisposed)UpdateDetails();}
        }
        private void ShowCoursePage() {
            main.Visible=false;studyPanel.Visible=true;studyPanel.BringToFront();
            if(officialPlayer!=null&&!officialPlayer.IsDisposed)officialPlayer.Show();
        }
        private void CloseCoursePage() {
            var previousHomework=homeworkView;homeworkView=null;
            if(previousHomework!=null&&!previousHomework.IsDisposed)previousHomework.Dispose();
            var previous=officialPlayer;officialPlayer=null;
            if(previous!=null&&!previous.IsDisposed){previous.Close();previous.Dispose();}
        }
        private void ReconcileCoursePage() {
            var selected=Selected();
            if(homeworkView!=null&&!homeworkView.IsDisposed&&(selected==null||homeworkView.CourseId!=selected.Id)){
                CloseCoursePage();ShowCourseOverview();
            }
            if(officialPlayer!=null&&!officialPlayer.IsDisposed&&(selected==null||officialPlayer.Progress.CourseId!=selected.Id)){
                CloseCoursePage();ShowCourseOverview();
            }
        }

        private async Task OpenOfficialPlayer() {
            CourseItem course = Selected();
            if (course == null || busy) return;
            if (CurrentBrowserProgress() != null) {
                ShowCoursePage(); officialPlayer.Browser.Focus();
                await officialPlayer.ControlPlayback("play"); return;
            }
            busy = true; UpdateDetails();
            try {
                if (course.Task != null && BoolOf(course.Task,"active")) {
                    ApplyState(await Request("POST","/local/desktop/pause",new Dictionary<string,string>{{"taskId",TextOf(course.Task,"taskId")}},15));
                    Stopwatch pauseWait = Stopwatch.StartNew();
                    while (pauseWait.Elapsed.TotalSeconds < 12 && items.Any(item => item.Id == course.Id && BoolOf(item.Task,"active"))) {
                        await Task.Delay(300); ApplyState(await Request("GET","/local/desktop/state",null,5));
                    }
                    if (items.Any(item => item.Id == course.Id && BoolOf(item.Task,"active"))) throw new Exception("旧接口任务尚未停止，请稍后再打开真实播放器。");
                }
                if(shuttingDown||IsDisposed)return;
                CloseCoursePage();
                officialPlayer = PlayerFactory==null ? new OfficialPlayer(root,course,false) : PlayerFactory(course);
                var embedded=officialPlayer;
                embedded.TopLevel=false;embedded.FormBorderStyle=FormBorderStyle.None;embedded.ShowInTaskbar=false;
                embedded.MinimumSize=Size.Empty;embedded.Dock=DockStyle.Fill;
                studyHost.Controls.Add(embedded);studyTitle.Text=course.Title;
                embedded.Changed += () => { if (!shuttingDown&&!IsDisposed) UpdateDetails(); };
                embedded.EndRequested+=async()=>await EndSelectedCourse();
                embedded.FormClosed += (sender,args) => {
                    if(officialPlayer!=embedded)return;
                    officialPlayer=null;
                    if(!shuttingDown&&!IsDisposed){ShowCourseOverview();UpdateDetails();}
                };
                courseProgress.End(courseProgress.Key(course),false);
                ShowCoursePage();
            } catch (Exception error) {
                SetNotice("官方播放器未打开",error.Message,true); noticeUntil=DateTime.UtcNow.AddSeconds(10);
            } finally { busy=false; UpdateDetails(); }
        }

        private async Task OpenHomework() {
            CourseItem course=Selected();if(course==null||busy||shuttingDown)return;
            if(homeworkView!=null&&!homeworkView.IsDisposed&&homeworkView.CourseId==course.Id){ShowCoursePage();homeworkView.Browser.Focus();return;}
            if(officialPlayer!=null&&!officialPlayer.IsDisposed)await officialPlayer.ControlPlayback("pause");
            if(course.Task!=null&&BoolOf(course.Task,"active")){
                SetNotice("请先暂停学习","先点击暂停学习，等后台任务停止后再打开课程作业。",false);return;
            }
            CloseCoursePage();
            homeworkView=new OfficialHomeworkView(root,course,http.BaseAddress,false);
            studyHost.Controls.Add(homeworkView);studyTitle.Text=course.Title+" · 作业";
            ShowCoursePage();homeworkView.BringToFront();
        }

        private void SetNotice(string title, string text, bool error)
        {
            alertTitle.Text = title; alertText.Text = text;
            overviewTips.SetToolTip(alertText,text);
            alertTitle.ForeColor = error ? Color.FromArgb(187, 61, 63) : Blue;
            alertPanel.BackColor = error ? Color.FromArgb(255, 237, 237) : DesktopTheme.VioletPale;
        }

        private static string TextOf(Dictionary<string, object> row, string key)
        {
            object value;
            return row != null && row.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
        }
        private static double NumberOf(Dictionary<string, object> row, string key, double fallback)
        {
            double value;
            return Double.TryParse(TextOf(row, key), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value) ? value : fallback;
        }
        private static bool BoolOf(Dictionary<string, object> row, string key)
        {
            string value = TextOf(row, key);
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
        }
        private static List<Dictionary<string, object>> Rows(Dictionary<string, object> row, string key)
        {
            object value;
            IEnumerable enumerable = row.TryGetValue(key, out value) ? value as IEnumerable : null;
            return enumerable == null ? new List<Dictionary<string, object>>() : enumerable.Cast<object>().OfType<Dictionary<string, object>>().ToList();
        }
        private static string Clock(double seconds)
        {
            TimeSpan value = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return value.TotalHours >= 1 ? String.Format("{0}:{1:00}:{2:00}", (int)value.TotalHours, value.Minutes, value.Seconds)
                : String.Format("{0:00}:{1:00}", (int)value.TotalMinutes, value.Seconds);
        }
        private static string TaskState(Dictionary<string, object> row)
        {
            if (row == null) return "尚未开始";
            if (BoolOf(row, "pauseRequested")) return "正在暂停";
            switch ((int)NumberOf(row, "status", -1))
            {
                case 0: return "待处理"; case 1: return "进行中"; case 2: return "排队中";
                case 3: return "测验中"; case 4: return "已完成"; case 5: return "异常，已停止";
                case 6: return "已暂停"; default: return "等待记录";
            }
        }
        private void OpenPath(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception error) { SetNotice("无法打开", error.Message, true); }
        }
    }

    internal sealed class LoginDialog : Form
    {
        private TextBox account, password;
        public string Account { get { return account.Text.Trim(); } }
        public string Password { get { return password.Text; } }
        public LoginDialog()
        {
            Text = "登录学习通"; ClientSize = new Size(410, 277); FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.White;
            Controls.Add(new Label { Text = "学习通账号", Location = new Point(25, 22), Size = new Size(350, 25) });
            account = new TextBox { Name = "LoginAccount", Location = new Point(26, 52), Width = 355 };
            Controls.Add(account);
            Controls.Add(new Label { Text = "密码", Location = new Point(25, 95), Size = new Size(350, 25) });
            password = new TextBox { Name = "LoginPassword", Location = new Point(26, 125), Width = 355, UseSystemPasswordChar = true };
            Controls.Add(password);
            Controls.Add(new Label { Text = "已有登录可直接关闭此窗口，点击“刷新课程”。", Location = new Point(25, 168), Size = new Size(360, 27), ForeColor = Color.Gray, Font = new Font("Microsoft YaHei UI", 9) });
            Button submit = new Button { Text = "登录并读取课程", Location = new Point(225, 216), Size = new Size(155, 38), DialogResult = DialogResult.OK };
            Controls.Add(submit); AcceptButton = submit;
            Button cancel = new Button { Text = "取消", Location = new Point(119, 216), Size = new Size(95, 38), DialogResult = DialogResult.Cancel };
            Controls.Add(cancel); CancelButton = cancel;
        }
        public void ClearPassword() { password.Clear(); }
    }
}
