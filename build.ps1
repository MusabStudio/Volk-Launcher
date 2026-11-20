$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "C# compiler (csc.exe) not found. Install .NET Framework 4.x." }

$out = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$sources = Get-ChildItem (Join-Path $root "src\*.cs") | ForEach-Object { $_.FullName }

$refs = @(
  "/nologo",
  "/target:winexe",
  "/platform:anycpu",
  "/optimize+",
  "/warn:0",
  "/win32manifest:src\app.manifest",
  "/r:System.dll",
  "/r:System.Core.dll",
  "/r:System.Windows.Forms.dll",
  "/r:System.Drawing.dll",
  "/r:System.Web.Extensions.dll",
  "/r:System.IO.Compression.dll",
  "/r:System.IO.Compression.FileSystem.dll",
  "/r:System.Management.dll"
)

# --- 1. small build (downloads Java when needed) ---
& $csc ($refs + @("/out:$out\VolkLauncher.exe") + $sources)
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Write-Host ("Built dist\VolkLauncher.exe  ({0:N0} bytes)" -f (Get-Item "$out\VolkLauncher.exe").Length)

# --- 2. offline build (Java 8 + Java 21 embedded) ---
$jre8  = Join-Path $root "assets\jre8.zip"
$jre21 = Join-Path $root "assets\jre21.zip"
if ((Test-Path $jre8) -and (Test-Path $jre21)) {
  & $csc ($refs + @(
      "/out:$out\VolkLauncher-offline.exe",
      "/resource:$jre8,Jre8",
      "/resource:$jre21,Jre21"
    ) + $sources)
  if ($LASTEXITCODE -ne 0) { throw "Offline build failed ($LASTEXITCODE)" }
  Write-Host ("Built dist\VolkLauncher-offline.exe  ({0:N0} bytes)" -f (Get-Item "$out\VolkLauncher-offline.exe").Length)
} else {
  Write-Host "Skipping offline build (assets\jre8.zip / jre21.zip missing)"
}
