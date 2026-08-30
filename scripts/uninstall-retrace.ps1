$ErrorActionPreference = "SilentlyContinue"
Get-Process -Name "Retrace" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 750
& taskkill.exe /F /IM Retrace.exe /T 2>$null | Out-Null

$installRoot = Join-Path $env:LOCALAPPDATA "Programs\Retrace"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Retrace.lnk"
$startMenuShortcut = Join-Path (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs") "Retrace.lnk"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

Remove-ItemProperty -Path $runKey -Name "Retrace" -ErrorAction SilentlyContinue
Remove-Item $desktopShortcut -Force -ErrorAction SilentlyContinue
Remove-Item $startMenuShortcut -Force -ErrorAction SilentlyContinue
Remove-Item $installRoot -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Retrace application files were removed."
Write-Host "Your local Retrace history under $env:LOCALAPPDATA\Retrace was left in place."
