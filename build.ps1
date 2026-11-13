param([switch]$Test)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$dotnetExe = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $dotnetExe)) { $dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source }
& $dotnetExe restore (Join-Path $PSScriptRoot 'src\Mycelview\Mycelview.csproj') --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $dotnetExe build (Join-Path $PSScriptRoot 'src\Mycelview\Mycelview.csproj') -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Test) {
    $testDir = Join-Path $PSScriptRoot ('artifacts\tests-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $dll = Join-Path $PSScriptRoot 'src\Mycelview\bin\Release\net10.0-windows\Mycelview.dll'
    & $dotnetExe $dll --self-test $testDir
    if ($LASTEXITCODE -ne 0) { throw "Smoke test failed. See $testDir" }
    & $dotnetExe $dll --verify-test $testDir
    if ($LASTEXITCODE -ne 0) { throw "Restart test failed. See $testDir" }
    Get-Content -LiteralPath (Join-Path $testDir 'test-results.txt'), (Join-Path $testDir 'restart-results.txt')
    Write-Host "Test artifacts: $testDir"
}
