<#
    诊断脚本：
      1) 枚举桌宠窗口句柄，抓像素判断 GIF 是否在动
      2) 用真实鼠标事件执行拖拽，记录窗口位移是否符合预期（验证「一动就飞出屏幕」）
    用法: powershell -ExecutionPolicy Bypass -File .\diag.ps1
#>
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class DiagWin {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, uint rop);

    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out System.Drawing.Point p);
        [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    public static void SendMouse(uint flags, int dx, int dy) {
        var inp = new INPUT[1];
        inp[0].type = 0;
        inp[0].mi.dwFlags = flags;
        inp[0].mi.dx = dx; inp[0].mi.dy = dy;
        SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
    }

    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;

    public static List<IntPtr> WindowsOfProcess(uint targetPid) {
        var found = new List<IntPtr>();
        EnumWindows((hwnd, lp) => {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == targetPid) found.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string Describe(IntPtr hwnd) {
        var cls = new StringBuilder(256); GetClassName(hwnd, cls, 256);
        var txt = new StringBuilder(256); GetWindowText(hwnd, txt, 256);
        RECT r; GetWindowRect(hwnd, out r);
        return string.Format("hwnd=0x{0:X} class='{1}' title='{2}' visible={3} rect=({4},{5})-({6},{7})",
            hwnd.ToInt64(), cls, txt, IsWindowVisible(hwnd), r.Left, r.Top, r.Right, r.Bottom);
    }
}
'@ -ReferencedAssemblies System.Drawing

$dir = 'D:\study\ElysiaPet\src\ElysiaPet\bin\Debug\net10.0-windows'
$exe = Join-Path $dir 'ElysiaPet.exe'
$log = Join-Path $dir 'elysia.log'
$cfg = Join-Path $dir 'config.json'

Copy-Item $cfg "$cfg.diagbak" -Force
$json = [System.IO.File]::ReadAllText($cfg, [System.Text.Encoding]::UTF8)
$json = $json -replace '"opacity_enabled":\s*true', '"opacity_enabled": false'
$json = $json -replace '"window_level":\s*"[^"]*"', '"window_level": "AlwaysOnTop"'
$json = $json -replace '"window_x":\s*[-\d.]+', '"window_x": 700'
$json = $json -replace '"window_y":\s*[-\d.]+', '"window_y": 300'
$json = $json -replace '"pet_width":\s*[-\d.]+', '"pet_width": 220'
[System.IO.File]::WriteAllText($cfg, $json, (New-Object System.Text.UTF8Encoding($false)))

Remove-Item $log -ErrorAction SilentlyContinue
$pet = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 6

Write-Output "=== 进程 $($pet.Id) 的所有顶层窗口 ==="
$windows = [DiagWin]::WindowsOfProcess([uint32]$pet.Id)
foreach ($w in $windows) { Write-Output ("  " + [DiagWin]::Describe($w)) }

$main = [IntPtr]::Zero
$best = 0
foreach ($w in $windows) {
    # 只考虑可见窗口，并用尺寸过滤掉 WPF 内部的 Hidden Window 之类
    if (-not [DiagWin]::IsWindowVisible($w)) { continue }
    $r = New-Object DiagWin+RECT
    [void][DiagWin]::GetWindowRect($w, [ref]$r)
    $w2 = $r.Right - $r.Left
    $h2 = $r.Bottom - $r.Top
    if ($w2 -lt 60 -or $h2 -lt 60 -or $w2 -gt 900 -or $h2 -gt 900) { continue }
    $area = $w2 * $h2
    if ($area -gt $best) { $best = $area; $main = $w }
}
Write-Output ""
Write-Output "选定主窗口: $([DiagWin]::Describe($main))"

$r0 = New-Object DiagWin+RECT
[void][DiagWin]::GetWindowRect($main, [ref]$r0)
$cw = $r0.Right - $r0.Left
$ch = $r0.Bottom - $r0.Top

Write-Output ""
Write-Output "=== 抓 6 帧判断 GIF 是否在动 ==="
$hashes = @()
for ($i = 0; $i -lt 12; $i++) {
    $hdcSrc = [DiagWin]::GetDC([IntPtr]::Zero)
    $hdcMem = [DiagWin]::CreateCompatibleDC($hdcSrc)
    $hBmp = [DiagWin]::CreateCompatibleBitmap($hdcSrc, $cw, $ch)
    $old = [DiagWin]::SelectObject($hdcMem, $hBmp)
    [void][DiagWin]::BitBlt($hdcMem, 0, 0, $cw, $ch, $hdcSrc, $r0.Left, $r0.Top, 0x00CC0020)
    $bmp = [System.Drawing.Image]::FromHbitmap($hBmp)
    [void][DiagWin]::SelectObject($hdcMem, $old)
    [void][DiagWin]::DeleteObject($hBmp); [void][DiagWin]::DeleteDC($hdcMem); [void][DiagWin]::ReleaseDC([IntPtr]::Zero, $hdcSrc)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()

    $md5 = [System.Security.Cryptography.MD5]::Create()
    $hash = [BitConverter]::ToString($md5.ComputeHash($bytes)).Replace('-', '')
    $hashes += $hash
    Write-Output ("  帧 {0}: {1}" -f $i, $hash.Substring(0, 12))
    Start-Sleep -Milliseconds 400
}
$distinct = ($hashes | Select-Object -Unique).Count
if ($distinct -gt 1) { Write-Output "  => 动画【在动】，$distinct 种画面" } else { Write-Output "  => 动画【静止】" }

Write-Output ""
Write-Output "=== 模拟真实左键拖拽：每步右下移动，检查窗口位移 ==="
$grabX = $r0.Left + [int]($cw / 2)
$grabY = $r0.Top + [int]($ch * 0.72)
Write-Output "  抓取点: ($grabX, $grabY)   窗口初始: ($($r0.Left), $($r0.Top))  尺寸 $cw x $ch"

[void][DiagWin]::SetCursorPos($grabX, $grabY)
Start-Sleep -Milliseconds 300
[DiagWin]::SendMouse([DiagWin]::LEFTDOWN, 0, 0)
Start-Sleep -Milliseconds 200

for ($step = 1; $step -le 8; $step++) {
    $tx = $grabX + ($step * 20)
    $ty = $grabY + ($step * 12)
    [void][DiagWin]::SetCursorPos($tx, $ty)
    Start-Sleep -Milliseconds 120

    $rn = New-Object DiagWin+RECT
    [void][DiagWin]::GetWindowRect($main, [ref]$rn)
    $expectLeft = $r0.Left + ($step * 20)
    $expectTop = $r0.Top + ($step * 12)
    Write-Output ("  步 {0,2}: 鼠标({1},{2})  窗口({3},{4})  期望({5},{6})  偏差({7},{8})" -f `
        $step, $tx, $ty, $rn.Left, $rn.Top, $expectLeft, $expectTop, ($rn.Left - $expectLeft), ($rn.Top - $expectTop))
}
[DiagWin]::SendMouse([DiagWin]::LEFTUP, 0, 0)
Start-Sleep -Milliseconds 400

$rf = New-Object DiagWin+RECT
[void][DiagWin]::GetWindowRect($main, [ref]$rf)
Write-Output "  最终窗口: ($($rf.Left), $($rf.Top))  尺寸 $($rf.Right-$rf.Left)x$($rf.Bottom-$rf.Top)"
Write-Output "  是否出屏: $($rf.Left -ge 1920 -or $rf.Top -ge 1080 -or $rf.Right -le 0 -or $rf.Bottom -le 0)"

Stop-Process -Id $pet.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Write-Output ""
Write-Output "=== 程序日志 ==="
if (Test-Path $log) { Get-Content $log -Encoding UTF8 }
Move-Item "$cfg.diagbak" $cfg -Force
Write-Output "配置已还原"
