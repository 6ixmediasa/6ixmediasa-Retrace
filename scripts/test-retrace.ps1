$ErrorActionPreference = "Stop"
$test = Join-Path ([Environment]::GetFolderPath("Desktop")) "RetraceTest"
New-Item -ItemType Directory -Force -Path $test | Out-Null
$original = Join-Path $test "contract.txt"
$renamed = Join-Path $test "contract-final.txt"
"Original Retrace test content" | Set-Content $original
Start-Sleep -Seconds 2
Add-Content $original "Edited at $(Get-Date)"
Start-Sleep -Seconds 2
Rename-Item $original $renamed
Start-Sleep -Seconds 2
New-Item -ItemType Directory -Force -Path (Join-Path $test "Moved") | Out-Null
Move-Item $renamed (Join-Path $test "Moved\contract-final.txt")
Start-Sleep -Seconds 2
Remove-Item (Join-Path $test "Moved\contract-final.txt")
Write-Host "Test sequence complete. Open Retrace > Timeline, then Recover." -ForegroundColor Green
Write-Host "If Desktop is protected, you should see the events above." 
