using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("栖页安装与启动")]
[assembly: AssemblyProduct("栖页")]
namespace QiyeDistribution {
 internal static class Program {
  [STAThread]static void Main(string[] args) {
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   if(Array.IndexOf(args,"--extract-only")>=0) {
    if(args.Length!=2)throw new ArgumentException("Use --extract-only <directory>");
    Extract(args[1]);return;
   }
   Application.Run(new SetupWindow());
  }
  internal static bool IsInstaller {get{return Assembly.GetExecutingAssembly().GetManifestResourceInfo("Qiye.payload.zip")!=null;}}
  internal static void Extract(string target) {
   string prefix=Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   Directory.CreateDirectory(prefix);
   using(var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("Qiye.payload.zip")) {
    if(payload==null)throw new IOException("This is the launcher; use Qiye-Setup to install.");
    using(var zip=new ZipArchive(payload,ZipArchiveMode.Read))foreach(var entry in zip.Entries) {
     string path=Path.GetFullPath(Path.Combine(prefix,entry.FullName));
     if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid package entry.");
     if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(path);continue;}
     Directory.CreateDirectory(Path.GetDirectoryName(path));
     using(var input=entry.Open())using(var output=File.Create(path))input.CopyTo(output);
    }
   }
  }
 }
 internal sealed class SetupWindow:Form {
  readonly TextBox path=new TextBox(),log=new TextBox();readonly Label status=new Label();
  readonly Button start=new Button(),browse=new Button();readonly ProgressBar progress=new ProgressBar();
  readonly CheckBox shortcut=new CheckBox();bool running;string root;
  internal SetupWindow() {
   Text=Program.IsInstaller?"安装栖页":"正在启动栖页";ClientSize=new Size(680,460);MinimumSize=new Size(696,499);
   StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);BackColor=Color.FromArgb(249,247,255);
   var title=new Label{Text="栖页 · 学习，自有栖处",Location=new Point(26,23),Size=new Size(620,40),Font=new Font(Font.FontFamily,20,FontStyle.Bold),ForeColor=Color.FromArgb(78,61,131)};
   var details=new Label{Text="首次安装会从官方来源下载约 335 MB 的 Java 和数据库组件。\n请预留 3 GB 空间；后续启动会复用已安装的运行环境。",Location=new Point(28,77),Size=new Size(620,52)};
   root=Program.IsInstaller?(Directory.Exists("E:\\")?"E:\\Qiye":Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Qiye")):AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
   path.SetBounds(28,143,510,30);path.Text=root;path.ReadOnly=!Program.IsInstaller;
   browse.Text="选择目录";browse.SetBounds(550,141,100,32);browse.Enabled=Program.IsInstaller;
   browse.Click+=(s,e)=>{using(var picker=new FolderBrowserDialog{Description="选择栖页的独立安装文件夹",SelectedPath=path.Text})if(picker.ShowDialog()==DialogResult.OK)path.Text=picker.SelectedPath;};
   shortcut.Text="创建桌面快捷方式";shortcut.Checked=true;shortcut.SetBounds(28,184,320,28);shortcut.Visible=Program.IsInstaller;
   status.SetBounds(28,222,622,28);status.Text=Program.IsInstaller?"选择目录后，点击安装并启动。":"正在准备本机服务…";
   progress.SetBounds(28,256,622,10);progress.Style=ProgressBarStyle.Marquee;progress.Visible=false;
   log.SetBounds(28,281,622,104);log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;log.BackColor=Color.White;log.BorderStyle=BorderStyle.FixedSingle;
   start.Text=Program.IsInstaller?"安装并启动":"重试启动";start.SetBounds(490,403,160,36);start.BackColor=Color.FromArgb(109,80,220);start.ForeColor=Color.White;start.FlatStyle=FlatStyle.Flat;
   start.Click+=async(s,e)=>await Run();
   var license=new Label{Text="开源许可：Apache 2.0 · 运行组件遵循各自许可证",Location=new Point(28,412),Size=new Size(450,25),ForeColor=Color.DimGray};
   Controls.AddRange(new Control[]{title,details,path,browse,shortcut,status,progress,log,start,license});
   FormClosing+=(s,e)=>{if(running){e.Cancel=true;status.Text="正在准备组件，请等待当前步骤结束。";}};
   if(!Program.IsInstaller)Shown+=async(s,e)=>await Run();
  }
  void UpdateStage(string message) {
   if(IsDisposed||String.IsNullOrWhiteSpace(message))return;
   string translated=message;
   if(message.StartsWith("Downloading "))translated="首次下载运行组件，请稍候… "+message;
   else if(message.StartsWith("Extracting "))translated="正在解压运行组件…";
   else if(message.StartsWith("Preparing "))translated="正在准备运行组件…";
   else if(message.StartsWith("Creating a new"))translated="正在创建空白数据库…";
   else if(message.StartsWith("Starting the local"))translated="正在启动本机数据库…";
   else if(message.StartsWith("Starting the app"))translated="正在启动服务，首次启动可能需要约一分钟…";
   else if(message.Contains("are ready"))translated="本机服务已就绪，正在打开栖页…";
   status.Text=translated;log.AppendText(message+Environment.NewLine);
  }
  async Task Run() {
   if(running)return;running=true;start.Enabled=false;browse.Enabled=false;path.ReadOnly=true;progress.Visible=true;
   try {
    root=Path.GetFullPath(path.Text).TrimEnd('\\');
    if(root==Path.GetPathRoot(root).TrimEnd('\\'))throw new IOException("请选择独立的安装文件夹，例如 E:\\Qiye。");
    foreach(char letter in root)if(letter>127)throw new IOException("MySQL 运行环境需要英文安装路径，请选择例如 E:\\Qiye。路径可以包含空格。");
    if(Program.IsInstaller){UpdateStage("正在释放应用文件…");await Task.Run(()=>Program.Extract(root));}
    string script=Path.Combine(root,"start-local.ps1");
    var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")) {
     Arguments="-NoLogo -NoProfile -ExecutionPolicy Bypass -File \""+script+"\"",WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
     StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
    };
    int code=await Task.Run(()=>{
     using(var child=new Process{StartInfo=info}) {
      child.OutputDataReceived+=(s,e)=>{if(e.Data!=null&&!IsDisposed)BeginInvoke(new Action(()=>UpdateStage(e.Data)));};
      child.ErrorDataReceived+=(s,e)=>{if(e.Data!=null&&!IsDisposed)BeginInvoke(new Action(()=>log.AppendText(e.Data+Environment.NewLine)));};
      child.Start();child.BeginOutputReadLine();child.BeginErrorReadLine();
      // Background services can retain inherited pipe handles. Wait for the
      // setup process itself, then stop readers without waiting for pipe EOF.
      while(!child.WaitForExit(250)){}
      try{child.CancelOutputRead();child.CancelErrorRead();}catch(InvalidOperationException){}
      return child.ExitCode;
     }
    });
    if(code!=0)throw new IOException("准备未完成。请查看下方信息和安装目录中的 logs，再点击重试。");
    if(Program.IsInstaller&&shortcut.Checked)CreateShortcut();
    Process.Start(new ProcessStartInfo(Path.Combine(root,"desktop","StudyDesktop.exe")){WorkingDirectory=root,UseShellExecute=true});
    running=false;Close();
   }catch(Exception error){status.Text="准备未完成，可以重试。";log.AppendText(error.Message+Environment.NewLine);start.Text="重试";}
   finally{running=false;if(!IsDisposed){start.Enabled=true;browse.Enabled=Program.IsInstaller;path.ReadOnly=!Program.IsInstaller;progress.Visible=false;}}
  }
  void CreateShortcut() {
   Type shellType=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(shellType);
   string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"栖页.lnk");
   object link=shellType.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{file});Type type=link.GetType();
   type.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{Path.Combine(root,"Qiye.exe")});
   type.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{root});
   type.InvokeMember("IconLocation",BindingFlags.SetProperty,null,link,new object[]{Path.Combine(root,"desktop","app.ico")});
   type.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);
   System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
  }
 }
}
