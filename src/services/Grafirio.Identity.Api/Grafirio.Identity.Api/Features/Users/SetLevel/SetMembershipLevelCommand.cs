namespace Grafirio.Identity.Api.Features.Users.SetLevel;

/// <summary>
/// Kişinin şirketteki üyelik seviyesini belirler; üyeliği yoksa açar.
///
/// <paramref name="Level"/> yalnızca admin ya da üye olabilir. Kurucu bu uçtan
/// verilmiyor: şirketi kuran e-postaya bağlı ve devri ayrı bir akış
/// (çift taraflı e-posta onayı).
/// </summary>
public record SetMembershipLevelCommand(
    string KeycloakUserId,
    Guid CompanyId,
    string Level
) : IRequestByServiceResult<SetMembershipLevelResponse>;

public record SetMembershipLevelResponse(Guid Id);
