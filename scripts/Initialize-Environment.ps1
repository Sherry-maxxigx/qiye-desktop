param([string]$ArchiveCache='', [switch]$SkipSystemComponents)
. (Join-Path $PSScriptRoot 'Common.ps1')
if($QiyeRoot -match '[^\x00-\x7F]'){throw 'Choose an English installation path, for example E:\Qiye. MySQL does not support this Unicode path.'}
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
foreach($directory in @('data','config','logs','temp','tools','.downloads')){[IO.Directory]::CreateDirectory((Join-Path $QiyeRoot $directory)) | Out-Null}
$runtimeLock=Get-QiyeLock
function Get-QiyeDownload([string]$Url,[string]$Path) {
    $client=New-Object Net.WebClient
    try{$client.Headers['User-Agent']='Qiye-Installer/1.0';$client.DownloadFile($Url,$Path)}finally{$client.Dispose()}
}
foreach($role in @('java','mysql')){
    $component=$runtimeLock.$role
    $binary=if($role -eq 'java'){'java.exe'}else{'mysqld.exe'}
    if(Test-Path -LiteralPath (Join-Path $QiyeRoot ('tools\'+$component.folder+'\bin\'+$binary))){continue}
    Write-Output ('Preparing '+$role+' '+$component.version+'...')
    $archive=Join-Path $QiyeRoot ('.downloads\'+$component.file)
    if($ArchiveCache -and (Test-Path -LiteralPath (Join-Path $ArchiveCache $component.file))){$archive=Join-Path $ArchiveCache $component.file}
    if(-not(Test-Path -LiteralPath $archive)){
        Write-Output ('Downloading '+$role+' ('+[math]::Round($component.size/1MB)+' MB). First installation only...')
        $partial=$archive+'.partial'
        Get-QiyeDownload $component.url $partial
        if((Get-QiyeSha256 $partial) -ne $component.sha256){throw 'Downloaded runtime checksum mismatch. Retry from the official source.'}
        [IO.File]::Copy($partial,$archive,$true)
        [IO.File]::Delete($partial)
    }
    if((Get-QiyeSha256 $archive) -ne $component.sha256){throw 'Runtime archive checksum mismatch. Remove the damaged archive and retry.'}
    Write-Output ('Extracting '+$role+'...')
    Expand-QiyeArchive $archive (Join-Path $QiyeRoot 'tools')
}
if(-not $SkipSystemComponents){
    $systemRoot=[Environment]::GetFolderPath('System')
    if(-not(Test-Path (Join-Path $systemRoot 'vcruntime140_1.dll'))){
        Write-Output 'Preparing Microsoft Visual C++ Runtime (Windows may request permission)...'
        $redist=Join-Path $QiyeRoot '.downloads\vc_redist.x64.exe'
        Get-QiyeDownload 'https://aka.ms/vs/17/release/vc_redist.x64.exe' $redist
        Assert-QiyeMicrosoftInstaller $redist
        $setup=Start-Process -FilePath $redist -ArgumentList '/install /quiet /norestart' -Verb RunAs -WindowStyle Hidden -Wait -PassThru
        if($setup.ExitCode -notin @(0,3010,1638)){throw 'Visual C++ runtime installation failed. Install it from Microsoft and retry.'}
    }
    $webviewReady=$false
    try{Add-Type -Path (Join-Path $QiyeRoot 'desktop\Microsoft.Web.WebView2.Core.dll');$webviewReady=-not[string]::IsNullOrEmpty([Microsoft.Web.WebView2.Core.CoreWebView2Environment]::GetAvailableBrowserVersionString())}catch{}
    if(-not $webviewReady){
        Write-Output 'Preparing Microsoft Edge WebView2 Runtime...'
        $webview=Join-Path $QiyeRoot '.downloads\WebView2Setup.exe'
        Get-QiyeDownload 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' $webview
        Assert-QiyeMicrosoftInstaller $webview
        $setup=Start-Process -FilePath $webview -ArgumentList '/silent /install' -WindowStyle Hidden -Wait -PassThru
        if($setup.ExitCode -notin @(0,3010)){throw 'WebView2 runtime installation failed. Install it from Microsoft and retry.'}
    }
}
$endpointFile=Join-Path $QiyeRoot 'data\service-endpoint.json'
if(-not(Test-Path -LiteralPath $endpointFile)){
    $endpoint=@{apiPort=(Get-QiyeFreePort 3030 3099);databasePort=(Get-QiyeFreePort 3307 3399);installationId=[guid]::NewGuid().ToString()}
    Write-QiyeFile $endpointFile ($endpoint | ConvertTo-Json)
}
$endpoint=Get-QiyeEndpoint
$credentialFile=Join-Path $QiyeRoot 'config\database.json'
if(-not(Test-Path -LiteralPath $credentialFile)){
    $bytes=New-Object byte[] 32;$rng=[Security.Cryptography.RandomNumberGenerator]::Create();try{$rng.GetBytes($bytes)}finally{$rng.Dispose()}
    Write-QiyeFile $credentialFile (@{password=([BitConverter]::ToString($bytes).Replace('-','').ToLowerInvariant())} | ConvertTo-Json)
}
$credential=Get-Content -LiteralPath $credentialFile -Raw -Encoding UTF8 | ConvertFrom-Json
if($credential.password -notmatch '^[a-f0-9]{64}$'){throw 'Local database configuration is invalid. Restore your config backup before continuing.'}
$mysqlRoot=(Join-Path $QiyeRoot ('tools\'+$runtimeLock.mysql.folder)).Replace('\','/')
$baseForward=$QiyeRoot.Replace('\','/')
Write-QiyeFile (Join-Path $QiyeRoot 'config\my.ini') @"
[mysqld]
basedir="$mysqlRoot"
datadir="$baseForward/data/mysql"
port=$($endpoint.databasePort)
bind-address=127.0.0.1
mysqlx=0
skip-name-resolve
character-set-server=utf8mb4
collation-server=utf8mb4_unicode_ci
innodb-buffer-pool-size=64M
log-error="$baseForward/logs/mysql-error.log"
"@
Write-QiyeFile (Join-Path $QiyeRoot 'config\mysql-admin.cnf') @"
[client]
user=root
password=$($credential.password)
host=127.0.0.1
port=$($endpoint.databasePort)
protocol=tcp
"@
$template=Get-Content -LiteralPath (Join-Path $QiyeRoot 'config\application-local.example.yml') -Raw -Encoding UTF8
$template=$template.Replace('__API_PORT__',[string]$endpoint.apiPort).Replace('__DB_PORT__',[string]$endpoint.databasePort).Replace('__DB_PASSWORD__',$credential.password).Replace('__INSTALLATION_ID__',$endpoint.installationId)
if(-not(Test-Path -LiteralPath (Join-Path $QiyeRoot 'config\application-local.yml'))){Write-QiyeFile (Join-Path $QiyeRoot 'config\application-local.yml') $template}
$mysqld=Join-Path $QiyeRoot ('tools\'+$runtimeLock.mysql.folder+'\bin\mysqld.exe')
if(-not(Test-Path -LiteralPath (Join-Path $QiyeRoot 'data\mysql\mysql'))){
    Write-Output 'Creating a new, empty local database...'
    $result=Invoke-QiyeProgram $mysqld @(('--defaults-file='+((Join-Path $QiyeRoot 'config\my.ini').Replace('\','/'))),'--initialize-insecure','--console') 180
    if($result.Code -ne 0){throw 'Database initialization failed. See logs/mysql-error.log.'}
}
# Secure both local account variants before the server accepts connections.
# This also handles interrupted first setup without relying on reverse DNS.
$databaseBootstrap=@"
ALTER USER 'root'@'localhost' IDENTIFIED BY '$($credential.password)';
CREATE USER IF NOT EXISTS 'root'@'127.0.0.1' IDENTIFIED BY '$($credential.password)';
ALTER USER 'root'@'127.0.0.1' IDENTIFIED BY '$($credential.password)';
GRANT ALL PRIVILEGES ON *.* TO 'root'@'127.0.0.1' WITH GRANT OPTION;
"@
Write-QiyeFile (Join-Path $QiyeRoot 'config\database-bootstrap.sql') $databaseBootstrap
$initLine='init-file="'+$baseForward+'/config/database-bootstrap.sql"'
[IO.File]::AppendAllText((Join-Path $QiyeRoot 'config\my.ini'),("`r`n"+$initLine+"`r`n"),$QiyeUtf8)
Write-Output 'Runtime environment is ready.'
