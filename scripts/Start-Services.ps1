. (Join-Path $PSScriptRoot 'Common.ps1')
if(Test-QiyeHealth){Write-Output 'Local services are ready.';return}
$endpoint=Get-QiyeEndpoint;$lock=Get-QiyeLock
$mysql=Join-Path $QiyeRoot ('tools\'+$lock.mysql.folder+'\bin\mysql.exe')
$clientConfig=Join-Path $QiyeRoot 'config\mysql-admin.cnf'
$database=Get-QiyeProcess 'mysql'
if(-not $database){
    if(-not(Test-QiyePort $endpoint.databasePort)){throw 'The local database port is occupied by another installation. Close that program and retry.'}
    Write-Output 'Starting the local database...'
    $mysqld=Join-Path $QiyeRoot ('tools\'+$lock.mysql.folder+'\bin\mysqld.exe')
    $database=Start-Process -FilePath $mysqld -ArgumentList (Join-QiyeArguments @(('--defaults-file='+((Join-Path $QiyeRoot 'config\my.ini').Replace('\','/'))))) -WorkingDirectory $QiyeRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $QiyeRoot 'logs\mysql-stdout.log') -RedirectStandardError (Join-Path $QiyeRoot 'logs\mysql-stderr.log') -PassThru
    Write-QiyeFile (Join-Path $QiyeRoot 'data\mysql-process.id') ([string]$database.Id)
}
$ready=$false
for($attempt=0;$attempt -lt 45;$attempt++){
    $check=Invoke-QiyeProgram $mysql @(('--defaults-extra-file='+$clientConfig),'--connect-timeout=2','--execute=SELECT 1;') 5
    if($check.Code -eq 0){$ready=$true;break}
    # Complete an interrupted first-time setup using only a fresh loopback root.
    $credential=Get-Content -LiteralPath (Join-Path $QiyeRoot 'config\database.json') -Raw | ConvertFrom-Json
    $secure=Invoke-QiyeProgram $mysql @('--no-defaults','--protocol=tcp','--host=127.0.0.1',('--port='+$endpoint.databasePort),'--user=root','--skip-password',('--execute=ALTER USER ''root''@''localhost'' IDENTIFIED BY '''+$credential.password+''';')) 5
    if($secure.Code -eq 0){continue}
    if($database.HasExited){throw 'The local database stopped. See logs/mysql-error.log.'}
    Start-Sleep -Milliseconds 400
}
if(-not $ready){throw 'The local database is not ready. See logs/mysql-error.log.'}
$schema=[IO.File]::ReadAllText((Join-Path $QiyeRoot 'backend\sql\schema.sql'))
$schemaRun=Invoke-QiyeProgram $mysql @(('--defaults-extra-file='+$clientConfig),'--default-character-set=utf8mb4',('--execute='+$schema)) 15
if($schemaRun.Code -ne 0){throw 'Local database schema could not be prepared.'}
$backend=Get-QiyeProcess 'app'
if(-not $backend){
    if(-not(Test-QiyePort $endpoint.apiPort)){throw 'The app service port belongs to another program. Close that program and retry.'}
    Write-Output 'Starting the app service (first startup can take about one minute)...'
    $java=Join-Path $QiyeRoot ('tools\'+$lock.java.folder+'\bin\java.exe');$forward=$QiyeRoot.Replace('\','/')
    $configUri=([Uri](Join-Path $QiyeRoot 'config\')).AbsoluteUri
    $args=@('-Xms64m','-Xmx512m','-Dfile.encoding=UTF-8',('-Duser.home='+$forward+'/data/java'),('-Djava.io.tmpdir='+$forward+'/temp'),'-jar',($forward+'/backend/app.jar'),'--spring.profiles.active=local',('--spring.config.additional-location='+$configUri))
    $backend=Start-Process -FilePath $java -ArgumentList (Join-QiyeArguments $args) -WorkingDirectory $QiyeRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $QiyeRoot 'logs\app-stdout.log') -RedirectStandardError (Join-Path $QiyeRoot 'logs\app-stderr.log') -PassThru
    Write-QiyeFile (Join-Path $QiyeRoot 'data\app-process.id') ([string]$backend.Id)
}
for($attempt=0;$attempt -lt 100;$attempt++){
    if(Test-QiyeHealth){Write-Output 'Local services are ready.';return}
    if($backend.HasExited){throw 'The app service stopped. See logs/app-stdout.log.'}
    Start-Sleep -Milliseconds 500
}
throw 'App service startup timed out. Retry or inspect logs/app-stdout.log.'
