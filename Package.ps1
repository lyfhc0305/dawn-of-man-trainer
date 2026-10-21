param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'release\v2.0.0')
)
$ErrorActionPreference = 'Stop'
if (-not [IO.Path]::IsPathRooted($OutputPath)) { $OutputPath = Join-Path $PSScriptRoot $OutputPath }
$destination = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$releaseFiles = @(
    @{ Source = 'Installer.exe'; Name = '人类黎明修改器安装器.exe' },
    @{ Source = '使用说明.txt'; Name = '使用说明.txt' },
    @{ Source = 'Mono.Cecil.LICENSE.txt'; Name = 'Mono.Cecil.LICENSE.txt' }
)
$publishPaths = @()
foreach ($item in $releaseFiles) {
    $source = Join-Path $PSScriptRoot $item.Source
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw ('缺少文件，请先运行 Build.ps1：' + $item.Source) }
    $published = Join-Path $destination $item.Name
    Copy-Item -LiteralPath $source -Destination $published -Force
    $publishPaths += $published
}
$archive = Join-Path $destination 'DawnOfManTrainer-v2.0.0.zip'
Compress-Archive -LiteralPath $publishPaths -DestinationPath $archive -Force
Write-Output ('安装器与说明已打包至：' + $destination)
