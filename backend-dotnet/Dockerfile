# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY ["IoclFleetApi.csproj", "./"]
RUN dotnet restore "IoclFleetApi.csproj"

# Copy everything else and build app
COPY . .
RUN dotnet publish "IoclFleetApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 5000

# Copy published files
COPY --from=build /app/publish .

# Create database and uploads directories
RUN mkdir -p database uploads

ENTRYPOINT ["dotnet", "IoclFleetApi.dll"]
