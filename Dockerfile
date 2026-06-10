# ==========================================
# 1. Build Backend API
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-backend
WORKDIR /src
COPY backend-dotnet/IoclFleetApi.csproj ./backend-dotnet/
RUN dotnet restore backend-dotnet/IoclFleetApi.csproj
COPY backend-dotnet/ ./backend-dotnet/
WORKDIR /src/backend-dotnet
RUN dotnet publish IoclFleetApi.csproj -c Release -o /app/publish/backend /p:UseAppHost=false

# ==========================================
# 2. Build Frontend Web App
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-frontend
WORKDIR /src
COPY frontend-dotnet/frontend-dotnet.csproj ./frontend-dotnet/
RUN dotnet restore frontend-dotnet/frontend-dotnet.csproj
COPY frontend-dotnet/ ./frontend-dotnet/
WORKDIR /src/frontend-dotnet
RUN dotnet publish frontend-dotnet.csproj -c Release -o /app/publish/frontend /p:UseAppHost=false

# ==========================================
# 3. Final Runtime Image
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install Nginx
RUN apt-get update && apt-get install -y nginx && rm -rf /var/lib/apt/lists/*

# Copy backend files
COPY --from=build-backend /app/publish/backend ./backend

# Copy frontend files
COPY --from=build-frontend /app/publish/frontend ./frontend

# Create folders for database and uploads in backend
RUN mkdir -p /app/backend/database /app/backend/uploads

# Copy seeded uploads
COPY backend-dotnet/uploads/seeded /app/backend/uploads/seeded

# Copy Nginx config
COPY nginx.conf /etc/nginx/nginx.conf

# Copy startup script
COPY start.sh /app/start.sh
RUN chmod +x /app/start.sh

# Expose port 80 (Nginx port)
EXPOSE 80

ENTRYPOINT ["/app/start.sh"]
