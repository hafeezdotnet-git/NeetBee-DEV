
# ==========================================
# Stage 1: Runtime environment
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base

WORKDIR /app

EXPOSE 5000

ENV ASPNETCORE_HTTP_PORTS=5000
# Docker Desktop resolves host.docker.internal to an IPv6 address without a container route in this setup.
ENV ReverseProxy__Clusters__authentication__Destinations__primary__Address=http://192.168.65.254:5001/
ENV ReverseProxy__Clusters__notification__Destinations__primary__Address=http://192.168.65.254:5002/
ENV ReverseProxy__Clusters__payment__Destinations__primary__Address=http://192.168.65.254:5003/


# ==========================================
# Stage 2: Build application
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY ["ApiGateway.csproj", "./"]

RUN dotnet restore "ApiGateway.csproj"

COPY . .

RUN dotnet publish "ApiGateway.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false


# ==========================================
# Stage 3: Final image
# ==========================================
FROM base AS final

WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "ApiGateway.dll"]
