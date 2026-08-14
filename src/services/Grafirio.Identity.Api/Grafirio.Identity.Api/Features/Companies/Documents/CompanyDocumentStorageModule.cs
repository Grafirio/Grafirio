using Microsoft.Extensions.FileProviders;

namespace Grafirio.Identity.Api.Features.Companies.Documents;

public static class CompanyDocumentStorageModule
{
    public static IServiceCollection AddCompanyDocumentStorage(this IServiceCollection services)
    {
        var wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        Directory.CreateDirectory(wwwroot);
        services.AddSingleton<IFileProvider>(new PhysicalFileProvider(wwwroot));

        services.AddScoped<ICompanyDocumentStore, LocalDiskCompanyDocumentStore>();
        services.AddSingleton<CompanyDocumentThumbnailer>();
        return services;
    }
}
