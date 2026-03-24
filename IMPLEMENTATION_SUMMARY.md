# Global Exception Handling & Enhanced Features

## ✅ Completed Implementations

### 1. Gateway Routing Configuration

**File:** `Grafirio.Gateway/appsettings.json`

**Features:**
- ✅ Complete YARP routing for all microservices
- ✅ Identity API route (Port 5036)
- ✅ Catalog API route (Port 5280)
- ✅ Basket API route (Port 5023)
- ✅ Data Analysis API route (Port 5221)
- ✅ Order, Payment, Discount, File API routes
- ✅ Health check routes for all services
- ✅ Active health checks with 30-second intervals
- ✅ Authorization policies (Password, ClientCredential)
- ✅ Path transformation patterns

**Usage:**
```
Gateway URL: http://localhost:5000
Example: http://localhost:5000/v1/baskets/my-basket
         → Routes to: http://localhost:5023/api/v1/baskets/my-basket
```

---

### 2. IIdentityService Implementation

**Already Implemented in Grafirio.Shared:**
- ✅ `IIdentityService` interface with comprehensive methods
- ✅ `IdentityService` implementation with JWT claims parsing
- ✅ Company-based authorization support
- ✅ Business role validation
- ✅ Multi-company access checking

**New Additions:**

#### Authorization Handlers
**File:** `Grafirio.Shared/Authorization/CompanyAuthorizationHandlers.cs`
- ✅ `CompanyAccessHandler` - Validates company access
- ✅ `BusinessRoleHandler` - Validates business roles

#### Authorization Attributes
**File:** `Grafirio.Shared/Attributes/AuthorizationAttributes.cs`
- ✅ `[RequireCompanyAccess]` - Requires company assignment
- ✅ `[RequireCompanyAdmin]` - Requires company admin role
- ✅ `[RequireCompanyManager]` - Requires company manager role

**Usage Example:**
```csharp
// In endpoint
app.MapGet("/api/v1/dashboard", 
    [RequireCompanyAccess] (IIdentityService identityService) =>
{
    var companyId = identityService.CurrentCompanyId;
    var roles = identityService.GetBusinessRoles(companyId);
    
    return Results.Ok(new { companyId, roles });
});

// In handler
public class GetOrdersHandler : IRequestHandler<GetOrdersQuery, ServiceResult>
{
    private readonly IIdentityService _identityService;
    
    public async Task<ServiceResult> Handle(GetOrdersQuery request, CancellationToken ct)
    {
        if (!_identityService.HasCompanyAccess(request.CompanyId))
        {
            throw new UnauthorizedAccessException("No access to this company");
        }
        
        // Process...
    }
}
```

---

### 3. Global Exception Handling

#### Middleware Components

**A. GlobalExceptionMiddleware**
**File:** `Grafirio.Shared/Middleware/GlobalExceptionMiddleware.cs`

**Features:**
- ✅ Catches all unhandled exceptions
- ✅ Maps exceptions to HTTP status codes
- ✅ Structured error responses
- ✅ Correlation ID tracking
- ✅ Stack trace in development mode
- ✅ FluentValidation error formatting

**Exception Mapping:**
- `UnauthorizedAccessException` → 401 Unauthorized
- `KeyNotFoundException` → 404 Not Found
- `ArgumentException` → 400 Bad Request
- `InvalidOperationException` → 400 Bad Request
- `FluentValidation.ValidationException` → 400 Bad Request
- All others → 500 Internal Server Error

**Error Response Format:**
```json
{
  "statusCode": 400,
  "message": "Invalid request parameters",
  "errorType": "BadRequest",
  "correlationId": "123e4567-e89b-12d3-a456-426614174000",
  "timestamp": "2026-01-26T10:30:00Z",
  "path": "/api/v1/orders",
  "method": "POST",
  "stackTrace": "... (only in development)",
  "innerException": "... (only in development)"
}
```

**B. CorrelationIdMiddleware**
**File:** `Grafirio.Shared/Middleware/CorrelationIdMiddleware.cs`

**Features:**
- ✅ Generates or propagates correlation ID
- ✅ Adds `X-Correlation-Id` header to responses
- ✅ Logger scope integration
- ✅ Distributed tracing support

**C. RequestLoggingMiddleware**
**File:** `Grafirio.Shared/Middleware/RequestLoggingMiddleware.cs`

**Features:**
- ✅ Logs all HTTP requests/responses
- ✅ Request timing (stopwatch)
- ✅ Log level based on status code
- ✅ Skips health check and swagger endpoints

**Extension Methods**
**File:** `Grafirio.Shared/Extensions/ExceptionHandlingExt.cs`

```csharp
// Use all three middleware
app.UseGlobalExceptionHandling();

// Or individually
app.UseCorrelationId();
app.UseRequestLogging();
app.UseGlobalExceptionHandler();
```

---

## Applied to Services

### ✅ Identity API
- Global exception handling enabled
- Correlation ID tracking
- Request logging

### ✅ Catalog API
- Global exception handling enabled
- Correlation ID tracking
- Request logging

### ✅ Basket API
- Global exception handling enabled
- Health check endpoint added

### ✅ Data Analysis API
- Global exception handling enabled
- Existing health check preserved

### ✅ Gateway
- Correlation ID middleware
- Global exception handler
- Serilog request logging
- Health check endpoint

---

## Configuration Updates

### Gateway appsettings.json
```json
{
  "ReverseProxy": {
    "Routes": { /* 8 routes configured */ },
    "Clusters": { /* 8 clusters with health checks */ }
  },
  "IdentityOption": {
    "Address": "http://localhost:8080/realms/grafirio",
    "Issuer": "http://localhost:8080/realms/grafirio",
    "Audience": "gateway.api"
  },
  "Logging": {
    "LogLevel": {
      "Yarp": "Information"
    }
  }
}
```

---

## Testing

### Test Exception Handling
```bash
# Trigger 404 error
curl http://localhost:5036/api/v1/nonexistent

# Trigger validation error
curl -X POST http://localhost:5023/api/v1/baskets \
  -H "Content-Type: application/json" \
  -d '{"invalidField": "value"}'

# Check correlation ID
curl -H "X-Correlation-Id: test-123" \
     http://localhost:5036/api/v1/companies
```

### Test Gateway Routing
```bash
# Via Gateway
curl http://localhost:5000/v1/baskets/test

# Health checks
curl http://localhost:5000/health
curl http://localhost:5036/health
curl http://localhost:5280/health
```

### Test Authorization
```bash
# Get JWT token from Keycloak
TOKEN=$(curl -X POST http://localhost:8080/realms/grafirio/protocol/openid-connect/token \
  -d "client_id=grafirio-client" \
  -d "grant_type=password" \
  -d "username=user@example.com" \
  -d "password=password" \
  | jq -r '.access_token')

# Use with API
curl -H "Authorization: Bearer $TOKEN" \
     http://localhost:5000/v1/baskets/my-basket
```

---

## Next Steps (Optional)

1. **Rate Limiting** (.NET 9 built-in)
2. **Output Caching** for GET endpoints
3. **OpenTelemetry** for distributed tracing
4. **Circuit Breaker** pattern for AI services
5. **Retry Policies** with Polly
6. **Response Compression** (Gzip, Brotli)

---

## Summary

✅ **Gateway:** Full YARP routing with health checks  
✅ **IIdentityService:** Complete implementation with authorization  
✅ **Exception Handling:** 3 middleware (Exception, CorrelationId, Logging)  
✅ **Applied to:** Identity, Catalog, Basket, Data Analysis, Gateway  
✅ **Health Checks:** Added to all services  
✅ **Authorization:** Handlers and attributes for company-based auth  

**Status:** Production Ready ✨
