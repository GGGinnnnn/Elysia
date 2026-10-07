<#
    公共路径解析：所有诊断脚本都通过它定位编译产物与输出目录，
    避免把本机的绝对路径写死在脚本里（换台电脑就失效）。

    默认取仓库内 Debug 编译产物的位置；如果发布版放在仓库根的 dist\ 下也会自动识别。
#>

$script:RepoRoot = Split-Path -Parent $PSScriptRoot

function Get-ElysiaExeDir {
    [CmdletBinding()]
    param([ValidateSet('Debug', 'Release', 'Auto')][string]$Configuration = 'Auto')

    $candidates = @()
    if ($Configuration -eq 'Auto') {
        $candidates += Join-Path $script:RepoRoot 'src\ElysiaPet\bin\Debug\net10.0-windows'
        $candidates += Join-Path $script:RepoRoot 'src\ElysiaPet\bin\Release\net10.0-windows'
        $candidates += Join-Path $script:RepoRoot 'dist'
    }
    else {
        $candidates += Join-Path $script:RepoRoot "src\ElysiaPet\bin\$Configuration\net10.0-windows"
    }

    foreach ($dir in $candidates) {
        if (Test-Path (Join-Path $dir 'ElysiaPet.exe')) { return $dir }
    }

    throw "找不到 ElysiaPet.exe。请先运行 build.ps1 编译，或 build.ps1 -Publish 生成发布版。`n已查找：`n  $($candidates -join "`n  ")"
}

function Get-ElysiaShotsDir {
    [CmdletBinding()]
    param()
    return Join-Path $script:RepoRoot 'shots'
}
