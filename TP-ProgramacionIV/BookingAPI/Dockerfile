# Build que se puede usar luego de integrar la app con gRPC (copia de feature booking)
#   docker build -f BookingAPI/Dockerfile -t booking-api:local TP-ProgramacionIV
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY BookingAPI/BookingAPI.csproj BookingAPI/
RUN dotnet restore BookingAPI/BookingAPI.csproj
COPY . ./
RUN dotnet publish BookingAPI/BookingAPI.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "BookingAPI.dll"]
