using AutoMapper;
using Grafirio.Identity.Api.Features.Subscriptions.Dtos;

namespace Grafirio.Identity.Api.Features.Subscriptions;

public class SubscriptionMapping : Profile
{
    public SubscriptionMapping()
    {
        CreateMap<Subscription, SubscriptionDto>()
            // Hesaplanan alan: durum "Active" gorunse bile suresi dolmus olabilir.
            .ForMember(dest => dest.IsCurrentlyActive,
                opt => opt.MapFrom(src => src.IsCurrentlyActive(null)));
    }
}
