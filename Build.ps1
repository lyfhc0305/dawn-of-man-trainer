param([string]$GamePath = '..\DawnOfMan')
$ErrorActionPreference = 'Stop'
if ([IO.Path]::IsPathRooted($GamePath) -and [IO.Path]::GetPathRoot($GamePath).Length -lt 3) { throw '请使用相对脚本的路径，或包含完整根目录的绝对路径。' }
if (-not [IO.Path]::IsPathRooted($GamePath)) { $GamePath = Join-Path $PSScriptRoot $GamePath }
$GamePath = [IO.Path]::GetFullPath($GamePath)
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$managedPath = Join-Path $GamePath 'DawnOfMan_Data\Managed'
$references = @('mscorlib','System','System.Core','netstandard','Assembly-CSharp','UnityEngine','UnityEngine.CoreModule','UnityEngine.IMGUIModule','UnityEngine.InputLegacyModule','UnityEngine.PhysicsModule','UnityEngine.TextRenderingModule','UnityEngine.ImageConversionModule') | ForEach-Object { '/reference:' + (Join-Path $managedPath ($PSItem + '.dll')) }
$sourceFiles = @('Trainer.cs','TrainerPolicy.cs','TrainerView.cs','TrainerUi.cs','TrainerData.cs','TrainerFeatures.cs','TrainerFeatureView.cs','TrainerMultipliers.cs','IntegrationChecks.cs','IntegrationFeatureChecks.cs','IntegrationMultiplierChecks.cs') | ForEach-Object { Join-Path (Join-Path $PSScriptRoot 'src') $PSItem }
& $compilerPath /nologo /noconfig /target:library /optimize+ /nostdlib+ /codepage:65001 ('/out:' + (Join-Path $PSScriptRoot 'DawnOfManTrainer.dll')) $references $sourceFiles
if ($LASTEXITCODE -ne 0) { throw '修改器编译失败' }
$installerSources = @('Installer.cs','InstallerHost.cs','GameDiscovery.cs','InstallerWindow.cs') | ForEach-Object { Join-Path (Join-Path $PSScriptRoot 'src') $PSItem }
$installerResources = @{
    'Mono.Cecil.dll' = 'Mono.Cecil.dll'
    'DawnOfManTrainer.dll' = 'DawnOfManTrainer.dll'
    '使用说明.txt' = 'Guide.txt'
    'Mono.Cecil.LICENSE.txt' = 'License.txt'
}.GetEnumerator() | ForEach-Object { '/resource:' + (Join-Path $PSScriptRoot $PSItem.Key) + ',DawnTrainer.Bundle.' + $PSItem.Value }
& $compilerPath /nologo /target:winexe /main:InstallerHost /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ('/win32manifest:' + (Join-Path $PSScriptRoot 'src\Installer.manifest')) ('/reference:' + (Join-Path $PSScriptRoot 'Mono.Cecil.dll')) ('/out:' + (Join-Path $PSScriptRoot 'Installer.exe')) $installerResources $installerSources
if ($LASTEXITCODE -ne 0) { throw '安装器编译失败' }
Write-Host '构建完成。'
