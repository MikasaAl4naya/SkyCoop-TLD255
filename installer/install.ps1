# Sky Co-op для The Long Dark 2.55: копирует MelonLoader и файлы мода в папку игры.
# Запуск: двойной клик по install.bat или  powershell -ExecutionPolicy Bypass -File install.ps1 [-GameDir <папка>]
param([string]$GameDir)
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-SteamGame {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -EA SilentlyContinue).SteamPath
    if (-not $steam) { return $null }
    $libs = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        Select-String $vdf -Pattern '"path"\s+"([^"]+)"' | % { $libs += $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
    }
    foreach ($l in $libs) {
        $p = Join-Path $l 'steamapps\common\TheLongDark'
        if (Test-Path (Join-Path $p 'tld.exe')) { return $p }
    }
    return $null
}

Write-Host '=== Установка Sky Co-op (TLD 2.55) ===' -ForegroundColor Cyan
if (-not $GameDir) {
    $found = Find-SteamGame
    Write-Host 'Рекомендуется ставить мод в КОПИЮ папки игры, а не в Steam-папку'
    Write-Host '(Steam может обновить игру и сломать мод).'
    if ($found) { Write-Host "Найдена игра в Steam: $found" }
    $GameDir = Read-Host 'Путь к папке игры (где лежит tld.exe). Enter = найденная Steam-папка'
    if (-not $GameDir) { $GameDir = $found }
}
$GameDir = $GameDir.Trim('"', ' ')
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'tld.exe'))) {
    Write-Host "В папке '$GameDir' нет tld.exe. Установка отменена." -ForegroundColor Red
    Read-Host 'Enter для выхода'; exit 1
}
if (Get-Process tld -EA SilentlyContinue) {
    Write-Host 'Сначала закройте игру.' -ForegroundColor Red
    Read-Host 'Enter для выхода'; exit 1
}

Write-Host "Копирую файлы в $GameDir ..."
foreach ($item in 'winhttp.dll', 'MelonLoader', 'Mods', 'UserLibs') {
    $from = Join-Path $src $item
    if (-not (Test-Path $from)) { Write-Host "Нет $item рядом с установщиком — распакуйте архив целиком." -ForegroundColor Red; Read-Host 'Enter для выхода'; exit 1 }
    Copy-Item $from $GameDir -Recurse -Force
}
foreach ($doc in 'README_RU.txt', 'SkyCoop_LICENSE.txt') {
    if (Test-Path (Join-Path $src $doc)) { Copy-Item (Join-Path $src $doc) $GameDir -Force }
}

Write-Host ''
Write-Host 'Готово.' -ForegroundColor Green
Write-Host '1. Запустите Steam.'
Write-Host '2. Запустите tld.exe из папки игры. Первый запуск MelonLoader занимает 1–3 минуты (нужен интернет).'
Write-Host '3. В главном меню появится кнопка MULTIPLAYER. Подробности в README_RU.txt.'
Write-Host 'Удаление мода: удалите из папки игры winhttp.dll и папки MelonLoader, Mods, UserLibs, UserData.'
Read-Host 'Enter для выхода'
