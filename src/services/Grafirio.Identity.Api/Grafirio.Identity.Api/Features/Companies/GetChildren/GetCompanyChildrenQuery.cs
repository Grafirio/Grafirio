using Grafirio.Identity.Api.Features.Companies.Dtos;

namespace Grafirio.Identity.Api.Features.Companies.GetChildren;

/// <summary>
/// Bir şirketin doğrudan alt şirketleri.
///
/// Panel bunu <c>GET /companies</c> listesini <c>ParentCompanyId</c>'ye göre
/// süzerek buluyordu, ama o liste token'daki <c>accessible_companies</c>
/// claim'iyle sınırlı: claim eksikse ya da yeni açılan alt şirket henüz oraya
/// yansımadıysa liste boş kalıyor ve alt şirket hiç eklenmemiş gibi
/// görünüyordu. Burada kaynak hiyerarşinin kendisi.
/// </summary>
public record GetCompanyChildrenQuery(Guid CompanyId) : IRequestByServiceResult<List<CompanyDto>>;
