using UmaPlanner.Core.Interfaces;

namespace UmaPlanner.Api.Endpoints;

public static class UmaEndpoints
{
    public static void MapUmaEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/umas/base", async (IUmaService service) =>
        {
            var baseUmas = await service.GetBaseAsync();
            return baseUmas;
        })
        .WithName("GetBaseUmas");

        routes.MapGet("/umas/variants", async (IUmaService service) =>
        {
            var variants = await service.GetVariantsAsync();
            return variants;
        })
        .WithName("GetVariantUmas");
    }
}
