FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY server/Formscope.csproj server/packages.lock.json ./server/
RUN dotnet restore server/Formscope.csproj --locked-mode
COPY server/ ./server/
RUN dotnet publish server/Formscope.csproj -c Release --no-restore -o /app/publish
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish ./
USER root
RUN mkdir -p /app/var && chown -R $APP_UID:$APP_UID /app/var
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:5007 DataPath=/app/var
EXPOSE 5007
ENTRYPOINT ["dotnet","Formscope.dll"]
