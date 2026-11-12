# Скриншоты главного меню (с IMGUI): powershell -ExecutionPolicy Bypass -File tools/shot_menu.ps1 -Label p3
# Обычный запуск exe (не batchmode), окно 1280x720; exe сам выходит. PNG -> Docs/shots/<Label>/, лог -> Logs/shot_menu.log
param([string]$Label = 'p3', [int]$TimeoutSec = 90)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'Build\Friendslop.exe'
$out = Join-Path $root ("Docs\shots\" + $Label)
$log = Join-Path $root 'Logs\shot_menu.log'
New-Item -ItemType Directory -Force $out | Out-Null
New-Item -ItemType Directory -Force (Join-Path $root 'Logs') | Out-Null
if (Test-Path $log) { Remove-Item $log -Force }
Remove-Item (Join-Path $out 'menu_*.png') -Force -ErrorAction SilentlyContinue
$p = Start-Process -FilePath $exe -PassThru -ArgumentList @('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-shotmenu',"`"$out`"",'-logFile',"`"$log`"")
if (-not $p.WaitForExit($TimeoutSec * 1000)) { $p.Kill(); Write-Output 'MENU SHOTS TIMEOUT' }
if (Test-Path $log) { Select-String -Path $log -Pattern '\[SHOT\]','Exception','error' | ForEach-Object { Write-Output $_.Line } }
Write-Output ("PNG: " + (Get-ChildItem $out -Filter menu_*.png | Measure-Object).Count + " in " + $out)
