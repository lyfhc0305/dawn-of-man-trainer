param(
    [string]$GamePath = '..\DawnOfMan',
    [string]$WorkPath = (Join-Path $env:TEMP ('DawnTrainer-setup-checks-' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
if (-not [IO.Path]::IsPathRooted($GamePath)) { $GamePath = Join-Path $PSScriptRoot $GamePath }
$GamePath = [IO.Path]::GetFullPath($GamePath)
$work = [IO.Path]::GetFullPath($WorkPath)
if (Test-Path -LiteralPath $work) { throw '请选择尚不存在的临时测试目录。' }
New-Item -ItemType Directory -Path $work | Out-Null
$script:passed = 0
$initialExtractedFolders = @(Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Filter 'DawnTrainer-setup-*' | ForEach-Object { $PSItem.FullName })
function Check([bool]$condition, [string]$label) {
    if (-not $condition) { throw ('FAIL ' + $label) }
    $script:passed++
    Write-Output ('PASS ' + $label)
}
function FileHash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
function RunSetup([string[]]$arguments, [int]$expected = 0) {
    $script:runNumber++
    $stdout = Join-Path $work ('stdout-' + $script:runNumber + '.txt')
    $stderr = Join-Path $work ('stderr-' + $script:runNumber + '.txt')
    $quoted = @($arguments | ForEach-Object { if ($PSItem.Contains('"')) { throw '测试参数包含非法引号。' }; '"' + $PSItem + '"' })
    $process = Start-Process -FilePath $script:standalone -ArgumentList $quoted -WorkingDirectory $work -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $script:lastOutput = [IO.File]::ReadAllText($stdout, [Text.Encoding]::UTF8) + [IO.File]::ReadAllText($stderr, [Text.Encoding]::UTF8)
    if ($process.ExitCode -ne $expected) { Write-Output $script:lastOutput }
    Check ($process.ExitCode -eq $expected) ('standalone exit ' + $expected + ': ' + ($arguments -join ' '))
}
$script:runNumber = 0
$standaloneFolder = Join-Path $work '仅有安装器 中文路径'
New-Item -ItemType Directory -Path $standaloneFolder | Out-Null
$script:standalone = Join-Path $standaloneFolder '人类黎明修改器安装器.exe'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Installer.exe') -Destination $script:standalone
Check (@(Get-ChildItem -LiteralPath $standaloneFolder -File).Count -eq 1) 'standalone folder contains only EXE'
$bytes = [IO.File]::ReadAllBytes($script:standalone)
$peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
Check ([BitConverter]::ToUInt16($bytes, $peOffset + 24 + 68) -eq 2) 'PE uses Windows GUI subsystem (no console on double-click)'
$metadata = [Reflection.Assembly]::Load($bytes)
$resources = @($metadata.GetManifestResourceNames())
Check ($resources -contains 'DawnTrainer.Bundle.Mono.Cecil.dll') 'Mono.Cecil embedded'
Check ($resources -contains 'DawnTrainer.Bundle.DawnOfManTrainer.dll') 'trainer embedded'
Check ($resources -contains 'DawnTrainer.Bundle.Guide.txt') 'guide embedded'
Check ($resources -contains 'DawnTrainer.Bundle.License.txt') 'third-party license embedded'
RunSetup @('--help')
Check ($script:lastOutput.Contains('图形安装界面')) 'standalone help writes UTF-8 output'
RunSetup @('--unknown') 1
Check ($script:lastOutput.Contains('未知选项')) 'unknown option rejected'
$sandbox = Join-Path $work '测试游戏 Dawn of Man'
$managed = Join-Path $sandbox 'DawnOfMan_Data\Managed'
New-Item -ItemType Directory -Path $managed | Out-Null
Copy-Item -Path (Join-Path $GamePath 'DawnOfMan_Data\Managed\*.dll') -Destination $managed
$sourceOriginal = Join-Path $GamePath 'DawnTrainer\Assembly-CSharp.dll.original'
if (-not (Test-Path -LiteralPath $sourceOriginal)) { $sourceOriginal = Join-Path $GamePath 'DawnOfMan_Data\Managed\Assembly-CSharp.dll' }
$assembly = Join-Path $managed 'Assembly-CSharp.dll'
Copy-Item -LiteralPath $sourceOriginal -Destination $assembly -Force
$plugin = Join-Path $managed 'DawnOfManTrainer.dll'
if (Test-Path -LiteralPath $plugin) { Remove-Item -LiteralPath $plugin }
[IO.File]::WriteAllText((Join-Path $sandbox 'DawnOfMan.exe'), 'Sandbox marker; not executable.')
$originalHash = FileHash $assembly
$realAssembly = Join-Path $GamePath 'DawnOfMan_Data\Managed\Assembly-CSharp.dll'
$realBefore = FileHash $realAssembly
RunSetup @('--verify', $sandbox)
Check ((FileHash $assembly) -eq $originalHash) 'pre-install verify does not change game assembly'
Check (-not (Test-Path -LiteralPath (Join-Path $sandbox 'DawnTrainer'))) 'pre-install verify creates no state directory'
RunSetup @($sandbox)
$installedHash = FileHash $assembly
Check ($installedHash -ne $originalHash) 'install patches sandbox assembly'
Check ((FileHash (Join-Path $sandbox 'DawnTrainer\Assembly-CSharp.dll.original')) -eq $originalHash) 'install preserves original backup'
Check ((FileHash $plugin) -eq (FileHash (Join-Path $PSScriptRoot 'DawnOfManTrainer.dll'))) 'installed plugin equals embedded current build'
Add-Type -Path (Join-Path $PSScriptRoot 'Mono.Cecil.dll')
$patchedAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($assembly)
try {
    foreach ($patchedType in $patchedAssembly.MainModule.Types) {
        foreach ($patchedMethod in $patchedType.Methods) {
            if (-not $patchedMethod.HasBody) { continue }
            $hookCalls = @($patchedMethod.Body.Instructions | Where-Object {
                $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'DawnTrainer.Trainer'
            })
            if ($hookCalls.Count -eq 0) { continue }
            $invalidBranches = @($patchedMethod.Body.Instructions | Where-Object {
                ($_.OpCode.OperandType.ToString() -in @('InlineBrTarget','ShortInlineBrTarget')) -and
                ($null -eq $_.Operand -or -not $patchedMethod.Body.Instructions.Contains($_.Operand))
            })
            Check ($invalidBranches.Count -eq 0) ('serialized hook branches are valid: ' + $patchedMethod.FullName)
        }
    }
} finally { $patchedAssembly.Dispose() }
RunSetup @('--verify', $sandbox)
Check ($script:lastOutput.Contains('安装状态')) 'installed verify checks backup and hooks'
RunSetup @($sandbox)
Check ((FileHash $assembly) -eq $installedHash) 'reinstall is deterministic'
RunSetup @('--uninstall', $sandbox)
Check ((FileHash $assembly) -eq $originalHash) 'uninstall restores exact original'
Check (-not (Test-Path -LiteralPath $plugin)) 'uninstall removes trainer plugin'
Check (Test-Path -LiteralPath (Join-Path $sandbox 'DawnTrainer\Assembly-CSharp.dll.original')) 'uninstall retains original backup'
RunSetup @('--verify', (Join-Path $work '不存在的游戏')) 1
Check ($script:lastOutput.Contains('找不到游戏程序集')) 'invalid game path produces readable failure'
Check ((FileHash $realAssembly) -eq $realBefore) 'real game assembly remains unchanged'
Check (@(Get-ChildItem -LiteralPath $standaloneFolder -File).Count -eq 1) 'setup never requires adjacent payload DLLs'
$remainingExtractedFolders = @(Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Filter 'DawnTrainer-setup-*' | Where-Object { $initialExtractedFolders -notcontains $PSItem.FullName })
Check ($remainingExtractedFolders.Count -eq 0) 'temporary extracted payloads cleaned after successful and failed operations'
Write-Output ('Setup checks: ' + $script:passed + ' passed, 0 failed. Sandbox: ' + $work)
