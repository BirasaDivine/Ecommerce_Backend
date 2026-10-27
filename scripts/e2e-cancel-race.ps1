<#
.SYNOPSIS
  End-to-end check on the real stack (SQL Server): cancelling an order and the consumer never both win.
.DESCRIPTION
  Assumes the stack is already running (docker compose up -d --wait api consumer). Exits non-zero on any failure.
  The consumer is always started again before the script ends, even when a check fails.
  1. Backlog race: with only the consumer stopped, a burst of orders for one SKU is bought. The consumer is then
     started and, at the same moment, the orders are cancelled newest first. The consumer works through the
     backlog oldest first, so the two meet in the middle and contest the orders there. Each order must end
     Cancelled (cancel got 200) or Confirmed (cancel got 409), and stock must drop only for Confirmed orders.
     The script warns if one side never won. A run that hit the lost-race path logs
     "cancelled while being processed" in the consumer log.
  2. Ownership and state checks: another user's order gives 404, a Confirmed order gives 409.
  3. With only the consumer stopped, orders are bought and some cancelled. After the consumer restarts it must
     skip the cancelled ones and leave their stock alone.
#>
param([int]$ApiPort = 8080, [int]$Burst = 150, [int]$TimeoutSeconds = 90)

. "$PSScriptRoot/common.ps1"
$api = "http://localhost:$ApiPort"
$repoRoot = $script:RepoRoot

# 0 = Pending, 1 = Confirmed, 2 = Rejected, 3 = Cancelled
function Get-OrderStatus { param($OrderId, $Token) (Invoke-Api -Method Get -Url "$api/api/orders/$OrderId" -Token $Token).Body.status }
function Buy { param($VariantId, $Token) Invoke-Api -Method Post -Url "$api/api/orders" -Token $Token -Body @{ variantId = $VariantId; quantity = 1 } }
function Cancel { param($OrderId, $Token) Invoke-Api -Method Post -Url "$api/api/orders/$OrderId/cancel" -Token $Token }
function Get-Stock { param($VariantId, $Token) (Invoke-Api -Method Get -Url "$api/api/variants/$VariantId" -Token $Token).Body.quantity }

$adminToken = Get-AdminToken -Api $api
$buyerToken = New-BuyerToken -Api $api
$otherToken = New-BuyerToken -Api $api
$initialStock = $Burst + 20
$product = New-TestProduct -Api $api -AdminToken $adminToken -Quantity $initialStock

try {
    Write-Host "== 1. Backlog of $Burst orders, consumer started while they are being cancelled newest first"
    Invoke-Compose stop consumer
    $bought = @(1..$Burst | ForEach-Object { Buy $product.VariantId $buyerToken })
    foreach ($b in $bought) { Assert-Equal $b.Status 202 'order accepted while consumer is down' }

    $since = (Get-Date).ToUniversalTime().ToString('o')
    $startConsumer = Start-Job -ScriptBlock { & docker compose --project-directory $using:repoRoot start consumer *> $null }
    # Begin cancelling as soon as the consumer is up and starts working through the backlog.
    Wait-Until -TimeoutSeconds 60 -What 'the consumer to start' -Condition {
        $null -ne (& docker compose --project-directory $repoRoot logs consumer --since $since --no-log-prefix 2>&1 | Select-String 'Application started')
    }
    $results = @()
    foreach ($b in ($bought | Sort-Object { $_.Body.id } -Descending)) {
        $cancel = Cancel $b.Body.id $buyerToken
        if ($cancel.Status -ne 200 -and $cancel.Status -ne 409) { throw "FAIL: cancel of order $($b.Body.id) returned HTTP $($cancel.Status), expected 200 or 409" }
        $results += @{ Id = $b.Body.id; Cancel = $cancel.Status }
    }
    $null = Wait-Job $startConsumer -Timeout 60
    Remove-Job $startConsumer -Force

    foreach ($r in $results) {
        Wait-Until -TimeoutSeconds $TimeoutSeconds -What "order $($r.Id) to leave Pending" -Condition { (Get-OrderStatus $r.Id $buyerToken) -ne 0 }
    }
    $confirmed = 0
    foreach ($r in $results) {
        $status = Get-OrderStatus $r.Id $buyerToken
        if ($r.Cancel -eq 200) { Assert-Equal $status 3 "order $($r.Id): cancel won, so it is Cancelled" }
        else { Assert-Equal $status 1 "order $($r.Id): cancel got 409, so it is Confirmed"; $confirmed++ }
    }
    $cancelled = $Burst - $confirmed
    Write-Host "Outcomes: $cancelled Cancelled, $confirmed Confirmed"
    if ($cancelled -eq 0 -or $confirmed -eq 0) { Write-Warning 'Only one side ever won, so the race was not contested. Run it again or raise -Burst.' }
    Assert-Equal (Get-Stock $product.VariantId $adminToken) ($initialStock - $confirmed) 'stock dropped only for Confirmed orders'

    Write-Host '== 2. Another user cannot cancel the order (404), and an unknown order gives 404'
    $mine = Buy $product.VariantId $buyerToken
    $foreign = Cancel $mine.Body.id $otherToken
    Assert-Equal $foreign.Status 404 'cancel of another user''s order'
    Assert-Equal (Cancel 999999999 $buyerToken).Status 404 'cancel of an unknown order'
    Wait-Until -TimeoutSeconds $TimeoutSeconds -What 'the order to be Confirmed' -Condition { (Get-OrderStatus $mine.Body.id $buyerToken) -eq 1 }
    Assert-Equal (Cancel $mine.Body.id $buyerToken).Status 409 'cancel of a Confirmed order'

    Write-Host '== 3. Consumer stopped: buy 4, cancel 3, restart. The cancelled ones are skipped'
    $stockBefore = Get-Stock $product.VariantId $adminToken
    Invoke-Compose stop consumer
    $four = @(1..4 | ForEach-Object { Buy $product.VariantId $buyerToken })
    foreach ($o in $four) { Assert-Equal $o.Status 202 'order accepted while consumer is down' }
    foreach ($o in $four[0..2]) { Assert-Equal (Cancel $o.Body.id $buyerToken).Status 200 "order $($o.Body.id) cancelled while Pending" }
    Invoke-Compose start consumer
    Wait-Until -TimeoutSeconds $TimeoutSeconds -What 'the one live order to be Confirmed' -Condition { (Get-OrderStatus $four[3].Body.id $buyerToken) -eq 1 }
    foreach ($o in $four[0..2]) { Assert-Equal (Get-OrderStatus $o.Body.id $buyerToken) 3 "order $($o.Body.id) is still Cancelled" }
    Assert-Equal (Get-Stock $product.VariantId $adminToken) ($stockBefore - 1) 'stock dropped only for the live order'

    $skips = (& docker compose --project-directory $repoRoot logs consumer --tail 400 2>&1 | Select-String 'already Cancelled').Count
    if ($skips -lt 3) { throw "FAIL: expected at least 3 'already Cancelled' skip lines in the consumer log, found $skips" }
    Write-Host "PASS: consumer logged $skips skip lines for cancelled orders"
    Write-Host 'E2E CANCEL RACE PASSED'
}
finally {
    # Never leave the consumer stopped, whatever failed above.
    # docker compose writes progress to stderr, which 'Stop' would turn into a terminating error here.
    $ErrorActionPreference = 'Continue'
    & docker compose --project-directory $repoRoot start consumer 2>&1 | Out-Null
    $global:LASTEXITCODE = 0
}
