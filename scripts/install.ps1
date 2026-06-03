$ErrorActionPreference = "Stop"

$installRoot = Join-Path $env:LOCALAPPDATA "AgentIsland"
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null

$sourceExe = Join-Path $PSScriptRoot "AgentIsland.exe"
$sourceUninstall = Join-Path $PSScriptRoot "uninstall.ps1"
$targetExe = Join-Path $installRoot "AgentIsland.exe"
$targetUninstall = Join-Path $installRoot "uninstall.ps1"
$targetAssets = Join-Path $installRoot "assets"

Get-Process AgentIsland -ErrorAction SilentlyContinue | Where-Object {
  try { $_.Path -eq $targetExe } catch { $false }
} | Stop-Process -Force
Start-Sleep -Milliseconds 300

Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force
Copy-Item -LiteralPath $sourceUninstall -Destination $targetUninstall -Force
New-Item -ItemType Directory -Force -Path $targetAssets | Out-Null
foreach ($asset in @("codex-openai.ico", "claude-code.ico", "icon.ico")) {
  $sourceAsset = Join-Path $PSScriptRoot $asset
  if (Test-Path -LiteralPath $sourceAsset) {
    Copy-Item -LiteralPath $sourceAsset -Destination (Join-Path $targetAssets $asset) -Force
  }
}

& $targetExe --install-support | Out-Null

$connectCodex = Join-Path $installRoot "connect-codex.ps1"
if (Test-Path -LiteralPath $connectCodex) {
  try {
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File $connectCodex -Quiet
  } catch {
    Write-Warning "Agent Island installed, but Codex hook connection failed: $($_.Exception.Message)"
  }
}


$connectClaude = Join-Path $installRoot "connect-claude.ps1"
if (Test-Path -LiteralPath $connectClaude) {
  try {
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File $connectClaude -Quiet
  } catch {
    Write-Warning "Agent Island installed, but Claude Code hook connection failed: $($_.Exception.Message)"
  }
}
$shell = New-Object -ComObject WScript.Shell
$desktop = [Environment]::GetFolderPath("DesktopDirectory")
$programs = [Environment]::GetFolderPath("Programs")
$startup = [Environment]::GetFolderPath("Startup")

function New-AgentIslandShortcut {
  param([string]$Path)
  $shortcut = $shell.CreateShortcut($Path)
  $shortcut.TargetPath = $targetExe
  $shortcut.WorkingDirectory = $installRoot
  $shortcut.Description = "Agent Island floating status window"
  $shortcut.Save()
}

New-AgentIslandShortcut (Join-Path $desktop "Agent Island.lnk")
New-AgentIslandShortcut (Join-Path $programs "Agent Island.lnk")
New-AgentIslandShortcut (Join-Path $startup "Agent Island.lnk")

$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AgentIsland"
New-Item -Path $uninstallKey -Force | Out-Null
$uninstallCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$targetUninstall`""
$assetSize = 0
if (Test-Path -LiteralPath $targetAssets) {
  $assetSize = (Get-ChildItem -Path $targetAssets -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
}
$estimatedSize = [Math]::Max(1, [Math]::Ceiling(((Get-Item -LiteralPath $targetExe).Length + $assetSize) / 1KB))
New-ItemProperty -Path $uninstallKey -Name DisplayName -Value "Agent Island" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value "0.1.0" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name Publisher -Value "Agent Island" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installRoot -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value $targetExe -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name UninstallString -Value $uninstallCommand -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name QuietUninstallString -Value $uninstallCommand -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name EstimatedSize -Value $estimatedSize -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

Start-Process -FilePath $targetExe -WorkingDirectory $installRoot
