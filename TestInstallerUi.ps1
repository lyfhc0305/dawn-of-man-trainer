param(
    [string]$GamePath = '',
    [string]$WorkPath = (Join-Path $env:TEMP ('DawnOfManTrainer-ui-tests-' + [Guid]::NewGuid().ToString('N'))),
    [switch]$CheckEnvironmentDiscovery
)
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework C# compiler was not found.' }
if ($GamePath) {
    if (-not [IO.Path]::IsPathRooted($GamePath)) { $GamePath = Join-Path $PSScriptRoot $GamePath }
    $GamePath = [IO.Path]::GetFullPath($GamePath).TrimEnd([IO.Path]::DirectorySeparatorChar)
}
if ($CheckEnvironmentDiscovery -and -not $GamePath) { throw 'Environment discovery requires -GamePath.' }
$testOutput = [IO.Path]::GetFullPath($WorkPath)
if (Test-Path -LiteralPath $testOutput) { throw 'Installer UI tests require a new sandbox directory.' }
New-Item -ItemType Directory -Path $testOutput | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Mono.Cecil.dll') -Destination (Join-Path $testOutput 'Mono.Cecil.dll')
$testExe = Join-Path $testOutput 'InstallerUiChecks.exe'
$sources = @('Installer.cs','InstallerHost.cs','GameDiscovery.cs','InstallerWindow.cs') | ForEach-Object { Join-Path (Join-Path $PSScriptRoot 'src') $PSItem }
$resources = @{
    'Mono.Cecil.dll' = 'Mono.Cecil.dll'
    'DawnOfManTrainer.dll' = 'DawnOfManTrainer.dll'
    '使用说明.txt' = 'Guide.txt'
    'Mono.Cecil.LICENSE.txt' = 'License.txt'
}.GetEnumerator() | ForEach-Object { '/resource:' + (Join-Path $PSScriptRoot $PSItem.Key) + ',DawnTrainer.Bundle.' + $PSItem.Value }
& $compilerPath /nologo /target:exe /langversion:5 /warn:4 /warnaserror+ /main:InstallerUiChecks /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ('/reference:' + (Join-Path $PSScriptRoot 'Mono.Cecil.dll')) ('/out:' + $testExe) $resources $sources (Join-Path $PSScriptRoot 'tests\InstallerUiChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer UI checks failed to compile.' }
$fixtures = Join-Path $testOutput 'fixtures'
$testProcess = New-Object System.Diagnostics.Process
$testProcess.StartInfo.FileName = $testExe
$testProcess.StartInfo.WorkingDirectory = $testOutput
$testProcess.StartInfo.Arguments = '"' + $fixtures + '" "' + $GamePath + '"'
if ($CheckEnvironmentDiscovery) { $testProcess.StartInfo.Arguments += ' --check-environment-discovery' }
$testProcess.StartInfo.UseShellExecute = $false
$testProcess.StartInfo.CreateNoWindow = $true
$testProcess.StartInfo.RedirectStandardOutput = $true
$testProcess.StartInfo.RedirectStandardError = $true
$testProcess.StartInfo.StandardOutputEncoding = [Text.Encoding]::UTF8
$testProcess.StartInfo.StandardErrorEncoding = [Text.Encoding]::UTF8
try {
    $testProcess.Start() | Out-Null
    if (-not $testProcess.WaitForExit(45000)) {
        $testProcess.Kill()
        throw 'Installer UI checks exceeded 45 seconds; the test process was stopped.'
    }
    $testStdout = $testProcess.StandardOutput.ReadToEnd()
    $testStderr = $testProcess.StandardError.ReadToEnd()
    if ($testStdout) { Write-Host $testStdout.TrimEnd() }
    if ($testStderr) { Write-Host $testStderr.TrimEnd() }
    if ($testProcess.ExitCode -ne 0) { throw 'Installer UI checks failed.' }
}
finally { $testProcess.Dispose() }
