FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /app
COPY src/core/Gradinware.csproj .
RUN dotnet restore
COPY src/core/ ./
RUN dotnet build -c Debug
RUN dotnet publish -c Debug -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:6.0

ENV ASPNETCORE_URLS=http://*:5000
ENV ASPNETCORE_ENVIRONMENT="production"

WORKDIR /app

COPY package.json entrypoint.sh .babelrc ./

RUN apt-get update -yq && apt-get upgrade -yq && apt-get install -yq curl git nano
RUN curl -sL https://deb.nodesource.com/setup_16.x | bash - && apt-get install -yq nodejs build-essential && npm i
RUN curl -sSL https://aka.ms/getvsdbgsh | /bin/sh /dev/stdin -v latest -l /vsdbg

COPY --from=build /publish ./
COPY src/ui/ /app/ui/

EXPOSE 3000
EXPOSE 5000
EXPOSE 9229

RUN chmod +x /app/entrypoint.sh
ENTRYPOINT /app/entrypoint.sh
