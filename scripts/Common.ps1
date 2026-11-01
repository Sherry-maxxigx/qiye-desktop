$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -le 5){
    $systemModules=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\Modules'
    if(($env:PSModulePath -split ';') -notcontains $systemModules){$env:PSModulePath=$systemModules+';'+$env:PSModulePath}
}
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$script:QiyeRoot=Split-Path -Parent $PSScriptRoot
$script:QiyeUtf8=New-Object Text.UTF8Encoding($false)
function Write-QiyeFile([string]$Path,[string]$Text){[IO.File]::WriteAllText($Path,$Text,$script:QiyeUtf8)}
function Get-QiyeSha256([string]$Path){
    $inputFile=[IO.File]::OpenRead($Path);$sha=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($sha.ComputeHash($inputFile)).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose();$inputFile.Dispose()}
}
function Assert-QiyeMicrosoftInstaller([string]$Path){
    # A launcher can inherit PowerShell 7 module paths while starting Windows
    # PowerShell 5.1. Load the matching inbox security module explicitly.
    if(-not(Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue)){
        Import-Module -Name (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1') -ErrorAction Stop
    }
    $signature=Get-AuthenticodeSignature -LiteralPath $Path
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){
        throw 'Microsoft installer signature validation failed. Download the official installer again.'
    }
}
function Get-QiyeEndpoint {
    Get-Content -LiteralPath (Join-Path $script:QiyeRoot 'data\service-endpoint.json') -Raw -Encoding UTF8 | ConvertFrom-Json
}
function Get-QiyeLock {Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime-lock.json') -Raw | ConvertFrom-Json}
function Join-QiyeArguments([string[]]$Values){($Values | ForEach-Object {'"'+$_+'"'}) -join ' '}
function Get-QiyeProcess([string]$Role) {
    $idFile=Join-Path $script:QiyeRoot ('data\'+$Role+'-process.id')
    $savedId=0
    if(Test-Path -LiteralPath $idFile){[int]::TryParse((Get-Content -LiteralPath $idFile -Raw).Trim(),[ref]$savedId) | Out-Null}
    $lock=Get-QiyeLock
    if($Role -eq 'mysql'){$expected=Join-Path $script:QiyeRoot ('tools\'+$lock.mysql.folder+'\bin\mysqld.exe');$marker=(Join-Path $script:QiyeRoot 'config\my.ini').Replace('\','/')}
    elseif($Role -eq 'app'){$expected=Join-Path $script:QiyeRoot ('tools\'+$lock.java.folder+'\bin\java.exe');$marker=(Join-Path $script:QiyeRoot 'backend\app.jar').Replace('\','/')}
    else{throw 'Unknown service role'}
    $processName=[IO.Path]::GetFileName($expected)
    $matches=@(Get-CimInstance Win32_Process -Filter ("Name = '"+$processName+"'") -ErrorAction SilentlyContinue | Where-Object {$_.ExecutablePath -eq $expected -and $_.CommandLine.Contains($marker)})
    $record=$matches | Where-Object {$_.ProcessId -eq $savedId} | Select-Object -First 1
    if(-not $record){$record=$matches | Select-Object -First 1}
    if(-not $record){return $null}
    Write-QiyeFile $idFile ([string]$record.ProcessId)
    Get-Process -Id $record.ProcessId -ErrorAction SilentlyContinue
}
function Test-QiyePort([int]$Port) {
    $listener=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,$Port)
    try{$listener.Start();return $true}catch{return $false}finally{$listener.Stop()}
}
function Get-QiyeFreePort([int]$First,[int]$Last) {
    for($port=$First;$port -le $Last;$port++){if(Test-QiyePort $port){return $port}}
    throw 'No free local port is available. Close other copies and retry.'
}
function Test-QiyeHealth {
    try{$endpoint=Get-QiyeEndpoint;$health=Invoke-RestMethod -Uri ('http://127.0.0.1:'+$endpoint.apiPort+'/health') -TimeoutSec 2;return $health.status -eq 'UP' -and $health.installationId -eq $endpoint.installationId}catch{return $false}
}
function Invoke-QiyeProgram([string]$File,[string[]]$Arguments,[int]$Timeout=120) {
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=$File;$info.Arguments=Join-QiyeArguments $Arguments;$info.WorkingDirectory=$script:QiyeRoot
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $process=New-Object Diagnostics.Process;$process.StartInfo=$info
    try{
        $process.Start() | Out-Null;$output=$process.StandardOutput.ReadToEndAsync();$errorText=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit($Timeout*1000)){try{$process.Kill()}catch{};throw 'A local component timed out. See logs and retry.'}
        [Threading.Tasks.Task]::WaitAll(@($output,$errorText))
        return [pscustomobject]@{Code=$process.ExitCode;Output=$output.Result;Error=$errorText.Result}
    }finally{$process.Dispose()}
}
function Expand-QiyeArchive([string]$Archive,[string]$Target) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $prefix=[IO.Path]::GetFullPath($Target).TrimEnd('\')+'\'
    $zip=[IO.Compression.ZipFile]::OpenRead($Archive)
    try{foreach($entry in $zip.Entries){
        $dest=[IO.Path]::GetFullPath((Join-Path $Target $entry.FullName))
        if(-not $dest.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Archive entry is outside the installation directory'}
        if($entry.FullName.EndsWith('/')){[IO.Directory]::CreateDirectory($dest) | Out-Null;continue}
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest)) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$dest,$true)
    }}finally{$zip.Dispose()}
}
