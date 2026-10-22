# Helpers shared by the scripts in this folder. Dot-source it: . "$PSScriptRoot/common.ps1"
# Written to run on Windows PowerShell 5.1 and PowerShell 7 (GitHub's Linux runners use 7).

$ErrorActionPreference = 'Stop'
$script:RepoRoot = Split-Path -Parent $PSScriptRoot

function Get-DotEnvValue {
    param([string]$Name)
    $fromProcess = [Environment]::GetEnvironmentVariable($Name)
    if ($fromProcess) { return $fromProcess }
    $envFile = Join-Path $script:RepoRoot '.env'
    if (Test-Path $envFile) {
        foreach ($line in Get-Content $envFile) {
            if ($line -match "^\s*$([regex]::Escape($Name))=(.*)$") { return $Matches[1].Trim() }
        }
    }
    throw "$Name is not set in the environment or in .env"
}

# Returns @{ Status = <int>; Body = <parsed json or $null> } and never throws on a non-2xx status.
function Invoke-Api {
    param([string]$Method, [string]$Url, $Body = $null, [string]$Token = $null)
    $params = @{ Method = $Method; Uri = $Url; UseBasicParsing = $true; TimeoutSec = 15 }
    if ($Token) { $params.Headers = @{ Authorization = "Bearer $Token" } }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 6); $params.ContentType = 'application/json' }
    $content = $null
    try {
        $response = Invoke-WebRequest @params
        $status = [int]$response.StatusCode
        $content = $response.Content
    }
    catch {
        if ($null -eq $_.Exception.Response) { return @{ Status = 0; Body = $null } }
        $status = [int]$_.Exception.Response.StatusCode
    }
    $parsed = $null
    if ($content) { try { $parsed = $content | ConvertFrom-Json } catch { $parsed = $null } }
    return @{ Status = $status; Body = $parsed }
}

function Wait-Until {
    param([scriptblock]$Condition, [int]$TimeoutSeconds, [string]$What)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) { return }
        Start-Sleep -Seconds 2
    }
    throw "Timed out after $TimeoutSeconds s waiting for: $What"
}

function Assert-Equal {
    param($Actual, $Expected, [string]$What)
    if ($Actual -ne $Expected) { throw "FAIL: $What (expected $Expected, got $Actual)" }
    Write-Host "PASS: $What = $Expected"
}

function Invoke-Compose {
    & docker compose --project-directory $script:RepoRoot @args
    if ($LASTEXITCODE -ne 0) { throw "docker compose $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

function Get-AdminToken {
    param([string]$Api)
    $login = Invoke-Api -Method Post -Url "$Api/api/auth/login" -Body @{
        email = (Get-DotEnvValue 'SEED_ADMIN_EMAIL'); password = (Get-DotEnvValue 'SEED_ADMIN_PASSWORD') }
    if ($login.Status -ne 200) { throw "Admin login failed with HTTP $($login.Status)" }
    return $login.Body.token
}

# Registers and logs in a throwaway buyer; the address is generated, not a real person.
function New-BuyerToken {
    param([string]$Api)
    $email = "buyer-$([guid]::NewGuid().ToString('N').Substring(0, 8))@example.test"
    $password = "Buyer123!$([guid]::NewGuid().ToString('N').Substring(0, 6))"
    $null = Invoke-Api -Method Post -Url "$Api/api/auth/register" -Body @{ email = $email; password = $password }
    $login = Invoke-Api -Method Post -Url "$Api/api/auth/login" -Body @{ email = $email; password = $password }
    if ($login.Status -ne 200) { throw "Buyer login failed with HTTP $($login.Status)" }
    return $login.Body.token
}

# Creates a category and a product with one variant (needs an admin token). Returns product and variant ids.
function New-TestProduct {
    param([string]$Api, [string]$AdminToken, [int]$Quantity)
    $suffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
    $category = Invoke-Api -Method Post -Url "$Api/api/categories" -Token $AdminToken -Body @{ name = "Smoke $suffix" }
    if ($category.Status -ne 201 -and $category.Status -ne 200) { throw "Creating the category failed with HTTP $($category.Status)" }
    $product = Invoke-Api -Method Post -Url "$Api/api/products" -Token $AdminToken -Body @{
        name = "Smoke product $suffix"; description = 'created by the scripts'; basePrice = 25; material = 'cotton'
        categoryId = $category.Body.id
        variants = @(@{ name = 'M'; sku = "SMK-$suffix"; quantity = $Quantity }) }
    if ($product.Status -ne 201 -and $product.Status -ne 200) { throw "Creating the product failed with HTTP $($product.Status)" }
    $detail = Invoke-Api -Method Get -Url "$Api/api/products/$($product.Body.id)"
    return @{ ProductId = $product.Body.id; VariantId = $detail.Body.variants[0].id }
}
