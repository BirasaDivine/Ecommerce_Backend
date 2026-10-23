<#
.SYNOPSIS
  Load buffering scenario: 6 units in stock, 20 purchase requests sent at the same time.
.DESCRIPTION
  Assumes the stack is running. Every request is accepted (202) straight away; the consumer then resolves them.
  Expected result: 6 Confirmed and 14 Rejected, and the stock ends at 0. Use it to fill the Grafana dashboard.
  Exits non-zero if the result is different.
#>
param(
    [int]$ApiPort = 8080,
    [int]$Stock = 6,
    [int]$Requests = 20,
    [int]$TimeoutSeconds = 90
)

. "$PSScriptRoot/common.ps1"
Add-Type -AssemblyName System.Net.Http
$api = "http://localhost:$ApiPort"

$adminToken = Get-AdminToken -Api $api
$buyerToken = New-BuyerToken -Api $api
$product = New-TestProduct -Api $api -AdminToken $adminToken -Quantity $Stock

Write-Host "== Sending $Requests purchase requests at once for a variant with $Stock in stock"
$client = New-Object System.Net.Http.HttpClient
$client.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $buyerToken)
$body = @{ variantId = $product.VariantId; quantity = 1 } | ConvertTo-Json
$tasks = @(1..$Requests | ForEach-Object {
    $content = New-Object System.Net.Http.StringContent($body, [System.Text.Encoding]::UTF8, 'application/json')
    $client.PostAsync("$api/api/orders", $content)
})
[System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$tasks)

$orderIds = @()
foreach ($task in $tasks) {
    Assert-Equal ([int]$task.Result.StatusCode) 202 'purchase accepted'
    $orderIds += ($task.Result.Content.ReadAsStringAsync().Result | ConvertFrom-Json).id
}

Write-Host '== Waiting for the consumer to resolve every order'
$statuses = @{}
Wait-Until -TimeoutSeconds $TimeoutSeconds -What "all $Requests orders to leave Pending" -Condition {
    $script:statuses = @{}
    foreach ($id in $orderIds) { $script:statuses[$id] = (Invoke-Api -Method Get -Url "$api/api/orders/$id" -Token $buyerToken).Body.status }
    -not ($script:statuses.Values -contains 0)
}

$confirmed = @($script:statuses.Values | Where-Object { $_ -eq 1 }).Count
$rejected = @($script:statuses.Values | Where-Object { $_ -eq 2 }).Count
Write-Host "Confirmed: $confirmed  Rejected: $rejected"
Assert-Equal $confirmed $Stock 'confirmed orders'
Assert-Equal $rejected ($Requests - $Stock) 'rejected orders'
Assert-Equal (Invoke-Api -Method Get -Url "$api/api/variants/$($product.VariantId)" -Token $adminToken).Body.quantity 0 'stock left'
Write-Host 'LOAD TEST PASSED'
