# Bygg, testa, publicera. Fallerar ett test stannar bygget och den gamla versionen ligger kvar.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
RUN apt-get update && apt-get install -y --no-install-recommends libsqlite3-0 && rm -rf /var/lib/apt/lists/*
COPY . .
RUN dotnet restore
RUN dotnet test tests/Tidbok.Tests --no-restore --configuration Release --verbosity normal
RUN dotnet publish src/Tidbok.Web --no-restore --configuration Release --output /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y --no-install-recommends libsqlite3-0 tzdata && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out .
ENV TZ=Europe/Stockholm \
    DataPath=/app/data/tidbok.db \
    ASPNETCORE_ENVIRONMENT=Production
RUN mkdir -p /app/data && chown -R $APP_UID /app/data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tidbok.Web.dll"]
