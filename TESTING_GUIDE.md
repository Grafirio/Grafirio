# 🧪 Testing Guide - Gateway, Auth & Exception Handling

## 🎯 Test Scenarios

### 1. Gateway Routing Tests

#### A. Test Identity API through Gateway
```powershell
# Direct access (bypassing gateway)
curl http://localhost:5036/health

# Through gateway
curl http://localhost:5000/v1/identity/companies
```

#### B. Test Catalog API through Gateway
```powershell
# Direct access
curl http://localhost:5280/health

# Through gateway
curl http://localhost:5000/v1/catalogs/categories
```

#### C. Test Basket API through Gateway
```powershell
# Direct access
curl http://localhost:5023/health

# Through gateway
curl http://localhost:5000/v1/baskets/my-basket
```

#### D. Test Data Analysis API through Gateway
```powershell
# Direct access
curl http://localhost:5221/health

# Through gateway
curl http://localhost:5000/api/ai/reports/ask-question
```

---

### 2. Health Check Tests

```powershell
# Test all services health
$services = @(
    @{ Name = "Gateway"; Url = "http://localhost:5000/health" },
    @{ Name = "Identity API"; Url = "http://localhost:5036/health" },
    @{ Name = "Catalog API"; Url = "http://localhost:5280/health" },
    @{ Name = "Basket API"; Url = "http://localhost:5023/health" },
    @{ Name = "Data Analysis API"; Url = "http://localhost:5221/health" }
)

foreach ($service in $services) {
    Write-Host "`n🔍 Testing $($service.Name)..." -ForegroundColor Cyan
    try {
        $response = Invoke-RestMethod -Uri $service.Url -Method Get
        Write-Host "✅ $($service.Name): $($response.Status)" -ForegroundColor Green
        Write-Host "   Service: $($response.Service)" -ForegroundColor Gray
        Write-Host "   Time: $($response.Timestamp)" -ForegroundColor Gray
    }
    catch {
        Write-Host "❌ $($service.Name): Failed" -ForegroundColor Red
        Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}
```

---

### 3. Exception Handling Tests

#### A. Test 404 Not Found
```powershell
# Should return structured error response
curl -v http://localhost:5036/api/v1/nonexistent

# Expected response:
# {
#   "statusCode": 404,
#   "message": "The requested resource was not found.",
#   "errorType": "NotFound",
#   "correlationId": "...",
#   "timestamp": "...",
#   "path": "/api/v1/nonexistent",
#   "method": "GET"
# }
```

#### B. Test Validation Error
```powershell
# Invalid company creation request
$body = @{
    name = ""  # Empty name should fail validation
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5036/api/v1/companies" `
    -Method POST `
    -Body $body `
    -ContentType "application/json"

# Expected: 400 Bad Request with validation errors
```

#### C. Test Unauthorized Access
```powershell
# Try to access protected endpoint without token
curl -v http://localhost:5023/api/v1/baskets/my-basket

# Expected: 401 Unauthorized
```

#### D. Test Correlation ID Propagation
```powershell
# Send request with custom correlation ID
$headers = @{
    "X-Correlation-Id" = "test-correlation-123"
}

$response = Invoke-WebRequest -Uri "http://localhost:5036/health" `
    -Headers $headers

# Check response header
$response.Headers["X-Correlation-Id"]
# Should return: test-correlation-123
```

---

### 4. Authorization Tests

#### A. Get Keycloak Token
```powershell
$keycloakUrl = "http://localhost:8080/realms/grafirio/protocol/openid-connect/token"

$body = @{
    client_id = "grafirio-client"
    grant_type = "password"
    username = "user@example.com"
    password = "password"
    scope = "openid profile email"
}

$response = Invoke-RestMethod -Uri $keycloakUrl `
    -Method POST `
    -Body $body `
    -ContentType "application/x-www-form-urlencoded"

$token = $response.access_token
Write-Host "Token: $token"
```

#### B. Use Token with API
```powershell
$headers = @{
    Authorization = "Bearer $token"
}

# Get companies
Invoke-RestMethod -Uri "http://localhost:5036/api/v1/companies" `
    -Headers $headers

# Through gateway
Invoke-RestMethod -Uri "http://localhost:5000/v1/identity/companies" `
    -Headers $headers
```

#### C. Test Company Access Validation
```powershell
# Assuming user has token with companyId claim

# Access own company (should succeed)
Invoke-RestMethod -Uri "http://localhost:5036/api/v1/companies/$companyId" `
    -Headers $headers

# Access other company (should fail with 403)
$otherCompanyId = "00000000-0000-0000-0000-000000000000"
Invoke-RestMethod -Uri "http://localhost:5036/api/v1/companies/$otherCompanyId" `
    -Headers $headers
```

---

### 5. IIdentityService Tests

#### Create Test Endpoint
```csharp
// Add to any API for testing
app.MapGet("/test/identity", (IIdentityService identityService) =>
{
    return Results.Ok(new
    {
        userId = identityService.UserId,
        userName = identityService.UserName,
        email = identityService.Email,
        fullName = identityService.FullName,
        roles = identityService.Roles,
        currentCompanyId = identityService.CurrentCompanyId,
        accessibleCompanies = identityService.AccessibleCompanyIds,
        claims = new
        {
            hasEmailClaim = identityService.HasClaim("email"),
            companyIdClaim = identityService.GetClaim("company_id"),
            businessRoles = identityService.GetBusinessRoles()
        }
    });
})
.RequireAuthorization("Password")
.WithTags("Testing");
```

#### Test Identity Info
```powershell
$headers = @{
    Authorization = "Bearer $token"
}

Invoke-RestMethod -Uri "http://localhost:5036/test/identity" `
    -Headers $headers
```

---

### 6. Load Testing (Optional)

#### Using Apache Bench
```bash
# Install: choco install apache-httpd (or use WSL)

# Test health endpoint
ab -n 1000 -c 10 http://localhost:5036/health

# Test with authentication
ab -n 100 -c 5 -H "Authorization: Bearer $TOKEN" \
   http://localhost:5036/api/v1/companies
```

#### Using PowerShell
```powershell
# Simple load test
$url = "http://localhost:5036/health"
$requests = 100

$results = 1..$requests | ForEach-Object -Parallel {
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        Invoke-RestMethod -Uri $using:url
        $success = $true
    }
    catch {
        $success = $false
    }
    $stopwatch.Stop()
    
    [PSCustomObject]@{
        Success = $success
        Duration = $stopwatch.ElapsedMilliseconds
    }
} -ThrottleLimit 10

# Results
$successCount = ($results | Where-Object Success).Count
$avgDuration = ($results | Measure-Object -Property Duration -Average).Average

Write-Host "`nLoad Test Results:"
Write-Host "  Total Requests: $requests"
Write-Host "  Successful: $successCount"
Write-Host "  Failed: $($requests - $successCount)"
Write-Host "  Average Duration: $([math]::Round($avgDuration, 2))ms"
```

---

### 7. Gateway YARP Health Checks

```powershell
# Check YARP health check status
# RabbitMQ Management UI
Start-Process "http://localhost:15672"

# Check service health through Gateway logs
docker logs gateway.container --tail 50 | Select-String "health"
```

---

## 🔍 Troubleshooting

### Problem: Gateway returns 503 Service Unavailable
**Solution:**
```powershell
# Check if backend service is running
curl http://localhost:5036/health

# Check Gateway logs
Get-Content "d:\Projeler\Grifirio\src\services\Grafirio.Gateway\logs\*.log" -Tail 50

# Restart Gateway
# (Gateway terminal - Ctrl+C and restart)
```

### Problem: 401 Unauthorized even with valid token
**Solution:**
```powershell
# Verify token is not expired
# Decode JWT at https://jwt.io

# Check Keycloak configuration
curl http://localhost:8080/realms/grafirio/.well-known/openid-configuration

# Verify audience and issuer match appsettings.json
```

### Problem: Exception not being caught by middleware
**Solution:**
```csharp
// Ensure middleware is registered BEFORE other middleware
app.UseGlobalExceptionHandling(); // Must be early in pipeline
app.UseAuthentication();
app.UseAuthorization();
```

---

## 📊 Expected Results

### Successful Health Check
```json
{
  "status": "Healthy",
  "service": "Identity API",
  "timestamp": "2026-01-26T10:30:00Z"
}
```

### Error Response
```json
{
  "statusCode": 400,
  "message": "Name is required",
  "errorType": "ValidationError",
  "correlationId": "abc-123",
  "timestamp": "2026-01-26T10:30:00Z",
  "path": "/api/v1/companies",
  "method": "POST"
}
```

### Gateway Routing Success
```
Request: http://localhost:5000/v1/identity/companies
         ↓
Gateway: Maps to Identity API
         ↓
Backend: http://localhost:5036/api/v1/companies
         ↓
Response: JSON data
```

---

## ✅ Checklist

Before testing:
- [ ] All services running (Identity, Catalog, Basket, Data Analysis, Gateway)
- [ ] Keycloak running (Port 8080)
- [ ] RabbitMQ running (Port 5672, 15672)
- [ ] Redis running (Port 6379)
- [ ] All appsettings.json files updated

After implementation:
- [ ] Gateway routes all services correctly
- [ ] Health checks return 200 OK
- [ ] Exceptions return structured errors
- [ ] Correlation IDs propagate correctly
- [ ] Authorization works with JWT tokens
- [ ] Company access validation works

---

**Happy Testing! 🎉**
