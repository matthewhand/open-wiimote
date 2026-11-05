# Local verification for Windows, or PowerShell 7 on Linux/macOS.
# Install .NET 10 SDK, Node, Python 3, npm dependencies and Chromium first.
$ErrorActionPreference = 'Stop'
function Invoke-Checked {
    param([string]$Executable, [string[]]$CommandArgs)
    & $Executable @CommandArgs
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE"
    }
}
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    foreach ($tool in @('dotnet', 'node')) {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            throw "Missing prerequisite: $tool"
        }
    }
    $npxTool = Get-Command npx.cmd -ErrorAction SilentlyContinue
    if (-not $npxTool) { $npxTool = Get-Command npx -ErrorAction Stop }
    $pythonTool = Get-Command py -ErrorAction SilentlyContinue
    $pythonPrefix = @('-3')
    if (-not $pythonTool) {
        $pythonPrefix = @()
        $pythonTool = Get-Command python3 -ErrorAction SilentlyContinue
        if (-not $pythonTool) { $pythonTool = Get-Command python -ErrorAction Stop }
    }
    Write-Host '== build =='
    Invoke-Checked dotnet @('build', '--nologo', '-c', 'Release')
    Write-Host '== unit and window tests =='
    Invoke-Checked dotnet @('test', '--no-build', '--no-restore', '--nologo', '-c', 'Release', '-v', 'q')
    Write-Host '== calibration and pose math =='
    Invoke-Checked node @('scripts/test-math.cjs')
    Write-Host '== web UI tests =='
    Invoke-Checked $npxTool.Source @('--no-install', 'playwright', 'test')
    Write-Host '== report replay =='
    Invoke-Checked dotnet @('run', '--no-build', '--no-restore', '--project', 'src/OpenWiimote.App/OpenWiimote.App.csproj', '-c', 'Release', '--', '--replay', 'tools/sample-reports.txt')
    Write-Host '== license and notices =='
    Invoke-Checked $pythonTool.Source ($pythonPrefix + @('-m', 'unittest', 'discover', '-s', 'tests/licensing', '-p', 'test_*.py'))
    Write-Host '== all checks passed =='
}
finally { Pop-Location }
