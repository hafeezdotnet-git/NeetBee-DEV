FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Services/AuthenticationService/AuthenticationService.csproj", "Services/AuthenticationService/"]
COPY ["Common/NeetBee.Common/NeetBee.Common.csproj", "Common/NeetBee.Common/"]
RUN dotnet restore "Services/AuthenticationService/AuthenticationService.csproj"

COPY . .
RUN dotnet publish "Services/AuthenticationService/AuthenticationService.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=5001
EXPOSE 5001
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AuthenticationService.dll"]
