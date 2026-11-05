ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG DOTNET_ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src

COPY jokester.admin/jokester.admin.csproj jokester.admin/
RUN dotnet restore jokester.admin/jokester.admin.csproj

COPY . .
RUN dotnet publish jokester.admin/jokester.admin.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM ${DOTNET_ASPNET_IMAGE} AS runtime

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
    TZ=Asia/Shanghai \
    HEALTHCHECK_HOST=localhost

EXPOSE 8080

# HEALTHCHECK_HOST 必须在 AllowedHosts 内；该接口同时验证 MySQL 可用。
HEALTHCHECK --interval=15s --timeout=10s --start-period=45s --retries=20 \
    CMD curl --fail --silent --show-error --header "Host: ${HEALTHCHECK_HOST}" http://127.0.0.1:8080/api/sites/site_code >/dev/null || exit 1

USER app

ENTRYPOINT ["dotnet", "jokester.admin.dll"]
