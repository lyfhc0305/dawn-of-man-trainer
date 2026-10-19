param([string]$GamePath = 'D:\Program Files (x86)\Steam\steamapps\common\DawnOfMan')
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$managedPath = Join-Path $GamePath 'DawnOfMan_Data\Managed'
$references = @('mscorlib','System','System.Core','netstandard','Assembly-CSharp','UnityEngine','UnityEngine.CoreModule','UnityEngine.IMGUIModule','UnityEngine.InputLegacyModule','UnityEngine.PhysicsModule','UnityEngine.TextRenderingModule') | ForEach-Object { '/reference:' + (Join-Path $managedPath ($PSItem + '.dll')) }
& $compilerPath /nologo /noconfig /target:library /optimize+ /nostdlib+ /codepage:65001 ('/out:' + (Join-Path $PSScriptRoot 'DawnOfManTrainer.dll')) $references (Join-Path $PSScriptRoot 'src\Trainer.cs') (Join-Path $PSScriptRoot 'src\TrainerPolicy.cs') (Join-Path $PSScriptRoot 'src\TrainerView.cs') (Join-Path $PSScriptRoot 'src\TrainerUi.cs') (Join-Path $PSScriptRoot 'src\TrainerData.cs') (Join-Path $PSScriptRoot 'src\TrainerFeatures.cs') (Join-Path $PSScriptRoot 'src\TrainerFeatureView.cs') (Join-Path $PSScriptRoot 'src\IntegrationChecks.cs') (Join-Path $PSScriptRoot 'src\IntegrationFeatureChecks.cs')
if ($LASTEXITCODE -ne 0) { throw '修改器编译失败' }
& $compilerPath /nologo /target:exe /optimize+ /codepage:65001 ('/reference:' + (Join-Path $PSScriptRoot 'Mono.Cecil.dll')) ('/out:' + (Join-Path $PSScriptRoot 'Installer.exe')) (Join-Path $PSScriptRoot 'src\Installer.cs')
if ($LASTEXITCODE -ne 0) { throw '安装器编译失败' }
Write-Host '构建完成。'
