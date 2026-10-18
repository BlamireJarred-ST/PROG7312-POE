# Build the SmartX API (Part 1 + Part 2). Build context = repository root.
# NOTE: folder / project names are case-sensitive on Linux (SmartX.API, not SmartX.Api).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore first so Docker can cache the NuGet layer
COPY SmartX.Shared/SmartX.Shared.csproj SmartX.Shared/
COPY SmartX.API/SmartX.API.csproj SmartX.API/
RUN dotnet restore SmartX.API/SmartX.API.csproj

COPY SmartX.Shared/ SmartX.Shared/
COPY SmartX.API/ SmartX.API/
RUN dotnet publish SmartX.API/SmartX.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SmartX.API.dll"]
