FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY NotificationService.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish NotificationService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:5050
EXPOSE 5050
USER $APP_UID
ENTRYPOINT ["dotnet", "NotificationService.dll"]
