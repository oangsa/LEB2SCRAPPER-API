# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files for dependency resolution
COPY LEB2SCRAPPER.sln ./
COPY LEB2SCRAPPER/*.csproj ./LEB2SCRAPPER/
COPY LEB2SCRAPPER.Contracts/*.csproj ./LEB2SCRAPPER.Contracts/
COPY LEB2SCRAPPER.Entity/*.csproj ./LEB2SCRAPPER.Entity/
COPY LEB2SCRAPPER.Infrastructure/*.csproj ./LEB2SCRAPPER.Infrastructure/
COPY LEB2SCRAPPER.Infrastructure.Contracts/*.csproj ./LEB2SCRAPPER.Infrastructure.Contracts/
COPY LEB2SCRAPPER.Presentation/*.csproj ./LEB2SCRAPPER.Presentation/
COPY LEB2SCRAPPER.Repository/*.csproj ./LEB2SCRAPPER.Repository/
COPY LEB2SCRAPPER.Service/*.csproj ./LEB2SCRAPPER.Service/
COPY LEB2SCRAPPER.Service.Contracts/*.csproj ./LEB2SCRAPPER.Service.Contracts/
COPY LEB2SCRAPPER.Tests/*.csproj ./LEB2SCRAPPER.Tests/

# Restore dependencies
RUN dotnet restore

# Copy source code
COPY . .

# Build and publish
WORKDIR /src/LEB2SCRAPPER
RUN dotnet publish -c Release -o /app --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0

# Install Chromium, ChromeDriver, certificates, fonts, and package dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    chromium \
    chromium-driver \
    fonts-liberation \
    && rm -rf /var/lib/apt/lists/*

# Environment variables for Chrome and ASP.NET Core
ENV CHROME_BIN=/usr/bin/chromium \
    CHROMEDRIVER_BIN=/usr/bin/chromedriver \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Production

# Create app directory and copy published application
WORKDIR /app
COPY --from=build /app ./

# Expose port for Cloud Run
EXPOSE 8080

# Run the application
ENTRYPOINT ["dotnet", "LEB2SCRAPPER.dll"]
