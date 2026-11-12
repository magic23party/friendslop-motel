# Скриншоты окружения: powershell -ExecutionPolicy Bypass -File tools/shots.ps1 -Label before [-ShotFile путь] [-Wait секунд смены до первого кадра]
# Обычный запуск exe (не batchmode), окно 1280x720; exe сам выходит. PNG -> Docs/shots/<Label>/, FPS -> Logs/shot.log
param([string]$Label = 'after', [string]$ShotFile = '', [int]$TimeoutSec = 150, [int]$Wait = 0)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'Build\Friendslop.exe'
$out = Join-Path $root ("Docs\shots\" + $Label)
$log = Join-Path $root 'Logs\shot.log'
New-Item -ItemType Directory -Force $out | Out-Null
New-Item -ItemType Directory -Force (Join-Path $root 'Logs') | Out-Null
if (Test-Path $log) { Remove-Item $log -Force }
Get-ChildItem $out -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force
$args = @('-screen-width','1280','-screen-height','720','-screen-fullscreen','0','-shot',"`"$out`"",'-logFile',"`"$log`"")
if ($Wait -gt 0) { $args += @('-shotwait',"$Wait") }
if ($ShotFile -ne '') { $args += @('-shotfile',"`"$ShotFile`"") }
$p = Start-Process -FilePath $exe -PassThru -ArgumentList $args
if (-not $p.WaitForExit($TimeoutSec * 1000)) { $p.Kill(); Write-Output 'SHOTS TIMEOUT' }
if (Test-Path $log) { Select-String -Path $log -Pattern '\[SHOT\]','Exception','error' | ForEach-Object { Write-Output $_.Line } }
Write-Output ("PNG: " + (Get-ChildItem $out -Filter *.png | Measure-Object).Count + " in " + $out)
