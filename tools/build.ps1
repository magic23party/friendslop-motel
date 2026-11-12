$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe'
$log = Join-Path $root 'Logs\build.log'
New-Item -ItemType Directory -Force (Join-Path $root 'Logs') | Out-Null
if (Test-Path $log) { Remove-Item $log -Force }
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode','-nographics','-projectPath',"`"$root`"",
  '-executeMethod','Friendslop.EditorTools.Builder.BuildWindows',
  '-logFile',"`"$log`"",'-quit')
$exe = Join-Path $root 'Build\Friendslop.exe'
if ((Test-Path $exe) -and (Select-String -Path $log -Pattern '\[BUILD OK\]' -Quiet) -and $p.ExitCode -eq 0) { Write-Output "BUILD OK: $exe"; exit 0 }
Write-Output 'BUILD FAIL'
Select-String -Path $log -Pattern 'error CS\d+','^[A-Za-z\.]*Exception[:\s]' | Select-Object -First 30 | ForEach-Object { Write-Output $_.Line }
exit 1
