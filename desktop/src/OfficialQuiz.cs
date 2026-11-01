using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuperAutoDesktop {
    internal sealed partial class OfficialPlayer {
        private CheckBox autoQuiz;
        private string quizFrame="",quizFingerprint="",quizAttemptedKey="",quizStatus="",quizRequest="",quizMode="chapter";
        private int quizTotal,quizGeneration;
        private bool quizVisible,quizBusy,quizSubmissionIssued,quizContinueRequested,quizRandomUsed;
        private readonly Random quizRandom=new Random();
        private DateTime quizSample=DateTime.MinValue;
        private TaskCompletionSource<Dictionary<string,object>> quizWait;
        internal Func<string,object,Task<Dictionary<string,object>>> QuizApiOverride {get;set;}
        internal bool AutomaticQuizEnabled {get{return autoQuiz.Checked;}}
        private bool VideoQuizActive {get{return quizMode == "video" && ((quizVisible && DateTime.UtcNow.Subtract(quizSample).TotalSeconds<4) || (quizBusy && quizSubmissionIssued));}}
        internal void SetAutomaticQuiz(bool enabled){autoQuiz.Checked=enabled;}

        private void InitializeQuizControls(Control header) {
            autoQuiz=new CheckBox {Text="题库自动答题并提交",Name="AutoQuizSubmit",Checked=true,AutoSize=true,Margin=new Padding(0,3,0,0)};
            if(!test) {
                try {
                    string file=Path.Combine(root,"data","browser-player-settings.json");
                    if(File.Exists(file)) {
                        var settings=json.Deserialize<Dictionary<string,bool>>(File.ReadAllText(file,Encoding.UTF8));
                        if(settings.ContainsKey("autoQuiz"))autoQuiz.Checked=settings["autoQuiz"];
                    }
                }catch(Exception){}
            }
            autoQuiz.CheckedChanged+=(sender,args)=>{
                InvalidateQuiz(false);
                if(!test) {
                    try {File.WriteAllText(Path.Combine(root,"data","browser-player-settings.json"),json.Serialize(new {autoResume=autoResume.Checked,autoAdvance=autoAdvance.Checked,autoQuiz=autoQuiz.Checked}),Encoding.UTF8);}
                    catch(IOException){}
                }
                SetWorkflow(autoQuiz.Checked ? "等待题库自动查题" : "题库自动答题已关闭");
            };
            header.Controls.Add(autoQuiz);
        }
        private string QuizKey(){return workflowKey+":"+quizFingerprint;}
        private void ReceiveQuiz(Dictionary<string,object> data) {
            string kind=OfficialRecord.Value(data,"kind"),frame=OfficialRecord.Value(data,"frameId");
            if(kind == "quiz-result") {
                if(frame == quizFrame && OfficialRecord.Value(data,"request") == quizRequest && quizWait != null)quizWait.TrySetResult(data);
                return;
            }
            if(kind == "video-quiz-gone") {
                if(quizMode != "video" || frame != quizFrame)return;
                quizVisible=false;
                // A successful official callback can close the popup before our result arrives.
                // Keep that pending reply; invalidate a lookup if the user closed the question.
                if(!quizBusy || !quizSubmissionIssued)InvalidateQuiz(true);
                return;
            }
            string signature=OfficialRecord.Value(data,"fingerprint");int total=(int)Number(data,"total");
            if(signature.Length == 0 || signature.Length>80 || total<1 || total>100)return;
            string mode=kind == "video-quiz-page" ? "video" : "chapter";
            if(mode=="video"&&NeedsTaskRecognition&&Progress.HasVideo&&frame!=Progress.FrameId)return;
            if(mode=="chapter"&&VideoQuizActive)return;
            if(quizFingerprint != signature || quizFrame != frame || quizMode != mode) {
                InvalidateQuiz(true);quizFingerprint=signature;quizMode=mode;
            }
            quizFrame=frame;quizTotal=total;quizVisible=Boolean(data,"pageVisible");quizSample=DateTime.UtcNow;
            if(mode == "video" && quizContinueRequested && quizSubmissionIssued && (Boolean(data,"accepted") || (quizRandomUsed && Boolean(data,"canContinueRejected")))
                && !quizBusy && autoQuiz.Checked && !userPaused && !Progress.ManualPause) {
                quizAttemptedKey="";quizContinueRequested=false;
            }
        }
        private void InvalidateQuiz(bool navigation) {
            quizGeneration++;
            if(!navigation && quizMode == "video" && quizSubmissionIssued)quizContinueRequested=true;
            if(Ready && quizFrame.Length>0) {
                try {Browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {channel="study-player-command",token=token,frameId=quizFrame,action="quiz-cancel"}));}
                catch(Exception){}
            }
            if(quizWait != null)quizWait.TrySetCanceled();
            if(!quizSubmissionIssued) {quizAttemptedKey="";quizStatus="";}
            if(navigation) {
                quizFrame="";quizFingerprint="";quizSample=DateTime.MinValue;
                quizAttemptedKey="";quizStatus="";quizSubmissionIssued=false;quizVisible=false;quizMode="chapter";quizContinueRequested=false;quizRandomUsed=false;
            }
        }
        private void RetryQuiz() {
            if(quizBusy || quizSubmissionIssued)return;
            quizAttemptedKey="";quizStatus="";
        }
        private bool QuizCurrent(string key,int generation) {
            return Ready && !closing && !studyPaused && autoQuiz.Checked && !userPaused && !Progress.ManualPause && key == QuizKey()
                && (quizMode=="video"||!TaskPlanCurrent||ChapterVideosReady(taskPlan))
                && (quizMode!="video"||!NeedsTaskRecognition||(TaskPlanCurrent&&Progress.HasVideo&&quizFrame==Progress.FrameId))
                && generation == quizGeneration && quizVisible && workflowVisible && PlaybackWindowAvailable
                && DateTime.UtcNow.Subtract(quizSample).TotalSeconds<4 && CurrentPrompt().Length == 0;
        }
        private async Task<Dictionary<string,object>> QuizCommand(string action,object answers) {
            var wait=new TaskCompletionSource<Dictionary<string,object>>();
            quizWait=wait;quizRequest=Guid.NewGuid().ToString("N");
            try {
                Browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(new {channel="study-player-command",token=token,frameId=quizFrame,
                    action=action,request=quizRequest,fingerprint=quizFingerprint,answers=answers,allowRejectedContinue=quizMode == "video" && quizRandomUsed}));
                if(await Task.WhenAny(wait.Task,Task.Delay(16000)) != wait.Task)throw new IOException("测验页面未响应，请检查官方页面");
                return await wait.Task;
            }finally {if(quizWait == wait){quizWait=null;quizRequest="";}}
        }
        private async Task<Dictionary<string,object>> QuizApi(string path,object body) {
            if(test) {
                if(QuizApiOverride == null)throw new IOException("本地测验检查没有配置模拟题库");
                return await QuizApiOverride(path,body);
            }
            using(var client=new HttpClient {Timeout=TimeSpan.FromSeconds(60)})
            using(var content=new StringContent(json.Serialize(body),Encoding.UTF8,"application/json"))
            using(var response=await client.PostAsync(new Uri(LocalServices.Address(root),"/local/desktop/quiz/"+path),content)) {
                string raw=await response.Content.ReadAsStringAsync();
                Dictionary<string,object> result;
                try {result=json.Deserialize<Dictionary<string,object>>(raw);}
                catch(Exception){throw new IOException("本机题库服务响应异常，请检查后台状态");}
                if(!response.IsSuccessStatusCode) {
                    string message=OfficialRecord.Value(result,"message");
                    throw new IOException(message.Length>0 ? message : "本机题库服务未就绪，请重新打开桌面 App");
                }
                return result;
            }
        }
        private void QuizStatus(string text){quizStatus=quizMode == "video" ? (quizRandomUsed ? "视频弹题（随机作答）：" : "视频弹题：")+text : (quizRandomUsed ? "章节测验（含随机作答）：" : "")+text;SetWorkflow(quizStatus);}
        private static bool QuizAnswerMissing(IOException failure) {
            string message=failure.Message;
            return message.StartsWith("题库未查到答案",StringComparison.Ordinal)
                || message.StartsWith("cxmooc 未查到题干、题型一致的答案",StringComparison.Ordinal);
        }
        private static string QuizRandomReason(IOException failure,bool video) {
            if(QuizAnswerMissing(failure))return "题库未查到答案";
            if(video)return "";
            // Only known answer-resolution failures qualify, never transport, parsing or page errors.
            string message=failure.Message;
            if(message.StartsWith("题库重查 3 次仍无法匹配当前选项",StringComparison.Ordinal)
                || message.StartsWith("题库答案无法唯一匹配当前选项",StringComparison.Ordinal)
                || message.StartsWith("单选题答案数量不正确",StringComparison.Ordinal))return "题库答案无法对应当前选项";
            if(message.StartsWith("无法识别判断题答案",StringComparison.Ordinal))return "题库判断题答案无法识别";
            if(message.StartsWith("cxmooc 返回互相冲突的答案",StringComparison.Ordinal))return "题库答案冲突，无法确定选择";
            return "";
        }
        private static string VideoOptionMask(List<string> letters,int mask) {
            var chosen=new List<string>();
            for(int i=0;i<letters.Count;i++)if((mask & (1<<i)) != 0)chosen.Add(letters[i]);
            return String.Join("",chosen);
        }
        private Dictionary<string,object> RandomChoiceAnswer(Dictionary<string,object> question,HashSet<string> tried) {
            string type=OfficialRecord.Value(question,"type");
            var options=question.ContainsKey("options") ? question["options"] as IEnumerable : null;
            var letters=new List<string>();var contents=new List<string>();
            if(options != null)foreach(object raw in options) {
                var match=Regex.Match(Convert.ToString(raw),@"^([A-Z])[\s、.．:：)）]+([\s\S]+)$");
                if(!match.Success || letters.Contains(match.Groups[1].Value))throw new IOException("当前题目选项无法识别，未随机提交");
                letters.Add(match.Groups[1].Value);contents.Add(match.Groups[2].Value.Trim());
            }
            if(letters.Count<2 || letters.Count>26)throw new IOException("当前题目选项不完整，未随机提交");
            string selection;
            if(type == "judgement") {
                int yes=0,no=0;
                foreach(string content in contents) {
                    if(Regex.IsMatch(content,@"^(对|正确|是|true|√)$",RegexOptions.IgnoreCase))yes++;
                    if(Regex.IsMatch(content,@"^(错|错误|否|false|×)$",RegexOptions.IgnoreCase))no++;
                }
                if(yes != 1 || no != 1 || letters.Count != 2)throw new IOException("当前判断题选项无法对应对错，未随机提交");
                var remaining=new List<string>();
                foreach(string value in new[]{"true","false"})if(!tried.Contains(value))remaining.Add(value);
                if(remaining.Count == 0)return null;
                selection=remaining[quizRandom.Next(remaining.Count)];
            } else if(type == "single") {
                var remaining=letters.FindAll(letter=>!tried.Contains(letter));
                if(remaining.Count == 0)return null;
                selection=remaining[quizRandom.Next(remaining.Count)];
            }
            else if(type == "multiple") {
                int total=(1<<letters.Count)-1;
                if(tried.Count>=Math.Min(8,total))return null;
                selection="";
                for(int attempt=0;attempt<32;attempt++) {
                    string candidate=VideoOptionMask(letters,quizRandom.Next(1,total+1));
                    if(!tried.Contains(candidate)){selection=candidate;break;}
                }
                if(selection.Length == 0)for(int mask=1;mask<=total;mask++) {
                    string candidate=VideoOptionMask(letters,mask);
                    if(!tried.Contains(candidate)){selection=candidate;break;}
                }
            } else throw new IOException("暂不支持此题型，未随机提交");
            tried.Add(selection);
            return new Dictionary<string,object>{{"id",OfficialRecord.Value(question,"id")},{"selection",selection}};
        }
        private static void RequireQuizResult(Dictionary<string,object> result) {
            if(!Boolean(result,"ok")) {
                string reason=OfficialRecord.Value(result,"reason");
                throw new IOException(reason == "verification-required" ? "提交遇到验证码或本人验证，请在官方页面处理"
                    : reason == "video-answer-rejected" ? "平台提示答案未通过，请检查题目并按页面要求处理"
                    : reason == "video-question-unsupported" ? "暂不支持此视频题型，请在官方页面作答"
                    : reason == "video-result-unconfirmed" ? "尚未确认视频题通过，请检查官方反馈；不会重复提交"
                    : reason == "submission-not-confirmed" ? "提交确认未完成，请检查官方页面后点击刷新平台记录重试"
                    : "测验页面操作未完成（"+reason+"），请检查页面后刷新重试");
            }
        }
        private async Task ProcessAutomaticQuiz() {
            if(!Ready || closing || studyPaused || quizBusy || !autoQuiz.Checked || quizFrame.Length == 0 || workflowKey.Length == 0 || workflowRequest.Length > 0)return;
            string key=QuizKey();int generation=quizGeneration;
            if(quizAttemptedKey == key || !QuizCurrent(key,generation))return;
            quizAttemptedKey=key;quizBusy=true;
            if(!quizSubmissionIssued)quizRandomUsed=false;
            try {
                bool video=quizMode == "video";
                if(!video) {
                    QuizStatus("正在核对章节测验任务状态");
                    OfficialCard task=await ReadWorkflowCard(Progress.KnowledgeId,workflowCard);
                    if(!QuizCurrent(key,generation))return;
                    if(task == null || !task.Known || !task.HasQuiz)throw new IOException("无法确认当前章节测验任务，未提交");
                    RememberTaskPlan(task,workflowKey);
                    if(!ChapterVideosReady(task)) {QuizStatus("先完成本知识点的全部视频，再处理检测");quizAttemptedKey="";return;}
                    if(!task.PendingQuiz) {QuizStatus("平台已确认测验完成，等待章节衔接");return;}
                }
                QuizStatus(video ? "正在读取当前题目" : "正在读取章节测验，共 "+quizTotal+" 题");
                var snapshot=await QuizCommand("quiz-snapshot",null);RequireQuizResult(snapshot);
                if(!QuizCurrent(key,generation))return;
                var parsed=video ? snapshot : await QuizApi("questions",new {html=OfficialRecord.Value(snapshot,"html")});
                if(!QuizCurrent(key,generation))return;
                bool accepted=video && Boolean(snapshot,"accepted");
                bool rejectedContinue=video && quizRandomUsed && Boolean(snapshot,"canContinueRejected");
                if(video && quizSubmissionIssued && !accepted && !rejectedContinue)throw new IOException("先前视频题已提交，等待官方反馈；不会重复提交");
                var questions=parsed.ContainsKey("questions") ? parsed["questions"] as IEnumerable : null;
                var answers=new List<Dictionary<string,object>>();
                var randomQuestionNumbers=new List<string>();
                var randomSelections=new HashSet<string>();
                Dictionary<string,object> randomQuestion=null;
                if(questions == null || Number(parsed,"total") != quizTotal)throw new IOException("题目数量不一致，未提交");
                foreach(object raw in questions) {
                    var question=raw as Dictionary<string,object>;
                    if(question == null)throw new IOException("题目格式无法识别，未提交");
                    if(accepted || rejectedContinue)break;
                    if(OfficialRecord.Value(question,"type") == "unsupported")throw new IOException("暂不支持此题型，请在官方页面作答");
                    QuizStatus("题库查询 "+(answers.Count+1)+" / "+quizTotal+"，已匹配 "+(answers.Count-randomQuestionNumbers.Count)+" 题"+(randomQuestionNumbers.Count>0 ? "，随机选择 "+randomQuestionNumbers.Count+" 题" : ""));
                    Dictionary<string,object> found;
                    try {found=await QuizApi("answer",question);}
                    catch(IOException failure){
                        if(!QuizCurrent(key,generation))return;
                        string randomReason=QuizRandomReason(failure,video);
                        if(randomReason.Length == 0)throw new IOException("第 "+(answers.Count+1)+" 题："+failure.Message,failure);
                        // Each chapter question has its own choices; retry history belongs only to a video popup.
                        found=RandomChoiceAnswer(question,video ? randomSelections : new HashSet<string>());quizRandomUsed=true;
                        randomQuestionNumbers.Add((answers.Count+1).ToString());
                        if(video)randomQuestion=question;
                        QuizStatus("第 "+(answers.Count+1)+" 题："+randomReason+"，已随机选择；继续核对其余题目");
                        WriteLog((video ? "video" : "chapter")+" quiz random fallback question="+(answers.Count+1)+" reason="+randomReason);
                    }
                    if(!QuizCurrent(key,generation))return;
                    if(OfficialRecord.Value(found,"id") != OfficialRecord.Value(question,"id") || OfficialRecord.Value(found,"selection").Length == 0)
                        throw new IOException("答案与当前题目不一致，未提交");
                    answers.Add(new Dictionary<string,object>{{"id",OfficialRecord.Value(question,"id")},{"type",OfficialRecord.Value(question,"type")},{"selection",OfficialRecord.Value(found,"selection")}});
                }
                Dictionary<string,object> submitted=null;
                for(int attempt=1;attempt<=26;attempt++) {
                    if(!accepted && !rejectedContinue) {
                        if(answers.Count != quizTotal || !QuizCurrent(key,generation))return;
                        QuizStatus(quizRandomUsed ? (video ? "题库未命中，正在填写第 "+attempt+" 个不同选择" : "题库匹配 "+(answers.Count-randomQuestionNumbers.Count)+" 题，第 "+String.Join("、",randomQuestionNumbers)+" 题随机选择；正在填写") : "全部 "+quizTotal+" 题匹配成功，正在填写官方页面");
                        RequireQuizResult(await QuizCommand("quiz-fill",answers));
                        if(!QuizCurrent(key,generation))return;
                    }
                    QuizStatus(accepted ? "平台已显示通过，正在继续学习" : rejectedContinue ? "答案未通过，正在按官方按钮继续学习" : "已填写，正在提交并等待平台反馈");
                    quizSubmissionIssued=true;
                    submitted=await QuizCommand("quiz-submit",null);
                    if(!Boolean(submitted,"ok") && !Boolean(submitted,"submitted"))quizSubmissionIssued=false;
                    if(video && quizRandomUsed && randomQuestion != null && OfficialRecord.Value(submitted,"reason") == "video-answer-rejected"
                        && Boolean(submitted,"retryAllowed") && QuizCurrent(key,generation)) {
                        var next=RandomChoiceAnswer(randomQuestion,randomSelections);
                        if(next == null)throw new IOException("本题随机选择已尝试 "+randomSelections.Count+" 次，仍未通过，请按官方页面处理");
                        answers[0]["selection"]=OfficialRecord.Value(next,"selection");
                        WriteLog("video quiz random retry attempt="+(attempt+1)+" selection="+OfficialRecord.Value(next,"selection"));
                        continue;
                    }
                    break;
                }
                RequireQuizResult(submitted);
                if(video) {
                    if(key != QuizKey() || generation != quizGeneration || !autoQuiz.Checked || studyPaused || userPaused || Progress.ManualPause)return;
                    bool passed=Boolean(submitted,"completed");
                    if(!passed && !(quizRandomUsed && Boolean(submitted,"continued")))throw new IOException("尚未确认视频题通过，请检查官方反馈");
                    WriteLog((passed ? "video quiz confirmed" : "video quiz rejected but official continue allowed")+" questions="+quizTotal+" random="+quizRandomUsed);
                    InvalidateQuiz(true);ResetRecovery();
                    SetWorkflow(passed ? "视频弹题已通过，等待恢复真实播放" : "视频题答案未通过，已按官方按钮继续观看");
                    return;
                }
                if(!QuizCurrent(key,generation))return;
                QuizStatus("已发起测验提交，等待学习通确认后进入下一章");
                nextWorkflowCheck=DateTime.MinValue;
                WriteLog("quiz submission requested questions="+quizTotal+" randomCount="+randomQuestionNumbers.Count+" randomQuestions="+String.Join(",",randomQuestionNumbers));
            }catch(OperationCanceledException){}
            catch(Exception error) {
                if(!closing && autoQuiz.Checked && key == QuizKey() && generation == quizGeneration) {
                    string message=error is IOException ? error.Message : "题库请求失败或超时，请检查网络";
                    QuizStatus(message.Length>180 ? message.Substring(0,180) : message);
                    WriteLog("quiz stopped "+error.GetType().Name);
                }
            }finally {
                quizBusy=false;
                if(!closing && autoQuiz.Checked && !quizSubmissionIssued && key == QuizKey() && !QuizCurrent(key,generation))quizAttemptedKey="";
                if(quizMode == "video" && !quizVisible && key == QuizKey())InvalidateQuiz(true);
            }
        }
    }
}
