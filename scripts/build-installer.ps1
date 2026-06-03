$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$root = Split-Path -Parent $repo
$publish = Join-Path $root "dist\publish"
$installer = Join-Path $root "dist\AgentIslandSetup.exe"
$sed = Join-Path $root "dist\agent-island.sed"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

function Resolve-GacAssembly {
  param(
    [string]$Name,
    [string]$PreferredBucket
  )

  $base = Join-Path $env:WINDIR "Microsoft.NET\assembly"
  $preferredRoot = Join-Path $base $PreferredBucket
  $preferred = Get-ChildItem -Path $preferredRoot -Recurse -Filter "$Name.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($preferred) {
    return $preferred.FullName
  }

  $fallback = Get-ChildItem -Path $base -Recurse -Filter "$Name.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($fallback) {
    return $fallback.FullName
  }

  throw "Cannot resolve $Name.dll from the .NET Framework GAC"
}

if (!(Test-Path $csc)) {
  throw "Cannot find .NET Framework compiler at $csc"
}

if (Test-Path $publish) {
  Remove-Item -LiteralPath $publish -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publish | Out-Null

$windowsBase = Resolve-GacAssembly "WindowsBase" "GAC_MSIL"
$presentationCore = Resolve-GacAssembly "PresentationCore" "GAC_64"
$presentationFramework = Resolve-GacAssembly "PresentationFramework" "GAC_MSIL"
$systemXaml = Resolve-GacAssembly "System.Xaml" "GAC_MSIL"

$icon = Join-Path $repo "assets\icon.ico"
if (!(Test-Path $icon)) {
  Write-Host "icon.ico missing, regenerating from scripts\generate-logo.ps1"
  & (Join-Path $repo "scripts\generate-logo.ps1")
}

$sources = Get-ChildItem -Path (Join-Path $repo "netfx") -Filter *.cs | ForEach-Object { $_.FullName }
$compilerArgs = @(
  "/nologo",
  "/codepage:65001",
  "/target:winexe",
  "/platform:x64",
  "/optimize+",
  "/win32manifest:`"$repo\app.manifest`"",
  "/win32icon:`"$icon`"",
  "/out:`"$publish\AgentIsland.exe`"",
  "/reference:System.dll",
  "/reference:System.Core.dll",
  "/reference:System.Drawing.dll",
  "/reference:System.Windows.Forms.dll",
  "/reference:$windowsBase",
  "/reference:$presentationCore",
  "/reference:$presentationFramework",
  "/reference:$systemXaml"
) + $sources

& $csc @compilerArgs
if ($LASTEXITCODE -ne 0) {
  throw "csc failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath (Join-Path $repo "scripts\install.ps1") -Destination (Join-Path $publish "install.ps1") -Force
Copy-Item -LiteralPath (Join-Path $repo "scripts\uninstall.ps1") -Destination (Join-Path $publish "uninstall.ps1") -Force
Copy-Item -LiteralPath (Join-Path $repo "assets\codex-openai.ico") -Destination (Join-Path $publish "codex-openai.ico") -Force
Copy-Item -LiteralPath (Join-Path $repo "assets\claude-code.ico") -Destination (Join-Path $publish "claude-code.ico") -Force
Copy-Item -LiteralPath $icon -Destination (Join-Path $publish "icon.ico") -Force

$publishEscaped = $publish.TrimEnd("\") + "\"
$sedContent = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=Agent Island installed.
TargetName=$installer
FriendlyName=Agent Island
AppLaunched=powershell.exe -NoProfile -ExecutionPolicy Bypass -File install.ps1
PostInstallCmd=<None>
AdminQuietInstCmd=powershell.exe -NoProfile -ExecutionPolicy Bypass -File install.ps1
UserQuietInstCmd=powershell.exe -NoProfile -ExecutionPolicy Bypass -File install.ps1
SourceFiles=SourceFiles
[Strings]
FILE0="AgentIsland.exe"
FILE1="install.ps1"
FILE2="uninstall.ps1"
FILE3="codex-openai.ico"
FILE4="claude-code.ico"
FILE5="icon.ico"
[SourceFiles]
SourceFiles0=$publishEscaped
[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
%FILE3%=
%FILE4%=
%FILE5%=
"@

Set-Content -LiteralPath $sed -Value $sedContent -Encoding ASCII
if (Test-Path $installer) {
  Remove-Item -LiteralPath $installer -Force
}

Push-Location $root
try {
  iexpress.exe /N /Q "dist\agent-island.sed"
} finally {
  Pop-Location
}

$deadline = (Get-Date).AddSeconds(10)
while (!(Test-Path $installer) -and (Get-Date) -lt $deadline) {
  Start-Sleep -Milliseconds 200
}

if (!(Test-Path $installer)) {
  throw "IExpress finished but did not create $installer"
}

Write-Host "Built $installer"
