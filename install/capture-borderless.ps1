# Turns on Windows' privacy switch that lets this account's desktop programs capture a window
# without the yellow border Windows 11 draws around it (Settings > Privacy & security). GlazeWM's
# window animations snapshot each window as it starts to move; with the border on, every window
# flashes a yellow outline then. Switching it off again in Settings only brings the outline back.
# Safe to run again; Windows PowerShell 5.1 too.
$consent = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\graphicsCaptureWithoutBorder'
foreach ($key in $consent, "$consent\NonPackaged") {
    if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
    Set-ItemProperty -Path $key -Name Value -Value 'Allow'
}
