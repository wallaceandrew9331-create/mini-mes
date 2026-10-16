FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Restore first so the package layer is cached until a project file changes.
COPY Directory.Build.props ./
COPY src/MiniMes.Api/MiniMes.Api.csproj src/MiniMes.Api/
RUN dotnet restore src/MiniMes.Api/MiniMes.Api.csproj
COPY src/ src/
RUN dotnet publish src/MiniMes.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "MiniMes.Api.dll"]
