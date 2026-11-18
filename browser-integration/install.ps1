param([switch]$Uninstall)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$name='com.mycelview.tabs'
$keys=@("Software\Google\Chrome\NativeMessagingHosts\$name","Software\Microsoft\Edge\NativeMessagingHosts\$name","Software\Naver\Naver Whale\NativeMessagingHosts\$name")
if($Uninstall){
 foreach($key in $keys){[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($key,$false)}
 Write-Output 'Mycelview browser connection registration removed.';return
}
$dotnet=Join-Path $root '.tools/dotnet/dotnet.exe'
$output=Join-Path $PSScriptRoot 'host'
$env:DOTNET_CLI_HOME=Join-Path $root '.tools/cli-home'
& $dotnet publish (Join-Path $root 'src/BrowserHost/BrowserHost.csproj') -c Release -o $output --nologo
if($LASTEXITCODE -ne 0){throw 'Browser host publish failed'}
$id=(Get-Content (Join-Path $root 'browser-extension/extension-id.txt') -Raw).Trim()
Copy-Item (Join-Path $root 'browser-extension/extension-id.txt') (Join-Path $output 'extension-id.txt')
$manifest=Join-Path $PSScriptRoot 'native-host.json'
@{name=$name;description='Mycelview browser tab connection';path=(Join-Path $output 'Mycelview.BrowserHost.exe');type='stdio';allowed_origins=@("chrome-extension://$id/","whale-extension://$id/")} | ConvertTo-Json | Set-Content -LiteralPath $manifest -Encoding utf8
foreach($key in $keys){
 $reg=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($key)
 try{$reg.SetValue('',$manifest,[Microsoft.Win32.RegistryValueKind]::String)}finally{$reg.Dispose()}
}
Write-Output "Installed for Chrome, Edge and Whale. Load unpacked extension: $(Join-Path $root 'browser-extension')"