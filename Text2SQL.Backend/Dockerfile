# ── Build aşaması ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Text2Sql.sln ./
COPY src/Text2Sql.Domain/Text2Sql.Domain.csproj src/Text2Sql.Domain/
COPY src/Text2Sql.Application/Text2Sql.Application.csproj src/Text2Sql.Application/
COPY src/Text2Sql.Infrastructure/Text2Sql.Infrastructure.csproj src/Text2Sql.Infrastructure/
COPY src/Text2Sql.Api/Text2Sql.Api.csproj src/Text2Sql.Api/
RUN dotnet restore src/Text2Sql.Api/Text2Sql.Api.csproj

COPY src/ src/
RUN dotnet publish src/Text2Sql.Api/Text2Sql.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ── Runtime aşaması ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Veri dizinleri: volume'lar ilk mount'ta bu sahiplikle başlar —
# non-root kullanıcı /data altına yazabilmelidir (SQLite yükleme + sonuç deposu).
RUN mkdir -p /data/sqlite /data/results && chown -R $APP_UID /data

# Non-root kullanıcı (imajda hazır gelir) — chown SONRASINDA geçilir
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s \
  CMD wget -qO- http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Text2Sql.Api.dll"]
