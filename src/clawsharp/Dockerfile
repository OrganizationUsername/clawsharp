# ── Stage 1: Build Svelte web UI ─────────────────────────────────────────────
FROM node:22-slim AS web
WORKDIR /web
COPY src/clawsharp-web/package.json src/clawsharp-web/package-lock.json* ./
RUN npm ci --prefer-offline --no-audit --no-fund
COPY src/clawsharp-web/ .
RUN npm run build

# ── Stage 2: .NET restore + publish ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0.102 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /repo

# Use bind mount from build context — avoids Buildah COPY overlay issue with MSBuild globs
COPY --from=web /web/dist/index.html /tmp/web-index.html
RUN --mount=type=bind,target=/src,rw \
    cp -a /src/src/clawsharp/. . && \
    cp /tmp/web-index.html Channels/Web/index.html && \
    dotnet publish "./clawsharp.csproj" -c $BUILD_CONFIGURATION -r linux-x64 -o /app/publish -p:SkipWebUI=true -p:SelfContained=false -p:PublishSingleFile=false

# ── Stage 3: Runtime image ──────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# The .NET base image pre-creates an 'app' user and group (UID/GID 1654).
# Set HOME so the key file and config are written to the right place.
ENV HOME=/home/app \
    DOTNET_RUNNING_IN_CONTAINER=true

# ── OpenShell transparent inference routing (optional) ───────────────────────
# When running inside an OpenShell sandbox, inference requests are routed
# transparently via 'inference.local'. Uncomment to use:
# ENV CLAWSHARP__providers__lmstudio__baseUrl=http://inference.local:443/v1

WORKDIR /app
COPY --from=build /app/publish .

# Npgsql requires libgssapi-krb5-2 for GSSAPI auth negotiation (even when using password auth).
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*

# Install Playwright Chromium and its system dependencies (~140 MB).
# MED-06: Install as root for system deps, then set PLAYWRIGHT_BROWSERS_PATH to a
# shared location and fix ownership so the app user can write temp files at runtime.
ENV PLAYWRIGHT_BROWSERS_PATH=/opt/playwright-browsers
RUN dotnet tool install --global Microsoft.Playwright.CLI --version 1.58.0 2>/dev/null || true && \
    export PATH="$PATH:/root/.dotnet/tools" && \
    mkdir -p $PLAYWRIGHT_BROWSERS_PATH && \
    playwright install chromium --with-deps 2>/dev/null || \
    pwsh -Command "& {./bin/Debug/net10.0/playwright.ps1 install chromium}" 2>/dev/null || \
    echo "WARNING: Playwright browser install skipped; browser tool will fail at runtime." && \
    chown -R app:app $PLAYWRIGHT_BROWSERS_PATH

# Pre-create the data directory with correct ownership before the USER switch.
# Docker copies this directory (with its permissions) into a new named volume on
# first mount, so the app user can write to it without needing an entrypoint shim.
RUN mkdir -p /home/app/.clawsharp/workspace && chown -R app:app /home/app /app

USER app
ENTRYPOINT ["dotnet", "/app/clawsharp.dll"]
