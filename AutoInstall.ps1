[CmdletBinding()]
param([string]$GamePath)
$ErrorActionPreference = 'Stop'

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
        foreach ($library in ($libraries | Select-Object -Unique)) {
            $apps = Join-Path $library steamapps
            $manifest = Join-Path $apps 'appmanifest_4423270.acf'
            if (-not [IO.File]::Exists($manifest)) { continue }
            if ([IO.File]::ReadAllText($manifest) -match '"installdir"\s+"([^"]+)"') {
                $found = Join-Path (Join-Path $apps common) $Matches[1]
                if ([IO.File]::Exists((Join-Path $found 'Takeover2.exe'))) { return $found }
            }
        }
    }
}

try {
    if (Get-Process -Name Takeover2 -ErrorAction SilentlyContinue) { throw '请先退出游戏。' }
    if (-not $GamePath) { $GamePath = Find-SteamGame }
    if (-not $GamePath) { $GamePath = (Read-Host '未找到 Steam 安装目录，请粘贴游戏文件夹的完整路径').Trim('"') }
    if (-not $GamePath) { throw '没有提供游戏文件夹。' }
    $game = (Resolve-Path -LiteralPath $GamePath).ProviderPath
    if (-not [IO.File]::Exists((Join-Path $game 'Takeover2.exe'))) { throw '所选文件夹里没有 Takeover2.exe。' }

    $oldState = Join-Path $game '.takeover2-zh\state.json'
    if ([IO.File]::Exists($oldState)) {
        throw '检测到旧版硬补丁。请先运行 Uninstall.cmd，再运行本安装器。'
    }
    $runtime = Join-Path $PSScriptRoot runtime
    if (-not [IO.Directory]::Exists($runtime)) { throw '安装包缺少 runtime 文件夹。' }
    Copy-Item -Path (Join-Path $runtime '*') -Destination $game -Recurse -Force
    $marker = [ordered]@{ version = 'auto-1.0.0'; installed = (Get-Date).ToUniversalTime().ToString('o') }
    $marker | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $game '.takeover2-auto.json') -Encoding UTF8
    Write-Host '自动注入安装成功。以后从 Steam 启动游戏即可，通常不需要随 Steam 更新重新安装。'
    exit 0
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
