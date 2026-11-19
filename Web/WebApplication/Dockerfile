FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Web/WebApplication/WebApplication.csproj", "Web/WebApplication/"]
RUN dotnet restore "Web/WebApplication/WebApplication.csproj"

COPY . .
RUN dotnet publish "Web/WebApplication/WebApplication.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=5100
EXPOSE 5100
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WebApplication.dll"]
