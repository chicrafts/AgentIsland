using System.IO;
using System.Text;

namespace AgentIsland
{
    public static class SupportFiles
    {
        public static void WriteAll()
        {
            AppPaths.EnsureDirectories();
            WriteIfChanged(AppPaths.HookScriptPath, HookScript);
            WriteIfChanged(Path.Combine(AppPaths.LocalRoot, "connect-codex.ps1"), ConnectCodexScript);
            WriteIfChanged(Path.Combine(AppPaths.LocalRoot, "connect-claude.ps1"), ConnectClaudeScript);
            WriteIfChanged(Path.Combine(AppPaths.ExamplesPath, "codex-hooks.example.toml"), CodexExample);
            WriteIfChanged(Path.Combine(AppPaths.ExamplesPath, "claude-settings.example.json"), ClaudeExample);
            WriteIfChanged(Path.Combine(AppPaths.LocalRoot, "README.txt"), Readme);
        }

        private static void WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == content)
            {
                return;
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private const string HookScript = @"param(
  [string]$Agent = ""agent"",
  [string]$Event = ""event""
)

$ErrorActionPreference = ""SilentlyContinue""
try {
  [Console]::InputEncoding = [System.Text.Encoding]::UTF8
  [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
} catch {}
$root = Join-Path $env:LOCALAPPDATA ""AgentIsland""
$events = Join-Path $root ""events.ndjson""
$last = Join-Path $root ""last-event.json""
New-Item -ItemType Directory -Force -Path $root | Out-Null

$raw = [Console]::In.ReadToEnd()
$payload = $null
if (-not [string]::IsNullOrWhiteSpace($raw)) {
  try {
    $payload = $raw | ConvertFrom-Json
  } catch {
    $payload = $raw
  }
}

function Get-AgentIslandConversationTitle {
  param([object]$Payload)

  $text = $null
  try {
    if ($null -ne $Payload -and $Payload.PSObject.Properties['prompt']) {
      $text = [string]$Payload.prompt
    } elseif ($Payload -is [string] -and $Payload -match '""prompt""\s*:\s*""(?<p>(?:\\.|[^""\\])*)""') {
      $text = [System.Text.RegularExpressions.Regex]::Unescape($Matches['p'])
    }
  } catch {}

  if ([string]::IsNullOrWhiteSpace($text)) {
    return $null
  }

  $text = ($text -replace '\s+', ' ').Trim()
  if ($text.Length -gt 42) {
    return $text.Substring(0, 39) + '...'
  }
  return $text
}

$conversationTitle = if ($Event -eq ""UserPromptSubmit"") { Get-AgentIslandConversationTitle $payload } else { $null }

$record = [ordered]@{
  v = 1
  agent = $Agent
  event = $Event
  ts = (Get-Date).ToUniversalTime().ToString(""o"")
  pid = $PID
  cwd = (Get-Location).Path
  conversation_name = $conversationTitle
  payload = $payload
}

$json = $record | ConvertTo-Json -Compress -Depth 80
$bytes = [System.Text.Encoding]::UTF8.GetBytes($json + [Environment]::NewLine)

for ($i = 0; $i -lt 6; $i++) {
  try {
    $stream = [System.IO.File]::Open($events, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite)
    try {
      $stream.Write($bytes, 0, $bytes.Length)
    } finally {
      $stream.Dispose()
    }
    break
  } catch {
    Start-Sleep -Milliseconds 35
  }
}

try {
  [System.IO.File]::WriteAllText($last, $json, [System.Text.Encoding]::UTF8)
} catch {}

if ($Agent -match '^(claude|claudecode)$' -and $Event -eq ""UserPromptSubmit"" -and -not [string]::IsNullOrWhiteSpace($conversationTitle)) {
  $hookOutput = [ordered]@{
    hookSpecificOutput = [ordered]@{
      hookEventName = ""UserPromptSubmit""
      sessionTitle = $conversationTitle
    }
  }
  $hookOutput | ConvertTo-Json -Compress -Depth 8
}
";

        private const string ConnectCodexScript = @"param(
  [switch]$Quiet
)

$ErrorActionPreference = ""Stop""

$codexHome = Join-Path $env:USERPROFILE "".codex""
$hooksFile = Join-Path $codexHome ""hooks.json""
$hook = Join-Path $env:LOCALAPPDATA ""AgentIsland\hook.ps1""

New-Item -ItemType Directory -Force -Path $codexHome | Out-Null

$events = @(
  @{ name = ""SessionStart""; matcher = ""startup|resume|clear|compact"" },
  @{ name = ""UserPromptSubmit""; matcher = $null },
  @{ name = ""PreToolUse""; matcher = ""*"" },
  @{ name = ""PostToolUse""; matcher = ""*"" },
  @{ name = ""PermissionRequest""; matcher = ""*"" },
  @{ name = ""PreCompact""; matcher = ""*"" },
  @{ name = ""PostCompact""; matcher = ""*"" },
  @{ name = ""SubagentStart""; matcher = ""*"" },
  @{ name = ""SubagentStop""; matcher = ""*"" },
  @{ name = ""Stop""; matcher = $null }
)

function New-AgentIslandGroup {
  param([string]$EventName, [object]$Matcher)

  $handler = [ordered]@{
    type = ""command""
    command = ""powershell.exe -NoProfile -ExecutionPolicy Bypass -File `""$hook`"" codex $EventName""
    timeout = 5
    statusMessage = ""Agent Island""
  }

  $group = [ordered]@{
    hooks = @($handler)
  }

  if ($null -ne $Matcher) {
    $group.matcher = $Matcher
  }

  return $group
}

function Test-AgentIslandGroup {
  param([object]$Group)

  foreach ($handler in @($Group.hooks)) {
    if ($handler.statusMessage -eq ""Agent Island"" -or [string]$handler.command -like ""*AgentIsland*hook.ps1*"") {
      return $true
    }
  }

  return $false
}

if (Test-Path $hooksFile) {
  $raw = [System.IO.File]::ReadAllText($hooksFile, [System.Text.Encoding]::UTF8)
  if ([string]::IsNullOrWhiteSpace($raw)) {
    $root = [pscustomobject]@{ hooks = [pscustomobject]@{} }
  } else {
    try {
      $root = $raw | ConvertFrom-Json
    } catch {
      throw ""Existing hooks.json is not valid JSON, leaving it untouched: $hooksFile""
    }
    if ($null -eq $root.hooks) {
      $root | Add-Member -MemberType NoteProperty -Name hooks -Value ([pscustomobject]@{})
    }
  }

  $backup = $hooksFile + ""."" + (Get-Date -Format ""yyyyMMdd-HHmmss"") + "".agent-island.bak""
  Copy-Item -LiteralPath $hooksFile -Destination $backup -Force
} else {
  $root = [pscustomobject]@{ hooks = [pscustomobject]@{} }
  $backup = $null
}

foreach ($event in $events) {
  $name = $event.name
  $group = New-AgentIslandGroup -EventName $name -Matcher $event.matcher
  $prop = $root.hooks.PSObject.Properties[$name]
  if ($null -eq $prop) {
    $root.hooks | Add-Member -MemberType NoteProperty -Name $name -Value @($group)
    continue
  }

  $kept = @()
  foreach ($existing in @($prop.Value)) {
    if (-not (Test-AgentIslandGroup $existing)) {
      $kept += $existing
    }
  }
  $kept += $group
  $prop.Value = $kept
}

$json = $root | ConvertTo-Json -Depth 40
$null = $json | ConvertFrom-Json
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($hooksFile, $json + [Environment]::NewLine, $utf8NoBom)

if (-not $Quiet) {
  Write-Host ""Agent Island hooks.json connected: $hooksFile""
  if ($backup) { Write-Host ""Backup: $backup"" }
  Write-Host ""Restart Codex, then run /hooks once to review and trust the Agent Island hook.""
}
";
        private const string ConnectClaudeScript = @"param(
  [switch]$Quiet
)

$ErrorActionPreference = ""Stop""

$claudeHome = Join-Path $env:USERPROFILE "".claude""
$settingsFile = Join-Path $claudeHome ""settings.json""
$hook = Join-Path $env:LOCALAPPDATA ""AgentIsland\hook.ps1""

New-Item -ItemType Directory -Force -Path $claudeHome | Out-Null

$events = @(
  @{ name = ""SessionStart""; matcher = $null },
  @{ name = ""UserPromptSubmit""; matcher = $null },
  @{ name = ""PreToolUse""; matcher = ""*"" },
  @{ name = ""PostToolUse""; matcher = ""*"" },
  @{ name = ""PostToolUseFailure""; matcher = $null },
  @{ name = ""PermissionRequest""; matcher = $null },
  @{ name = ""PermissionDenied""; matcher = $null },
  @{ name = ""Notification""; matcher = $null },
  @{ name = ""Elicitation""; matcher = $null },
  @{ name = ""ElicitationResult""; matcher = $null },
  @{ name = ""PreCompact""; matcher = $null },
  @{ name = ""PostCompact""; matcher = $null },
  @{ name = ""SubagentStart""; matcher = $null },
  @{ name = ""SubagentStop""; matcher = $null },
  @{ name = ""TaskCreated""; matcher = $null },
  @{ name = ""TaskCompleted""; matcher = $null },
  @{ name = ""Stop""; matcher = $null },
  @{ name = ""StopFailure""; matcher = $null },
  @{ name = ""SessionEnd""; matcher = $null }
)

function New-AgentIslandGroup {
  param([string]$EventName, [object]$Matcher)

  $handler = [ordered]@{
    type = ""command""
    command = ""powershell.exe -NoProfile -ExecutionPolicy Bypass -File `""$hook`"" claude $EventName""
    timeout = 5
  }

  $group = [ordered]@{
    hooks = @($handler)
  }

  if ($null -ne $Matcher) {
    $group.matcher = $Matcher
  }

  return $group
}

function Test-AgentIslandGroup {
  param([object]$Group)

  foreach ($handler in @($Group.hooks)) {
    if ([string]$handler.command -like ""*AgentIsland*hook.ps1*"") {
      return $true
    }
  }

  return $false
}

if (Test-Path $settingsFile) {
  $raw = [System.IO.File]::ReadAllText($settingsFile, [System.Text.Encoding]::UTF8)
  if ([string]::IsNullOrWhiteSpace($raw)) {
    $root = [pscustomobject]@{ hooks = [pscustomobject]@{} }
  } else {
    try {
      $root = $raw | ConvertFrom-Json
    } catch {
      throw ""Existing Claude settings.json is not valid JSON, leaving it untouched: $settingsFile""
    }
    if ($null -eq $root.hooks) {
      $root | Add-Member -MemberType NoteProperty -Name hooks -Value ([pscustomobject]@{})
    }
  }

  $backup = $settingsFile + ""."" + (Get-Date -Format ""yyyyMMdd-HHmmss"") + "".agent-island.bak""
  Copy-Item -LiteralPath $settingsFile -Destination $backup -Force
} else {
  $root = [pscustomobject]@{ hooks = [pscustomobject]@{} }
  $backup = $null
}

foreach ($event in $events) {
  $name = $event.name
  $group = New-AgentIslandGroup -EventName $name -Matcher $event.matcher
  $prop = $root.hooks.PSObject.Properties[$name]
  if ($null -eq $prop) {
    $root.hooks | Add-Member -MemberType NoteProperty -Name $name -Value @($group)
    continue
  }

  $kept = @()
  foreach ($existing in @($prop.Value)) {
    if (-not (Test-AgentIslandGroup $existing)) {
      $kept += $existing
    }
  }
  $kept += $group
  $prop.Value = $kept
}

$json = $root | ConvertTo-Json -Depth 50
$null = $json | ConvertFrom-Json
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($settingsFile, $json + [Environment]::NewLine, $utf8NoBom)

if (-not $Quiet) {
  Write-Host ""Agent Island Claude Code hooks connected: $settingsFile""
  if ($backup) { Write-Host ""Backup: $backup"" }
  Write-Host ""Restart Claude Code for hooks to take effect.""
}
";        private const string CodexExample = @"Agent Island now connects Codex through %USERPROFILE%\.codex\hooks.json.

Run once:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File %LOCALAPPDATA%\AgentIsland\connect-codex.ps1

Then restart Codex and run /hooks once to review and trust the Agent Island hook.

Supported Codex events used by the connector:
SessionStart, UserPromptSubmit, PreToolUse, PostToolUse, PermissionRequest, PreCompact, PostCompact, SubagentStart, SubagentStop, Stop.

The connector intentionally does not edit config.toml.
";
        private const string ClaudeExample = @"{
  ""hooks"": {
    ""UserPromptSubmit"": [
      {
        ""hooks"": [
          {
            ""type"": ""command"",
            ""command"": ""powershell.exe -NoProfile -ExecutionPolicy Bypass -File $env:LOCALAPPDATA\\AgentIsland\\hook.ps1 claude UserPromptSubmit""
          }
        ]
      }
    ],
    ""PreToolUse"": [
      {
        ""matcher"": ""*"",
        ""hooks"": [
          {
            ""type"": ""command"",
            ""command"": ""powershell.exe -NoProfile -ExecutionPolicy Bypass -File $env:LOCALAPPDATA\\AgentIsland\\hook.ps1 claude PreToolUse""
          }
        ]
      }
    ],
    ""PermissionRequest"": [
      {
        ""matcher"": ""*"",
        ""hooks"": [
          {
            ""type"": ""command"",
            ""command"": ""powershell.exe -NoProfile -ExecutionPolicy Bypass -File $env:LOCALAPPDATA\\AgentIsland\\hook.ps1 claude PermissionRequest""
          }
        ]
      }
    ],
    ""PermissionDenied"": [
      {
        ""matcher"": ""*"",
        ""hooks"": [
          {
            ""type"": ""command"",
            ""command"": ""powershell.exe -NoProfile -ExecutionPolicy Bypass -File $env:LOCALAPPDATA\\AgentIsland\\hook.ps1 claude PermissionDenied""
          }
        ]
      }
    ],
    ""Notification"": [
      {
        ""hooks"": [
          {
            ""type"": ""command"",
            ""command"": ""powershell.exe -NoProfile -ExecutionPolicy Bypass -File $env:LOCALAPPDATA\\AgentIsland\\hook.ps1 claude Notification""
          }
        ]
      }
    ],
    ""Elicitation"": [
      {
        ""hooks"": [
          {
            ""type"": ""command"",
            ""command"": ""powershell.exe -NoProfile -ExecutionPolicy Bypass -File $env:LOCALAPPDATA\\AgentIsland\\hook.ps1 claude Elicitation""
          }
        ]
      }
    ],
    ""ElicitationResult"": [
      {
        ""hooks"": [
          {
            ""type"": ""command"",
            ""command"": ""powershell.exe -NoProfile -ExecutionPolicy Bypass -File $env:LOCALAPPDATA\\AgentIsland\\hook.ps1 claude ElicitationResult""
          }
        ]
      }
    ],
    ""PostToolUse"": [
      {
        ""matcher"": ""*"",
        ""hooks"": [
          {
            ""type"": ""command"",
            ""command"": ""powershell.exe -NoProfile -ExecutionPolicy Bypass -File $env:LOCALAPPDATA\\AgentIsland\\hook.ps1 claude PostToolUse""
          }
        ]
      }
    ],
    ""Stop"": [
      {
        ""hooks"": [
          {
            ""type"": ""command"",
            ""command"": ""powershell.exe -NoProfile -ExecutionPolicy Bypass -File $env:LOCALAPPDATA\\AgentIsland\\hook.ps1 claude Stop""
          }
        ]
      }
    ]
  }
}
";

        private const string Readme = @"Agent Island

This floating window listens to hook events written by:
%LOCALAPPDATA%\AgentIsland\hook.ps1

Event log:
%LOCALAPPDATA%\AgentIsland\events.ndjson

Examples:
%LOCALAPPDATA%\AgentIsland\examples\codex-hooks.example.toml
%LOCALAPPDATA%\AgentIsland\examples\claude-settings.example.json

Codex is connected through connect-codex.ps1. Claude Code is connected through connect-claude.ps1. Both scripts back up and merge existing hook settings. Waiting reminders are driven by PermissionRequest, Notification, PermissionDenied, Elicitation, and ElicitationResult where supported.
";
    }
}
