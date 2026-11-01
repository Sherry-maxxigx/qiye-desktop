param([Parameter(Mandatory=$true)][string[]]$Files)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach($file in $Files){
 $zip=[IO.Compression.ZipFile]::OpenRead($file)
 $names=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
 try{foreach($entry in $zip.Entries){
  if($entry.FullName.Contains('\')){throw ('ZIP entry must use forward slashes: '+$entry.FullName)}
  $name=$entry.FullName.Replace('\','/')
  if(-not $names.Add($name)){throw ('Duplicate archive entry: '+$name)}
  if($name -match '(^|/)(data|logs|backups|cache|research|temp|tools|\.downloads|\.git)(/|$)' -or $name -match '(^|/)(database\.json|mysql-admin\.cnf|application-local\.yml|browser-.*\.json|course-list\.json|desktop-account\.txt)$'){
   throw ('Private/generated file was included: '+$name)
  }
  if($name -match '(^|/)\.\./' -or [IO.Path]::IsPathRooted($name)){throw ('Unsafe archive entry: '+$name)}
 }
 foreach($required in @('README.md','LICENSE','NOTICE')){if(-not $names.Contains($required)){throw ('Required public file is missing: '+$required)}}
 if([IO.Path]::GetFileName($file) -notlike '*-source.zip'){
  foreach($required in @('Qiye.exe','backend/app.jar','scripts/Common.ps1','desktop/StudyDesktop.exe','desktop/assets/qiye-icon.png','docs/images/first-start.png','config/application-local.example.yml')){
   if(-not $names.Contains($required)){throw ('Required app file is missing: '+$required)}
  }
 }
 }finally{$zip.Dispose()}
 Write-Output ('PASS public file allowlist: '+[IO.Path]::GetFileName($file))
}
