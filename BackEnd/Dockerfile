# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY K2Agents.sln ./
COPY src/K2Agents.Domain/K2Agents.Domain.csproj src/K2Agents.Domain/
COPY src/K2Agents.Application/K2Agents.Application.csproj src/K2Agents.Application/
COPY src/K2Agents.Infrastructure/K2Agents.Infrastructure.csproj src/K2Agents.Infrastructure/
COPY src/K2Agents.Api/K2Agents.Api.csproj src/K2Agents.Api/
RUN dotnet restore src/K2Agents.Api/K2Agents.Api.csproj
COPY src/ src/
RUN dotnet publish src/K2Agents.Api/K2Agents.Api.csproj -c Release -o /app --no-restore

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
# tzdata: fuso America/Sao_Paulo; curl: healthcheck.
RUN apt-get update && apt-get install -y --no-install-recommends tzdata curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app ./
# Arquivos da base de conhecimento (montada como volume persistente no docker-compose).
RUN mkdir -p /app/storage && chown -R app:app /app/storage
ENV ASPNETCORE_URLS=http://+:8080 \
    Storage__RootPath=/app/storage
EXPOSE 8080
USER app
HEALTHCHECK --interval=15s --timeout=5s --retries=5 CMD curl -fs http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "K2Agents.Api.dll"]
