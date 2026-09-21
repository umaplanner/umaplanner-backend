FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY UmaPlanner.slnx ./
COPY src/UmaPlanner.Api/UmaPlanner.Api.csproj src/UmaPlanner.Api/
COPY src/UmaPlanner.Core/UmaPlanner.Core.csproj src/UmaPlanner.Core/
COPY src/UmaPlanner.Infrastructure/UmaPlanner.Infrastructure.csproj src/UmaPlanner.Infrastructure/
COPY tests/UmaPlanner.Api.Tests/UmaPlanner.Api.Tests.csproj tests/UmaPlanner.Api.Tests/
RUN dotnet restore UmaPlanner.slnx

COPY src ./src
RUN dotnet publish src/UmaPlanner.Api/UmaPlanner.Api.csproj \
    --configuration Release \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "UmaPlanner.Api.dll"]
