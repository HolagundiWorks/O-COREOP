# Deprecated: use uia-osp-takeoff.ps1 with a .osp project file.
param(
  [string]$Exe,
  [string]$Vdb,
  [string]$Osp
)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$ospPath = if ($Osp) { $Osp } elseif ($Vdb) { $Vdb -replace '\.vdb$','.osp' } else { "" }
if (-not $ospPath -or -not (Test-Path -LiteralPath $ospPath)) {
  Write-Error "Pass -Osp path to a .osp file (legacy -Vdb is not supported). Example: uia-osp-takeoff.ps1 -Exe ... -Osp ...\small.osp"
  exit 2
}
& (Join-Path $here "uia-osp-takeoff.ps1") -Exe $Exe -Osp $ospPath
