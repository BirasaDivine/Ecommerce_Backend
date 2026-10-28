<#
.SYNOPSIS
  Negative check: with the OpenTelemetry Collector stopped, the shop keeps working.
.DESCRIPTION
  Assumes the stack is running (docker compose up -d --wait api consumer). Stops only the Collector, places an order,
  expects 202 Accepted and the order to become Confirmed, then starts the Collector again.
  Telemetry failures must never break the shop. Exits non-zero on any failure.
#>
param([int]$ApiPort = 8080, [int]$TimeoutSeconds = 60)

. "$PSScriptRoot/common.ps1"
$api = "http://localhost:$ApiPort"

$adminToken = Get-AdminToken -Api $api
$buyerToken = New-BuyerToken -Api $api
$product = New-TestProduct -Api $api -AdminToken $adminToken -Quantity 5

try {
    Write-Host '== Stopping only the Collector'
    Invoke-Compose stop otel-collector

    $order = Invoke-Api -Method Post -Url "$api/api/orders" -Token $buyerToken -Body @{ variantId = $product.VariantId; quantity = 1 }
    Assert-Equal $order.Status 202 'POST /api/orders status with the Collector down'

    Wait-Until -TimeoutSeconds $TimeoutSeconds -What 'the order to be Confirmed' -Condition {
        (Invoke-Api -Method Get -Url "$api/api/orders/$($order.Body.id)" -Token $buyerToken).Body.status -eq 1 }
    Write-Host 'PASS: order Confirmed with the Collector down'

    Assert-Equal (Invoke-Api -Method Get -Url "$api/health/ready").Status 200 '/health/ready status with the Collector down'
    Write-Host 'COLLECTOR-DOWN CHECK PASSED'
}
finally {
    Write-Host '== Starting the Collector again'
    & docker compose --project-directory $script:RepoRoot start otel-collector | Out-Null
}
