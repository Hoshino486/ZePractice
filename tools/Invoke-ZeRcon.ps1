param([Parameter(Mandatory=$true)][string]$Command,[string]$SettingsPath='')
$ErrorActionPreference='Stop'
# Resolve script-local defaults after param binding (Windows PowerShell 5.1 -File).
if([string]::IsNullOrWhiteSpace($SettingsPath)){$SettingsPath=Join-Path (Split-Path -Parent $PSScriptRoot) 'server-settings.json'}
# Windows PowerShell 5.1 defaults to ANSI for BOM-less files; read JSON explicitly as UTF-8.
try {
 $settings=Get-Content -LiteralPath $SettingsPath -Raw -Encoding UTF8|ConvertFrom-Json -ErrorAction Stop
} catch {
 throw 'Failed to read server-settings.json as UTF-8 JSON. Check JSON quotes and escape Windows path backslashes (\\). Configuration values are not printed.'
}
$hostAddress=if($settings.BindAddress -in @('0.0.0.0','::')){'127.0.0.1'}else{[string]$settings.BindAddress}
$client=[Net.Sockets.TcpClient]::new(); $stream=$null
function Write-Packet([int]$id,[int]$type,[string]$body){
 $data=[Text.Encoding]::UTF8.GetBytes($body)
 $packet=[byte[]]::new($data.Length+14)
 [Array]::Copy([BitConverter]::GetBytes($data.Length+10),0,$packet,0,4)
 [Array]::Copy([BitConverter]::GetBytes($id),0,$packet,4,4)
 [Array]::Copy([BitConverter]::GetBytes($type),0,$packet,8,4)
 [Array]::Copy($data,0,$packet,12,$data.Length)
 $stream.Write($packet,0,$packet.Length)
}
function Read-Exact([int]$length){
 $buffer=[byte[]]::new($length);$offset=0
 while($offset -lt $length){$n=$stream.Read($buffer,$offset,$length-$offset);if($n -le 0){throw 'RCON connection closed'};$offset+=$n}
 return ,$buffer
}
function Read-Packet {
 $header=Read-Exact 4; $size=[BitConverter]::ToInt32($header,0)
 if($size -lt 10 -or $size -gt 4194304){throw 'Invalid RCON packet length'}
 $body=Read-Exact $size
 return [pscustomobject]@{Id=[BitConverter]::ToInt32($body,0);Type=[BitConverter]::ToInt32($body,4);Text=[Text.Encoding]::UTF8.GetString($body,8,$size-10)}
}
try{
 $task=$client.ConnectAsync($hostAddress,[int]$settings.Port)
 if(-not $task.Wait(5000)){throw 'RCON connect timed out. Is the server running?'}
 $stream=$client.GetStream();$stream.ReadTimeout=10000;$stream.WriteTimeout=5000
 Write-Packet 1 3 ([string]$settings.RconPassword)
 $authenticated=$false
 for($i=0;$i -lt 5;$i++){$packet=Read-Packet;if($packet.Id -eq -1){throw 'RCON authentication failed'};if($packet.Id -eq 1 -and $packet.Type -eq 2){$authenticated=$true;break}}
 if(-not $authenticated){throw 'No RCON authentication confirmation'}
 Write-Packet 2 2 $Command
 $output=[Text.StringBuilder]::new()
 $stopwatch=[Diagnostics.Stopwatch]::StartNew()
 $lastPacket=[Diagnostics.Stopwatch]::StartNew()
 $received=$false
 while($stopwatch.ElapsedMilliseconds -lt 5000){
  if(-not $stream.DataAvailable){
   if($received -and $lastPacket.ElapsedMilliseconds -gt 250){break}
   Start-Sleep -Milliseconds 20
   continue
  }
  $packet=Read-Packet
  Write-Verbose ("RCON packet id="+$packet.Id+" type="+$packet.Type+" bytes="+$packet.Text.Length)
  if($packet.Id -eq 2){[void]$output.Append($packet.Text);$received=$true}
  $lastPacket.Restart()
 }
 $output.ToString()
}finally{if($stream){$stream.Dispose()};$client.Dispose()}