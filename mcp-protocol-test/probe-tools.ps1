param(
    [Parameter(Mandatory=$true)][string]$SessionId
)

Write-Host "--- notifications/initialized ---"
$notifyBody = '{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}'
$notifyResp = Invoke-WebRequest -Uri "http://localhost:5701/mcp" -Method Post -ContentType "application/json" -Headers @{ "Accept" = "application/json, text/event-stream"; "Mcp-Session-Id" = $SessionId } -Body $notifyBody -SkipHttpErrorCheck
Write-Host "StatusCode: $($notifyResp.StatusCode)"
Write-Host "Body: $($notifyResp.Content)"

Write-Host "--- tools/list ---"
$listBody = '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
$listResp = Invoke-WebRequest -Uri "http://localhost:5701/mcp" -Method Post -ContentType "application/json" -Headers @{ "Accept" = "application/json, text/event-stream"; "Mcp-Session-Id" = $SessionId } -Body $listBody -SkipHttpErrorCheck
Write-Host "StatusCode: $($listResp.StatusCode)"
Write-Host "Body: $($listResp.Content)"

Write-Host "--- tools/call describe_entities ---"
$describeBody = '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"describe_entities","arguments":{}}}'
$describeResp = Invoke-WebRequest -Uri "http://localhost:5701/mcp" -Method Post -ContentType "application/json" -Headers @{ "Accept" = "application/json, text/event-stream"; "Mcp-Session-Id" = $SessionId } -Body $describeBody -SkipHttpErrorCheck
Write-Host "StatusCode: $($describeResp.StatusCode)"
Write-Host "Body: $($describeResp.Content)"

Write-Host "--- tools/call read_records ---"
$readBody = '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"read_records","arguments":{"entity":"Products","first":3}}}'
$readResp = Invoke-WebRequest -Uri "http://localhost:5701/mcp" -Method Post -ContentType "application/json" -Headers @{ "Accept" = "application/json, text/event-stream"; "Mcp-Session-Id" = $SessionId } -Body $readBody -SkipHttpErrorCheck
Write-Host "StatusCode: $($readResp.StatusCode)"
Write-Host "Body: $($readResp.Content)"
