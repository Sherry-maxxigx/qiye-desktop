param([string]$TestRoot='')
. (Join-Path $PSScriptRoot 'Common.ps1')
if(-not $TestRoot){$TestRoot=Join-Path $QiyeRoot ('temp\homework-check-'+[guid]::NewGuid().ToString('N'))}
$TestRoot=[IO.Path]::GetFullPath($TestRoot)
[IO.Directory]::CreateDirectory($TestRoot) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $TestRoot 'fixtures')) | Out-Null
Copy-Item -Path (Join-Path $QiyeRoot 'desktop\tests\fixtures\*') -Destination (Join-Path $TestRoot 'fixtures') -Recurse -Force
foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')){Copy-Item -LiteralPath (Join-Path $QiyeRoot ('desktop\'+$name)) -Destination $TestRoot -Force}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $TestRoot 'HomeworkChecks.exe'
$arguments=@('/nologo','/target:exe','/platform:x64','/codepage:65001','/utf8output','/main:HomeworkChecks',('/out:'+$exe),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Net.Http.dll','/reference:System.Web.Extensions.dll')
foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){$arguments+=('/reference:'+(Join-Path $TestRoot $name))}
foreach($name in @('player-monitor.js','player-layout.js','homework.js')){$arguments+=('/resource:'+(Join-Path $QiyeRoot ('desktop\src\'+$name))+',SuperAutoDesktop.'+$name)}
$arguments+=@(Get-ChildItem -LiteralPath (Join-Path $QiyeRoot 'desktop\src') -Filter '*.cs' -File | ForEach-Object {$_.FullName})
$arguments+=(Join-Path $QiyeRoot 'desktop\tests\HomeworkChecks.cs')
& $compiler @arguments
if($LASTEXITCODE -ne 0){throw 'Homework test compilation failed'}
$process=Start-Process -FilePath $exe -ArgumentList ('"'+$TestRoot+'"') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $TestRoot 'checks.log') -RedirectStandardError (Join-Path $TestRoot 'checks-error.log') -PassThru
Write-Output ('Homework fixture tests started. Logs='+$TestRoot)
if(-not $process.WaitForExit(55000)){$process.Kill();throw 'Homework fixture tests timed out'}
Get-Content -LiteralPath (Join-Path $TestRoot 'checks.log') -Encoding UTF8
if($process.ExitCode -ne 0){throw 'Homework fixture tests failed'}
