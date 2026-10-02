function Get-CaptureReceiptProblems {
 param($Receipt,$Builds,$Processes,[string]$Log,[long]$Length,$Checkpoint,[string]$TailSession,[DateTimeOffset]$Now=[DateTimeOffset]::UtcNow)
 $issues=[Collections.Generic.List[string]]::new()
 try {
  $age=($Now-[DateTimeOffset]::Parse($Receipt.utc)).TotalSeconds
  $process=@($Processes|Where-Object {$_.Id -eq $Receipt.pid})
  if($process.Count -ne 1 -or [Math]::Abs(($process[0].StartTime.ToUniversalTime()-[DateTimeOffset]::Parse($Receipt.processStartUtc).UtcDateTime).TotalSeconds) -gt 1){$issues.Add('Readiness PID/start does not match running game')}
  if($age -lt 0 -or $age -gt 20){$issues.Add('Readiness heartbeat stale or future-dated')}
  if($Receipt.schema -ne 1 -or $Receipt.status -ne 'RECORDING'){$issues.Add('Runtime '+$Receipt.status+': '+$Receipt.reason)}
  $id=[guid]::Empty
  if($Receipt.requiredHooks -le 0 -or ![guid]::TryParse($Receipt.session,[ref]$id)){$issues.Add('Missing runtime hook/session evidence')}
  foreach($build in $Builds){
   $loaded=@($Receipt.assemblies|Where-Object name -eq $build.name)
   if($loaded.Count -ne 1 -or $loaded[0].mvid -ne $build.mvid -or $loaded[0].diskSha256 -ne $build.sha256){$issues.Add('Loaded build mismatch: '+$build.name)}
  }
  if($Receipt.log -ne $Log -or $Length -lt 0){$issues.Add('Readiness log identity missing/mismatched')}
  elseif($Length -lt $Receipt.bytes -or $Receipt.bytes -le 0){$issues.Add('Capture shorter than last flushed receipt or invalid size')}
  if(!$TailSession -or $TailSession -ne $Receipt.session){$issues.Add('Latest complete log row does not match readiness session; recording unverified')}
  if($Checkpoint.Count -ne 6 -or $Checkpoint[0] -ne 'AOC_FRAMEWORK_LOG_V1' -or $Checkpoint[5] -ne 'open' -or [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Checkpoint[1])) -ne $Receipt.campaign){$issues.Add('Runtime campaign/checkpoint mismatch')}
  if($Receipt.discardedRows -ne 0){$issues.Add('Capture has discarded records')}
 }catch{$issues.Add('Unreadable/inconsistent runtime receipt: '+$_.Exception.Message)}
 return $issues.ToArray()
}

function Read-CaptureTailSession {
 param([string]$Path)
 $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
 try{
  $count=[int][Math]::Min(65536,$stream.Length)
  $stream.Seek(-$count,[IO.SeekOrigin]::End)|Out-Null
  $buffer=New-Object byte[] $count
  $read=0
  while($read -lt $count){$n=$stream.Read($buffer,$read,$count-$read);if($n -eq 0){break};$read+=$n}
  $text=[Text.Encoding]::UTF8.GetString($buffer,0,$read)
  $last=$text.LastIndexOf("`n")
  if($last -lt 0){return $null}
  $lines=$text.Substring(0,$last).Split("`n")
  $fields=$lines[-1].TrimEnd("`r").Split("`t")
  if($fields.Count -eq 12 -and $fields[1] -ne 'session'){return $fields[1]}
  return $null # Unfinished/oversized last line is uncertainty, not data corruption.
 }finally{$stream.Dispose()}
}
