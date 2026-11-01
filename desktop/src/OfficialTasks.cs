using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace SuperAutoDesktop {
    internal sealed class OfficialTask {
        internal int Index;
        internal string Kind="",JobId="",ObjectId="",Title="";
        internal bool Passed,Required=true;
        internal string Key {get {return Index+":"+Kind+":"+JobId+":"+ObjectId;}}
        internal bool Matches(string job,string resource) {
            if(JobId.Length>0&&job.Length>0&&JobId!=job)return false;
            if(ObjectId.Length>0&&resource.Length>0&&ObjectId!=resource)return false;
            return (JobId.Length>0&&JobId==job)||(ObjectId.Length>0&&ObjectId==resource);
        }
    }

    internal sealed partial class OfficialPlayer {
        private OfficialCard taskPlan;
        private string taskPlanKey="",activeTaskKey="",selectedMediaKey="";
        private bool taskRecognitionBusy;
        private int taskRecognitionFailures;
        private DateTime nextTaskRecognition=DateTime.MinValue;
        private string revealTaskKey="";
        private DateTime nextTaskReveal=DateTime.MinValue;
        private sealed class CardRead {
            internal string Html="";
            internal Task<string> Pending;
            internal DateTime At=DateTime.MinValue,RetryAt=DateTime.MinValue;
            internal int Failures;
        }
        private readonly Dictionary<string,CardRead> cardReads=new Dictionary<string,CardRead>();
        private readonly Dictionary<string,TaskCompletionSource<string>> pageReads=new Dictionary<string,TaskCompletionSource<string>>();
        private readonly Dictionary<string,TaskCompletionSource<bool>> pageReadAcks=new Dictionary<string,TaskCompletionSource<bool>>();
        internal Func<string,Task<string>> OfficialReadOverride;
        private async Task<string> ReadCardFromPage(string chapter,int card) {
            if(!Ready||closing||chapter!=Progress.KnowledgeId)return await ReadCardFallback(chapter,card);
            string request=Guid.NewGuid().ToString("N");var reply=new TaskCompletionSource<string>();pageReads[request]=reply;
            var acknowledged=new TaskCompletionSource<bool>();pageReadAcks[request]=acknowledged;
            try {
                Browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {channel="study-player-command",token=token,
                    action="read-card",knowledgeId=chapter,card=card,request=request}));
                // Missing/loading frames cannot acknowledge a read. Start the HTTP
                // fallback promptly in that case, while still accepting a late page reply.
                var started=await Task.WhenAny(reply.Task,acknowledged.Task,Task.Delay(750));
                if(started==reply.Task)return await reply.Task;
                if(started!=acknowledged.Task) {
                    WriteLog("official-read page-not-ready fallback");
                    return await FirstSuccessfulRead(WaitForPageRead(reply.Task),ReadCardFallback(chapter,card));
                }
                if(await Task.WhenAny(reply.Task,Task.Delay(10000))==reply.Task)return await reply.Task;
                return await ReadCardFallback(chapter,card);
            }finally{pageReads.Remove(request);pageReadAcks.Remove(request);reply.TrySetCanceled();ObserveLateRead(reply.Task);}
        }
        private Task<string> ReadCardFallback(string chapter,int card) {return test&&OfficialReadOverride!=null?OfficialReadOverride(CardUrl(chapter,card)):ReadOfficial(CardUrl(chapter,card));}
        private static async Task<string> WaitForPageRead(Task<string> pending) {
            if(await Task.WhenAny(pending,Task.Delay(10000))==pending)return await pending;
            throw new IOException("课程页面读取超时");
        }
        private static async Task<string> FirstSuccessfulRead(Task<string> page,Task<string> fallback) {
            var first=await Task.WhenAny(page,fallback);var other=first==page?fallback:page;
            string value=null;bool failed=false;
            try{value=await first;}catch(Exception){failed=true;}
            if(failed)return await other;
            ObserveLateRead(other);return value;
        }
        private void ReceiveCardReadStarted(Dictionary<string,object> data) {
            TaskCompletionSource<bool> reply;if(pageReadAcks.TryGetValue(OfficialRecord.Value(data,"request"),out reply))reply.TrySetResult(true);
        }
        private string CardUrl(string chapter,int card) {return "https://mooc1.chaoxing.com/mooc-ans/knowledge/cards?clazzid="+course.ClazzId+"&courseid="+course.Id+"&knowledgeid="+chapter+"&num="+card+"&ut=s&cpi="+course.Cpi+"&v=20160407-3&mooc2=1";}
        private void ReceiveCardRead(Dictionary<string,object> data) {
            TaskCompletionSource<string> reply;if(!pageReads.TryGetValue(OfficialRecord.Value(data,"request"),out reply))return;
            object attachments;
            if(!Boolean(data,"ok")||!data.TryGetValue("attachments",out attachments)) {
                WriteLog("official-read browser="+OfficialRecord.Value(data,"reason"));
                reply.TrySetException(new IOException("课程页面任务状态读取失败，请检查登录、网络或页面提示"));return;
            }
            reply.TrySetResult("mArg="+json.Serialize(new {attachments=attachments})+";");
        }
        private Task<string> ReadCardHtml(string chapter,int card) {
            string key=chapter+":"+card;CardRead read;
            if(!cardReads.TryGetValue(key,out read)) {read=new CardRead();cardReads[key]=read;}
            if(read.Pending!=null)return read.Pending;
            if(read.Html.Length>0&&DateTime.UtcNow.Subtract(read.At).TotalSeconds<5)return Task.FromResult(read.Html);
            if(DateTime.UtcNow<read.RetryAt)throw new IOException("平台任务状态尚未恢复，稍后重试");
            read.Pending=LoadCardHtml(read,chapter,card);
            return read.Pending;
        }
        private async Task<string> LoadCardHtml(CardRead read,string chapter,int card) {
            await Task.Yield(); // Store the shared task before even a synchronous test reader completes.
            var started=DateTime.UtcNow;
            try {
                var pending=test&&OfficialReadOverride!=null?OfficialReadOverride(CardUrl(chapter,card)):ReadCardFromPage(chapter,card);
                if(await Task.WhenAny(pending,Task.Delay(16000))!=pending) {
                    ObserveLateRead(pending);
                    throw new IOException("平台任务状态读取超时");
                }
                string html=await pending;
                OfficialCard.Parse(html); // Error/login responses are never cached as task records.
                read.Html=html;read.At=DateTime.UtcNow;read.Failures=0;read.RetryAt=DateTime.MinValue;
                return html;
            }catch {
                read.Failures++;read.Html="";
                read.RetryAt=DateTime.UtcNow.AddSeconds(Math.Min(60,5*Math.Pow(2,Math.Min(4,read.Failures-1))));
                throw;
            }finally{read.Pending=null;WriteLog("task-read elapsedMs="+(int)DateTime.UtcNow.Subtract(started).TotalMilliseconds);}
        }
        private static async void ObserveLateRead(Task<string> pending) {try{await pending;}catch{}}
        private bool NeedsTaskRecognition {get {return workflowKey.Length>0&&(!test||WorkflowRecordReader!=null);}}
        private bool TaskPlanCurrent {get {return taskPlan!=null&&taskPlanKey==workflowKey;}}
        private static string MediaKey(string frame,string item){return frame+"|"+item;}
        private static string MediaKey(BrowserProgress value){return MediaKey(value.FrameId,value.MediaId);}
        internal string TaskRecognitionStatus {get {return TaskSummary();}}
        private void ResetTaskRecognition() {
            taskPlan=null;taskPlanKey="";activeTaskKey="";selectedMediaKey="";
            nextTaskRecognition=DateTime.MinValue;
            taskRecognitionFailures=0;
            cardReads.Clear();
            revealTaskKey="";nextTaskReveal=DateTime.MinValue;
        }
        private void RememberTaskPlan(OfficialCard value,string key) {
            taskPlan=value;taskPlanKey=key;
            taskRecognitionFailures=0;
        }
        private void ReceiveTaskPlan(Dictionary<string,object> data) {
            // Only whitelisted task fields are sent by the current cards document.
            string chapter=OfficialRecord.Value(data,"knowledgeId");int card;
            if(!Int32.TryParse(OfficialRecord.Value(data,"cardNum"),out card)||chapter+":"+card!=workflowKey)return;
            if(TaskPlanCurrent)return; // A loaded-page snapshot cannot replace later server completion checks.
            object attachments;if(!data.TryGetValue("attachments",out attachments))return;
            var value=OfficialCard.Parse("mArg="+json.Serialize(new {attachments=attachments})+";");
            if(!value.Known)return;
            RememberTaskPlan(value,workflowKey);nextTaskRecognition=DateTime.UtcNow.AddSeconds(30);PickMedia();
            WriteLog("task-recognition source=loaded-page tasks="+value.Tasks.Count);
        }
        private async Task RecognizeTasks() {
            if(!Ready||closing||!NeedsTaskRecognition||taskRecognitionBusy||workflowBusy||DateTime.UtcNow<nextTaskRecognition)return;
            // Explicit refresh (MinValue) still rechecks; routine polling can wait
            // while an already identified video is playing.
            if(nextTaskRecognition!=DateTime.MinValue&&TaskPlanCurrent&&taskPlan.Known&&ActiveVideoFinishing())return;
            string key=workflowKey;int version=workflowVersion;
            taskRecognitionBusy=true;nextTaskRecognition=DateTime.UtcNow.AddSeconds(TaskPlanCurrent?30:5);
            try {
                var value=await ReadWorkflowCard(Progress.KnowledgeId,workflowCard);
                if(closing||key!=workflowKey||version!=workflowVersion)return;
                RememberTaskPlan(value,key);
                if(value==null||!value.Known)SetWorkflow("任务清单含暂不支持的类型，请在官方页面检查");
                else PickMedia();
            }catch(Exception error){
                if(!closing&&key==workflowKey) {
                    taskRecognitionFailures++;
                    nextTaskRecognition=DateTime.UtcNow.AddSeconds(Math.Min(60,5*Math.Pow(2,Math.Min(4,taskRecognitionFailures-1))));
                    if(!TaskPlanCurrent)SetWorkflow("任务清单暂不可读，正在重试；请检查页面登录或网络");
                    else if(Progress.Ended)SetWorkflow("视频已结束，等待平台任务状态恢复");
                }
                WriteLog("task-recognition unavailable "+error.GetType().Name);
            }finally{taskRecognitionBusy=false;}
        }
        private List<BrowserProgress> FreshMedia() {
            return media.Values.Where(value=>DateTime.UtcNow.Subtract(value.SampleAt).TotalSeconds<4
                &&(value.KnowledgeId.Length==0||Progress.KnowledgeId.Length==0||value.KnowledgeId==Progress.KnowledgeId)
                &&(value.CardIndex<0||workflowCard<0||value.CardIndex==workflowCard)).ToList();
        }
        private bool ActiveVideoFinishing() {
            if(!TaskPlanCurrent||activeTaskKey.Length==0)return false;
            var task=taskPlan.Tasks.FirstOrDefault(value=>value.Key==activeTaskKey);
            BrowserProgress current;
            return task!=null&&task.Kind=="video"&&media.TryGetValue(selectedMediaKey,out current)
                &&DateTime.UtcNow.Subtract(current.SampleAt).TotalSeconds<4&&!current.Ended;
        }
        private bool ChapterVideosReady(OfficialCard plan) {
            return plan!=null&&plan.Known&&!plan.PendingVideo&&!ActiveVideoFinishing();
        }
        private BrowserProgress PickMedia() {
            var fresh=FreshMedia();
            BrowserProgress selected;
            if(!NeedsTaskRecognition) {
                // Retain a stable paused player instead of following the last frame to report.
                return fresh.FirstOrDefault(value=>!value.Paused&&!value.Ended)
                    ??(media.TryGetValue(selectedMediaKey,out selected)&&fresh.Contains(selected)?selected:null)
                    ??fresh.OrderBy(value=>value.FrameId).ThenBy(value=>value.MediaId).FirstOrDefault();
            }
            if(!TaskPlanCurrent||!taskPlan.Known)return null;
            var videos=taskPlan.Tasks.Where(value=>value.Required&&value.Kind=="video").ToList();
            var active=videos.FirstOrDefault(value=>value.Key==activeTaskKey);
            if(active!=null&&active.Passed&&!ActiveVideoFinishing())active=null;
            if(active==null)active=videos.FirstOrDefault(value=>!value.Passed);
            // Retain the final ended sample so the displayed player and chapter gate see
            // the same end event even when the official pass flag arrived earlier.
            if(active==null)return fresh.FirstOrDefault(value=>MediaKey(value)==selectedMediaKey)
                ??fresh.OrderBy(value=>value.JobId).ThenBy(value=>value.MediaId).FirstOrDefault();
            if(activeTaskKey!=active.Key)activeTaskKey=active.Key;
            var matches=fresh.Where(value=>active.Matches(value.JobId,value.ObjectId)).ToList();
            // A single unlabelled video can be matched only when neither side has a conflicting identity.
            if(matches.Count==0&&videos.Count==1&&fresh.Count==1&&fresh[0].JobId.Length==0&&fresh[0].ObjectId.Length==0)matches.Add(fresh[0]);
            if(matches.Count!=1) {
                Progress.HasVideo=false;
                SetStatus("等待对应视频播放器","已识别 "+videos.Count+" 个视频，正在匹配第 "+(videos.IndexOf(active)+1)+" 个；不会跳过未确认的任务。");
                if(matches.Count==0&&!studyPaused&&PlaybackWindowAvailable&&autoResume.Checked&&CurrentPrompt().Length==0&&!VideoQuizActive
                    &&(revealTaskKey!=active.Key||DateTime.UtcNow>=nextTaskReveal)) {
                    revealTaskKey=active.Key;nextTaskReveal=DateTime.UtcNow.AddSeconds(5);
                    Browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {channel="study-player-command",token=token,
                        action="reveal-task",jobId=active.JobId,objectId=active.ObjectId,knowledgeId=Progress.KnowledgeId,card=workflowCard,version=studyCommandVersion}));
                }
                return null;
            }
            selected=matches[0];
            if(quizMode=="video"&&quizFrame!=selected.FrameId)InvalidateQuiz(true);
            string next=MediaKey(selected);
            if(selectedMediaKey!=next) {
                selectedMediaKey=next;Progress.HasRecord=false;ResetRecovery();lastRecordAttempt=DateTime.MinValue;
                if(!studyPaused)SelectMediaTarget(selected);
                WriteLog("task-selected video="+(videos.IndexOf(active)+1)+"/"+videos.Count);
            }
            return selected;
        }
        private void SelectMediaTarget(BrowserProgress value) {
            if(!Ready||closing||studyPaused)return;
            Browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {channel="study-player-command",token=token,
                action="select-media",frameId=value.FrameId,mediaId=value.MediaId,version=studyCommandVersion}));
        }
        private string TaskSummary() {
            if(!TaskPlanCurrent||!taskPlan.Known)return "";
            var videos=taskPlan.Tasks.Where(value=>value.Required&&value.Kind=="video").ToList();
            var quizzes=taskPlan.Tasks.Where(value=>value.Required&&value.Kind=="workid").ToList();
            string summary="当前页 "+(workflowCard+1)+"/"+workflowCardCount+" · 视频 "+videos.Count(value=>value.Passed)+"/"+videos.Count+" · 检测 "+quizzes.Count(value=>value.Passed)+"/"+quizzes.Count;
            int active=videos.FindIndex(value=>value.Key==activeTaskKey);
            return summary+(active>=0?" · 当前视频 "+(active+1)+"/"+videos.Count:"");
        }
    }
}
