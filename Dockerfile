FROM node:24.22.1-bookworm-slim@sha256:be8204d4bd327363357f532187b752ff62759c31bedf98e9e18fc08c9cbd605c AS ui-build
WORKDIR /ui
RUN corepack prepare yarn@1.22.22 --activate && yarn --version
COPY package.json yarn.lock .babelrc ./
RUN yarn install --frozen-lockfile --non-interactive
COPY src/ui/ ./src/ui/
RUN mkdir -p src/ui/public/assets
RUN yarn build:frontend
RUN yarn install --frozen-lockfile --production --non-interactive --modules-folder /production-node-modules

FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS dotnet-build
WORKDIR /app
COPY src/core/Gradinware.csproj ./
RUN dotnet restore
COPY src/core/ ./
RUN dotnet publish -c Release --no-restore -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee
ARG SSR_BUILD_VERSION
ENV SSR_BUILD_VERSION=$SSR_BUILD_VERSION
ENV ASPNETCORE_URLS=http://*:5000
ENV ASPNETCORE_ENVIRONMENT=production
RUN apt-get update && apt-get install -y --no-install-recommends libstdc++6 && rm -rf /var/lib/apt/lists/*
WORKDIR /app

COPY --from=ui-build /usr/local/bin/node /usr/local/bin/node
COPY --from=ui-build /production-node-modules ./node_modules
COPY --from=dotnet-build /publish ./
COPY --from=ui-build /ui/src/ui/ ./ui/
COPY entrypoint.sh .babelrc ./

EXPOSE 5000
RUN chmod +x /app/entrypoint.sh
ENTRYPOINT ["/app/entrypoint.sh"]
