using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SuperAutoDesktop {
    internal sealed class CourseProgressSnapshot {
        public int Total, Completed, Locked, Pending, Unknown;
        public DateTime CheckedUtc;
        public bool AllCompleted {get {return Total>0&&Completed==Total&&Unknown==0;}}
        public double Percent {get {return Total>0?100.0*Completed/Total:0;}}
    }
    internal sealed class CourseProgressEntry {
        public CourseProgressSnapshot Progress;
        public bool Ended;
    }
    internal sealed class CourseProgressStore {
        private readonly string root,path;
        private Dictionary<string,CourseProgressEntry> entries=new Dictionary<string,CourseProgressEntry>();
        internal CourseProgressStore(string installRoot) {
            root=installRoot;path=Path.Combine(root,"data","browser-course-progress.json");
            try {if(File.Exists(path))entries=new JavaScriptSerializer().Deserialize<Dictionary<string,CourseProgressEntry>>(File.ReadAllText(path,Encoding.UTF8))??entries;}catch{}
        }
        internal string Key(CourseItem course) {
            if(course==null)return "";
            string account="";try{account=File.ReadAllText(Path.Combine(root,"data","desktop-account.txt"),Encoding.UTF8).Trim();}catch{}
            using(var hash=SHA256.Create())account=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(account))).Replace("-","");
            return account+":"+course.Id+":"+course.ClazzId+":"+course.Cpi;
        }
        internal CourseProgressEntry Get(string key) {CourseProgressEntry value;return entries.TryGetValue(key,out value)?value:null;}
        internal void Update(string key,CourseProgressSnapshot snapshot) {Entry(key).Progress=snapshot;Save();}
        internal void End(string key,bool ended) {Entry(key).Ended=ended;Save();}
        private CourseProgressEntry Entry(string key){CourseProgressEntry value;if(!entries.TryGetValue(key,out value))entries[key]=value=new CourseProgressEntry();return value;}
        private void Save() {
            Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+".tmp";
            File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(entries),Encoding.UTF8);
            if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
        }
    }
    internal static class OfficialCourseProgress {
        private const string Tag=@"</?div\b(?:[^'""<>]|'[^']*'|""[^""]*"")*>";
        private static string Attribute(string tag,string name) {
            var match=Regex.Match(tag,@"\b"+name+@"\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))",RegexOptions.IgnoreCase);
            return WebUtility.HtmlDecode(match.Groups["v"].Value);
        }
        private static bool HasClass(string tag,string name){return Attribute(tag,"class").Split((char[])null,StringSplitOptions.RemoveEmptyEntries).Contains(name);}
        private static string Text(string html){return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html,"<[^>]*>"," ")),@"\s+"," ").Trim();}
        internal static CourseProgressSnapshot Parse(string html) {
            html=Regex.Replace(html,@"<!--.*?-->|<script\b[^>]*>.*?</script\s*>|<style\b[^>]*>.*?</style\s*>","",RegexOptions.Singleline|RegexOptions.IgnoreCase);
            var result=new CourseProgressSnapshot {CheckedUtc=DateTime.UtcNow};
            var known=new Dictionary<string,string>();int depth=0,start=-1,chapterDepth=-1;string id="";
            foreach(Match tag in Regex.Matches(html,Tag,RegexOptions.IgnoreCase)) {
                if(Regex.IsMatch(tag.Value,@"^<\s*/")) {
                    depth--;
                    if(start>=0&&depth==chapterDepth) {
                        string chapter=html.Substring(start,tag.Index+tag.Length-start);string state="unknown";
                        var tips=Regex.Matches(chapter,@"<span\b(?:[^'""<>]|'[^']*'|""[^""]*"")*>.*?</span\s*>",RegexOptions.Singleline|RegexOptions.IgnoreCase)
                            .Cast<Match>().Where(m=>HasClass(m.Value.Substring(0,m.Value.IndexOf('>')+1),"bntHoverTips")).Select(m=>Text(m.Value)).ToList();
                        if(tips.Any(t=>t=="已完成"))state="completed";
                        else if(tips.Any(t=>t.Contains("解锁")||t.Contains("锁定")))state="locked";
                        else if(tips.Any(t=>Regex.IsMatch(t,@"[1-9]\d*\s*个待完成任务点")||t=="未完成"))state="pending";
                        else if(Regex.Matches(chapter,@"<input\b(?:[^'""<>]|'[^']*'|""[^""]*"")*>",RegexOptions.IgnoreCase).Cast<Match>()
                            .Any(m=>HasClass(m.Value,"knowledgeJobCount")&&Regex.IsMatch(Attribute(m.Value,"value"),@"^[1-9]\d*$")))state="pending";
                        string existing;
                        if(known.TryGetValue(id,out existing)&&existing!=state)throw new IOException("官方目录状态不一致，请稍后刷新");
                        known[id]=state;start=-1;
                    }
                } else {
                    if(start<0&&HasClass(tag.Value,"chapter_item")&&Regex.IsMatch(Attribute(tag.Value,"id"),@"^cur\d+$")) {
                        start=tag.Index;chapterDepth=depth;id=Attribute(tag.Value,"id");
                    }
                    depth++;
                }
            }
            if(start>=0||known.Count==0)throw new IOException("未读取到官方章节目录，请检查扫码登录状态");
            result.Total=known.Count;result.Completed=known.Values.Count(v=>v=="completed");result.Locked=known.Values.Count(v=>v=="locked");
            result.Pending=known.Values.Count(v=>v=="pending");result.Unknown=known.Values.Count(v=>v=="unknown");
            return result;
        }
        internal static async Task<CourseProgressSnapshot> ReadSaved(string root,CourseItem course) {
            if(!Regex.IsMatch(course.Id??"",@"^\d+$")||!Regex.IsMatch(course.ClazzId??"",@"^\d+$")||!Regex.IsMatch(course.Cpi??"",@"^\d+$"))throw new IOException("课程信息不完整，请先刷新课程列表");
            var cookies=await Task.Run(()=>SavedBrowserLogin.Read(root));
            if(!cookies.ContainsKey("_uid"))throw new IOException("请先扫码登录后再刷新章节进度");
            var jar=new CookieContainer();
            foreach(var pair in cookies){try{jar.Add(new Cookie(pair.Key,pair.Value,"/",".chaoxing.com"));}catch(CookieException){}}
            using(var handler=new HttpClientHandler {CookieContainer=jar,AllowAutoRedirect=false})
            using(var client=new HttpClient(handler) {Timeout=TimeSpan.FromSeconds(12)}) {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Referrer=new Uri("https://i.chaoxing.com/");
                using(var response=await client.GetAsync(OfficialPlayer.CourseUrl(course))) {
                    if(!response.IsSuccessStatusCode)throw new IOException("官方目录暂时无法读取，请检查登录或稍后刷新");
                    return Parse(Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync()));
                }
            }
        }
    }
}
