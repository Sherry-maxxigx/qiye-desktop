param([string]$Version='1.1.5',[string]$Maven='mvn.cmd',[string]$JavaHome='',[string]$MavenCache='',[switch]$SkipBackendBuild)
. (Join-Path $PSScriptRoot 'Common.ps1')
if($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?$'){throw 'Invalid release version'}
$previousJava=$env:JAVA_HOME
try {
 if($JavaHome){$env:JAVA_HOME=$JavaHome}
 if(-not $SkipBackendBuild){
  $mavenArgs=@('-B','-ntp','-f',(Join-Path $QiyeRoot 'backend\pom.xml'))
  if($MavenCache){$mavenArgs+=('-Dmaven.repo.local='+$MavenCache)}
  $mavenArgs+='package'
  & $Maven @mavenArgs
  if($LASTEXITCODE -ne 0){throw 'Backend build or tests failed'}
 }
 $jar=Join-Path $QiyeRoot 'backend\target\SuperAuto-1.0-SNAPSHOT.jar'
 if(-not(Test-Path -LiteralPath $jar)){throw 'Build the backend first'}
 $dist=Join-Path $QiyeRoot 'dist'
 $stage=Join-Path $dist ('stage-'+[guid]::NewGuid().ToString('N'))
 [IO.Directory]::CreateDirectory($stage) | Out-Null
 foreach($dir in @('desktop','desktop\assets','backend','backend\sql','scripts','config','docs','third-party')){[IO.Directory]::CreateDirectory((Join-Path $stage $dir)) | Out-Null}
 & (Join-Path $QiyeRoot 'desktop\build-desktop.ps1') -OutputPath (Join-Path $stage 'desktop\StudyDesktop.exe')
 if($LASTEXITCODE -ne 0){throw 'Desktop build failed'}
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','WebView2-LICENSE.txt','StudyDesktop.exe.config','app.ico')){Copy-Item -LiteralPath (Join-Path $QiyeRoot ('desktop\'+$name)) -Destination (Join-Path $stage 'desktop')}
 foreach($name in @('qiye-taskbar.ico','qiye-icon.png')){Copy-Item -LiteralPath (Join-Path $QiyeRoot ('desktop\assets\'+$name)) -Destination (Join-Path $stage 'desktop\assets')}
 Copy-Item -LiteralPath $jar -Destination (Join-Path $stage 'backend\app.jar')
 Copy-Item -LiteralPath (Join-Path $QiyeRoot 'backend\sql\schema.sql') -Destination (Join-Path $stage 'backend\sql')
 foreach($name in @('Common.ps1','Initialize-Environment.ps1','Start-Services.ps1','Stop-Services.ps1','runtime-lock.json')){Copy-Item -LiteralPath (Join-Path $QiyeRoot ('scripts\'+$name)) -Destination (Join-Path $stage 'scripts')}
 foreach($name in @('README.md','LICENSE','NOTICE','THIRD_PARTY.md','CHANGELOG.md','CONTRIBUTING.md','SECURITY.md','start-local.ps1','Stop-Services.cmd')){Copy-Item -LiteralPath (Join-Path $QiyeRoot $name) -Destination $stage}
 Copy-Item -LiteralPath (Join-Path $QiyeRoot 'config\application-local.example.yml') -Destination (Join-Path $stage 'config')
 Copy-Item -Path (Join-Path $QiyeRoot 'docs\*') -Destination (Join-Path $stage 'docs') -Recurse
 Copy-Item -Path (Join-Path $QiyeRoot 'third-party\*') -Destination (Join-Path $stage 'third-party')
 Write-QiyeFile (Join-Path $stage 'version.json') (@{version=$Version;platform='win-x64';runtimeDownloadRequired=$true} | ConvertTo-Json)
 $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
 $compile=@('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001','/utf8output','/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll',('/win32icon:'+(Join-Path $QiyeRoot 'desktop\app.ico')))
 & $compiler @compile ('/out:'+(Join-Path $stage 'Qiye.exe')) (Join-Path $QiyeRoot 'installer\Program.cs')
 if($LASTEXITCODE -ne 0){throw 'Launcher compilation failed'}
 Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
 $payload=Join-Path $dist ('payload-'+$Version+'.zip')
 if(Test-Path $payload){[IO.File]::Delete($payload)}
 # Framework's CreateFromDirectory can emit Windows backslash entry names.
 # Use portable ZIP paths consistently in PowerShell 5.1 and PowerShell 7.
 $payloadArchive=[IO.Compression.ZipFile]::Open($payload,[IO.Compression.ZipArchiveMode]::Create)
 try{foreach($file in Get-ChildItem -LiteralPath $stage -Recurse -File){
  $entryName=$file.FullName.Substring($stage.Length+1).Replace('\','/')
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($payloadArchive,$file.FullName,$entryName,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
 }}finally{$payloadArchive.Dispose()}
 $installer=Join-Path $dist ('Qiye-Setup-'+$Version+'-win-x64.exe')
 & $compiler @compile ('/resource:'+$payload+',Qiye.payload.zip') ('/out:'+$installer) (Join-Path $QiyeRoot 'installer\Program.cs')
 if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
 $portable=Join-Path $dist ('Qiye-'+$Version+'-win-x64.zip')
 Copy-Item -LiteralPath $payload -Destination $portable -Force
 # Source files are selected explicitly. Generated config, profiles, runtime data,
 # tests from the personal workspace and previous releases cannot enter the zip.
 $sourceZip=Join-Path $dist ('Qiye-'+$Version+'-source.zip')
 if(Test-Path $sourceZip){[IO.File]::Delete($sourceZip)}
 $archive=[IO.Compression.ZipFile]::Open($sourceZip,[IO.Compression.ZipArchiveMode]::Create)
 try {
  $sourceFiles=@()
  foreach($folder in @('desktop\src','desktop\assets','desktop\tests','backend\src','backend\sql','scripts','installer','docs','third-party','.github')){$sourceFiles+=Get-ChildItem -LiteralPath (Join-Path $QiyeRoot $folder) -Recurse -File}
  foreach($folder in @('desktop','config')){$sourceFiles+=Get-ChildItem -LiteralPath (Join-Path $QiyeRoot $folder) -File | Where-Object {$_.Name -notmatch '\.exe$' -and ($folder -ne 'config' -or $_.Name -like '*.example.*')}}
  $sourceFiles+=Get-Item -LiteralPath (Join-Path $QiyeRoot 'backend\pom.xml')
  foreach($name in @('README.md','LICENSE','NOTICE','THIRD_PARTY.md','CHANGELOG.md','CONTRIBUTING.md','SECURITY.md','.gitignore','.gitattributes','.editorconfig','start-local.ps1','Stop-Services.cmd')){$sourceFiles+=Get-Item -LiteralPath (Join-Path $QiyeRoot $name)}
  foreach($file in $sourceFiles){$name=$file.FullName.Substring($QiyeRoot.Length+1).Replace('\','/');[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$name,[IO.Compression.CompressionLevel]::Optimal) | Out-Null}
 }finally{$archive.Dispose()}
 & (Join-Path $PSScriptRoot 'Test-Release.ps1') -Files @($portable,$sourceZip)
 $hashes=foreach($file in @($installer,$portable,$sourceZip)){(Get-QiyeSha256 $file)+'  '+[IO.Path]::GetFileName($file)}
 Write-QiyeFile (Join-Path $dist 'SHA256SUMS.txt') ($hashes -join "`r`n")
 Write-Output ('Release files are ready in '+$dist)
}finally{$env:JAVA_HOME=$previousJava}
