param([string]$WorkPath = (Join-Path $env:TEMP ('DawnOfManTrainer-discovery-tests-' + [Guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework C# compiler was not found.' }
$testOutput = [IO.Path]::GetFullPath($WorkPath)
if (Test-Path -LiteralPath $testOutput) { throw 'Discovery tests require a new sandbox directory.' }
New-Item -ItemType Directory -Path $testOutput | Out-Null
$testExe = Join-Path $testOutput 'DiscoveryChecks.exe'
& $compilerPath /nologo /target:exe /langversion:5 /warn:4 /warnaserror+ /optimize+ /codepage:65001 /main:DiscoveryChecks ('/out:' + $testExe) (Join-Path $PSScriptRoot 'src\GameDiscovery.cs') (Join-Path $PSScriptRoot 'tests\DiscoveryChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Discovery checks failed to compile.' }
& $testExe (Join-Path $testOutput 'fixtures')
if ($LASTEXITCODE -ne 0) { throw 'Discovery checks failed.' }
