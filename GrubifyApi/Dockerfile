# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy project file, local packages, and NuGet config then restore
COPY *.csproj nuget.config ./
COPY lib/ ./lib/
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish -c Release -o out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .

# Build argument for version (can be v1 or v2)
ARG API_VERSION=v1

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV API_VERSION=${API_VERSION}

ENTRYPOINT ["dotnet", "GrubifyApi.dll"]
