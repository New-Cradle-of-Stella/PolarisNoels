# 不使用 dotnet build 的编译路径：直接调 Roslyn csc.dll 编译 SessionSmoke。
# 用途：沙箱/MSBuild 的 Csc 任务无法创建重定向 stdio 管道（"Access is denied"）时，
#       仍能产出可运行的 managed 会话集成测试。用法：
#   pwsh -File tools/SessionSmoke/build-sessionsmoke.ps1
param(
    [string]$Configuration = 'Release',
    [string]$TargetFramework = 'net8.0'
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here '..\..')).Path

# 直接枚举本机 SDK 目录，避免通过管道捕获 dotnet 输出（部分沙箱会拒绝该管道）。
$sdkRoot = Join-Path $env:ProgramFiles 'dotnet\sdk'
if (-not (Test-Path $sdkRoot)) { throw "dotnet sdk root not found at $sdkRoot" }
$sdkVersion = (Get-ChildItem $sdkRoot -Directory | Sort-Object { [version]$_.Name } | Select-Object -Last 1).Name
$csc = Join-Path $sdkRoot "$sdkVersion\Roslyn\bincore\csc.dll"
if (-not (Test-Path $csc)) { throw "csc.dll not found at $csc" }

$refRoot = Join-Path $env:ProgramFiles 'dotnet\packs\Microsoft.NETCore.App.Ref'
$refVersion = Get-ChildItem $refRoot -Directory | Where-Object { $_.Name -like "$($TargetFramework.TrimStart('net'))*" -or $_.Name -like "$([int]($TargetFramework -replace 'net','').Substring(0,1)).*" } | Sort-Object Name | Select-Object -Last 1
if (-not $refVersion) { throw "no reference pack for $TargetFramework under $refRoot" }
$refDir = Join-Path $refVersion.FullName "ref\$TargetFramework"
if (-not (Test-Path $refDir)) { throw "missing ref dir $refDir" }

# Newtonsoft.Json 用本机 NuGet 缓存 13.0.4 的明确路径；缺失时回退到仓库 .build/plugin。
$newtonsoft = Join-Path $env:USERPROFILE '.nuget\packages\newtonsoft.json\13.0.4\lib\net6.0\Newtonsoft.Json.dll'
if (-not (Test-Path $newtonsoft)) { $newtonsoft = Join-Path $root '.build\plugin\Newtonsoft.Json.dll' }
if (-not (Test-Path $newtonsoft)) { throw "Newtonsoft.Json.dll not found (nuget cache 13.0.4 or .build/plugin)" }

$outDir = Join-Path $here "bin\$Configuration\$TargetFramework"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$sources = @('Program.cs', 'SessionChecks.cs', 'FakeTransport.cs', 'Stubs.cs') |
    ForEach-Object { Join-Path $here $_ }
$sources += @(
    'Networking\INetTransport.cs',
    'Networking\NetworkRuntime.cs',
    'Networking\BulkCodec.cs',
    'Networking\SaveTransfer.cs',
    'Networking\NativeMethods.cs',
    'Networking\NativeTransport.cs',
    'Networking\ReliableSendQueue.cs',
    'Networking\SafeEvents.cs',
    'Networking\Session\ClientSession.cs',
    'Networking\Session\HostSession.cs',
    'Networking\ClientServer\HandshakeCodec.cs',
    'Networking\ClientServer\PolarisNoelsHostMessage.cs',
    'Networking\ClientServer\PolarisNoelsClientMessage.cs'
) | ForEach-Object { Join-Path $root $_ }

$refs = Get-ChildItem $refDir -Filter *.dll | ForEach-Object { "-r:$($_.FullName)" }
$refs += "-r:$newtonsoft"
$out = Join-Path $outDir 'SessionSmoke.dll'

dotnet $csc -nologo -nowarn:CS1591,CS0067 -unsafe -langversion:latest -nullable:disable -target:exe -out:$out @refs @sources
if ($LASTEXITCODE -ne 0) { throw "csc failed with $LASTEXITCODE" }

Copy-Item $newtonsoft $outDir -Force

# 手写 runtimeconfig，便于 `dotnet SessionSmoke.dll` 直接运行
$runtimeConfig = @"
{
  "runtimeOptions": {
    "tfm": "$TargetFramework",
    "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.0" }
  }
}
"@
[System.IO.File]::WriteAllText((Join-Path $outDir 'SessionSmoke.runtimeconfig.json'), $runtimeConfig, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "built $out (Newtonsoft: $newtonsoft)"
