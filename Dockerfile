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

EXPOSE 5000
WORKDIR /app
COPY --from=build /publish ./
COPY src/ui/public/ /public/
ENTRYPOINT ["dotnet", "Gradinware.dll"]