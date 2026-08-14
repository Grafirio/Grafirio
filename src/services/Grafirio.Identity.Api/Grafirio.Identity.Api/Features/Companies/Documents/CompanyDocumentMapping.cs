using AutoMapper;
using Grafirio.Identity.Api.Features.Companies.Documents.Dtos;

namespace Grafirio.Identity.Api.Features.Companies.Documents;

public class CompanyDocumentMapping : Profile
{
    public CompanyDocumentMapping()
    {
        CreateMap<CompanyDocument, CompanyDocumentDto>()
            .ForMember(d => d.HasThumbnail,
                o => o.MapFrom(s => !string.IsNullOrEmpty(s.ThumbnailFileName)));
    }
}
