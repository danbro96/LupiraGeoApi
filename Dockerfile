# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Copy both project files before restore (the host references Core) so layer caching works.
COPY src/LupiraGeoApi/LupiraGeoApi.csproj src/LupiraGeoApi/
COPY src/LupiraGeoApi.Core/LupiraGeoApi.Core.csproj src/LupiraGeoApi.Core/
RUN dotnet restore src/LupiraGeoApi/LupiraGeoApi.csproj
COPY . .
RUN dotnet publish src/LupiraGeoApi/LupiraGeoApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# curl: compose healthcheck (curl -fsS .../readyz).
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "LupiraGeoApi.dll"]
