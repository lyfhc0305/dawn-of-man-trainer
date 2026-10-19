param(
    [string]$GamePath = 'D:\Program Files (x86)\Steam\steamapps\common\DawnOfMan',
    [string]$WorkPath = (Join-Path $env:TEMP 'DawnOfManTrainer-offline-tests')
)
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testOutput = [IO.Path]::GetFullPath($WorkPath)
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Mono.Cecil.dll') -Destination (Join-Path $testOutput 'Mono.Cecil.dll') -Force
$testExe = Join-Path $testOutput 'RegressionChecks.exe'
& $compilerPath /nologo /target:exe /optimize+ /codepage:65001 ('/reference:' + (Join-Path $PSScriptRoot 'Mono.Cecil.dll')) ('/out:' + $testExe) (Join-Path $PSScriptRoot 'src\TrainerPolicy.cs') (Join-Path $PSScriptRoot 'src\TrainerData.cs') (Join-Path $PSScriptRoot 'tests\FeatureChecks.cs') (Join-Path $PSScriptRoot 'tests\RegressionChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed to compile' }
& $testExe (Join-Path $PSScriptRoot 'DawnOfManTrainer.dll') $GamePath
if ($LASTEXITCODE -ne 0) { throw 'Offline regression checks failed' }
$uiTestExe = Join-Path $testOutput 'UiPreview.exe'
& $compilerPath /nologo /target:exe /optimize+ /codepage:65001 /reference:System.Drawing.dll ('/out:' + $uiTestExe) (Join-Path $PSScriptRoot 'src\TrainerView.cs') (Join-Path $PSScriptRoot 'src\TrainerFeatureView.cs') (Join-Path $PSScriptRoot 'src\TrainerData.cs') (Join-Path $PSScriptRoot 'tests\UiPreview.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI checks failed to compile' }
& $uiTestExe --fixtures
if ($LASTEXITCODE -ne 0) { throw 'Offline UI checks failed' }
