using Microsoft.AspNetCore.Mvc;
using Grafirio.Shared.Services;
using Grafirio.Shared.Attributes;
using Grafirio.Shared.Filters;

namespace Grafirio.Example.Api.Features.Orders
{
    /// <summary>
    /// Example: Company-scoped orders endpoint with authorization
    /// </summary>
    public static class OrderEndpoints
    {
        public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/v1/orders")
                .WithTags("Orders")
                .RequireAuthorization("Password"); // Require authentication

            // Example 1: Get all orders for current user's company
            group.MapGet("/", GetMyCompanyOrders)
                .WithName("GetMyCompanyOrders")
                .WithOpenApi();

            // Example 2: Get orders for specific company (with validation)
            group.MapGet("/company/{companyId}", GetCompanyOrders)
                .WithName("GetCompanyOrders")
                .WithCompanyAccessValidation() // Custom filter
                .WithOpenApi();

            // Example 3: Create order (requires company manager role)
            group.MapPost("/", CreateOrder)
                .WithName("CreateOrder")
                .WithBusinessRole("COMPANY_MANAGER") // Custom filter
                .WithOpenApi();

            // Example 4: Get order details (with manual validation)
            group.MapGet("/{orderId}", GetOrderDetails)
                .WithName("GetOrderDetails")
                .WithOpenApi();
        }

        /// <summary>
        /// Get all orders for the current user's company
        /// </summary>
        private static IResult GetMyCompanyOrders(IIdentityService identityService)
        {
            var companyId = identityService.CurrentCompanyId;
            
            if (!companyId.HasValue)
            {
                return Results.BadRequest(new { error = "User is not assigned to a company" });
            }

            // Simulate fetching orders
            var orders = new[]
            {
                new { Id = 1, CompanyId = companyId, Product = "Product A", Amount = 100 },
                new { Id = 2, CompanyId = companyId, Product = "Product B", Amount = 200 }
            };

            return Results.Ok(new
            {
                companyId,
                orders,
                user = new
                {
                    id = identityService.UserId,
                    name = identityService.FullName,
                    roles = identityService.Roles
                }
            });
        }

        /// <summary>
        /// Get orders for specific company (validated by filter)
        /// </summary>
        private static IResult GetCompanyOrders(
            [FromRoute] Guid companyId,
            IIdentityService identityService)
        {
            // CompanyAccessValidation filter already validated access
            // This code only runs if user has access to this company

            var orders = new[]
            {
                new { Id = 1, CompanyId = companyId, Product = "Product A", Amount = 100 },
                new { Id = 2, CompanyId = companyId, Product = "Product B", Amount = 200 }
            };

            return Results.Ok(new
            {
                companyId,
                orderCount = orders.Length,
                orders,
                accessedBy = identityService.UserName
            });
        }

        /// <summary>
        /// Create new order (requires company manager role)
        /// </summary>
        private static IResult CreateOrder(
            CreateOrderRequest request,
            IIdentityService identityService)
        {
            // BusinessRole filter already validated the role
            // This code only runs if user has COMPANY_MANAGER role

            var companyId = identityService.CurrentCompanyId;
            
            if (!companyId.HasValue)
            {
                return Results.BadRequest(new { error = "User is not assigned to a company" });
            }

            // Simulate creating order
            var orderId = Guid.NewGuid();

            return Results.Created($"/api/v1/orders/{orderId}", new
            {
                orderId,
                companyId,
                product = request.Product,
                quantity = request.Quantity,
                createdBy = identityService.FullName,
                createdAt = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Get order details with manual company access validation
        /// </summary>
        private static IResult GetOrderDetails(
            [FromRoute] Guid orderId,
            IIdentityService identityService)
        {
            // Simulate fetching order from database
            var order = new
            {
                Id = orderId,
                CompanyId = Guid.Parse("123e4567-e89b-12d3-a456-426614174000"),
                Product = "Product A",
                Amount = 100,
                Status = "Shipped"
            };

            // Manual company access validation
            if (!identityService.HasCompanyAccess(order.CompanyId))
            {
                // This will be caught by GlobalExceptionMiddleware
                throw new UnauthorizedAccessException(
                    $"You don't have access to orders for company {order.CompanyId}");
            }

            return Results.Ok(new
            {
                order,
                accessInfo = new
                {
                    userId = identityService.UserId,
                    userName = identityService.UserName,
                    currentCompany = identityService.CurrentCompanyId,
                    accessibleCompanies = identityService.AccessibleCompanyIds,
                    businessRoles = identityService.GetBusinessRoles(order.CompanyId)
                }
            });
        }

        public record CreateOrderRequest(string Product, int Quantity);
    }

    /// <summary>
    /// Example: Using IIdentityService in a handler/service
    /// </summary>
    public class OrderService
    {
        private readonly IIdentityService _identityService;

        public OrderService(IIdentityService identityService)
        {
            _identityService = identityService;
        }

        public async Task<bool> CanUserAccessOrder(Guid orderId)
        {
            // Get order's company ID from database
            var orderCompanyId = await GetOrderCompanyIdFromDatabase(orderId);

            // Check if user has access to this company
            return _identityService.HasCompanyAccess(orderCompanyId);
        }

        public async Task<List<Order>> GetAccessibleOrders()
        {
            // Get all companies user has access to
            var accessibleCompanies = _identityService.AccessibleCompanyIds;

            // Fetch orders for these companies
            return await GetOrdersByCompanies(accessibleCompanies);
        }

        public bool CanUserManageOrders()
        {
            // Check if user has manager or admin role
            return _identityService.HasBusinessRole("COMPANY_MANAGER") ||
                   _identityService.HasBusinessRole("COMPANY_ADMIN");
        }

        public string GetAuditInfo()
        {
            return $"Action performed by {_identityService.FullName} " +
                   $"({_identityService.Email}) at {DateTime.UtcNow}";
        }

        // Dummy methods
        private Task<Guid> GetOrderCompanyIdFromDatabase(Guid orderId) => 
            Task.FromResult(Guid.NewGuid());
        
        private Task<List<Order>> GetOrdersByCompanies(List<Guid> companyIds) => 
            Task.FromResult(new List<Order>());
    }

    public record Order(Guid Id, Guid CompanyId, string Product, decimal Amount);

    /// <summary>
    /// Example: Using authorization attributes
    /// </summary>
    public static class AdminEndpoints
    {
        public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/v1/admin")
                .WithTags("Admin");

            // Requires company admin role
            group.MapGet("/dashboard", 
                [RequireCompanyAdmin] (IIdentityService identityService) =>
            {
                return Results.Ok(new
                {
                    message = "Admin Dashboard",
                    admin = identityService.FullName,
                    companyId = identityService.CurrentCompanyId,
                    roles = identityService.Roles
                });
            });

            // Requires company manager role
            group.MapGet("/reports", 
                [RequireCompanyManager] (IIdentityService identityService) =>
            {
                return Results.Ok(new
                {
                    message = "Manager Reports",
                    manager = identityService.FullName,
                    businessRoles = identityService.GetBusinessRoles()
                });
            });
        }
    }

    /// <summary>
    /// Example: Exception handling demonstration
    /// </summary>
    public static class ExceptionExampleEndpoints
    {
        public static void MapExceptionExamples(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/v1/examples")
                .WithTags("Examples");

            // Example 1: ArgumentException → 400 Bad Request
            group.MapGet("/bad-request", () =>
            {
                throw new ArgumentException("Invalid parameter provided");
            });

            // Example 2: UnauthorizedAccessException → 401 Unauthorized
            group.MapGet("/unauthorized", () =>
            {
                throw new UnauthorizedAccessException("You don't have permission");
            });

            // Example 3: KeyNotFoundException → 404 Not Found
            group.MapGet("/not-found", () =>
            {
                throw new KeyNotFoundException("Resource not found");
            });

            // Example 4: Generic Exception → 500 Internal Server Error
            group.MapGet("/server-error", () =>
            {
                throw new Exception("Something went wrong");
            });

            // Example 5: Custom validation error
            group.MapPost("/validate", (CreateOrderRequest request) =>
            {
                if (string.IsNullOrEmpty(request.Product))
                {
                    throw new ArgumentException("Product name is required");
                }

                if (request.Quantity <= 0)
                {
                    throw new ArgumentException("Quantity must be greater than zero");
                }

                return Results.Ok(new { message = "Validation passed", request });
            });
        }
    }
}
