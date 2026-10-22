<#
.SYNOPSIS
  End-to-end check: a purchase is confirmed, then orders placed while only the consumer is stopped are processed after it restarts.
.DESCRIPTION
  Assumes the stack is already running (docker compose up -d --wait api consumer). The emulator is never restarted,
  because that would lose the waiting messages. Exits non-zero on any failure.
#>
param([int]$ApiPort = 8080, [int]$TimeoutSeconds = 60)

. "$PSScriptRoot/common.ps1"
$api = "http://localhost:$ApiPort"

function Get-OrderStatus { param($OrderId, $Token) (Invoke-Api -Method Get -Url "$api/api/orders/$OrderId" -Token $Token).Body.status }
function Buy { param($VariantId, $Token) Invoke-Api -Method Post -Url "$api/api/orders" -Token $Token -Body @{ variantId = $VariantId; quantity = 1 } }

$adminToken = Get-AdminToken -Api $api
$buyerToken = New-BuyerToken -Api $api
$product = New-TestProduct -Api $api -AdminToken $adminToken -Quantity 10

Write-Host '== 1. Normal purchase becomes Confirmed (0 = Pending, 1 = Confirmed, 2 = Rejected)'
$first = Buy $product.VariantId $buyerToken
Assert-Equal $first.Status 202 'POST /api/orders status'
Wait-Until -TimeoutSeconds $TimeoutSeconds -What 'first order to be Confirmed' -Condition { (Get-OrderStatus $first.Body.id $buyerToken) -eq 1 }
Write-Host 'PASS: first order Confirmed'

Write-Host '== 2. Stop only the consumer, place 3 orders'
Invoke-Compose stop consumer
$pending = @(1..3 | ForEach-Object { Buy $product.VariantId $buyerToken })
foreach ($order in $pending) { Assert-Equal $order.Status 202 'order accepted while consumer is down' }
Start-Sleep -Seconds 5
foreach ($order in $pending) { Assert-Equal (Get-OrderStatus $order.Body.id $buyerToken) 0 'order stays Pending while consumer is down' }

Write-Host '== 3. Start the consumer, all 3 are processed exactly once'
Invoke-Compose start consumer
foreach ($order in $pending) {
    Wait-Until -TimeoutSeconds $TimeoutSeconds -What "order $($order.Body.id) to be Confirmed" -Condition { (Get-OrderStatus $order.Body.id $buyerToken) -eq 1 }
}
$stock = (Invoke-Api -Method Get -Url "$api/api/variants/$($product.VariantId)" -Token $adminToken).Body.quantity
Assert-Equal $stock 6 'stock after 4 purchases of 1 from 10 (no double processing)'
Write-Host 'E2E CONSUMER RECOVERY PASSED'
