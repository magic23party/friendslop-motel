$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe'
$log = Join-Path $root 'Logs\check.log'
New-Item -ItemType Directory -Force (Join-Path $root 'Logs') | Out-Null
if (Test-Path $log) { Remove-Item $log -Force }
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode','-nographics','-projectPath',"`"$root`"",
  '-executeMethod','Friendslop.EditorTools.Builder.BuildAll',
  '-logFile',"`"$log`"",'-quit')
$errs = @()
if (Test-Path $log) {
  $errs = @(Select-String -Path $log -Pattern 'error CS\d+','^[A-Za-z\.]*Exception[:\s]','Unable to resolve','Scripts have compiler errors' | ForEach-Object { $_.Line })
  $ok = Select-String -Path $log -Pattern '\[BUILDALL OK\]' -Quiet
} else { $ok = $false; $errs = @('no log') }
if ($ok -and $errs.Count -eq 0 -and $p.ExitCode -eq 0) { Write-Output 'CHECK OK'; exit 0 }
Write-Output 'CHECK FAIL'
if (-not $ok) { Write-Output '(no [BUILDALL OK] in log)' }
Write-Output ("exit code: " + $p.ExitCode)
$errs | Select-Object -First 30 | ForEach-Object { Write-Output $_ }
exit 1
