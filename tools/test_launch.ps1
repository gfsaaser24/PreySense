$proc = Start-Process -FilePath ".\PreySense.Frontend\bin\Debug\net10.0-windows\PreySense.Frontend.exe" -PassThru
Start-Sleep -Seconds 3
if (-not $proc.HasExited) {
    Write-Host "LAUNCH_SUCCESSFUL: PID $($proc.Id)"
    Stop-Process -Id $proc.Id -Force
} else {
    Write-Host "PROCESS_EXITED_CODE: $($proc.ExitCode)"
}
