# Contexto de build: la carpeta TP-ProgramacionIV, no PaymentAPI. Hace falta para
# copiar el .proto compartido, que vive en TP-ProgramacionIV/Protos.
#   docker build -f PaymentAPI/Dockerfile -t payment-api:local TP-ProgramacionIV
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Protos/ Protos/
COPY PaymentAPI/PaymentAPI.csproj PaymentAPI/
RUN dotnet restore PaymentAPI/PaymentAPI.csproj
COPY PaymentAPI/ PaymentAPI/
RUN dotnet publish PaymentAPI/PaymentAPI.csproj -c Release -o /app/publish --no-restore

# La imagen final solo trae el runtime de ASP.NET: escucha en el 8080 y corre con
# un usuario sin privilegios.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "PaymentAPI.dll"]
