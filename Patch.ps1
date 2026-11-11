[CmdletBinding()]
param(
    [ValidateSet('Install','Uninstall')][string]$Action = 'Install',
    [string]$GamePath
)
$ErrorActionPreference = 'Stop'
$build = '26531378'
$original = [ordered]@{
    'Assembly-CSharp.dll'   = '7D0D4A6BDC6707EFEDF203AFD0DA63018758EE042F8E29BEEA7D0B6778B7DB7B'
    'Unity.TextMeshPro.dll' = '87FEA8C37344842CF6AF4AFF6AA6978F0445AA7F389DD271B4F71E01FBF653AB'
    'UnityEngine.UI.dll'    = 'BE6AB48ECE53AF48FB6ED0D58BB92DB712CC8F827D0D9577F1DBF5D48F9DD3AA'
}
$payload = @('TakeoverChinese.dll', 'zh-CN.txt', 'labels.tsv')

function Find-SteamGame {
    $roots = @(
        (Get-ItemPropertyValue 'HKCU:\Software\Valve\Steam' SteamPath -ErrorAction SilentlyContinue)
        (Get-ItemPropertyValue 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' InstallPath -ErrorAction SilentlyContinue)
        (Get-ItemPropertyValue 'HKLM:\SOFTWARE\Valve\Steam' InstallPath -ErrorAction SilentlyContinue)
        (Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) Steam)
    ) | Where-Object { $_ } | Select-Object -Unique
    foreach ($root in $roots) {
        $libraries = @($root)
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if ([IO.File]::Exists($vdf)) {
            foreach ($entry in [regex]::Matches([IO.File]::ReadAllText($vdf), '"path"\s+"([^"]+)"')) {
                $libraries += $entry.Groups[1].Value.Replace('\\', '\')
            }
        }
        foreach ($library in $libraries) {
            $apps = Join-Path $library steamapps
            $manifest = Join-Path $apps appmanifest_4423270.acf
            if (-not [IO.File]::Exists($manifest)) { continue }
            if ([IO.File]::ReadAllText($manifest) -match '"installdir"\s+"([^"]+)"') {
                $found = Join-Path (Join-Path $apps common) $Matches[1]
                if ([IO.File]::Exists((Join-Path $found 'Takeover2.exe'))) { return $found }
            }
        }
    }
}

function Hash-File([string]$Path) {
    if ([IO.File]::Exists($Path)) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
    return ''
}

function Compile([string]$Output, [string]$Source, [string[]]$References, [switch]$Library) {
    $arguments = @('/nologo', '/codepage:65001', '/optimize+', "/out:$Output")
    if ($Library) { $arguments += @('/target:library', '/noconfig', '/nostdlib+') }
    foreach ($reference in $References) { $arguments += "/reference:$reference" }
    & $compiler @arguments $Source
    if ($LASTEXITCODE -ne 0 -or -not [IO.File]::Exists($Output)) { throw "编译失败：$Source" }
}

function Replace-File([string]$Source,[string]$Destination) {
    # A null third argument binds to an empty string in Windows PowerShell.
    [IO.File]::Replace($Source, $Destination, [NullString]::Value)
}

try {
    if (Get-Process -Name Takeover2 -ErrorAction SilentlyContinue) { throw '请先退出游戏。' }
    if (-not $GamePath) { $GamePath = Find-SteamGame }
    if (-not $GamePath) { $GamePath = (Read-Host '未找到 Steam 安装目录，请粘贴游戏文件夹的完整路径').Trim('"') }
    if (-not $GamePath) { throw '没有提供游戏文件夹。' }
    $game = (Resolve-Path -LiteralPath $GamePath).ProviderPath
    if (-not [IO.File]::Exists((Join-Path $game Takeover2.exe))) { throw '所选文件夹里没有 Takeover2.exe。' }
    $managed = Join-Path $game 'Takeover2_Data\Managed'
    $state = Join-Path $game '.takeover2-zh'
    $stateFile = Join-Path $state 'state.json'
    $backups = Join-Path $state 'original'

    if ($Action -eq 'Uninstall') {
        if (-not [IO.File]::Exists($stateFile)) { throw '找不到补丁备份。若曾手动安装，请在 Steam 验证游戏文件的完整性。' }
        $record = Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($record.build -ne $build) { throw '补丁版本不同，请用安装时附带的卸载脚本。' }
        foreach ($name in $original.Keys) {
            $current = Hash-File (Join-Path $managed $name)
            if ((Hash-File (Join-Path $backups $name)) -ne $original[$name] -or
                ($current -ne $original[$name] -and $current -ne $record.patched.$name)) {
                throw "备份或游戏文件已改变：$name。请在 Steam 验证游戏文件的完整性。"
            }
        }
        foreach ($name in $original.Keys) {
            $destination = Join-Path $managed $name
            if ((Hash-File $destination) -eq $original[$name]) { continue }
            $temporary = Join-Path $state "$name.restore"
            [IO.File]::Copy((Join-Path $backups $name), $temporary, $true)
            Replace-File $temporary $destination
        }
        foreach ($name in $payload) {
            $file = Join-Path $managed $name
            if ([IO.File]::Exists($file) -and (Hash-File $file) -eq $record.payload.$name) { [IO.File]::Delete($file) }
        }
        Remove-Item -LiteralPath $state -Recurse -Force
        Write-Host '已卸载简体中文补丁。'
        exit 0
    }

    if (Test-Path -LiteralPath $state) { throw '检测到已有补丁。要重新安装，请先运行 Uninstall.cmd。' }
    foreach ($name in $original.Keys) {
        if ((Hash-File (Join-Path $managed $name)) -ne $original[$name]) { throw "只支持 Steam Build $build 的原版文件。校验不匹配：$name。" }
    }
    foreach ($name in $payload) {
        if (Test-Path -LiteralPath (Join-Path $managed $name)) { throw "已有同名文件：$name。请先移除旧版补丁。" }
    }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not [IO.File]::Exists($compiler)) { throw '找不到 Windows 自带的 .NET Framework C# 编译器。' }
    $stage = Join-Path $game ('.takeover2-zh-stage-' + [guid]::NewGuid().ToString('N'))
    $changed = [System.Collections.Generic.List[string]]::new()
    $added = [System.Collections.Generic.List[string]]::new()
    $ownsState = $false
    try {
        $patched = Join-Path $stage patched
        [void][IO.Directory]::CreateDirectory($patched)
        $cecil = Join-Path $stage Mono.Cecil.dll
        [IO.File]::Copy((Join-Path $PSScriptRoot 'vendor\Mono.Cecil.dll'), $cecil)
        $helper = Join-Path $stage TakeoverChinese.dll
        $references = @('netstandard','mscorlib','System','System.Core','UnityEngine','UnityEngine.CoreModule','UnityEngine.UI','UnityEngine.UIModule','UnityEngine.ScreenCaptureModule','UnityEngine.TextCoreFontEngineModule','UnityEngine.TextCoreTextEngineModule','UnityEngine.TextRenderingModule','Unity.TextMeshPro') |
            ForEach-Object { Join-Path $managed "$_.dll" }
        Compile $helper (Join-Path $PSScriptRoot 'src\Plugin.cs') $references -Library
        $patcher = Join-Path $stage Patch.exe
        Compile $patcher (Join-Path $PSScriptRoot 'src\Patch.cs') @($cecil)
        & $patcher $managed $patched $helper $managed
        if ($LASTEXITCODE -ne 0) { throw '生成补丁失败，游戏文件未修改。' }
        $pistolPatcher = Join-Path $stage PatchPistol.exe
        Compile $pistolPatcher (Join-Path $PSScriptRoot 'src\PatchPistol.cs') @($cecil)
        $pistolOutput = Join-Path $stage 'Assembly-CSharp-pistol.dll'
        & $pistolPatcher (Join-Path $patched 'Assembly-CSharp.dll') (Join-Path $managed 'PlayMaker.dll') $pistolOutput
        if ($LASTEXITCODE -ne 0 -or -not [IO.File]::Exists($pistolOutput)) { throw '生成手枪弹药补丁失败，游戏文件未修改。' }
        [IO.File]::Copy($pistolOutput, (Join-Path $patched 'Assembly-CSharp.dll'), $true)
        foreach ($name in $payload | Where-Object { $_ -ne 'TakeoverChinese.dll' }) {
            [IO.File]::Copy((Join-Path $PSScriptRoot "translations\$name"), (Join-Path $stage $name))
        }
        if (Test-Path -LiteralPath $state) { throw '检测到已有补丁。' }
        [void][IO.Directory]::CreateDirectory($backups)
        $ownsState = $true
        $record = @{ build = $build; patched = @{}; payload = @{} }
        foreach ($name in $original.Keys) {
            $record.patched[$name] = Hash-File (Join-Path $patched $name)
            if (-not $record.patched[$name]) { throw "未生成 $name，游戏文件未修改。" }
            [IO.File]::Copy((Join-Path $managed $name), (Join-Path $backups $name))
            if ((Hash-File (Join-Path $backups $name)) -ne $original[$name]) { throw "安装时原版文件已改变：$name。" }
        }
        foreach ($name in $payload) { $record.payload[$name] = Hash-File (Join-Path $stage $name) }
        $record | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
        foreach ($name in $payload) {
            [IO.File]::Copy((Join-Path $stage $name), (Join-Path $managed $name))
            $added.Add($name)
        }
        foreach ($name in $original.Keys) {
            Replace-File (Join-Path $patched $name) (Join-Path $managed $name)
            $changed.Add($name)
        }
        Write-Host '安装成功。现在可以从 Steam 启动游戏。'
    } catch {
        foreach ($name in $changed) {
            $temporary = Join-Path $stage "$name.restore"
            [IO.File]::Copy((Join-Path $backups $name), $temporary)
            Replace-File $temporary (Join-Path $managed $name)
        }
        foreach ($name in $added) { [IO.File]::Delete((Join-Path $managed $name)) }
        if ($ownsState) { Remove-Item -LiteralPath $state -Recurse -Force }
        throw
    } finally {
        for ($attempt = 0; $attempt -lt 6 -and (Test-Path -LiteralPath $stage); $attempt++) {
            try { Remove-Item -LiteralPath $stage -Recurse -Force } catch { Start-Sleep -Milliseconds 250 }
        }
        if (Test-Path -LiteralPath $stage) { Write-Warning '临时目录未清理，可稍后删除。' }
    }
    exit 0
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    if ($_.Exception.GetBaseException() -is [UnauthorizedAccessException]) {
        Write-Host '游戏目录不可写时，请右键脚本，选择「以管理员身份运行」。'
    }
    exit 1
}
