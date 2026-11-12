# Дымовой прогон самотеста: powershell -ExecutionPolicy Bypass -File tools/smoke.ps1 -Test guesttest|fighttest
# Хост + клиент в batchmode, ждём "selftest OK (all done)" в логе хоста, затем гасим процессы.
param([string]$Test = 'guesttest', [int]$TimeoutSec = 300)
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'Build\Friendslop.exe'
$hostLog = Join-Path $root "Logs\smoke_$Test`_host.log"
$cliLog = Join-Path $root "Logs\smoke_$Test`_client.log"
Remove-Item $hostLog, $cliLog -Force -ErrorAction SilentlyContinue
$h = Start-Process $exe -PassThru -ArgumentList @('-batchmode','-nographics','-autohost','-autostart',"-$Test",'-nick','Host','-logFile',"`"$hostLog`"")
Start-Sleep 4
$c = Start-Process $exe -PassThru -ArgumentList @('-batchmode','-nographics','-autojoin','127.0.0.1','-nick','Client','-logFile',"`"$cliLog`"")
$t = 0; $done = $false
while ($t -lt $TimeoutSec -and -not $done) {
  Start-Sleep 3; $t += 3
  if (Test-Path $hostLog) { $done = [bool](Select-String -Path $hostLog -Pattern 'selftest (OK \(all done\)|FAIL count)' -Quiet) }
}
Start-Sleep 2
foreach ($p in @($h, $c)) { if ($p -and -not $p.HasExited) { $p.Kill() } }
$fails = @(Select-String -Path $hostLog, $cliLog -Pattern 'selftest FAIL','Exception','NullReference' | ForEach-Object { $_.Line })
$ok = Select-String -Path $hostLog -Pattern 'selftest OK \(all done\)' -Quiet
if ($ok -and $fails.Count -eq 0) { Write-Output "SMOKE $Test OK (${t}s)" } else { Write-Output "SMOKE $Test FAIL (done=$done)"; $fails | Select-Object -First 15 | ForEach-Object { Write-Output $_ } }
