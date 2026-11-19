FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Services/NotificationService/NotificationService.csproj", "Services/NotificationService/"]
COPY ["Common/NeetBee.Common/NeetBee.Common.csproj", "Common/NeetBee.Common/"]
RUN dotnet restore "Services/NotificationService/NotificationService.csproj"

COPY . .
RUN dotnet publish "Services/NotificationService/NotificationService.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=5002
EXPOSE 5002
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "NotificationService.dll"]
