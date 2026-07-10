# Mimir Display - Resolution E2E Test
# Compatible with Windows PowerShell 5.1+

param(
    [string]$BrokerHost     = "mimir.local",
    [int]   $BrokerPort     = 1883,
    [string]$Username = "mimir-display",
    [string]$Password       = "tu4kZj37jBvSGrXcKsB57k0x",
    [string]$ExePath        = "MimirDisplay\bin\Release\net8.0-windows\win-x64\MimirDisplay.exe",
    [int]   $StartupWaitSec = 12,
    [int]   $ResizeWaitSec  = 25,
    [switch]$SkipLaunch
)

Add-Type -TypeDefinition @"
using System; using System.Diagnostics; using System.Runtime.InteropServices;
public static class Win32Helper {
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
    public static int[] GetContentSize(string pn) {
        foreach(var p in Process.GetProcessesByName(pn)) {
            if(p.MainWindowHandle==IntPtr.Zero) continue; RECT r;
            if(!GetClientRect(p.MainWindowHandle,out r)) continue;
            uint d=GetDpiForWindow(p.MainWindowHandle);
            return new int[]{r.Right-r.Left,r.Bottom-r.Top,(int)d};
        }
        return new int[]{-1,-1,96};
    }
}
"@ -Language CSharp

function mqttConnect([string]$h,[int]$p,[string]$u,[string]$pw) {
    $tcp=New-Object System.Net.Sockets.TcpClient; $tcp.Connect($h,$p); $s=$tcp.GetStream()
    $enc=[System.Text.Encoding]::UTF8
    $cid=$enc.GetBytes("mimir-test-"+[guid]::NewGuid().ToString("N").Substring(0,8))
    $uB=$enc.GetBytes($u); $pwB=$enc.GetBytes($pw); $proto=$enc.GetBytes("MQTT")
    $payLen=2+$cid.Length+2+$uB.Length+2+$pwB.Length
    $rem=10+$payLen
    $pk=New-Object Collections.Generic.List[byte]
    $pk.Add(0x10); $pk.Add([byte]$rem)
    $pk.Add(0x00); $pk.Add(0x04)
    foreach($b in $proto){$pk.Add($b)}
    $pk.Add(0x04); $pk.Add(0xC2); $pk.Add(0x00); $pk.Add(0x3C)
    $pk.Add([byte]($cid.Length -shr 8)); $pk.Add([byte]($cid.Length -band 0xFF))
    foreach($b in $cid){$pk.Add($b)}
    $pk.Add([byte]($uB.Length -shr 8)); $pk.Add([byte]($uB.Length -band 0xFF))
    foreach($b in $uB){$pk.Add($b)}
    $pk.Add([byte]($pwB.Length -shr 8)); $pk.Add([byte]($pwB.Length -band 0xFF))
    foreach($b in $pwB){$pk.Add($b)}
    $arr=$pk.ToArray(); $s.Write($arr,0,$arr.Length); $s.Flush()
    $ack=New-Object byte[] 4; $s.Read($ack,0,4)|Out-Null
    if($ack[0]-ne 0x20-or $ack[3]-ne 0x00){throw "CONNACK failed code $($ack[3])"}
    return @{Tcp=$tcp;Stream=$s}
}

function mqttSub($conn,[string[]]$topics) {
    $s=$conn.Stream; $enc=[System.Text.Encoding]::UTF8
    $pl=New-Object Collections.Generic.List[byte]
    foreach($t in $topics){$tb=$enc.GetBytes($t);$pl.Add([byte]($tb.Length -shr 8));$pl.Add([byte]($tb.Length -band 0xFF));foreach($b in $tb){$pl.Add($b)};$pl.Add(0x01)}
    $rem=2+$pl.Count; $pkt=New-Object byte[] (2+$rem)
    $pkt[0]=0x82;$pkt[1]=[byte]$rem;$pkt[2]=0x00;$pkt[3]=0x01
    [Array]::Copy($pl.ToArray(),0,$pkt,4,$pl.Count)
    $s.Write($pkt,0,$pkt.Length);$s.Flush()
    $ack=New-Object byte[] ($topics.Count+5); $s.Read($ack,0,$ack.Length)|Out-Null
}

function mqttRead($conn,[int]$ms) {
    $s=$conn.Stream; $conn.Tcp.ReceiveTimeout=$ms
    try {
        $h=New-Object byte[] 1; if($s.Read($h,0,1)-eq 0){return $null}
        $type=($h[0] -band 0xF0) -shr 4
        $rem=0;$shift=0
        do{$b=New-Object byte[] 1;$s.Read($b,0,1)|Out-Null;$rem=$rem -bor(($b[0] -band 0x7F) -shl $shift);$shift+=7}while(($b[0] -band 0x80)-ne 0)
        if($rem-eq 0){return @{T=$type;Topic="";Body=""}}
        $data=New-Object byte[] $rem;$got=0
        while($got-lt $rem){$n=$s.Read($data,$got,$rem-$got);if($n-eq 0){break};$got+=$n}
        if($type-eq 3){
            $tLen=($data[0] -shl 8) -bor $data[1]
            $topic=[System.Text.Encoding]::UTF8.GetString($data,2,$tLen)
            $pOff=2+$tLen; if(($h[0] -band 0x06)-eq 0x02){$pOff+=2}
            $body=[System.Text.Encoding]::UTF8.GetString($data,$pOff,$rem-$pOff)
            return @{T=$type;Topic=$topic;Body=$body}
        }
        return @{T=$type;Topic="";Body=""}
    } catch {return $null}
}

function getRes([string]$json) {
    try{$o=$json|ConvertFrom-Json;if($o.capabilities -and $o.capabilities.resolution){$r=$o.capabilities.resolution;return @{W=[int]$r[0];H=[int]$r[1]}}}catch{}
    return $null
}

$PASS=0;$FAIL=0
function ok([string]$lbl,$got,$want,[int]$tol=4){
    $d=[Math]::Abs($got-$want)
    if($d-le $tol){$script:PASS++;Write-Host ("  PASS  {0}: {1} (want {2} diff {3})" -f $lbl,$got,$want,$d) -ForegroundColor Green}
    else{$script:FAIL++;Write-Host ("  FAIL  {0}: {1} (want {2} diff {3} > tol {4})" -f $lbl,$got,$want,$d,$tol) -ForegroundColor Red}
}

Write-Host "=== Mimir Resolution E2E Test ===" -ForegroundColor Cyan
Write-Host ""

$stateFile=Join-Path $env:APPDATA "MimirDisplay\state\display-state.json"
$deviceId=$null
if(Test-Path $stateFile){try{$st=Get-Content $stateFile -Raw|ConvertFrom-Json;if($st.ServerAssignedDisplayId){$deviceId=$st.ServerAssignedDisplayId}elseif($st.AssignedId){$deviceId=$st.AssignedId}}catch{}}
if(-not $deviceId){$deviceId = [System.Net.Dns]::GetHostName().ToLower() -replace "[^a-z0-9-]","-"}
Write-Host "Device ID: $deviceId" -ForegroundColor Yellow

Write-Host "Connecting to $BrokerHost`:$BrokerPort ..." -ForegroundColor Cyan
$conn=mqttConnect $BrokerHost $BrokerPort $Username $Password
Write-Host "Connected." -ForegroundColor Green
$sT="mimir/$deviceId/status"; $eT="mimir/$deviceId/evt"
mqttSub $conn @($sT,$eT)
Write-Host "Subscribed to $sT and $eT" -ForegroundColor Yellow
Write-Host ""

if(-not $SkipLaunch){
    Get-Process MimirDisplay -ErrorAction SilentlyContinue|Stop-Process -Force
    Start-Sleep -Milliseconds 600
    $exeR=Resolve-Path $ExePath -ErrorAction SilentlyContinue
    if(-not $exeR){Write-Host "EXE not found: $ExePath" -ForegroundColor Red;mqttClose $conn;exit 1}
    Write-Host "Launching app..." -ForegroundColor Yellow
    Start-Process -FilePath $exeR.Path -WorkingDirectory (Split-Path $exeR.Path)|Out-Null
    Write-Host "Waiting ${StartupWaitSec}s..." -ForegroundColor Yellow
    Start-Sleep -Seconds $StartupWaitSec
}

Write-Host "Listening for initial resolution publish..." -ForegroundColor Cyan
$initRes=$null
$dl=(Get-Date).AddSeconds(18)
while((Get-Date)-lt $dl -and -not $initRes){
    $m=mqttRead $conn 2000
    if($m -and $m.T-eq 3 -and $m.Body){$r=getRes $m.Body;if($r){$initRes=$r;Write-Host ("  Got [{0}]: {1} x {2}" -f $m.Topic,$r.W,$r.H) -ForegroundColor Yellow}}
}
if(-not $initRes){Write-Host "TIMEOUT - no resolution in 18s" -ForegroundColor Red;mqttClose $conn;exit 1}

Start-Sleep -Seconds 1
$wm=[Win32Helper]::GetContentSize("MimirDisplay")
$wW=$wm[0];$wH=$wm[1];$wDpi=$wm[2]
Write-Host ("Win32 GetClientRect: {0} x {1} px  DPI={2}" -f $wW,$wH,$wDpi) -ForegroundColor Yellow
Write-Host ""

Write-Host "-- Assertion 1: Initial resolution --" -ForegroundColor Cyan
if($wW-gt 0){ok "Width  (MQTT vs Win32)" $initRes.W $wW;ok "Height (MQTT vs Win32)" $initRes.H $wH}
else{Write-Host "  SKIP (window handle not found)" -ForegroundColor Yellow}

Write-Host ""
Write-Host "-- Assertion 2: Resize --" -ForegroundColor Cyan
Write-Host ">>> RESIZE the MimirDisplay window now. ${ResizeWaitSec}s to resize. <<<" -ForegroundColor White
Write-Host ""

$rzRes=$null; $dl=(Get-Date).AddSeconds($ResizeWaitSec)
while((Get-Date)-lt $dl -and -not $rzRes){
    $m=mqttRead $conn 1500
    if($m -and $m.T-eq 3 -and $m.Body){$r=getRes $m.Body;if($r -and($r.W-ne $initRes.W -or $r.H-ne $initRes.H)){$rzRes=$r;Write-Host ("  Resize publish: {0} x {1}" -f $r.W,$r.H) -ForegroundColor Yellow}}
}
if($rzRes){
    Start-Sleep -Milliseconds 400
    $a=[Win32Helper]::GetContentSize("MimirDisplay")
    Write-Host ("  Win32 after resize: {0} x {1}" -f $a[0],$a[1]) -ForegroundColor Yellow
    ok "Width  after resize" $rzRes.W $a[0]
    ok "Height after resize" $rzRes.H $a[1]
} else {
    Write-Host "  SKIP (no resize in ${ResizeWaitSec}s)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "-- DPI --" -ForegroundColor Cyan
$scale=[Math]::Round($wDpi/96.0,2)
Write-Host ("  DPI={0}  scale={1}x" -f $wDpi,$scale) -ForegroundColor Yellow
if($scale-ne 1.0){
    Write-Host ("  Published WPF DIPs : {0} x {1}" -f $initRes.W,$initRes.H) -ForegroundColor Yellow
    Write-Host ("  Physical pixels    : {0} x {1}" -f ([int]($initRes.W*$scale)),[int]($initRes.H*$scale)) -ForegroundColor Yellow
    Write-Host "  NOTE: Server needs physical pixels if rendering at native resolution." -ForegroundColor Red
}

Write-Host ""
Write-Host "==============================" -ForegroundColor Cyan
if($FAIL-eq 0){Write-Host ("ALL {0} ASSERTIONS PASSED" -f $PASS) -ForegroundColor Green}
else{Write-Host ("{0} FAILED  {1} PASSED" -f $FAIL,$PASS) -ForegroundColor Red}
Write-Host "==============================" -ForegroundColor Cyan

try{$conn.Tcp.Close()}catch{}
if($FAIL-gt 0){exit 1}else{exit 0}



