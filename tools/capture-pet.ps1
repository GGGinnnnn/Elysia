<#
    抓取桌宠窗口的真实画面并存成 PNG，用于确认动画与渲染是否正常。
    用法: powershell -ExecutionPolicy Bypass -File .\shot.ps1
    结果: D:\study\ElysiaPet\shots\pet-0.png ... pet-5.png
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class Shot {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
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
    public static string Title(IntPtr hwnd) {
        var sb = new StringBuilder(256); GetWindowText(hwnd, sb, 256); return sb.ToString();
    }
}
'@ -ReferencedAssemblies System.Drawing

$dir = 'D:\study\ElysiaPet\src\ElysiaPet\bin\Debug\net10.0-windows'
$exe = Join-Path $dir 'ElysiaPet.exe'
$shots = 'D:\study\ElysiaPet\shots'
New-Item -ItemType Directory -Force -Path $shots | Out-Null
Get-ChildItem "$shots\*.png" -ErrorAction SilentlyContinue | Remove-Item -Force

$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 6

$target = [IntPtr]::Zero
foreach ($w in [Shot]::WindowsOfProcess([uint32]$proc.Id)) {
    if (-not [Shot]::IsWindowVisible($w)) { continue }
    $r = New-Object Shot+RECT
    [void][Shot]::GetWindowRect($w, [ref]$r)
    $w2 = $r.Right - $r.Left; $h2 = $r.Bottom - $r.Top
    if ($w2 -lt 60 -or $h2 -lt 60 -or $w2 -gt 900 -or $h2 -gt 900) { continue }
    if ([Shot]::Title($w) -eq '爱莉希雅桌宠') { $target = $w }
}
if ($target -eq [IntPtr]::Zero) { Write-Output '没找到桌宠窗口'; Stop-Process -Id $proc.Id -Force; exit 1 }

$r = New-Object Shot+RECT
[void][Shot]::GetWindowRect($target, [ref]$r)
$cw = $r.Right - $r.Left; $ch = $r.Bottom - $r.Top
Write-Output "桌宠窗口: ($($r.Left),$($r.Top)) 尺寸 $cw x $ch"

Write-Output "开始每隔 200ms 抓一张，共 6 张..."
for ($i = 0; $i -lt 14; $i++) {
    $hdcSrc = [Shot]::GetDC([IntPtr]::Zero)
    $hdcMem = [Shot]::CreateCompatibleDC($hdcSrc)
    $hBmp = [Shot]::CreateCompatibleBitmap($hdcSrc, $cw, $ch)
    $old = [Shot]::SelectObject($hdcMem, $hBmp)
    [void][Shot]::BitBlt($hdcMem, 0, 0, $cw, $ch, $hdcSrc, $r.Left, $r.Top, 0x00CC0020)
    $bmp = [System.Drawing.Image]::FromHbitmap($hBmp)
    [void][Shot]::SelectObject($hdcMem, $old)
    [void][Shot]::DeleteObject($hBmp); [void][Shot]::DeleteDC($hdcMem); [void][Shot]::ReleaseDC([IntPtr]::Zero, $hdcSrc)
    $path = Join-Path $shots ("pet-{0}.png" -f $i)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ("  已保存 pet-{0}.png" -f $i)
    Start-Sleep -Milliseconds 100
}

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Write-Output "完成"
