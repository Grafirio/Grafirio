# 🚀 Quick Test Script - Run All Tests

Write-Host "╔════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     GRAFIRIO - API Testing Suite                      ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Test 1: Health Checks
Write-Host "🔍 Test 1: Health Check All Services" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$services = @(
    @{ Name = "Gateway"; Url = "http://localhost:5000/health" },
    @{ Name = "Identity API"; Url = "http://localhost:5036/health" },
    @{ Name = "Catalog API"; Url = "http://localhost:5280/health" },
    @{ Name = "Basket API"; Url = "http://localhost:5023/health" },
    @{ Name = "Data Analysis"; Url = "http://localhost:5221/health" }
)

$healthResults = @()

foreach ($service in $services) {
    try {
        $response = Invoke-RestMethod -Uri $service.Url -Method Get -TimeoutSec 3
        Write-Host "  ✅ $($service.Name.PadRight(20)): $($response.Status)" -ForegroundColor Green
        $healthResults += [PSCustomObject]@{ Service = $service.Name; Status = "Healthy" }
    }
    catch {
        Write-Host "  ❌ $($service.Name.PadRight(20)): Offline" -ForegroundColor Red
        $healthResults += [PSCustomObject]@{ Service = $service.Name; Status = "Offline" }
    }
}

Write-Host ""

# Test 2: Gateway Routing
Write-Host "🌐 Test 2: Gateway Routing" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$gatewayTests = @(
    @{ Name = "Identity Route"; Url = "http://localhost:5000/v1/identity/companies"; Direct = "http://localhost:5036/api/v1/companies" },
    @{ Name = "Catalog Route"; Url = "http://localhost:5000/v1/catalogs/categories"; Direct = "http://localhost:5280/api/v1/categories" }
)

foreach ($test in $gatewayTests) {
    try {
        # Test without auth (should get 401 or return data if public)
        $gatewayResponse = Invoke-WebRequest -Uri $test.Url -Method Get -SkipHttpErrorCheck -TimeoutSec 3
        
        if ($gatewayResponse.StatusCode -in @(200, 401)) {
            Write-Host "  ✅ $($test.Name.PadRight(20)): Routed (Status: $($gatewayResponse.StatusCode))" -ForegroundColor Green
        }
        else {
            Write-Host "  ⚠️  $($test.Name.PadRight(20)): Unexpected status $($gatewayResponse.StatusCode)" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "  ❌ $($test.Name.PadRight(20)): Failed - $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""

# Test 3: Exception Handling
Write-Host "🛡️ Test 3: Exception Handling" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

# Test 404 Error
Write-Host "  Testing 404 Not Found..." -ForegroundColor Gray
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5036/api/v1/nonexistent" -Method Get -ErrorAction Stop
}
catch {
    $errorResponse = $_.ErrorDetails.Message | ConvertFrom-Json
    if ($errorResponse.statusCode -eq 404) {
        Write-Host "  ✅ 404 Error: Correctly formatted error response" -ForegroundColor Green
        Write-Host "     ErrorType: $($errorResponse.errorType)" -ForegroundColor Gray
        Write-Host "     CorrelationId: $($errorResponse.correlationId)" -ForegroundColor Gray
    }
    else {
        Write-Host "  ❌ 404 Error: Unexpected error format" -ForegroundColor Red
    }
}

Write-Host ""

# Test 4: Correlation ID
Write-Host "🔗 Test 4: Correlation ID Propagation" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$testCorrelationId = "test-$(Get-Random -Minimum 1000 -Maximum 9999)"
$headers = @{
    "X-Correlation-Id" = $testCorrelationId
}

try {
    $response = Invoke-WebRequest -Uri "http://localhost:5036/health" -Headers $headers
    $returnedCorrelationId = $response.Headers["X-Correlation-Id"]
    
    if ($returnedCorrelationId -eq $testCorrelationId) {
        Write-Host "  ✅ Correlation ID propagated correctly" -ForegroundColor Green
        Write-Host "     Sent: $testCorrelationId" -ForegroundColor Gray
        Write-Host "     Received: $returnedCorrelationId" -ForegroundColor Gray
    }
    else {
        Write-Host "  ❌ Correlation ID mismatch" -ForegroundColor Red
        Write-Host "     Sent: $testCorrelationId" -ForegroundColor Gray
        Write-Host "     Received: $returnedCorrelationId" -ForegroundColor Gray
    }
}
catch {
    Write-Host "  ❌ Failed to test correlation ID" -ForegroundColor Red
}

Write-Host ""

# Test 5: Authentication Check
Write-Host "🔐 Test 5: Authentication Check" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

try {
    $response = Invoke-WebRequest -Uri "http://localhost:5023/api/v1/baskets/test" -Method Get -SkipHttpErrorCheck
    
    if ($response.StatusCode -eq 401) {
        Write-Host "  ✅ Authentication required (401 Unauthorized)" -ForegroundColor Green
    }
    elseif ($response.StatusCode -eq 200) {
        Write-Host "  ⚠️  Endpoint is public (no authentication required)" -ForegroundColor Yellow
    }
    else {
        Write-Host "  ❌ Unexpected status code: $($response.StatusCode)" -ForegroundColor Red
    }
}
catch {
    Write-Host "  ❌ Failed to test authentication" -ForegroundColor Red
}

Write-Host ""

# Summary
Write-Host "╔════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     TEST SUMMARY                                       ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════╝" -ForegroundColor Cyan

$healthyServices = ($healthResults | Where-Object Status -eq "Healthy").Count
$totalServices = $healthResults.Count

Write-Host ""
Write-Host "Services Online: $healthyServices/$totalServices" -ForegroundColor $(if($healthyServices -eq $totalServices){"Green"}else{"Yellow"})
Write-Host ""

$healthResults | Format-Table -AutoSize

Write-Host ""
Write-Host "✅ Tests Completed!" -ForegroundColor Green
Write-Host ""
Write-Host "For detailed testing, see: TESTING_GUIDE.md" -ForegroundColor Gray
Write-Host ""

# Open test results in browser (optional)
$openBrowser = Read-Host "Open Swagger UI in browser? (y/n)"
if ($openBrowser -eq 'y') {
    Start-Process "http://localhost:5036/swagger"
    Start-Process "http://localhost:5280/swagger"
    Start-Process "http://localhost:5023/swagger"
    Start-Process "http://localhost:5221/swagger"
}
