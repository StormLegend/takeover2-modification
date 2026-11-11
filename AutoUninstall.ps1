[CmdletBinding()]
param([string]$GamePath)
$ErrorActionPreference = 'Stop'

try {
    if (Get-Process -Name Takeover2 -ErrorAction SilentlyContinue) { throw '请先退出游戏。' }
    if (-not $GamePath) { $GamePath = (Read-Host '请粘贴游戏文件夹的完整路径').Trim('"') }
    $game = (Resolve-Path -LiteralPath $GamePath).ProviderPath
    $marker = Join-Path $game '.takeover2-auto.json'
    if (-not [IO.File]::Exists($marker)) { throw '找不到自动注入安装记录。' }
    foreach ($relative in @(
        'winhttp.dll', '.doorstop_version', 'doorstop_config.ini',
        'BepInEx\patchers\Takeover2AutoPatcher.dll',
        'BepInEx\patchers\TakeoverChinese.dll',
        'BepInEx\patchers\zh-CN.txt',
        'BepInEx\patchers\labels.tsv'
    )) {
        $file = Join-Path $game $relative
        if ([IO.File]::Exists($file)) { Remove-Item -LiteralPath $file -Force }
    }
    Remove-Item -LiteralPath $marker -Force
    Write-Host '自动注入已卸载。BepInEx 其他文件已保留，避免影响其他模组。'
    exit 0
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
