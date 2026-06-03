$ErrorActionPreference = "SilentlyContinue"

$installRoot = Join-Path $env:LOCALAPPDATA "AgentIsland"
$targetExe = Join-Path $installRoot "AgentIsland.exe"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AgentIsland"

Get-Process AgentIsland -ErrorAction SilentlyContinue | Where-Object {
  try { $_.Path -eq $targetExe } catch { $false }
} | Stop-Process -Force

$desktop = [Environment]::GetFolderPath("DesktopDirectory")
$programs = [Environment]::GetFolderPath("Programs")
$startup = [Environment]::GetFolderPath("Startup")
@(
  (Join-Path $desktop "Agent Island.lnk"),
  (Join-Path $programs "Agent Island.lnk"),
  (Join-Path $startup "Agent Island.lnk")
) | ForEach-Object {
  if (Test-Path -LiteralPath $_) {
    Remove-Item -LiteralPath $_ -Force
  }
}

function Test-AgentIslandGroup {
  param([object]$Group)

  foreach ($handler in @($Group.hooks)) {
    if ($handler.statusMessage -eq "Agent Island" -or [string]$handler.command -like "*AgentIsland*hook.ps1*") {
      return $true
    }
  }

  return $false
}

$hooksFile = Join-Path $env:USERPROFILE ".codex\hooks.json"
if (Test-Path -LiteralPath $hooksFile) {
  try {
    $raw = [System.IO.File]::ReadAllText($hooksFile, [System.Text.Encoding]::UTF8)
    if (-not [string]::IsNullOrWhiteSpace($raw)) {
      $root = $raw | ConvertFrom-Json
      if ($null -ne $root.hooks) {
        $backup = $hooksFile + "." + (Get-Date -Format "yyyyMMdd-HHmmss") + ".agent-island-uninstall.bak"
        Copy-Item -LiteralPath $hooksFile -Destination $backup -Force

        foreach ($prop in @($root.hooks.PSObject.Properties)) {
          $kept = @()
          foreach ($group in @($prop.Value)) {
            if (-not (Test-AgentIslandGroup $group)) {
              $kept += $group
            }
          }

          if ($kept.Count -eq 0) {
            $root.hooks.PSObject.Properties.Remove($prop.Name)
          } else {
            $prop.Value = $kept
          }
        }

        $json = $root | ConvertTo-Json -Depth 40
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($hooksFile, $json + [Environment]::NewLine, $utf8NoBom)
      }
    }
  } catch {
  }
}


$claudeSettingsFile = Join-Path $env:USERPROFILE ".claude\settings.json"
if (Test-Path -LiteralPath $claudeSettingsFile) {
  try {
    $raw = [System.IO.File]::ReadAllText($claudeSettingsFile, [System.Text.Encoding]::UTF8)
    if (-not [string]::IsNullOrWhiteSpace($raw)) {
      $root = $raw | ConvertFrom-Json
      if ($null -ne $root.hooks) {
        $backup = $claudeSettingsFile + "." + (Get-Date -Format "yyyyMMdd-HHmmss") + ".agent-island-uninstall.bak"
        Copy-Item -LiteralPath $claudeSettingsFile -Destination $backup -Force

        foreach ($prop in @($root.hooks.PSObject.Properties)) {
          $kept = @()
          foreach ($group in @($prop.Value)) {
            if (-not (Test-AgentIslandGroup $group)) {
              $kept += $group
            }
          }

          if ($kept.Count -eq 0) {
            $root.hooks.PSObject.Properties.Remove($prop.Name)
          } else {
            $prop.Value = $kept
          }
        }

        $json = $root | ConvertTo-Json -Depth 50
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($claudeSettingsFile, $json + [Environment]::NewLine, $utf8NoBom)
      }
    }
  } catch {
  }
}
Remove-Item -LiteralPath $uninstallKey -Recurse -Force

$cleanup = Join-Path $env:TEMP ("AgentIslandCleanup-" + [Guid]::NewGuid().ToString("N") + ".ps1")
$installRootLiteral = $installRoot.Replace("'", "''")
$cleanupScript = @"
Start-Sleep -Milliseconds 700
Remove-Item -LiteralPath '$installRootLiteral' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath `$MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
"@
[System.IO.File]::WriteAllText($cleanup, $cleanupScript, [System.Text.Encoding]::UTF8)
Start-Process -FilePath "powershell.exe" -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $cleanup) -WindowStyle Hidden
