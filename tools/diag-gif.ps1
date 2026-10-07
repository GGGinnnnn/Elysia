<#
    GIF 动画诊断：启动 --giftest 模式，抓取窗口像素判断动画是否在动。
    用法: powershell -ExecutionPolicy Bypass -File .\giftest.ps1
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class GT {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, uint rop);
    public static List<IntPtr> WindowsOfProcess(uint pid) {
        var found = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) found.Add(h); return true; }, IntPtr.Zero);
        return found;
    }
}
'@ -ReferencedAssemblies System.Drawing

. (Join-Path $PSScriptRoot 'common.ps1')
$dir = Get-ElysiaExeDir
$exe = Join-Path $dir 'ElysiaPet.exe'
$log = Join-Path $dir 'elysia.log'
Remove-Item $log -ErrorAction SilentlyContinue

$proc = Start-Process -FilePath $exe -ArgumentList '--giftest' -PassThru
Start-Sleep -Seconds 3

$target = [IntPtr]::Zero
foreach ($w in [GT]::WindowsOfProcess([uint32]$proc.Id)) {
    if (-not [GT]::IsWindowVisible($w)) { continue }
    $sb = New-Object System.Text.StringBuilder 256
    [void][GT]::GetWindowText($w, $sb, 256)
    if ($sb.ToString() -eq 'GIF 渲染测试') { $target = $w }
}
Write-Output "测试窗口句柄: $target"

if ($target -ne [IntPtr]::Zero) {
    $r = New-Object GT+RECT
    [void][GT]::GetWindowRect($target, [ref]$r)
    $cw = $r.Right - $r.Left; $ch = $r.Bottom - $r.Top
    Write-Output "窗口矩形: ($($r.Left),$($r.Top)) 尺寸 $cw x $ch"
    Write-Output ""
    Write-Output "=== 连续抓 10 帧 ==="
    $hashes = @()
    for ($i = 0; $i -lt 10; $i++) {
        $hdcSrc = [GT]::GetDC([IntPtr]::Zero)
        $hdcMem = [GT]::CreateCompatibleDC($hdcSrc)
        $hBmp = [GT]::CreateCompatibleBitmap($hdcSrc, $cw, $ch)
        $old = [GT]::SelectObject($hdcMem, $hBmp)
        [void][GT]::BitBlt($hdcMem, 0, 0, $cw, $ch, $hdcSrc, $r.Left, $r.Top, 0x00CC0020)
        $bmp = [System.Drawing.Image]::FromHbitmap($hBmp)
        [void][GT]::SelectObject($hdcMem, $old)
        [void][GT]::DeleteObject($hBmp); [void][GT]::DeleteDC($hdcMem); [void][GT]::ReleaseDC([IntPtr]::Zero, $hdcSrc)
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray(); $ms.Dispose(); $bmp.Dispose()
        $md5 = [System.Security.Cryptography.MD5]::Create()
        $h = [BitConverter]::ToString($md5.ComputeHash($bytes)).Replace('-', '')
        $hashes += $h
        Write-Output ("  帧 {0}: {1}  ({2} 字节)" -f $i, $h.Substring(0, 12), $bytes.Length)
        Start-Sleep -Milliseconds 250
    }
    $d = ($hashes | Select-Object -Unique).Count
    Write-Output ""
    if ($d -gt 1) { Write-Output "结论: GIF 在【动】($d 种画面)" } else { Write-Output "结论: GIF 【静止】" }
}

Start-Sleep -Seconds 4
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Write-Output ""
Write-Output "=== 日志 ==="
if (Test-Path $log) { Get-Content $log -Encoding UTF8 | Select-String -Pattern 'GIF测试' | ForEach-Object { $_.Line } }
