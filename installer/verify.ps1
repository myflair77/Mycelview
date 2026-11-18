param([string]$Version='0.1.0')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$setup=Join-Path $root "artifacts/releases/Mycelview-Setup-$Version-win-x64.exe"
$testRoot=Join-Path $root ('artifacts/installer-verification-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$install=Join-Path $testRoot '설치 경로 with spaces'
New-Item -ItemType Directory -Force $testRoot | Out-Null
$keys=@('Software\Google\Chrome\NativeMessagingHosts\com.mycelview.tabs','Software\Microsoft\Edge\NativeMessagingHosts\com.mycelview.tabs','Software\Naver\Naver Whale\NativeMessagingHosts\com.mycelview.tabs')
$uninstallKey='Software\Microsoft\Windows\CurrentVersion\Uninstall\{924475B5-7DC8-4E58-8391-6FB868B792F3}_is1'
$existing=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($uninstallKey)
if($existing){$existing.Dispose();throw 'Existing installed release detected; do not overwrite it with a test installation.'}
$registryBackup=@{}
foreach($key in $keys){$reg=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($key);$registryBackup[$key]=if($reg){$reg.GetValue('')}else{$null};if($reg){$reg.Dispose()}}
function RunHidden([string]$exe,[string[]]$arguments){
 $process=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(55000)){throw "Timed out: $exe"}
 return $process.ExitCode
}
$running=Get-Process Mycelview -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $root 'src/Mycelview/bin/Release/net10.0-windows/Mycelview.exe')}
foreach($process in $running){$null=$process.CloseMainWindow();if(!$process.WaitForExit(10000)){throw 'Close Mycelview dialogs before verification.'}}
$settings=Join-Path $env:LOCALAPPDATA 'PinShelf/workspace.json'
$before=if(Test-Path $settings){(Get-FileHash $settings).Hash}else{''}
$oldRuntime=$env:DOTNET_ROOT;$oldRuntime64=$env:DOTNET_ROOT_X64
try{
 $env:DOTNET_ROOT=Join-Path $testRoot 'no-runtime'
 $env:DOTNET_ROOT_X64=$env:DOTNET_ROOT
 $installArguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/GROUP=Mycelview-Installer-Test','/TASKS=',('/DIR="'+$install+'"'),('/LOG="'+(Join-Path $testRoot 'install.log')+'"'))
 if((RunHidden $setup $installArguments) -ne 0){throw 'Installation failed'}
 $manifest=Join-Path $install 'browser-host/native-host.json'
 $data=Get-Content $manifest -Raw | ConvertFrom-Json
 if($data.path -ne (Join-Path $install 'browser-host/Mycelview.BrowserHost.exe')){throw 'Manifest path mismatch'}
 foreach($key in $keys){
  $reg=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($key)
  try{if($reg.GetValue('') -ne $manifest){throw "Registration failed: $key"}}finally{$reg.Dispose()}
 }
 if(!(Test-Path (Join-Path $install 'browser-extension/manifest.json')) -or !(Test-Path (Join-Path $install 'browser-guide.html'))){throw 'Extension or guide missing'}
 if((RunHidden (Join-Path $install 'browser-host/Mycelview.BrowserHost.exe') @('invalid-origin')) -ne 2){throw 'Standalone host runtime failed'}
 $probe=Join-Path $testRoot 'app-tests'
 if((RunHidden (Join-Path $install 'Mycelview.exe') @('--test-browser-tabs',('"'+$probe+'"'))) -ne 0){throw 'Standalone app tests failed'}
 if((RunHidden $setup $installArguments) -ne 0){throw 'Upgrade/reinstall failed'}
 $uninstall=Join-Path $install 'unins000.exe'
 if((RunHidden $uninstall @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $testRoot 'uninstall.log')+'"'))) -ne 0){throw 'Uninstall failed'}
 if(Test-Path (Join-Path $install 'Mycelview.exe')){throw 'App remains after uninstall'}
 foreach($key in $keys){$reg=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($key);if($reg){$reg.Dispose();throw 'Owned native-host registration remains after uninstall'}}
 $after=if(Test-Path $settings){(Get-FileHash $settings).Hash}else{''}
 if($before -ne $after){throw 'Existing workspace settings changed'}
 @('PASS: silent install','PASS: Chrome/Edge/Whale host registration','PASS: extension files and Korean guide','PASS: app and host without installed runtime','PASS: repeat install','PASS: uninstall','PASS: existing workspace.json SHA256 unchanged') | Set-Content (Join-Path $testRoot 'passed.txt')
 Get-Content (Join-Path $testRoot 'passed.txt')
}finally{
 $env:DOTNET_ROOT=$oldRuntime;$env:DOTNET_ROOT_X64=$oldRuntime64
 foreach($key in $keys){
  if($null -ne $registryBackup[$key]){$reg=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($key);try{$reg.SetValue('',$registryBackup[$key])}finally{$reg.Dispose()}}
  else{[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($key,$false)}
 }
 if($running){& (Join-Path $root 'run.ps1')}
}