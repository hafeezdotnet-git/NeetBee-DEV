FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Services/PaymentService/PaymentService.csproj", "Services/PaymentService/"]
COPY ["Common/NeetBee.Common/NeetBee.Common.csproj", "Common/NeetBee.Common/"]
RUN dotnet restore "Services/PaymentService/PaymentService.csproj"

COPY . .
RUN dotnet publish "Services/PaymentService/PaymentService.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=5003
EXPOSE 5003
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PaymentService.dll"]
