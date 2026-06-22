FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["FeedCord.csproj", "./"]
COPY ["src", "./src"]

RUN dotnet restore && \
    dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

RUN mkdir -p /app/config /app/state && \
    chown -R $APP_UID:$APP_UID /app

ENV FEEDCORD_HEALTH_FILE=/tmp/feedcord-health

USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["dotnet", "FeedCord.dll", "--health-check"]

ENTRYPOINT ["dotnet", "FeedCord.dll"]
