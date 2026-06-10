# VxPayBridge API Dockerfile
# Build: docker build -f Dockerfile -t vxpaybridge .
# Fly.io: fly deploy --dockerfile Dockerfile --app vxpaybridge

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy solution file
COPY ["VxPayBridge.sln", "./"]

# Copy project file for restore
COPY ["VxPayBridge.API/VxPayBridge.API.csproj", "VxPayBridge.API/"]

# Restore dependencies
RUN dotnet restore "VxPayBridge.API/VxPayBridge.API.csproj"

# Copy all source code
COPY . .

# Build the application
WORKDIR "/src/VxPayBridge.API"
RUN dotnet build "VxPayBridge.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "VxPayBridge.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "VxPayBridge.API.dll"]
