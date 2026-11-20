$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" }

$out = Join-Path $root "dist"
$jre8  = Join-Path $root "assets\jre8.zip"
$jre21 = Join-Path $root "assets\jre21.zip"
$lnch  = Join-Path $out "VolkLauncher.exe"

foreach ($f in @($jre8, $jre21, $lnch)) {
  if (-not (Test-Path $f)) { throw "Missing $f - run build.ps1 first" }
}

# csc cannot handle ",Name" resource suffixes on paths containing spaces - stage them
$stage = Join-Path $env:TEMP "volkres"
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$rL = Join-Path $stage "launcher.exe"
$r8 = Join-Path $stage "jre8.zip"
$r21 = Join-Path $stage "jre21.zip"
Copy-Item $lnch $rL -Force
Copy-Item $jre8 $r8 -Force
Copy-Item $jre21 $r21 -Force

$sources = @(
  (Join-Path $root "src\Core.cs"),
  (Join-Path $root "src\Game.cs"),
  (Join-Path $root "src\Fabric.cs"),
  (Join-Path $root "src\JavaBoot.cs"),
  (Join-Path $root "installer\Setup.cs")
)

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /warn:0 `
  /win32manifest:src\app.manifest `
  /out:$out\VolkSetup.exe `
  /resource:$rL,Launcher `
  /resource:$r8,Jre8 `
  /resource:$r21,Jre21 `
  /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
  /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
  /r:System.Management.dll `
  $sources

if ($LASTEXITCODE -ne 0) { throw "Installer build failed ($LASTEXITCODE)" }
Write-Host ("Built dist\VolkSetup.exe  ({0:N0} bytes)" -f (Get-Item "$out\VolkSetup.exe").Length)


