# NetSmoke 编排脚本：先跑 ABI 布局自检（不需要 DLL），DLL 存在时再跑三进程 mesh 冒烟。
#   pwsh -File tools/NetSmoke/run-smoke.ps1            # 构建 + ABI + mesh(若有 DLL)
#   pwsh -File tools/NetSmoke/run-smoke.ps1 -SkipBuild # 只跑
# 退出码：0 = 全部通过；1 = 构建失败；2 = ABI 失败；3 = mesh 失败；10 = 无 DLL，联调被阻塞，不能视为通过。
param(
    [switch]$SkipBuild,
    [int]$JoinTimeoutSec = 90
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here '..\..')).Path
$proj = Join-Path $here 'NetSmoke.csproj'
$outDir = Join-Path $here 'bin\Release\net8.0'
$dll = Join-Path $outDir 'NetSmoke.dll'
$nativeDll = Join-Path $root 'native\polaris_net\target\release\polaris_net.dll'

if (-not $SkipBuild) {
    Write-Host '== build NetSmoke =='
    dotnet build $proj -c Release -p:UseSharedCompilation=false -v m
    if ($LASTEXITCODE -ne 0) { Write-Host 'RESULT: FAIL (build)'; exit 1 }
}
if (-not (Test-Path $dll)) { Write-Host "RESULT: FAIL (missing $dll)"; exit 1 }

if (Test-Path $nativeDll) { Copy-Item $nativeDll $outDir -Force }
Write-Host '== ABI layout check =='
dotnet $dll abi --with-native-selftest
$abiExit = $LASTEXITCODE
if ($abiExit -ne 0) { Write-Host "RESULT: FAIL (abi exit=$abiExit)"; exit 2 }

Write-Host '== managed self-check (reliable queue / event isolation) =='
dotnet $dll selfcheck
$selfExit = $LASTEXITCODE
if ($selfExit -ne 0) { Write-Host "RESULT: FAIL (selfcheck exit=$selfExit)"; exit 4 }

if (-not (Test-Path $nativeDll)) {
    Write-Host "RESULT: BLOCKED (no $nativeDll - native mesh smoke skipped, ABI layout passed)"
    exit 10
}
Copy-Item $nativeDll $outDir -Force

Write-Host '== three-process mesh smoke =='
dotnet $dll orchestrate $JoinTimeoutSec
exit $LASTEXITCODE
