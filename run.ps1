$ErrorActionPreference = 'Stop'
$dotnetExe = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnetExe)) { $dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source }
$dll = Join-Path $PSScriptRoot 'src\PinShelf\bin\Release\net10.0-windows\PinShelf.dll'
if (!(Test-Path -LiteralPath $dll)) { & (Join-Path $PSScriptRoot 'build.ps1') }
$appExe = Join-Path $PSScriptRoot 'src\PinShelf\bin\Release\net10.0-windows\PinShelf.exe'
if (Test-Path -LiteralPath $appExe) {
    $env:DOTNET_ROOT = Split-Path $dotnetExe
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    Start-Process -FilePath $appExe -WindowStyle Hidden
} else {
    Start-Process -FilePath $dotnetExe -ArgumentList @(('"' + $dll + '"')) -WindowStyle Hidden
}
