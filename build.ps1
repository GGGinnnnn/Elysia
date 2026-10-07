<#
.SYNOPSIS
    爱莉希雅桌宠一键构建 / 发布脚本。

.DESCRIPTION
    -Build    只编译（默认），产物在 src\ElysiaPet\bin\Debug\net10.0-windows
    -Release  编译 Release 版本
    -Publish  发布单文件绿色版到 dist\（自包含，目标机无需装 .NET）
    -Test     编译后运行自检模式（--selftest），结果写入 elysia.log

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Test
    .\build.ps1 -Publish

.NOTES
    如果提示「在此系统上禁止运行脚本」（执行策略限制），用下面任意一种方式启动：
        powershell -ExecutionPolicy Bypass -File .\build.ps1 -Publish
        pwsh -ExecutionPolicy Bypass -File .\build.ps1 -Publish
    也可以先执行一次 set-executionpolicy -scope currentuser RemoteSigned 永久放行本地脚本。
#>
[CmdletBinding()]
param(
    [switch]$Release,
    [switch]$Publish,
    [switch]$Test
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src\ElysiaPet\ElysiaPet.csproj'
$configuration = if ($Release -or $Publish) { 'Release' } else { 'Debug' }

if (-not (Test-Path $project)) {
    throw "找不到项目文件: $project"
}

Write-Host "==> 构建配置: $configuration" -ForegroundColor Cyan
dotnet build $project -c $configuration --nologo
if ($LASTEXITCODE -ne 0) { throw '构建失败' }

$outputDir = Join-Path $root "src\ElysiaPet\bin\$configuration\net10.0-windows"

if ($Test) {
    $exe = Join-Path $outputDir 'ElysiaPet.exe'
    $log = Join-Path $outputDir 'elysia.log'
    Remove-Item $log -ErrorAction SilentlyContinue

    Write-Host '==> 运行自检 (--selftest)' -ForegroundColor Cyan
    $process = Start-Process -FilePath $exe -ArgumentList '--selftest' -PassThru -Wait

    if (Test-Path $log) {
        Get-Content $log -Encoding UTF8 | Select-String -Pattern '\[自检\]' | ForEach-Object { $_.Line }
        $failed = (Get-Content $log -Encoding UTF8 | Select-String -Pattern '\[失败\]').Count
        Write-Host "==> 自检完成：失败 $failed 项" -ForegroundColor ($(if ($failed -eq 0) { 'Green' } else { 'Red' }))
    }

    if ($process.ExitCode -ne 0) { throw "自检未通过 (退出码 $($process.ExitCode))" }
}

if ($Publish) {
    $dist = Join-Path $root 'dist'
    Write-Host '==> 发布单文件绿色版（首次会下载 win-x64 运行时包，请耐心等待）' -ForegroundColor Cyan

    dotnet publish $project `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -o $dist `
        --nologo

    if ($LASTEXITCODE -ne 0) { throw '发布失败' }

    # 素材已内嵌进 exe，这里额外复制一份到 dist 只是方便想换角色的人直接替换同名文件。
    # 把它们删掉程序也照样能跑（会自动回退到内嵌资源）。
    $assetTarget = Join-Path $dist 'Assets'
    New-Item -ItemType Directory -Force -Path (Join-Path $assetTarget 'gifs') | Out-Null
    Copy-Item (Join-Path $root 'src\ElysiaPet\Assets\gifs\*.gif') (Join-Path $assetTarget 'gifs') -Force
    Copy-Item (Join-Path $root 'src\ElysiaPet\Assets\icon.ico') $assetTarget -Force

    $exe = Join-Path $dist 'ElysiaPet.exe'
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "==> 发布完成: $exe ($size MB)" -ForegroundColor Green
    Write-Host '    说明：exe 自带素材，单独复制这一个文件就能运行；'
    Write-Host '          旁边的 Assets 目录只是方便替换角色素材，删掉也不影响使用。'
    Write-Host '          首次启动会在同目录生成 config.json，可在程序内「系统设置」页填 API Key。'
}

Write-Host '==> 全部完成' -ForegroundColor Green
