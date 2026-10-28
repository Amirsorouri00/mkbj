# One image, four roles. The entrypoint takes no arguments by default (the API); compose passes
# --worker, --migrate or --seed to the same image, so there is no second artefact to build,
# version or keep in step.
ARG DOTNET_REGISTRY=mcr.microsoft.com

FROM ${DOTNET_REGISTRY}/dotnet/sdk:10.0 AS build
WORKDIR /src

# Build files first: these change rarely, so restore stays cached across source edits.
COPY global.json nuget.config Directory.Build.props Directory.Build.targets Directory.Packages.props EwaysVNext.slnx ./
COPY EwaysVNext.Modules.Catalog.Contracts/ EwaysVNext.Modules.Catalog.Contracts/
COPY src/ src/

RUN dotnet restore src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj
RUN dotnet publish src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj \
    -c Release --no-restore -o /app /p:UseAppHost=false

# -extra keeps ICU and tzdata: the invoice and FX code formats dates and money, and a culture-less
# runtime would silently fall back to the invariant culture.
FROM ${DOTNET_REGISTRY}/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app

COPY --from=build --chown=$APP_UID:$APP_UID /app ./

# Non-root, and the image has no shell or package manager at all.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

# Chiseled has no curl or wget, which is why the host implements --probe itself.
ENTRYPOINT ["dotnet", "EwaysVNext.Host.dll"]
