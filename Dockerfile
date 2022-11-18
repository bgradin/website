FROM mcr.microsoft.com/dotnet/core/sdk AS build
WORKDIR /app
COPY src/core/Gradinware.csproj .
RUN dotnet restore
COPY src/core/ ./
RUN dotnet build -c Release
RUN dotnet publish -c Release -o /publish

FROM mcr.microsoft.com/dotnet/core/aspnet

ENV ASPNETCORE_URLS=http://*:5000
ENV ASPNETCORE_ENVIRONMENT="production"

WORKDIR /app

COPY package.json entrypoint.sh .babelrc ./

RUN chmod a+x entrypoint.sh && apt-get update -yq && apt-get upgrade -yq && apt-get install -yq curl git nano
RUN curl -sL https://deb.nodesource.com/setup_16.x | bash - && apt-get install -yq nodejs build-essential
RUN npm i

COPY --from=build /publish ./
COPY src/ui/ /app/ui/

EXPOSE 3000
EXPOSE 5000

ENTRYPOINT /app/entrypoint.sh
