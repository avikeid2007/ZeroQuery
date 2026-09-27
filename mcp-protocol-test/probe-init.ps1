$body = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"zeroquery-probe","version":"0.1.0"}}}'
$resp = Invoke-WebRequest -Uri "http://localhost:5701/mcp" -Method Post -ContentType "application/json" -Headers @{ "Accept" = "application/json, text/event-stream" } -Body $body -SkipHttpErrorCheck
Write-Host "StatusCode: $($resp.StatusCode)"
Write-Host "Headers:"
$resp.Headers.GetEnumerator() | ForEach-Object { Write-Host "  $($_.Key): $($_.Value)" }
Write-Host "Body:"
Write-Host $resp.Content
