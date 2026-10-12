using Grafirio.Identity.Api.Features.Companies.Access;
using Grafirio.Identity.Api.Features.Permissions;
using Grafirio.Identity.Api.Repositories;
using Grafirio.Shared.Identity.Permissions;
using Grafirio.Shared.Identity.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Grafirio.Identity.Tests;

public sealed class PermissionServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("FOUNDER")]
    [InlineData("ADMIN")]
    [InlineData("PLATFORM_ADMIN")]
    public async Task Effective_permissions_follow_the_pinned_contract_and_preserve_access_rules(string? level)
    {
        var companyId = Guid.NewGuid();
        var access = new Mock<ICompanyAccessService>(MockBehavior.Strict);
        access.Setup(service => service.EffectiveLevelAsync(companyId, CancellationToken.None)).ReturnsAsync(level);
        var identity = new Mock<IIdentityService>();
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var permissions = new PermissionService(context, access.Object, identity.Object,
            NullLogger<PermissionService>.Instance);

        var result = await permissions.ForCompanyAsync(companyId, CancellationToken.None);

        Assert.Equal(companyId, result.CompanyId);
        Assert.Equal(level, result.Role);
        if (level is null)
        {
            Assert.Empty(result.Modules);
            Assert.Empty(result.Permissions);
            Assert.False(await permissions.CanAsync(companyId, AppPermissions.PanelRead, CancellationToken.None));
        }
        else
        {
            Assert.Equal(AppPermissions.All.Order(), result.Permissions.Order());
            Assert.Equal(AppPermissions.ModulesOf(AppPermissions.All).Order(), result.Modules.Order());
            Assert.True(await permissions.CanAsync(companyId, AppPermissions.PanelRead, CancellationToken.None));
        }

        Assert.Same(result, await permissions.ForCompanyAsync(companyId, CancellationToken.None));
        access.Verify(service => service.EffectiveLevelAsync(companyId, CancellationToken.None), Times.Once);
    }
}
