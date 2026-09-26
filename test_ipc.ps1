$pipeName = 'BiosOptimizerIpcPipe'
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $pipeName, [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
$pipe.Connect(5000)

$msg = [PSCustomObject]@{ Type = 15; Payload = 'Normal' }
$json = ($msg | ConvertTo-Json -Compress) + "`n"
$bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
$pipe.Write($bytes, 0, $bytes.Length)

$reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)
while ($true) {
    $line = $reader.ReadLine()
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    Write-Host "APPLY_TIER_RESPONSE:" $line
    if ($line -match 'FinalResult') { break }
}

$pipe.Dispose()

$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $pipeName, [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
$pipe.Connect(5000)

$msg = [PSCustomObject]@{ Type = 14; Payload = 'Normal' }
$json = ($msg | ConvertTo-Json -Compress) + "`n"
$bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
$pipe.Write($bytes, 0, $bytes.Length)

$reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)
$line = $reader.ReadLine()
Write-Host "PREVIEW_TIER_RESPONSE:" $line

$pipe.Dispose()
