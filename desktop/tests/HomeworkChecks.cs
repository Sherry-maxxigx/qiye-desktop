using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SuperAutoDesktop;

internal static class HomeworkChecks {
    static int count;
    static void Check(bool condition,string name){if(!condition)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    static async Task Until(Func<bool> condition,string name){var clock=Stopwatch.StartNew();while(!condition()&&clock.Elapsed.TotalSeconds<15)await Task.Delay(100);Check(condition(),name);}
    static Dictionary<string,object> Found(string id,string selection){return new Dictionary<string,object>{{"id",id},{"matched",true},{"selection",selection}};}
    static async Task<string> Js(OfficialHomeworkView view,string script){return await view.Browser.CoreWebView2.ExecuteScriptAsync(script);}
    static async Task Navigate(OfficialHomeworkView view,string path){
        var done=new TaskCompletionSource<bool>();
        EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs> handler=(s,e)=>done.TrySetResult(e.IsSuccess);
        view.Browser.CoreWebView2.NavigationCompleted+=handler;
        try{view.Browser.CoreWebView2.Navigate("https://player-test.invalid/"+path);if(await Task.WhenAny(done.Task,Task.Delay(15000))!=done.Task||!await done.Task)throw new Exception("Fixture navigation failed");}
        finally{view.Browser.CoreWebView2.NavigationCompleted-=handler;}
    }
    [STAThread]static int Main(string[] args){
        Console.OutputEncoding=System.Text.Encoding.UTF8;
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        string root=args[0];int exit=0,lookups=0;bool networkFailure=false;TaskCompletionSource<Dictionary<string,object>> delayed=null;
        var view=new OfficialHomeworkView(root,new CourseItem {Id="1",ClazzId="2",Cpi="3",Title="合成本地作业"},new Uri("http://127.0.0.1:1"),true);
        view.LookupOverride=async(q,cancel)=>{
            lookups++;string id=Convert.ToString(q["id"]);
            if(id=="301"){
                Check(Convert.ToString(q["title"])=="合成新版单选","display numbering and type labels are excluded from the bank query");
                Check(String.Join("|",((System.Collections.IEnumerable)q["options"]).Cast<object>())=="A 合成甲|B 合成乙","div-based answer content is separated from option markers");
                return Found(id,"B");
            }
            if(id=="302")return Found(id,"AC");
            if(id=="303")return new Dictionary<string,object>{{"id",id},{"matched",true},{"values",new[]{"乙","<img src=x onerror=alert(1)>"}}};
            if(id=="304"||id=="305")throw new Exception("Existing rich text and unsupported questions must not be queried");
            if(id=="306"){
                Check(Convert.ToString(q["title"])=="3.14 保留题干里的小数","decimal values in the question stem are preserved");
                return new Dictionary<string,object>{{"id",id},{"matched",false},{"reason","no-match"}};
            }
            if(id=="201")return Found(id,"B");
            if(id=="202")throw new Exception("Partially answered questions must be preserved");
            if(id=="203")return new Dictionary<string,object>{{"id",id},{"matched",true},{"values",new[]{"合成文本"}}};
            if(id=="101"){
                Check(((System.Collections.IEnumerable)q["options"])!=null,"question options present");
                if(delayed!=null)return await delayed.Task;
                if(networkFailure)throw new IOException("synthetic network failure");
            }
            if(id=="104")return new Dictionary<string,object>{{"id",id},{"matched",false}};
            if(id=="105"||id=="107")throw new Exception("A completed or unsupported question must not be queried");
            if(id=="106")return new Dictionary<string,object>{{"id",id},{"matched",true},{"values",new[]{"乙","甲"}}};
            return Found(id,id=="101"?"B":id=="102"?"AC":"false");
        };
        var host=new Form {Text="Homework fixture",ClientSize=new Size(1180,780),StartPosition=FormStartPosition.Manual,Location=new Point(-2200,0),ShowInTaskbar=false};host.Controls.Add(view);
        host.Shown+=async(s,e)=>{
            try {
                await view.Initialization;
                var entry=new Uri(OfficialHomeworkView.CourseHomeUrl(new CourseItem {Id="1",ClazzId="2",Cpi="3"}));
                Check(entry.Host=="mooc1.chaoxing.com"&&entry.AbsolutePath=="/visit/stucoursemiddle"&&entry.Query.Contains("ismooc2=1"),"homework starts from the official middle entry, which supplies signed navigation");
                Check(OfficialHomeworkView.CourseHomeUrl(new CourseItem {Id="1&bad=2",ClazzId="2",Cpi="3"})=="https://i.chaoxing.com/","invalid course parameters cannot alter the homework entry URL");
                await Until(()=>view.PendingCount==3,"official-style nested cards list only pending assignments; submitted, expired and not-started items excluded");
                Check(view.Browser.Source.AbsolutePath=="/mycourse/stu.html","assignment reading keeps the official course shell");
                Check(await Js(view,"menuClicks")=="1","delayed official menu handler is retried and invoked once");
                await Task.Delay(3200);
                Check(await Js(view,"menuClicks")=="1"&&view.PendingCount==3,"existing homework iframe is not repeatedly reopened");
                Check(lookups==0,"reading the list does not query or answer anything");
                var list=view.Controls.Find("PendingHomeworkList",true)[0] as ListBox;
                Check(list.Items[0].ToString()=="合成卡片作业一","modern homework card title is extracted");
                list.SelectedIndex=0;view.OpenSelectedWork();await Task.Delay(500);
                await Until(()=>view.DetectedQuestionCount==7,"nested homework questions detected");
                await Task.Delay(1200);Check(lookups==0,"opening an assignment waits for the explicit fill click");
                await view.FillAsync();
                Check(await Js(view,"frames[0].document.getElementById('answer101').value")=="\"B\"","single choice filled through official option handler");
                Check(await Js(view,"frames[0].document.getElementById('answer102').value")=="\"AC\"","multiple choice filled");
                Check(await Js(view,"frames[0].document.getElementById('answer103').value")=="\"false\"","judgement filled");
                Check(await Js(view,"frames[0].document.getElementById('answer104').value")=="\"\"","unmatched answer stays blank");
                Check(await Js(view,"frames[0].document.getElementById('answer105').value")=="\"A\"","manual answer is preserved");
                Check(await Js(view,"Array.from(frames[0].document.querySelectorAll('textarea')).map(t=>t.value).join('|')")=="\"乙|甲\"","fill-in answers preserve blank order");
                Check(await Js(view,"frames[0].submits===0&&frames[0].confirms===0")=="true","neither submission nor confirmation is clicked");
                Check(view.StatusText.Contains("已填写 4")&&view.StatusText.Contains("留空 2"),"progress reports filled and blank questions");
                using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(root,"homework-ui.png"));}
                int previous=lookups;await view.FillAsync();Check(lookups==previous+1,"a repeat fill only queries the remaining unanswered supported question");

                await Navigate(view,"work/detail.html?workId=1");
                string courseLoads=await Js(view,"sessionStorage.courseLoads");previous=lookups;
                list.SelectedIndex=1;view.OpenSelectedWork();
                await Until(()=>view.Browser.Source.Query.Contains("workId=2")&&view.DetectedQuestionCount==3,"the next assignment opens from cached official data URL after leaving the original list");
                Check(await Js(view,"openedId")=="\"2\""&&view.Browser.Source.Query.Contains("ticket=synthetic-two"),"the selected assignment keeps its official navigation parameters");
                Check(await Js(view,"sessionStorage.courseLoads")==courseLoads,"direct assignment switching does not reload the course homepage");
                Check(lookups==previous&&await Js(view,"frames[0].submits===0")=="true","switching from completed details does not answer or submit the next assignment");
                list.SelectedIndex=2;view.OpenSelectedWork();await Until(()=>view.Browser.Source.Query.Contains("workId=3")&&view.DetectedQuestionCount==6,"a third assignment also opens after two document replacements");
                list.SelectedIndex=0;view.OpenSelectedWork();await Until(()=>view.Browser.Source.Query.Contains("workId=1")&&view.DetectedQuestionCount==7,"switching back to the first assignment remains available");

                delayed=new TaskCompletionSource<Dictionary<string,object>>();previous=lookups;
                var switching=view.FillAsync();await Until(()=>lookups>previous,"lookup is pending before switching assignments");
                list.SelectedIndex=1;view.OpenSelectedWork();await Until(()=>view.DetectedQuestionCount==3,"switching assignments cancels an active lookup and opens the selected assignment");
                delayed.SetResult(Found("101","B"));await switching;
                Check(await Js(view,"Array.from(frames[0].document.querySelectorAll('input[type=radio]')).every(input=>!input.checked)")=="true","a late answer from the previous assignment never fills the next assignment");
                Check(view.StatusText.Contains("识别到 3"),"a cancelled lookup does not overwrite the next assignment status");
                delayed=null;list.SelectedIndex=0;view.OpenSelectedWork();await Until(()=>view.DetectedQuestionCount==7,"return to the first assignment after cancellation");

                networkFailure=true;await Navigate(view,"homework-shell.html?network=1");
                await Until(()=>view.DetectedQuestionCount==7,"new homework loaded for network failure check");
                await view.FillAsync();
                Check(await Js(view,"frames[0].document.getElementById('answer101').value")=="\"\"","network failure leaves that question blank");
                Check(await Js(view,"frames[0].document.getElementById('answer102').value")=="\"AC\"","network failure does not prevent later matched questions");

                networkFailure=false;delayed=new TaskCompletionSource<Dictionary<string,object>>();previous=lookups;
                await Navigate(view,"homework-shell.html?stop=1");
                await Until(()=>view.DetectedQuestionCount==7,"new homework loaded for cancellation check");
                var pending=view.FillAsync();await Until(()=>lookups>previous,"delayed lookup is running");
                var stop=view.Controls.Find("StopHomework",true)[0] as Button;stop.PerformClick();delayed.SetResult(Found("101","B"));await pending;
                Check(await Js(view,"frames[0].document.getElementById('answer101').value")=="\"\"","stop cancels a late lookup before filling");
                Check(await Js(view,"frames[0].submits===0")=="true","stop never submits the assignment");

                delayed=new TaskCompletionSource<Dictionary<string,object>>();previous=lookups;pending=view.FillAsync();await Until(()=>lookups>previous,"lookup started before a manual answer");
                await Js(view,"frames[0].document.querySelector('[data=\"101\"] li').click()");delayed.SetResult(Found("101","B"));await pending;
                Check(await Js(view,"frames[0].document.getElementById('answer101').value")=="\"A\"","an answer entered while lookup is running is never overwritten");

                await Navigate(view,"homework-shell.html?navigation=1");await Until(()=>view.DetectedQuestionCount==7,"new homework loaded for navigation check");
                delayed=new TaskCompletionSource<Dictionary<string,object>>();previous=lookups;pending=view.FillAsync();await Until(()=>lookups>previous,"lookup started before navigation");
                view.Browser.CoreWebView2.Navigate("https://player-test.invalid/homework-list.html?return=1");await Task.Delay(500);delayed.SetResult(Found("101","B"));await pending;
                Check(view.StatusText.Contains("未提交")||view.PendingCount==2,"navigation cancels filling without submission");
                delayed=null;await Navigate(view,"homework-modern.html");await Until(()=>view.DetectedQuestionCount==3,"modern homework layout detected");
                await view.FillAsync();
                Check(await Js(view,"document.querySelectorAll('input[type=radio]')[1].checked")=="true","modern native radio options filled");
                Check(await Js(view,"Array.from(document.querySelectorAll('textarea')).map(t=>t.value).join('|')")=="\"我自己写的|\"","partially answered fill-in questions are preserved");
                Check(await Js(view,"document.querySelector('[contenteditable]').textContent")=="\"合成文本\"","editable text field filled with matched text");
                Check(await Js(view,"submits===0")=="true","modern homework is not submitted");

                await Navigate(view,"work/dowork-modern.html");await Task.Delay(1000);await Until(()=>view.DetectedQuestionCount==6,"official dowork div-based choice and editor questions are detected");
                previous=lookups;await view.FillAsync();
                Check(lookups==previous+4,"current official options reach the bank; existing editor and unsupported questions are skipped");
                Check(await Js(view,"document.getElementById('answer301').value")=="\"B\"","current official div-based single choice uses its click handler");
                Check(await Js(view,"document.getElementById('answer302').value")=="\"AC\"","current official div-based multiple choice uses its click handler");
                Check(await Js(view,"UE.instants.ueditorInstant0.body.textContent")=="\"乙\"","hidden textarea rich-text editor is filled through the existing editor instance");
                Check(await Js(view,"UE.instants.ueditorInstant1.body.textContent==='<img src=x onerror=alert(1)>'&&!UE.instants.ueditorInstant1.body.querySelector('img')")=="true","bank text is escaped before passing it to the rich-text editor");
                Check(await Js(view,"UE.instants.ueditorInstant2.body.textContent==='我自己写的'&&UE.instants.ueditorInstant3.body.textContent===''")=="true","partial rich-text answers are preserved");
                Check(await Js(view,"editorEvents===2&&document.getElementById('answerEditor3031').value==='乙'")=="true","editor change callbacks and backing fields stay synchronized");
                Check(view.StatusText.Contains("已填写 3")&&view.StatusText.Contains("题库未命中 1")&&view.StatusText.Contains("页面或题型未支持 1"),"completion distinguishes bank misses from unsupported page structure");
                Check(await Js(view,"submits===0&&confirms===0")=="true","current official layout is never submitted or confirmed");

                await Navigate(view,"mycourse/studentcourse.html");await Task.Delay(1200);
                Check(view.Browser.Source.AbsolutePath=="/mycourse/studentcourse.html"&&view.DetectedQuestionCount==0,"a chapter directory is not mistaken for an assignment list or question page");
                await view.ReadAssignments();await Until(()=>view.PendingCount==3,"refresh re-enters the official course menu and reads the nested list");
                Check(view.Browser.Source.AbsolutePath=="/mycourse/stu.html","refresh uses the course homepage rather than the chapter directory");
                await Navigate(view,"work/empty.html");
                await Until(()=>view.PendingCount==0&&view.StatusText.Contains("未发现"),"unknown-to-confirmed-empty list updates even when both row arrays are empty");
                await Navigate(view,"homework-list.html");await Until(()=>view.PendingCount==2,"legacy button-based homework list remains supported");
                await Js(view,"sessionStorage.fixtureMode='event-only'");await view.ReadAssignments();await Until(()=>view.PendingCount==2,"event-only assignment list is loaded");
                previous=lookups;list.SelectedIndex=0;view.OpenSelectedWork();await Until(()=>view.Browser.Source.Query.Contains("workId=1")&&view.DetectedQuestionCount==7,"event-only assignment reopens from a freshly read list");
                list.SelectedIndex=0;view.OpenSelectedWork();await Until(()=>view.Browser.Source.Query.Contains("workId=2")&&view.DetectedQuestionCount==3,"same-title event entries follow stable assignment IDs after the refreshed list changes order");
                await Js(view,"sessionStorage.hideEvent='12'");list.SelectedIndex=1;view.OpenSelectedWork();
                await Until(()=>view.PendingCount==1&&view.StatusText.Contains("不在当前未完成列表"),"a removed or completed assignment reports its refreshed state instead of opening another same-title entry");
                Check(view.Browser.Source.AbsolutePath=="/mycourse/stu.html"&&lookups==previous,"event-only fallback does not answer questions or choose an unrelated assignment");
                await Js(view,"sessionStorage.removeItem('hideEvent')");
                previous=lookups;await Js(view,"sessionStorage.fixtureMode='denied'");await view.ReadAssignments();
                await Until(()=>view.StatusText.Contains("无权访问"),"official access errors are reported instead of an empty list or endless loading");
                await Js(view,"sessionStorage.fixtureMode='login'");await view.ReadAssignments();
                await Until(()=>view.StatusText.Contains("登录已失效"),"expired login prompts the user to log in again");
                Check(lookups==previous,"navigation failures never start question lookup or fill answers");
                Console.WriteLine("Homework checks passed: "+count);
            }catch(Exception error){Console.WriteLine(error);Console.WriteLine(view.StatusText);exit=1;}
            finally{host.Close();}
        };
        Application.Run(host);return exit;
    }
}
