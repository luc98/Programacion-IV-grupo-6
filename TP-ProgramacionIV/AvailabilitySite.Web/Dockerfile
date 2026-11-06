FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AvailabilitySite.Domain/AvailabilitySite.Domain.csproj AvailabilitySite.Domain/
COPY AvailabilitySite.Application/AvailabilitySite.Application.csproj AvailabilitySite.Application/
COPY AvailabilitySite.Infrastructure/AvailabilitySite.Infrastructure.csproj AvailabilitySite.Infrastructure/
COPY AvailabilitySite.Web/AvailabilitySite.Web.csproj AvailabilitySite.Web/

RUN dotnet restore AvailabilitySite.Web/AvailabilitySite.Web.csproj

COPY AvailabilitySite.Domain/ AvailabilitySite.Domain/
COPY AvailabilitySite.Application/ AvailabilitySite.Application/
COPY AvailabilitySite.Infrastructure/ AvailabilitySite.Infrastructure/
COPY AvailabilitySite.Web/ AvailabilitySite.Web/

RUN dotnet publish AvailabilitySite.Web/AvailabilitySite.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "AvailabilitySite.Web.dll"]
