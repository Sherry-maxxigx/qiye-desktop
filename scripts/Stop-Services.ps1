. (Join-Path $PSScriptRoot 'Common.ps1')
$app=Get-QiyeProcess 'app'
if($app){Stop-Process -Id $app.Id;$app.WaitForExit(10000) | Out-Null}
$database=Get-QiyeProcess 'mysql'
if($database){
    $lock=Get-QiyeLock;$admin=Join-Path $QiyeRoot ('tools\'+$lock.mysql.folder+'\bin\mysqladmin.exe')
    $result=Invoke-QiyeProgram $admin @(('--defaults-extra-file='+(Join-Path $QiyeRoot 'config\mysql-admin.cnf')),'--connect-timeout=3','shutdown') 20
    if($result.Code -ne 0){throw 'The local database did not shut down cleanly.'}
    $database.WaitForExit(20000) | Out-Null
}
Write-Output 'Local services have stopped. Personal data is preserved.'
