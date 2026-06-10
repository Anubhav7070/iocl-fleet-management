# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY ["frontend-dotnet.csproj", "./"]
RUN dotnet restore "frontend-dotnet.csproj"

# Copy everything else and build app
COPY . .
RUN dotnet publish "frontend-dotnet.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 5173

# Copy published files
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "frontend-dotnet.dll"]
