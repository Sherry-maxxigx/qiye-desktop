using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SuperAutoDesktop {
    // This view never loads the automatic chapter/video scripts. Its browser
    // bridge only reads assignments, opens a user-selected item and fills blanks.
    internal sealed class OfficialHomeworkView : UserControl {
        internal readonly WebView2 Browser = new WebView2 { Dock=DockStyle.Fill };
        private readonly string root, token=Guid.NewGuid().ToString("N");
        private readonly CourseItem course;
        private readonly bool test;
        private readonly Uri service;
        private readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=4*1024*1024};
        private readonly HttpClient http=new HttpClient {Timeout=TimeSpan.FromSeconds(60)};
        private readonly Dictionary<string,PageState> pages=new Dictionary<string,PageState>();
        private readonly Dictionary<string,TaskCompletionSource<Dictionary<string,object>>> replies=new Dictionary<string,TaskCompletionSource<Dictionary<string,object>>>();
        private readonly ListBox works=new ListBox {Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,IntegralHeight=false,HorizontalScrollbar=true};
        private readonly Label status, count;
        private readonly Button read, fill, stop;
        private CancellationTokenSource run;
        private bool ready, closing;
        private bool awaitingList;
        private DateTime readStarted;
        private WorkItem pendingWork;
        private readonly System.Windows.Forms.Timer readWatchdog=new System.Windows.Forms.Timer {Interval=1000};
        private int generation;
        internal Task Initialization;
        internal Func<Dictionary<string,object>,CancellationToken,Task<Dictionary<string,object>>> LookupOverride;
        internal string StatusText {get{return status.Text;}}
        internal int PendingCount {get{return works.Items.Count;}}
        internal string CourseId {get{return course.Id;}}
        internal int DetectedQuestionCount {get{var page=CurrentPage();return page==null?0:page.Total;}}
        private sealed class PageState {internal string Frame,Fingerprint;internal int Total;internal DateTime At;}
        private sealed class WorkItem {
            internal string Frame,Key,Id,Title,Url;
            public override string ToString(){return Title;}
        }
        internal OfficialHomeworkView(string installRoot,CourseItem selected,Uri api,bool testing) {
            root=installRoot;course=selected;service=api;test=testing;
            Dock=DockStyle.Fill;Name="OfficialHomework";BackColor=DesktopTheme.Pale;Font=new Font("Microsoft YaHei UI",9.5F);
            var header=new Panel {Dock=DockStyle.Top,Height=112,Padding=new Padding(16,8,16,0),BackColor=Color.White};
            var actions=new FlowLayoutPanel {Dock=DockStyle.Top,Height=41,WrapContents=false};
            read=Button("读取作业",async()=>await ReadAssignments());read.Name="ReadHomework";
            fill=Button("刷作业",FillAsync);fill.Name="FillHomework";fill.BackColor=DesktopTheme.Blue;fill.ForeColor=Color.White;
            stop=Button("停止查题",()=>{CancelRun();SetStatus("已停止查题；已填写的答案保留，未提交。只需核对后自行提交。");return Task.FromResult(0);});stop.Name="StopHomework";
            actions.Controls.AddRange(new Control[]{read,fill,stop});
            status=new Label {Dock=DockStyle.Top,Height=29,AutoEllipsis=true,Text="正在打开课程作业…",ForeColor=DesktopTheme.Ink};
            var note=new Label {Dock=DockStyle.Top,Height=25,Text="只填写空白题 · 未命中或无法确认的题目留空 · 最后由你手动提交",ForeColor=DesktopTheme.Teal,AutoEllipsis=true};
            header.Controls.Add(note);header.Controls.Add(status);header.Controls.Add(actions);
            var sidebar=new Panel {Dock=DockStyle.Left,Width=205,Padding=new Padding(12),BackColor=DesktopTheme.Sidebar};
            count=new Label {Dock=DockStyle.Top,Height=31,Text="未完成作业",ForeColor=DesktopTheme.Violet};
            var help=new Label {Dock=DockStyle.Bottom,Height=68,Text="双击作业打开\n若列表未识别，可在右侧官方页面直接进入作业。",ForeColor=DesktopTheme.Muted};
            works.BackColor=DesktopTheme.Sidebar;works.ForeColor=DesktopTheme.Ink;works.DrawMode=DrawMode.OwnerDrawFixed;works.ItemHeight=44;works.HorizontalScrollbar=false;
            works.Name="PendingHomeworkList";
            works.DrawItem+=(s,e)=>{
                if(e.Index<0||e.Index>=works.Items.Count)return;
                bool selectedRow=(e.State&DrawItemState.Selected)!=0;
                using(var background=new SolidBrush(selectedRow?DesktopTheme.VioletPale:DesktopTheme.Sidebar))e.Graphics.FillRectangle(background,e.Bounds);
                var bounds=new Rectangle(e.Bounds.X+8,e.Bounds.Y,e.Bounds.Width-16,e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics,works.Items[e.Index].ToString(),Font,bounds,selectedRow?DesktopTheme.Violet:DesktopTheme.Ink,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter);
            };
            works.DoubleClick+=(s,e)=>OpenSelectedWork();
            works.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.Handled=true;OpenSelectedWork();}};
            sidebar.Controls.Add(works);sidebar.Controls.Add(help);sidebar.Controls.Add(count);
            Controls.Add(Browser);Controls.Add(sidebar);Controls.Add(header);Browser.BringToFront();
            Load+=(s,e)=>{if(Initialization==null)Initialization=Initialize();};
            VisibleChanged+=(s,e)=>{if(!Visible)CancelRun();};
            readWatchdog.Tick+=(s,e)=>{if(awaitingList&&DateTime.UtcNow.Subtract(readStarted).TotalSeconds>35){awaitingList=false;pendingWork=null;SetStatus("作业列表读取超时。请检查右侧是否需要重新登录，再点击“读取作业”重试。");}};
            readWatchdog.Start();
            UpdateButtons();
        }
        private Button Button(string text,Func<Task> action) {
            var button=new ModernButton {Text=text,Width=112,Height=34,Margin=new Padding(0,0,10,0),BackColor=DesktopTheme.TealPale,ForeColor=DesktopTheme.Teal};
            button.Click+=async(s,e)=>{try{await action();}catch(OperationCanceledException){}catch(Exception){SetStatus("操作未完成，请检查官方页面和本机服务后重试；未提交。");}};
            return button;
        }
        private void SetStatus(string value){if(!closing)status.Text=value;}
        private PageState CurrentPage(){return pages.Values.Where(p=>p.Total>0&&DateTime.UtcNow.Subtract(p.At).TotalSeconds<4).OrderByDescending(p=>p.At).FirstOrDefault();}
        private void UpdateButtons(){if(closing)return;read.Enabled=ready&&run==null;fill.Enabled=ready&&run==null&&CurrentPage()!=null;stop.Enabled=run!=null;}
        private void CancelRun(){generation++;if(run!=null)run.Cancel();foreach(var reply in replies.Values.ToArray())reply.TrySetCanceled();}
        internal static string CourseHomeUrl(CourseItem value) {
            if(value==null||!Regex.IsMatch(value.Id??"",@"^\d+$")||!Regex.IsMatch(value.ClazzId??"",@"^\d+$")||!Regex.IsMatch(value.Cpi??"",@"^\d+$"))return "https://i.chaoxing.com/";
            return "https://mooc1.chaoxing.com/visit/stucoursemiddle?courseid="+value.Id+"&clazzid="+value.ClazzId+"&cpi="+value.Cpi+"&ismooc2=1&v=2";
        }
        private void BeginRead(){awaitingList=true;readStarted=DateTime.UtcNow;}
        private async Task Initialize() {
            try {
                var environment=await DesktopBrowserRuntime.Get(root,test);if(closing)return;
                await Browser.EnsureCoreWebView2Async(environment);if(closing)return;
                var core=Browser.CoreWebView2;core.IsMuted=true;core.Settings.AreDevToolsEnabled=test;core.Settings.IsStatusBarEnabled=false;
                string script;
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("SuperAutoDesktop.homework.js"))using(var reader=new StreamReader(stream))script=reader.ReadToEnd();
                script=script.Replace("__HOMEWORK_TOKEN__",json.Serialize(token)).Replace("__HOMEWORK_TEST__",test?"true":"false");
                await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
                core.WebMessageReceived+=Receive;
                core.NavigationStarting+=(s,e)=>{
                    if(!OfficialPlayer.Trusted(e.Uri,test)){
                        Uri target;
                        if(Uri.TryCreate(e.Uri,UriKind.Absolute,out target)&&target.Scheme=="http"&&OfficialPlayer.Trusted("https"+e.Uri.Substring(4),test)){
                            e.Cancel=true;BeginInvoke(new Action(()=>{if(!closing)core.Navigate("https"+e.Uri.Substring(4));}));return;
                        }
                        e.Cancel=true;SetStatus("请在学习通官方页面中打开课程作业。");return;
                    }
                    CancelRun();pages.Clear();UpdateButtons();
                };
                core.NewWindowRequested+=(s,e)=>{e.Handled=true;if(OfficialPlayer.Trusted(e.Uri,test))core.Navigate(e.Uri);};
                core.NavigationCompleted+=(s,e)=>{if(!closing&&!e.IsSuccess&&e.WebErrorStatus!=CoreWebView2WebErrorStatus.OperationCanceled){awaitingList=false;pendingWork=null;SetStatus("官方页面未能加载，请点击读取作业重试。");}};
                if(test)core.SetVirtualHostNameToFolderMapping("player-test.invalid",Path.Combine(root,"fixtures"),CoreWebView2HostResourceAccessKind.DenyCors);
                else {
                    var existing=await core.CookieManager.GetCookiesAsync("https://mooc1.chaoxing.com/");
                    if(!existing.Any(c=>c.Name=="_uid"))try {
                        var saved=await Task.Run(()=>SavedBrowserLogin.Read(root));if(closing)return;
                        foreach(var pair in saved)try {var cookie=core.CookieManager.CreateCookie(pair.Key,pair.Value,".chaoxing.com","/");cookie.IsSecure=true;core.CookieManager.AddOrUpdateCookie(cookie);}catch(ArgumentException){}
                    }catch(Exception){}
                }
                if(closing)return;ready=true;
                BeginRead();core.Navigate(test?"https://player-test.invalid/mycourse/stu.html":CourseHomeUrl(course));UpdateButtons();
                SetStatus("正在打开官方课程主页并读取未完成作业…");
            }catch(Exception){SetStatus("作业页面未能初始化，请重新打开或检查 WebView2 组件。");}
        }
        internal Task ReadAssignments() {
            if(!ready||closing)return Task.FromResult(0);
            CancelRun();pendingWork=null;pages.Clear();works.Items.Clear();count.Text="未完成作业";
            BeginRead();Browser.CoreWebView2.Navigate(test?"https://player-test.invalid/mycourse/stu.html":CourseHomeUrl(course));
            SetStatus("正在从官方课程主页重新读取作业…");UpdateButtons();return Task.FromResult(0);
        }
        internal void OpenSelectedWork() {
            var item=works.SelectedItem as WorkItem;if(item==null||!ready||closing)return;
            CancelRun();pendingWork=null;pages.Clear();
            if(!OfficialPlayer.Trusted(item.Url,test)){
                // Event-only entries belong to a document that may have been
                // replaced. Reload the list and resolve the fresh entry.
                pendingWork=item;BeginRead();
                Browser.CoreWebView2.Navigate(test?"https://player-test.invalid/mycourse/stu.html":CourseHomeUrl(course));
                SetStatus("正在重新读取作业列表，以打开“"+item.Title+"”…");UpdateButtons();return;
            }
            NavigateWork(item);
        }
        private void NavigateWork(WorkItem item) {
            awaitingList=false;
            if(OfficialPlayer.Trusted(item.Url,test))Browser.CoreWebView2.Navigate(item.Url);
            else Browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {channel="qiye-homework-command",token,frameId=item.Frame,action="open-work",key=item.Key,title=item.Title}));
            SetStatus("正在打开“"+item.Title+"”；进入题目后点击“刷作业”。");UpdateButtons();
        }
        private void Receive(object sender,CoreWebView2WebMessageReceivedEventArgs e) {
            if(closing||!OfficialPlayer.Trusted(e.Source,test))return;
            try {
                var data=json.Deserialize<Dictionary<string,object>>(e.WebMessageAsJson);
                if(OfficialRecord.Value(data,"channel")!="qiye-homework"||OfficialRecord.Value(data,"token")!=token)return;
                string kind=OfficialRecord.Value(data,"kind"),frame=OfficialRecord.Value(data,"frameId");
                if(kind=="result"){TaskCompletionSource<Dictionary<string,object>> reply;if(replies.TryGetValue(OfficialRecord.Value(data,"request"),out reply))reply.TrySetResult(data);return;}
                if(kind=="state"&&awaitingList&&run==null){
                    string code=OfficialRecord.Value(data,"code"),message=null;
                    if(code=="loading-list")message="正在通过官方作业菜单加载列表…";
                    else if(code=="login-required")message="学习通登录已失效，请在右侧重新登录，或返回扫码登录后再读取作业。";
                    else if(code=="access-denied")message="官方页面提示无权访问或参数已失效，请重新读取作业；仍失败时请重新登录。";
                    else if(code=="missing-menu")message="当前课程主页没有可用的作业菜单，请核对课程或刷新后重试。";
                    else if(code=="chapter-page")message="当前是章节目录页，请点击“读取作业”，从课程主页重新进入。";
                    else if(code=="menu-timeout"||code=="list-timeout")message="官方作业列表未能加载，请检查右侧页面后点击“读取作业”重试。";
                    if(message!=null){SetStatus(message);if(code!="loading-list"){awaitingList=false;pendingWork=null;}}
                }
                if(kind=="list"&&IsTrue(data,"known")){
                    awaitingList=false;
                    object raw;var rows=data.TryGetValue("rows",out raw)?raw as IEnumerable:null;if(rows==null)return;
                    works.BeginUpdate();works.Items.Clear();
                    foreach(object value in rows){var row=value as Dictionary<string,object>;if(row==null||works.Items.Count>=200)continue;works.Items.Add(new WorkItem {Frame=frame,Key=OfficialRecord.Value(row,"key"),Id=OfficialRecord.Value(row,"id"),Title=OfficialRecord.Value(row,"title"),Url=OfficialRecord.Value(row,"url")});}
                    works.EndUpdate();count.Text="未完成作业 · "+works.Items.Count;
                    if(pendingWork!=null){
                        var requested=pendingWork;pendingWork=null;
                        var matches=works.Items.Cast<WorkItem>().Where(item=>String.IsNullOrEmpty(requested.Id)?item.Title==requested.Title:item.Id==requested.Id).ToList();
                        if(matches.Count==1){works.SelectedItem=matches[0];NavigateWork(matches[0]);}
                        else SetStatus(matches.Count==0?"这份作业已不在当前未完成列表中，请选择其他作业。":"存在同名作业，无法确定要打开哪一份，请在右侧官方列表中选择。");
                        return;
                    }
                    if(run==null&&CurrentPage()==null)SetStatus(works.Items.Count>0?"已读取未完成作业，双击左侧条目，进入后点击“刷作业”。":"当前页面未发现可作答的未完成作业；请核对官方列表或切换课程。");
                }
                if(kind=="page"){
                    int total;Int32.TryParse(OfficialRecord.Value(data,"total"),out total);
                    if(total>0&&total<=100&&IsTrue(data,"pageVisible")){
                        string signature=OfficialRecord.Value(data,"fingerprint");PageState previous;
                        bool changed=!pages.TryGetValue(frame,out previous)||previous.Fingerprint!=signature;
                        pages[frame]=new PageState {Frame=frame,Fingerprint=signature,Total=total,At=DateTime.UtcNow};
                        if(changed&&run==null)SetStatus("识别到 "+total+" 题。点击“刷作业”查询并填写，完成后请手动提交。");
                    }else pages.Remove(frame);
                    UpdateButtons();
                }
            }catch(Exception){/* Ignore unrelated or stale page notifications. */}
        }
        private static bool IsTrue(Dictionary<string,object> data,string key){return OfficialRecord.Value(data,key).Equals("true",StringComparison.OrdinalIgnoreCase);}
        private async Task<Dictionary<string,object>> Command(string action,PageState page,Dictionary<string,object> answer,CancellationToken cancel) {
            string request=Guid.NewGuid().ToString("N");var reply=new TaskCompletionSource<Dictionary<string,object>>();replies[request]=reply;
            try {
                using(cancel.Register(()=>reply.TrySetCanceled())){
                    Browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {channel="qiye-homework-command",token,frameId=page.Frame,action,request,fingerprint=page.Fingerprint,answer}));
                    if(await Task.WhenAny(reply.Task,Task.Delay(8000,cancel))!=reply.Task){cancel.ThrowIfCancellationRequested();throw new IOException("作业页面未响应");}
                    return await reply.Task;
                }
            }finally{replies.Remove(request);}
        }
        private async Task<Dictionary<string,object>> Lookup(Dictionary<string,object> question,CancellationToken cancel) {
            if(test){if(LookupOverride==null)throw new IOException("本地测试未配置题库");return await LookupOverride(question,cancel);}
            using(var content=new StringContent(json.Serialize(question),Encoding.UTF8,"application/json"))
            using(var response=await http.PostAsync(new Uri(service,"/local/desktop/quiz/homework-answer"),content,cancel)){
                if(!response.IsSuccessStatusCode)throw new IOException("题库查询未完成");
                return json.Deserialize<Dictionary<string,object>>(await response.Content.ReadAsStringAsync());
            }
        }
        private bool SamePage(PageState page,int version){PageState current;return !closing&&Visible&&generation==version&&pages.TryGetValue(page.Frame,out current)&&current.Fingerprint==page.Fingerprint&&DateTime.UtcNow.Subtract(current.At).TotalSeconds<4;}
        internal async Task FillAsync() {
            if(!ready||closing||run!=null)return;var page=CurrentPage();if(page==null){SetStatus("请先进入一份作业的答题页面。");return;}
            var cancellation=new CancellationTokenSource();run=cancellation;int version=generation,filled=0,kept=0,blank=0,failed=0,unsupported=0,missing=0,uncertain=0;UpdateButtons();
            try {
                var snapshot=await Command("snapshot",page,null,cancellation.Token);
                if(!IsTrue(snapshot,"ok")||OfficialRecord.Value(snapshot,"fingerprint")!=page.Fingerprint)throw new IOException("作业页面已经变化");
                object raw;var questions=snapshot.TryGetValue("questions",out raw)?raw as IEnumerable:null;
                if(questions==null)throw new IOException("未读取到作业题目");
                var rows=questions.Cast<object>().Select(q=>q as Dictionary<string,object>).ToList();
                if(rows.Count!=page.Total||rows.Any(q=>q==null))throw new IOException("作业题目数量发生变化");
                for(int i=0;i<rows.Count;i++){
                    cancellation.Token.ThrowIfCancellationRequested();if(!SamePage(page,version))throw new OperationCanceledException();
                    var question=rows[i];if(IsTrue(question,"answered")){kept++;continue;}
                    if(OfficialRecord.Value(question,"type")=="unsupported"){blank++;unsupported++;continue;}
                    SetStatus("题库查询 "+(i+1)+" / "+rows.Count+" · 已填写 "+filled+" 题 · 留空 "+blank+" 题");
                    Dictionary<string,object> found;
                    try{found=await Lookup(question,cancellation.Token);}catch(OperationCanceledException){throw;}catch(Exception){blank++;failed++;continue;}
                    cancellation.Token.ThrowIfCancellationRequested();if(!SamePage(page,version))throw new OperationCanceledException();
                    if(!IsTrue(found,"matched")||OfficialRecord.Value(found,"id")!=OfficialRecord.Value(question,"id")){
                        blank++;string reason=OfficialRecord.Value(found,"reason");
                        if(reason=="no-match")missing++;
                        else if(reason=="provider-error")failed++;
                        else if(reason=="unsupported-type"||reason=="unsupported-fields")unsupported++;
                        else uncertain++;
                        continue;
                    }
                    found["type"]=OfficialRecord.Value(question,"type");
                    var applied=await Command("fill",page,found,cancellation.Token);
                    if(!IsTrue(applied,"ok")){
                        if(OfficialRecord.Value(applied,"reason")=="page-changed")throw new OperationCanceledException();
                        blank++;failed++;continue;
                    }
                    if(IsTrue(applied,"skipped"))kept++;else filled++;
                }
                var reasons=new List<string>();
                if(missing>0)reasons.Add("题库未命中 "+missing);
                if(uncertain>0)reasons.Add("答案无法确认 "+uncertain);
                if(unsupported>0)reasons.Add("页面或题型未支持 "+unsupported);
                if(failed>0)reasons.Add("查询或填写失败 "+failed);
                SetStatus("已填写 "+filled+" 题，保留原答案 "+kept+" 题，留空 "+blank+" 题"+(reasons.Count>0?"（"+String.Join(" · ",reasons)+"）":"")+"。请核对并手动提交。");
            }catch(OperationCanceledException){if(generation==version)SetStatus("查题已停止或页面已切换；已填写 "+filled+" 题，未提交。");}
            catch(Exception){if(generation==version)SetStatus("作业读取或填写未完成；已填写 "+filled+" 题。请检查当前页面后重试，未提交。");}
            finally{
                if(run==cancellation)run=null;cancellation.Dispose();
                var current=CurrentPage();
                if(generation!=version&&current!=null&&(current.Frame!=page.Frame||current.Fingerprint!=page.Fingerprint))
                    SetStatus("识别到 "+current.Total+" 题。点击“刷作业”查询并填写，完成后请手动提交。");
                UpdateButtons();
            }
        }
        protected override void Dispose(bool disposing) {
            if(disposing&&!closing){closing=true;readWatchdog.Dispose();CancelRun();http.Dispose();Browser.Dispose();}
            base.Dispose(disposing);
        }
    }
}
