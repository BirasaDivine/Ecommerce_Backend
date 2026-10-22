<#
.SYNOPSIS
  Container smoke test: starts the compose stack, checks it works, then checks readiness reacts to a dead database.
.DESCRIPTION
  Positive: stack up, /health/ready returns 200, GET /api/products/{id} returns 200 for a product the script creates.
  Negative: with the db container stopped, /health/ready returns 503 while /health/live still returns 200.
  Exits non-zero on any failure. Tears the stack down (volumes included) unless -KeepStack is given.
#>
param(
    [int]$ApiPort = 8080,
    [int]$TimeoutSeconds = 240,
    [switch]$NoBuild,
    [switch]$KeepStack
)

. "$PSScriptRoot/common.ps1"
$api = "http://localhost:$ApiPort"

try {
    Write-Host '== Starting the stack'
    $upArgs = @('up', '-d', '--wait', 'api', 'consumer')
    if (-not $NoBuild) { $upArgs += '--build' }
    Invoke-Compose @upArgs

    Write-Host '== Positive: ready, then read a product'
    Wait-Until -TimeoutSeconds $TimeoutSeconds -What '/health/ready to return 200' -Condition {
        (Invoke-Api -Method Get -Url "$api/health/ready").Status -eq 200 }
    Write-Host 'PASS: /health/ready = 200'

    $adminToken = Get-AdminToken -Api $api
    $product = New-TestProduct -Api $api -AdminToken $adminToken -Quantity 1
    Assert-Equal (Invoke-Api -Method Get -Url "$api/api/products/$($product.ProductId)").Status 200 'GET /api/products/{id} status'

    Write-Host '== Negative: stop only the database'
    Invoke-Compose stop db
    Wait-Until -TimeoutSeconds 60 -What '/health/ready to return 503' -Condition {
        (Invoke-Api -Method Get -Url "$api/health/ready").Status -eq 503 }
    Assert-Equal (Invoke-Api -Method Get -Url "$api/health/ready").Status 503 '/health/ready status with db stopped'
    Assert-Equal (Invoke-Api -Method Get -Url "$api/health/live").Status 200 '/health/live status with db stopped'

    Write-Host 'SMOKE TEST PASSED'
}
finally {
    if ($KeepStack) { Write-Host 'Leaving the stack running (-KeepStack).' }
    else { Write-Host '== Tearing the stack down'; & docker compose --project-directory $script:RepoRoot down -v | Out-Null }
}
