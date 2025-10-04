using Grafirio.Identity.Api.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Grafirio.Identity.Api.Features.Companies.Create;

public class CreateCompanyCommandHandler(AppDbContext context, IIdentityService identityService)
    : IRequestHandler<CreateCompanyCommand, ServiceResult<CreateCompanyResponse>>
{
    public async Task<ServiceResult<CreateCompanyResponse>> Handle(CreateCompanyCommand request,
        CancellationToken cancellationToken)
    {
        // Check if company code already exists
        if (!string.IsNullOrEmpty(request.Code))
        {
            var existingCompany = await context.Companies
                .AnyAsync(x => x.Code == request.Code, cancellationToken);

            if (existingCompany)
            {
                return ServiceResult<CreateCompanyResponse>.Error("Company code already exists",
                    $"The company code '{request.Code}' already exists", HttpStatusCode.BadRequest);
            }
        }

        // Check if parent company exists and user has access
        int level = 0;
        if (request.ParentCompanyId.HasValue)
        {
            var parentCompany = await context.Companies
                .FirstOrDefaultAsync(x => x.Id == request.ParentCompanyId.Value, cancellationToken);

            if (parentCompany == null)
            {
                return ServiceResult<CreateCompanyResponse>.Error("Parent company not found",
                    HttpStatusCode.NotFound);
            }

            // Check if user has access to parent company
            if (!identityService.HasCompanyAccess(request.ParentCompanyId.Value))
            {
                return ServiceResult<CreateCompanyResponse>.Error("Access denied to parent company",
                    HttpStatusCode.Forbidden);
            }

            level = parentCompany.Level + 1;
        }
        else
        {
            // Only company admins can create root companies
            if (!identityService.HasBusinessRole("COMPANY_ADMIN"))
            {
                return ServiceResult<CreateCompanyResponse>.Error("Only company admins can create root companies",
                    HttpStatusCode.Forbidden);
            }
        }

        var company = new Company
        {
            Id = NewId.NextSequentialGuid(),
            Name = request.Name,
            Code = request.Code,
            Description = request.Description,
            ParentCompanyId = request.ParentCompanyId,
            Level = level,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await context.Companies.AddAsync(company, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return ServiceResult<CreateCompanyResponse>.SuccessAsCreated(
            new CreateCompanyResponse(company.Id),
            $"/api/v1/companies/{company.Id}");
    }
}