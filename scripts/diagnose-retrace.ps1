$ErrorActionPreference = "Continue"
$logDir = Join-Path $env:LOCALAPPDATA "Retrace"
$activeFile = Join-Path $logDir "active-install.txt"
$log = Join-Path $logDir "retrace.log"
$exe = $null

if (Test-Path $activeFile) {
    $candidate = (Get-Content $activeFile -ErrorAction SilentlyContinue | Select-Object -First 1)
    if ($candidate -and (Test-Path $candidate)) { $exe = $candidate }
}

if (-not $exe) {
    $installRoot = Join-Path $env:LOCALAPPDATA "Programs\Retrace"
    $candidate = Get-ChildItem -Path $installRoot -Filter Retrace.exe -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($candidate) { $exe = $candidate.FullName }
}

Write-Host "========================================"
Write-Host "       RETRACE STARTUP DIAGNOSTIC"
Write-Host "========================================"
Write-Host ""
Write-Host "Executable: $exe"
if (-not $exe -or -not (Test-Path $exe)) {
    Write-Host "ERROR: No installed Retrace.exe could be found." -ForegroundColor Red
    exit 1
}

Write-Host "Existing Retrace processes:"
@(Get-Process -Name Retrace -ErrorAction SilentlyContinue) | ForEach-Object {
    Write-Host ("  PID {0} - started {1}" -f $_.Id, $_.StartTime)
}

Write-Host ""
Write-Host "Starting active Retrace build..."
$p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -PassThru
Start-Sleep -Seconds 4
$p.Refresh()
if ($p.HasExited) {
    Write-Host "Retrace exited during startup. Exit code: $($p.ExitCode)" -ForegroundColor Red
} else {
    Write-Host "Retrace is still running after 4 seconds (PID $($p.Id))." -ForegroundColor Green
}

Write-Host ""
if (Test-Path $log) {
    Write-Host "Last Retrace log entries:" -ForegroundColor Cyan
    Get-Content $log -Tail 100
} else {
    Write-Host "No retrace.log exists yet." -ForegroundColor Yellow
}
