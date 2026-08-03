using Asp.Versioning.Builder;
using Grafirio.Identity.Api.Features.Subscriptions.Cancel;
using Grafirio.Identity.Api.Features.Subscriptions.Create;
using Grafirio.Identity.Api.Features.Subscriptions.Dtos;
using Grafirio.Identity.Api.Features.Subscriptions.GetByCompany;
using Grafirio.Identity.Api.Features.Subscriptions.MyAccess;
using Grafirio.Identity.Api.Features.Subscriptions.Start;

namespace Grafirio.Identity.Api.Features.Subscriptions;

public static class SubscriptionEndpointExt
{
    public static void AddSubscriptionGroupEndpointExt(this WebApplication app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("api/v{version:apiVersion}/subscriptions")
            .WithTags("Subscriptions")
            .WithApiVersionSet(apiVersionSet);

        // UserAdmin giriste bunu sorar; kural tek yerde tanimli kalsin diye
        // diger servisler de ayni ucu kullanabilir.
        group.MapGet("/my-access", async (IMediator mediator) =>
            {
                var result = await mediator.Send(new GetMyAccessQuery());

                return result.IsSuccess
                    ? Results.Ok(result.Data)
                    : Results.BadRequest(result.Fail);
            })
            .WithName("GetMyAccess")
            .Produces<MyAccessResponse>(StatusCodes.Status200OK)
            .RequireAuthorization("Password");

        group.MapGet("/company/{companyId:guid}", async (Guid companyId, IMediator mediator) =>
            {
                var result = await mediator.Send(new GetCompanySubscriptionsQuery(companyId));

                return result.IsSuccess
                    ? Results.Ok(result.Data)
                    : Results.BadRequest(result.Fail);
            })
            .WithName("GetCompanySubscriptions")
            .Produces<List<SubscriptionDto>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        group.MapPost("/", async (CreateSubscriptionCommand command, IMediator mediator) =>
            {
                var result = await mediator.Send(command);

                return result.IsSuccess
                    ? Results.Created(result.UrlAsCreated, result.Data)
                    : Results.BadRequest(result.Fail);
            })
            .WithName("CreateSubscription")
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        // Kayit akisinin kullandigi uc: kisi kendi yonettigi firma icin,
        // satilabilir bir paketle abonelik baslatir. Genis yetkili
        // "CreateSubscription" ucu platform ekibine ayri kaliyor.
        group.MapPost("/start", async (StartSubscriptionCommand command, IMediator mediator) =>
            {
                var result = await mediator.Send(command);

                return result.IsSuccess
                    ? Results.Created(result.UrlAsCreated, result.Data)
                    : Results.BadRequest(result.Fail);
            })
            .WithName("StartSubscription")
            .Produces<StartSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .RequireAuthorization("Password");

        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator) =>
            {
                var result = await mediator.Send(new CancelSubscriptionCommand(id));

                return result.IsSuccess
                    ? Results.NoContent()
                    : Results.BadRequest(result.Fail);
            })
            .WithName("CancelSubscription")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Password");

        group.MapToApiVersion(1, 0);
    }
}
