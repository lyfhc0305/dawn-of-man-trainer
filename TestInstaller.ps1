param([string]$WorkPath = (Join-Path $env:TEMP ('DawnOfManTrainer-installer-tests-' + [Guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testOutput = [IO.Path]::GetFullPath($WorkPath)
if (Test-Path -LiteralPath $testOutput) { throw 'Installer tests require a new, empty sandbox directory.' }
New-Item -ItemType Directory -Path $testOutput | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Mono.Cecil.dll') -Destination (Join-Path $testOutput 'Mono.Cecil.dll')
$testExe = Join-Path $testOutput 'InstallerChecks.exe'
& $compilerPath /nologo /target:exe /optimize+ /codepage:65001 /define:INSTALLER_TESTS /main:InstallerChecks ('/reference:' + (Join-Path $PSScriptRoot 'Mono.Cecil.dll')) ('/out:' + $testExe) (Join-Path $PSScriptRoot 'src\Installer.cs') (Join-Path $PSScriptRoot 'tests\InstallerChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer checks failed to compile' }
& $testExe (Join-Path $testOutput 'sandboxes')
if ($LASTEXITCODE -ne 0) { throw 'Installer transaction checks failed' }
