<#
    用真实鼠标事件验证：右键拖拽改尺寸后不应该弹出右键菜单。
    原理：右键菜单是独立弹窗，出现时会产生新的顶层窗口；比较拖拽前后的窗口数量即可。
    用法: powershell -ExecutionPolicy Bypass -File .\verify-rightdrag.ps1
#>
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class V {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
    public delegate bool E(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(E cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int m);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int m);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll", SetLastError=true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);

    public const uint LEFTDOWN=0x0002, LEFTUP=0x0004, RIGHTDOWN=0x0008, RIGHTUP=0x0010;

    public static void Mouse(uint flags) {
        var a = new INPUT[1];
        a[0].type = 0;
        a[0].mi.dwFlags = flags;
        SendInput(1, a, Marshal.SizeOf(typeof(INPUT)));
    }

    public static List<IntPtr> VisibleWindows(uint pid) {
        var found = new List<IntPtr>();
        EnumWindows((h,l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string Class(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
    public static string Title(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
    public static int Width(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.Right - r.Left; }
    public static int Height(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.Bottom - r.Top; }
    public static RECT Rect(IntPtr h) { RECT r; GetWindowRect(h, out r); return r; }
}
'@

$dir = 'D:\study\ElysiaPet\src\ElysiaPet\bin\Debug\net10.0-windows'
$log = Join-Path $dir 'elysia.log'
Remove-Item $log -ErrorAction SilentlyContinue

# 先把桌宠宽度复位，避免沿用上次测试改小的值
$cfg = Join-Path $dir 'config.json'
if (Test-Path $cfg) {
    $json = [System.IO.File]::ReadAllText($cfg, [System.Text.Encoding]::UTF8)
    $json = [regex]::Replace($json, '"pet_width":\s*[-\d.]+', '"pet_width": 220')
    [System.IO.File]::WriteAllText($cfg, $json, (New-Object System.Text.UTF8Encoding($false)))
}

$proc = Start-Process -FilePath (Join-Path $dir 'ElysiaPet.exe') -PassThru
Start-Sleep -Seconds 6

function Find-Pet([uint32]$processId) {
    foreach ($w in [V]::VisibleWindows($processId)) {
        if ([V]::Title($w) -eq '爱莉希雅桌宠') { return $w }
    }
    return [IntPtr]::Zero
}

$pet = Find-Pet $proc.Id
if ($pet -eq [IntPtr]::Zero) { Write-Output '没找到桌宠窗口'; Stop-Process -Id $proc.Id -Force; exit 1 }

$r = [V]::Rect($pet)
Write-Output ("桌宠窗口: ({0},{1}) 尺寸 {2}x{3}" -f $r.Left, $r.Top, [V]::Width($pet), [V]::Height($pet))

# 抓取点放在身体中下部，避开气泡与输入框
$gx = $r.Left + [int]([V]::Width($pet) / 2)
$gy = $r.Top + [int]([V]::Height($pet) * 0.45)

$winsBefore = ([V]::VisibleWindows([uint32]$proc.Id)).Count
$widthBefore = [V]::Width($pet)
Write-Output ("拖拽前: 可见窗口数={0} 桌宠宽={1}" -f $winsBefore, $widthBefore)

# --- 右键按住左移 60 像素（改尺寸）---
[void][V]::SetCursorPos($gx, $gy)
Start-Sleep -Milliseconds 400
[V]::Mouse([V]::RIGHTDOWN)
Start-Sleep -Milliseconds 200

foreach ($step in 1..6) {
    [void][V]::SetCursorPos($gx - ($step * 10), $gy)
    Start-Sleep -Milliseconds 90
}

[V]::Mouse([V]::RIGHTUP)
Start-Sleep -Milliseconds 1200   # 留足时间让可能出现的菜单弹出来

$pet = Find-Pet $proc.Id
$r2 = [V]::Rect($pet)
$winsAfter = ([V]::VisibleWindows([uint32]$proc.Id)).Count
$widthAfter = [V]::Width($pet)
Write-Output ("拖拽后: 可见窗口数={0} 桌宠宽={1}" -f $winsAfter, $widthAfter)

$changed = $widthAfter -ne $widthBefore
$menuAppeared = $winsAfter -gt $winsBefore

Write-Output ""
Write-Output ("尺寸是否改变 : {0}  ({1} -> {2})" -f $changed, $widthBefore, $widthAfter)
Write-Output ("是否弹出菜单 : {0}  (窗口数 {1} -> {2})" -f $menuAppeared, $winsBefore, $winsAfter)
Write-Output ""
if ($changed -and -not $menuAppeared) {
    Write-Output '结论: 【通过】右键拖拽改变了尺寸，且没有弹出右键菜单'
} elseif (-not $changed) {
    Write-Output '结论: 【失败】右键拖拽没有改变尺寸'
} else {
    Write-Output '结论: 【失败】右键拖拽后仍然弹出了菜单'
}

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
