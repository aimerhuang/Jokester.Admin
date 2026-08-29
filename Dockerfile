FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY jokester.admin/jokester.admin.csproj jokester.admin/
RUN dotnet restore jokester.admin/jokester.admin.csproj

COPY . .
RUN dotnet publish jokester.admin/jokester.admin.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        ca-certificates \
        curl \
        libfontconfig1 \
        libfreetype6 \
        libgomp1 \
        tzdata \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build --chown=app:app /app/publish .

RUN mkdir -p \
        /data/private-media/ai \
        /data/prompt-images \
        /app/wwwroot/blog \
        /app/wwwroot/avatar \
        /home/app/.aspnet/DataProtection-Keys \
    && chown -R app:app /data /app/wwwroot /home/app/.aspnet

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    TZ=Asia/Shanghai

EXPOSE 8080

USER app

ENTRYPOINT ["dotnet", "jokester.admin.dll"]
