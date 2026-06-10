#!/bin/sh

echo "============================================="
echo "Starting IOCL Fleet Compliance Unified Stack"
echo "============================================="

# Apply default environment variables if not set
export ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Production}
export Database__StoragePath=${Database__StoragePath:-/app/backend/database/iocl_compliance.sqlite}
export Upload__Directory=${Upload__Directory:-/app/backend/uploads}

# Start backend in background on port 5000
echo "[1/3] Starting Backend API..."
cd /app/backend
dotnet IoclFleetApi.dll &

# Start frontend in background on port 5173
echo "[2/3] Starting Blazor Frontend..."
cd /app/frontend
dotnet frontend-dotnet.dll &

# Start Nginx in foreground to keep container alive
echo "[3/3] Starting Nginx Proxy..."
nginx -g "daemon off;"
