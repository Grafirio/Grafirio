namespace Grafirio.Identity.Api.Features.Subscriptions.Start;

/// <summary>
/// Müşterinin kendi firması için abonelik başlatması.
///
/// <see cref="Create.CreateSubscriptionCommand"/> bilerek yalnızca platform
/// ekibine açık: orası herhangi bir firmaya, istenen tarih aralığıyla erişim
/// tanımlayabiliyor. Kayıt akışının ihtiyacı olan şey ise dar: kişi yalnızca
/// kendi yöneticisi olduğu firmaya, satılabilir bir paketle, standart koşullarda
/// abonelik açabilsin. İki yetkiyi tek uçta toplamak, müşteriye istediği
/// aboneliği istediği süreyle yazma imkânı verirdi.
/// </summary>
public record StartSubscriptionCommand(string Plan)
    : IRequestByServiceResult<StartSubscriptionResponse>;

public record StartSubscriptionResponse(
    Guid Id,
    Guid CompanyId,
    string Plan,
    DateTime StartsAt,
    DateTime? TrialEndsAt);
