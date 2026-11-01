param([string]$OutputPath)

$ErrorActionPreference = 'Stop'
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot 'StudyDesktop.exe' }
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskArguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', '/codepage:65001',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll',
    ('/out:' + $OutputPath))
$taskArguments += '/reference:' + (Join-Path $PSScriptRoot 'Microsoft.Web.WebView2.Core.dll')
$taskArguments += '/reference:' + (Join-Path $PSScriptRoot 'Microsoft.Web.WebView2.WinForms.dll')
$taskArguments += '/resource:' + (Join-Path $PSScriptRoot 'src/player-monitor.js') + ',SuperAutoDesktop.player-monitor.js'
$taskArguments += '/resource:' + (Join-Path $PSScriptRoot 'src/player-layout.js') + ',SuperAutoDesktop.player-layout.js'
$taskArguments += '/resource:' + (Join-Path $PSScriptRoot 'src/homework.js') + ',SuperAutoDesktop.homework.js'
$taskIcon = Join-Path $PSScriptRoot 'app.ico'
if (Test-Path -LiteralPath $taskIcon) { $taskArguments += '/win32icon:' + $taskIcon }
$taskArguments += Join-Path $PSScriptRoot 'src/DesktopApp.cs'
$taskArguments += Join-Path $PSScriptRoot 'src/OfficialPlayer.cs'
$taskArguments += Join-Path $PSScriptRoot 'src/OfficialQuiz.cs'
$taskArguments += Join-Path $PSScriptRoot 'src/OfficialTasks.cs'
$taskArguments += Join-Path $PSScriptRoot 'src/OfficialLogin.cs'
$taskArguments += Join-Path $PSScriptRoot 'src/OfficialHomework.cs'
$taskArguments += Join-Path $PSScriptRoot 'src/CourseProgress.cs'
$taskArguments += Join-Path $PSScriptRoot 'src/TaskbarIdentity.cs'
& $taskCompiler @taskArguments
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
Write-Host 'Desktop build succeeded.'
