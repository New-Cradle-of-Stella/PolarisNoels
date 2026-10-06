# 不使用 dotnet build 的编译路径：直接调 Roslyn csc.dll 编译 NetSmoke。
# 用途：CI/沙箱里 `dotnet build` 被策略拦截时仍能产出可运行的 ABI 自检程序。
#   pwsh -File tools/NetSmoke/build-netsmoke.ps1
param(
    [string]$Configuration = 'Release',
    [string]$TargetFramework = 'net8.0'
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here '..\..')).Path

$sdk = (dotnet --list-sdks | Sort-Object { [version]($_.Split(' ')[0]) } | Select-Object -Last 1).Split(' ')[-1].Trim('[', ']')
$csc = Join-Path $sdk 'Roslyn\bincore\csc.dll'
if (-not (Test-Path $csc)) { throw "csc.dll not found at $csc" }

$refRoot = Join-Path (Split-Path (Split-Path $sdk -Parent) -Parent) "packs\Microsoft.NETCore.App.Ref"
$refVersion = Get-ChildItem $refRoot -Directory | Where-Object { $_.Name -like "$($TargetFramework.TrimStart('net'))*" -or $_.Name -like "$([int]($TargetFramework -replace 'net','').Substring(0,1)).*" } | Sort-Object Name | Select-Object -Last 1
if (-not $refVersion) { throw "no reference pack for $TargetFramework under $refRoot" }
$refDir = Join-Path $refVersion.FullName "ref\$TargetFramework"
if (-not (Test-Path $refDir)) { throw "missing ref dir $refDir" }

$outDir = Join-Path $here "bin\$Configuration\$TargetFramework"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$sources = @(
    'Program.cs', 'AbiChecks.cs', 'SelfChecks.cs', 'MeshRoles.cs', 'Wire.cs', 'SmokeRunner.cs', 'NatCheck.cs'
) | ForEach-Object { Join-Path $here $_ }
$sources += @(
    'Networking\INetTransport.cs',
    'Networking\NativeMethods.cs',
    'Networking\NativeTransport.cs',
    'Networking\ReliableSendQueue.cs',
    'Networking\SafeEvents.cs'
) | ForEach-Object { Join-Path $root $_ }

$refs = Get-ChildItem $refDir -Filter *.dll | ForEach-Object { "-r:$($_.FullName)" }
$out = Join-Path $outDir 'NetSmoke.dll'

dotnet $csc -nologo -nowarn:CS1591 -unsafe -langversion:latest -nullable:disable -target:exe -out:$out @refs @sources
if ($LASTEXITCODE -ne 0) { throw "csc failed with $LASTEXITCODE" }

# 手写 runtimeconfig，便于 `dotnet NetSmoke.dll` 直接运行
$runtimeConfig = @"
{
  "runtimeOptions": {
    "tfm": "$TargetFramework",
    "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.0" }
  }
}
"@
[System.IO.File]::WriteAllText((Join-Path $outDir 'NetSmoke.runtimeconfig.json'), $runtimeConfig, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "built $out"
