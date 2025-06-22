FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app
COPY . .
RUN dotnet publish -c Release -o publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
RUN apt-get update -y && apt-get install -y curl
HEALTHCHECK --interval=5s --timeout=3s CMD curl --fail http://localhost:8080/api/health || exit 1
EXPOSE 8080
ENTRYPOINT ["dotnet", "ElevationWebApi.dll"]
