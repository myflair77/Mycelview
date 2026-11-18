param([string]$Version='0.1.0')
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$'){throw 'Use a numeric release version'}
$root=Split-Path $PSScriptRoot
$dotnet=Join-Path $root '.tools/dotnet/dotnet.exe'
$compiler=Join-Path $root '.tools/inno643/tools/ISCC.exe'
if(!(Test-Path $compiler)){throw 'Install the signed Tools.InnoSetup 6.4.3 compiler in .tools/inno643 first. See installer/README.md'}
$stage=Join-Path $root ('artifacts/installer-stage-'+[Guid]::NewGuid().ToString('N'))
$env:DOTNET_CLI_HOME=Join-Path $root '.tools/cli-home'
& $dotnet publish (Join-Path $root 'src/Mycelview/Mycelview.csproj') -c Release -r win-x64 --self-contained true -p:UseSharedCompilation=false "-p:Version=$Version" -o (Join-Path $stage 'app') --nologo
if($LASTEXITCODE -ne 0){throw 'Application publish failed'}
& $dotnet publish (Join-Path $root 'src/BrowserHost/BrowserHost.csproj') -c Release -r win-x64 --self-contained true -p:UseSharedCompilation=false "-p:Version=$Version" -o (Join-Path $stage 'browser-host') --nologo
if($LASTEXITCODE -ne 0){throw 'Browser host publish failed'}
$licenses=Join-Path $stage 'app/licenses'
New-Item -ItemType Directory -Force $licenses | Out-Null
foreach($file in @('LICENSE.txt','ThirdPartyNotices.txt')){Copy-Item (Join-Path $root ".tools/dotnet/$file") (Join-Path $licenses $file)}
& $compiler "/DAppVersion=$Version" "/DStageDir=$stage" (Join-Path $PSScriptRoot 'Mycelview.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
$setup=Join-Path $root "artifacts/releases/Mycelview-Setup-$Version-win-x64.exe"
$hash=(Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($setup))" | Set-Content "$setup.sha256" -Encoding ascii
Write-Output $setup