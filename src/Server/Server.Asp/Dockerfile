# The control panel frontend; only its built files reach the final image
FROM node:24-bookworm-slim AS panel
WORKDIR /src/Web/admin-panel
COPY Web/admin-panel/package.json Web/admin-panel/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci
COPY Web/admin-panel/ ./
# Writes to /src/Server/Server.Asp/Web/wwwroot/panel, see vite.config.ts
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish Server/Server.Asp/Server.Asp.csproj -c Release -o /out -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Links the image on ghcr.io to the repository
LABEL org.opencontainers.image.source=https://github.com/puffer-bit/FlowRate-Server
WORKDIR /app
COPY --from=build /out .
COPY --from=panel /src/Server/Server.Asp/Web/wwwroot/panel Web/wwwroot/panel
RUN mkdir -p /data /logs && chown app:app /data /logs
ENV ASPNETCORE_ENVIRONMENT=Production \
    Storage__Directory=/data \
    Serilog__WriteTo__0__Args__configure__1__Args__path=/logs/log-.txt \
    Orchestrator__LocalSfuHost=mediasoup
USER app
EXPOSE 7114 26666
ENTRYPOINT ["dotnet", "Server.Asp.dll"]
