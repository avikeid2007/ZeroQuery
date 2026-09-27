# ==============================================================================
# Zeroquery Backend & DAB Subprocess Host Dockerfile
# ==============================================================================

# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project definitions for caching layer
COPY Zeroquery.slnx ./
COPY backend/Zeroquery.Core/Zeroquery.Core.csproj backend/Zeroquery.Core/
COPY backend/Zeroquery.Api/Zeroquery.Api.csproj backend/Zeroquery.Api/
COPY backend/Zeroquery.Tests/Zeroquery.Tests.csproj backend/Zeroquery.Tests/

# Restore dependencies
RUN dotnet restore backend/Zeroquery.Api/Zeroquery.Api.csproj

# Copy all source files
COPY backend/ backend/

# Publish API
RUN dotnet publish backend/Zeroquery.Api/Zeroquery.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Install Microsoft Data API builder globally in a dedicated tools directory
RUN dotnet tool install -g Microsoft.DataApiBuilder --tool-path /app/tools

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Ensure DAB tools are available in system PATH
ENV PATH="${PATH}:/app/tools"

# Default persistence directory for encrypted profiles and audit logs
ENV MCP_PERSISTENCE_DIR="/data"
VOLUME /data

# Copy published application and DAB CLI
COPY --from=build /app/publish .
COPY --from=build /app/tools /app/tools

# Expose API port and dynamic DAB instance port range
EXPOSE 5000
EXPOSE 6000-6050

ENV ASPNETCORE_URLS="http://+:5000"

ENTRYPOINT ["dotnet", "Zeroquery.Api.dll"]
