# PIZON BBS production image. Build context is src/ (see deploy/deploy.ps1):
#   docker build -t pizon-bbs:latest -f src/Dockerfile src
# The site listens on 8080 inside the container; Caddy (deploy/docker-compose.yml) terminates HTTPS.

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish Pizon.Web/Pizon.Web.csproj -c Release -o /app/publish -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# The SQLite database and data-protection keys live on the bbs-data volume.
RUN mkdir -p /app/App_Data && chown -R app:app /app/App_Data
VOLUME /app/App_Data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__bbsdb="Data Source=/app/App_Data/bbs.db"
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pizon.Web.dll"]
