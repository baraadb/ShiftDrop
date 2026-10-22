# Compares two FFmpeg builds on the same machine, on the kind of work ShiftDrop does.
#   bench.ps1 -Old <folder with ffmpeg.exe> -New <folder with ffmpeg.exe> [-Runs 3]
# Writes build/bench.md and fails when the new build is slower overall (more than -Slack percent).
param(
    [Parameter(Mandatory)] [string]$Old,
    [Parameter(Mandatory)] [string]$New,
    [int]$Runs = 3,
    [double]$Slack = 3,
    [string]$Out = 'build/bench.md'
)
$ErrorActionPreference = 'Stop'
$work = Join-Path ([IO.Path]::GetTempPath()) "ffbench-$([guid]::NewGuid().ToString('N').Substring(0,6))"
New-Item -ItemType Directory -Force $work | Out-Null
$oldFf = Join-Path $Old 'ffmpeg.exe'; $newFf = Join-Path $New 'ffmpeg.exe'
$oldProbe = Join-Path $Old 'ffprobe.exe'; $newProbe = Join-Path $New 'ffprobe.exe'

function Run($exe, [string[]]$a) {
    $p = Start-Process -FilePath $exe -ArgumentList $a -NoNewWindow -Wait -PassThru -RedirectStandardError "$work/err.txt" -RedirectStandardOutput "$work/out.txt"
    return $p.ExitCode
}
function Q([string]$s) { if ($s -match '\s') { return '"' + $s + '"' } return $s }

# ---- inputs (made once, with the old build)
Write-Host 'making inputs...'
$in = @{
    hd     = "$work/hd.mp4";    uhd   = "$work/uhd.png";  song = "$work/song.wav"
    av1    = "$work/av1.mkv";   vp9   = "$work/vp9.webm"; jpgs = "$work/photo.jpg"
}
Run $oldFf @('-y','-v','error','-f','lavfi','-i','testsrc2=s=1920x1080:r=30:d=10','-f','lavfi','-i','sine=f=440:d=10','-c:v','mpeg4','-q:v','2','-c:a','aac','-shortest',$in.hd) | Out-Null
Run $oldFf @('-y','-v','error','-f','lavfi','-i','testsrc2=s=3840x2160:d=1','-frames:v','1',$in.uhd) | Out-Null
Run $oldFf @('-y','-v','error','-f','lavfi','-i','sine=f=440:d=180','-ac','2',$in.song) | Out-Null
Run $oldFf @('-y','-v','error','-f','lavfi','-i','testsrc2=s=1920x1080:r=30:d=10','-c:v','libsvtav1','-preset','10',$in.av1) | Out-Null
Run $oldFf @('-y','-v','error','-f','lavfi','-i','testsrc2=s=1280x720:r=30:d=10','-c:v','libvpx-vp9','-deadline','realtime','-cpu-used','8',$in.vp9) | Out-Null
Run $oldFf @('-y','-v','error','-i',$in.uhd,'-q:v','2',$in.jpgs) | Out-Null

# ---- the work: what ShiftDrop runs when you pick these on the wheel
$tests = [ordered]@{
    'start-up ×20 (ffmpeg -version)' = @{ repeat = 20; args = @('-version') }
    'file info ×20 (ffprobe)'        = @{ repeat = 20; probe = $true; args = @('-v','error','-print_format','json','-show_format','-show_streams',$in.hd) }
    'video → MP4 (H.264, Media Foundation)' = @{ args = @('-y','-i',$in.hd,'-c:v','h264_mf','-c:a','aac','-b:a','192k',"$work/o1.mp4") }
    'video → WebM (VP9)'             = @{ args = @('-y','-i',$in.hd,'-t','4','-c:v','libvpx-vp9','-b:v','0','-crf','32','-row-mt','1','-deadline','good','-cpu-used','4','-c:a','libopus',"$work/o2.webm") }
    'video → AV1 (SVT-AV1)'          = @{ args = @('-y','-i',$in.hd,'-t','4','-c:v','libsvtav1','-crf','30','-preset','8',"$work/o3.mkv") }
    'AV1 decode (dav1d)'             = @{ args = @('-y','-i',$in.av1,'-f','null','-') }
    'VP9 decode'                     = @{ args = @('-y','-i',$in.vp9,'-f','null','-') }
    'video → GIF'                    = @{ args = @('-y','-i',$in.hd,'-t','4','-vf','fps=12,scale=480:-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse',"$work/o4.gif") }
    'picture 4K → AVIF (libaom)'     = @{ args = @('-y','-i',$in.uhd,'-c:v','libaom-av1','-still-picture','1','-crf','28','-b:v','0','-cpu-used','6','-pix_fmt','yuv420p',"$work/o5.avif") }
    'picture 4K → WebP'              = @{ args = @('-y','-i',$in.uhd,'-c:v','libwebp','-quality','90',"$work/o6.webp") }
    'picture 4K → PNG'               = @{ args = @('-y','-i',$in.jpgs,'-c:v','png','-compression_level','9','-pred','mixed',"$work/o7.png") }
    'picture 4K → JPG, half size'    = @{ args = @('-y','-i',$in.uhd,'-vf','scale=iw/2:-2','-q:v','2',"$work/o8.jpg") }
    'audio 3 min → MP3'              = @{ args = @('-y','-i',$in.song,'-c:a','libmp3lame','-q:a','2',"$work/o9.mp3") }
    'audio 3 min → Opus'             = @{ args = @('-y','-i',$in.song,'-c:a','libopus','-b:a','128k',"$work/o10.opus") }
    'audio 3 min → AAC (M4A)'        = @{ args = @('-y','-i',$in.song,'-c:a','aac','-b:a','192k',"$work/o11.m4a") }
    'audio 3 min → FLAC'             = @{ args = @('-y','-i',$in.song,'-c:a','flac',"$work/o12.flac") }
}

function Time($exe, $t) {
    $n = if ($t.repeat) { $t.repeat } else { 1 }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt $n; $i++) {
        if ((Run $exe $t.args) -ne 0) { return $null }
    }
    return $sw.Elapsed.TotalMilliseconds
}

$rows = @(); $sumOld = 0; $sumNew = 0
foreach ($name in $tests.Keys) {
    $t = $tests[$name]
    $eOld = if ($t.probe) { $oldProbe } else { $oldFf }
    $eNew = if ($t.probe) { $newProbe } else { $newFf }
    $o = @(); $n = @()
    # warm the disk cache, then alternate old/new so both see the same machine state
    Time $eOld $t | Out-Null; Time $eNew $t | Out-Null
    for ($r = 0; $r -lt $Runs; $r++) { $o += Time $eOld $t; $n += Time $eNew $t }
    if ($o -contains $null -or $n -contains $null) {
        $rows += "| $name | $(if ($o -contains $null) {'fails'} else {'ok'}) | $(if ($n -contains $null) {'fails'} else {'ok'}) | — |"
        if ($env:GITHUB_ACTIONS) { Write-Host "::notice title=bench::$name - old: $(if ($o -contains $null) {'fails'} else {'ok'}), new: $(if ($n -contains $null) {'fails'} else {'ok'})" }
        if (($n -contains $null) -and -not ($o -contains $null)) { $script:broken = $true; Write-Host "!! $name works on the old build but not the new one" }
        continue
    }
    $mo = ($o | Sort-Object)[[int][Math]::Floor($Runs / 2)]; $mn = ($n | Sort-Object)[[int][Math]::Floor($Runs / 2)]
    $sumOld += $mo; $sumNew += $mn
    $pct = ($mn - $mo) / $mo * 100
    $rows += "| $name | $([Math]::Round($mo)) ms | $([Math]::Round($mn)) ms | $('{0:+0.0;-0.0}' -f $pct)% |"
    $line = "{0,-40} old {1,7:0} ms   new {2,7:0} ms   {3,6:+0.0;-0.0}%" -f $name, $mo, $mn, $pct
    Write-Host $line
    if ($env:GITHUB_ACTIONS) { Write-Host "::notice title=bench::$line" }
}

function Size($dir) { (Get-ChildItem $dir -File | Where-Object { $_.Name -match '\.(exe|dll)$' -and $_.Name -ne 'ffplay.exe' } | Measure-Object Length -Sum).Sum / 1MB }
$total = ($sumNew - $sumOld) / $sumOld * 100
$md = @(
    '## FFmpeg: ShiftDrop build vs the previous build', '',
    "Size of ffmpeg + ffprobe (+ DLLs): **$([Math]::Round((Size $Old),1)) MB → $([Math]::Round((Size $New),1)) MB**", '',
    "Median of $Runs runs on the same machine. Negative = faster.", '',
    '| Work | Before | Now | Change |', '|---|---:|---:|---:|'
) + $rows + @('', "**Overall: $('{0:+0.0;-0.0}' -f $total)%** ($([Math]::Round($sumOld/1000,1)) s → $([Math]::Round($sumNew/1000,1)) s)")
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$md -join "`n" | Set-Content -Encoding UTF8 $Out
Get-Content $Out | Write-Host
if ($env:GITHUB_ACTIONS) { Write-Host "::notice title=bench total::size $([Math]::Round((Size $Old),1)) MB -> $([Math]::Round((Size $New),1)) MB, time $([Math]::Round($sumOld/1000,1)) s -> $([Math]::Round($sumNew/1000,1)) s ($('{0:+0.0;-0.0}' -f $total)%)" }
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue

if ($script:broken) { throw 'Some work fails with the new build' }
if ($total -gt $Slack) { throw "The new build is $([Math]::Round($total,1))% slower overall" }
