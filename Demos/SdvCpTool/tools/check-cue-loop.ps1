<#
.SYNOPSIS
  拿**游戏自己的 MonoGame** 校验一条 XACT Cue 到底会不会循环。

.DESCRIPTION
  为什么不能自己判：「会不会循环」由 MonoGame 的 XactClip 决定 —— 只有 sound 记录里带
  clips（`complexSound = true`）时才会建出 XactClip，而 loopCount 就存在它的播放事件里
  （255 = 无限）。写出来的字节「看着对」不算数，得让游戏那份运行时真的认。

  ⚠ 必须用 **Windows PowerShell**（跑在 .NET Framework 上）—— 游戏的 MonoGame.Framework.dll
  是 .NET Framework 程序集，net8 宿主 `Assembly.LoadFrom` 会直接 FileLoadException。

  用法：
    powershell -ExecutionPolicy Bypass -File tools\check-cue-loop.ps1 `
        -GameDir "D:\Stardew.Valley.v1.6.15\Stardew Valley" `
        -XactDir "D:\tmp\xacttest" -CueName "sdvcp_loop_probe"

  XactDir 里要有 `FarmerSounds.xgs` 与 `Sound Bank.xsb`（后者就是待验的那份）。
#>
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [Parameter(Mandatory = $true)][string]$XactDir,
    [Parameter(Mandatory = $true)][string]$CueName
)

$ErrorActionPreference = 'Stop'

$dll = Join-Path $GameDir 'MonoGame.Framework.dll'
if (-not (Test-Path $dll)) { throw "找不到 $dll" }

# ⚠ 用 Assembly::LoadFrom 而不是 Add-Type：后者会枚举程序集里的**全部**类型，
#    而 MonoGame 有些类型引用着本机没装的依赖（图形/媒体那几支），
#    于是丢 ReflectionTypeLoadException —— 我们只要音频那几个类型，按名字取就行。
$asm = [System.Reflection.Assembly]::LoadFrom($dll)
$engineType = $asm.GetType('Microsoft.Xna.Framework.Audio.AudioEngine')
$bankType = $asm.GetType('Microsoft.Xna.Framework.Audio.SoundBank')
if ($null -eq $engineType -or $null -eq $bankType) { throw 'MonoGame 里找不到音频类型' }

Write-Host '  [1/4] 建 AudioEngine…'
$engine = [System.Activator]::CreateInstance($engineType, @((Join-Path $XactDir 'FarmerSounds.xgs')))
Write-Host '  [2/4] 读 Sound Bank…'
$bank = [System.Activator]::CreateInstance($bankType, @($engine, (Join-Path $XactDir 'Sound Bank.xsb')))

Write-Host '  [3/4] GetCue…'
$cue = $bankType.GetMethod('GetCue', @([string])).Invoke($bank, @($CueName))
if ($null -eq $cue) { Write-Host "GetCue(`"$CueName`") 返回 null ✗"; exit 1 }

function Get-PrivateField($target, $name) {
    if ($null -eq $target) { return $null }
    $t = $target.GetType()
    while ($null -ne $t) {
        $f = $t.GetField($name, [System.Reflection.BindingFlags]'Instance,NonPublic,Public')
        if ($null -ne $f) { return $f.GetValue($target) }
        $t = $t.BaseType
    }
    return $null
}

$definition = Get-PrivateField $cue '_cue'
$sounds = Get-PrivateField $definition 'sounds'
$count = 0
if ($null -ne $sounds) { $count = $sounds.Length }

Write-Host "Cue「$CueName」：sounds=$count"

$clips = $null
if ($count -gt 0) {
    $clips = Get-PrivateField $sounds[0] '_clips'
}

$clipCount = 0
if ($null -ne $clips) { $clipCount = $clips.Length }
Write-Host "  clips=$clipCount"

if ($clipCount -eq 0) {
    Write-Host '  ⇒ 没有 XactClip ⇒ **游戏只会播一遍** ✗'
    exit 1
}

$events = Get-PrivateField $clips[0] '_events'
$loops = 0
if ($null -ne $events) {
    foreach ($e in $events) {
        $lc = Get-PrivateField $e 'loopCount'
        Write-Host "    事件 $($e.GetType().Name)：loopCount=$lc"
        if ($null -ne $lc -and [int]$lc -ge 255) { $loops++ }
    }
}

Write-Host '  [4/4] 试播 1.2 秒…'
$cue.GetType().GetMethod('Play').Invoke($cue, @()) | Out-Null
Start-Sleep -Milliseconds 1200
$stopped = $cue.GetType().GetProperty('IsStopped').GetValue($cue)
$playing = $cue.GetType().GetProperty('IsPlaying').GetValue($cue)
$cue.GetType().GetMethod('Stop').Invoke($cue, @()) | Out-Null

$ok = ($loops -gt 0) -and $playing -and (-not $stopped)
Write-Host "  ⇒ 无限循环事件 $loops 条；播 1.2 秒后 IsPlaying=$playing IsStopped=$stopped  $(if ($ok) {'[OK]'} else {'[FAIL]'})"
exit $(if ($ok) { 0 } else { 1 })
