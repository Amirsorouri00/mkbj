# syntax=docker/dockerfile:1.7
FROM repository.eways.co/docker-mcr/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Packages.props Directory.Build.props Directory.Build.targets nuget.config global.json ./
COPY EwaysVNext.slnx ./
COPY EwaysVNext.Modules.Catalog.Contracts/ ./EwaysVNext.Modules.Catalog.Contracts/
COPY src/ ./src/
COPY tests/ ./tests/
RUN dotnet restore EwaysVNext.slnx
RUN dotnet publish src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM repository.eways.co/docker-mcr/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN adduser --disabled-password --gecos "" --uid 10001 appuser && chown -R appuser /app
USER appuser
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "EwaysVNext.Host.dll"]
