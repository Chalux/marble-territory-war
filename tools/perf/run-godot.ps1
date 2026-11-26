# Runs the Godot mono build and keeps its game window restored/foreground.
# Sandboxed background launches start GUI windows minimized, and Godot pauses or
# skews rendering for occluded/minimized windows, so the harness restores the
# real game window (the non-console process) for the whole measured run.
param(
    [Parameter(Mandatory = $true)][string[]]$GodotArgs,
    [string]$LogPath = "",
    [int]$TimeoutSeconds = 600
)

$engine = 'E:/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($LogPath)) { $LogPath = Join-Path $project '.godot\perf-run.log' }
foreach ($f in @($LogPath, $LogPath + '.err')) {
    Remove-Item $f -Force -ErrorAction SilentlyContinue
}

$src = @'
using System;
using System.Runtime.InteropServices;
public class DshWinCtl {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public static readonly IntPtr HwndTopmost = new IntPtr(-1);
    public static readonly IntPtr HwndNotTopmost = new IntPtr(-2);
    public const uint SwpNoMove = 0x0002, SwpNoSize = 0x0001, SwpNoActivate = 0x0010;
}
'@
if (-not ('DshWinCtl' -as [type])) { Add-Type -TypeDefinition $src }

function Get-GameProcess {
    Get-Process -Name 'Godot_v4.7.2-stable_mono_win64' -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
        Select-Object -First 1
}

function Restore-GameWindow {
    param($Game)
    if (-not $Game) { return }
    $handle = $Game.MainWindowHandle
    if ($handle -eq [IntPtr]::Zero) { return }
    if ([DshWinCtl]::IsIconic($handle)) { [void][DshWinCtl]::ShowWindow($handle, 9) }
    # Keep it unoccluded without stealing keyboard focus from the user's desktop.
    [void][DshWinCtl]::SetWindowPos($handle, [DshWinCtl]::HwndTopmost, 0, 0, 0, 0,
        [DshWinCtl]::SwpNoMove -bor [DshWinCtl]::SwpNoSize -bor [DshWinCtl]::SwpNoActivate)
}

function Clear-GameTopmost {
    param($Game)
    if (-not $Game) { return }
    $handle = $Game.MainWindowHandle
    if ($handle -ne [IntPtr]::Zero) {
        [void][DshWinCtl]::SetWindowPos($handle, [DshWinCtl]::HwndNotTopmost, 0, 0, 0, 0,
            [DshWinCtl]::SwpNoMove -bor [DshWinCtl]::SwpNoSize -bor [DshWinCtl]::SwpNoActivate)
    }
}

$env:GODOT_MCP_RUNTIME = '0'
$argv = @('--path', $project) + $GodotArgs
$proc = Start-Process -FilePath $engine -ArgumentList $argv -PassThru -WindowStyle Normal `
    -RedirectStandardOutput $LogPath -RedirectStandardError ($LogPath + '.err')

$started = Get-Date
$game = $null
$restoredTracker = $false
while (-not $proc.HasExited) {
    $now = Get-Date
    if (($now - $started).TotalSeconds -gt $TimeoutSeconds) {
        Get-Process -Name 'Godot_v4.7.2-stable_mono_win64*' -ErrorAction SilentlyContinue | Stop-Process -Force
        if (-not $proc.HasExited) { $proc.Kill($true) }
        Write-Host "TIMEOUT after $TimeoutSeconds s; see $LogPath"
        exit 124
    }
    Start-Sleep -Milliseconds 500
    $game = Get-GameProcess
    if ($game) {
        Restore-GameWindow $game
        if (-not $restoredTracker) { Write-Host "game window restored pid=$($game.Id)"; $restoredTracker = $true }
    }
}
$code = $proc.ExitCode
Clear-GameTopmost $game
Write-Host "EXIT=$code restored=$restoredTracker log=$LogPath"
if (Test-Path $LogPath) { Get-Content $LogPath }
$err = $LogPath + '.err'
if ((Test-Path $err) -and (Get-Item $err).Length -gt 0) {
    Write-Host '--- stderr tail ---'
    Get-Content $err | Select-Object -Last 20
}
exit $code
