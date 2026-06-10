# Smart Fleet Compliance & Renewal Management System for Panipat Refinery

A production-ready enterprise web portal built for **Indian Oil Corporation Limited (IOCL) Panipat Refinery** to digitally manage refinery transport vehicles, department ownership scopes, automatic license validity tracking, alert notification triggers, renewal workflows, and gate safety verification.

---

## Technical Stack & Architecture

The application is built using a clean, modern .NET architecture, fully containerized and hosted using a reverse-proxy setup:

*   **Frontend Core:** Blazor Server (InteractiveServer mode) — dynamic, real-time UI synchronization without page refreshes.
*   **Backend Core:** ASP.NET Core 8.0 Web API — clean REST endpoints and SignalR hub connectivity.
*   **Database Engine:** SQLite (via Entity Framework Core) — normal forms database configured with relational integrity and foreign keys.
*   **Document Generator:** QuestPDF — generates high-performance compliance PDF reports.
*   **Real-time Synced Alerts:** ASP.NET Core SignalR — pushes immediate notifications to open browser sessions.
*   **Proxy & Routing:** Nginx — routes public internet requests to appropriate internal services (`/` to Blazor, `/api` and `/hubs` to Backend API).
*   **Secure Public Hosting:** Localtunnel — exposes the local docker-compose setup to the public internet securely at a custom domain.

---

## Software Project Architecture

```
IOCL/
├── backend-dotnet/
│   ├── Controllers/        # ASP.NET Core API controllers
│   ├── Data/               # DB Context, Migration scripts, and Database Seeder
│   ├── DTOs/               # Data Transfer Objects
│   ├── Hubs/               # SignalR Hubs for real-time notifications
│   ├── Middleware/         # Exception handling and CORS mapping
│   ├── Models/             # Entity Framework Core Database Models
│   ├── Services/           # Email, compliance calculation, and audit services
│   ├── database/           # SQLite database directory (mounted as persistent volume)
│   ├── uploads/            # Compliance certificates/RC files (mounted as persistent volume)
│   └── Dockerfile          # Multi-stage production build script
├── frontend-dotnet/
│   ├── Components/         # Blazor components, layout pages, and views
│   ├── DTOs/               # Client-side data contracts
│   ├── Services/           # Scoped API Client wrappers and SignalR Client listeners
│   ├── wwwroot/            # Static files (images, JS helpers, site CSS)
│   └── Dockerfile          # Blazor Server production build script
├── nginx/
│   └── nginx.conf          # Reverse proxy config routing client requests
└── docker-compose.yml      # Docker compose stack definition orchestrating the services
```

---

## Database Schema & Tables

The database consists of 9 normalized tables managed using Entity Framework Core:

1.  **`Departments`:** Registry of refinery divisions, including division name, unique company code, description, and overall compliance score.
2.  **`Users`:** Operator accounts, role credentials (`SUPER_ADMIN` | `DEPT_ADMIN` | `VIEWER`), and department scoping filters.
3.  **`Vehicles`:** Plate registration numbers, category type, driver/vendor details, QR code link, and aggregated health status.
4.  **`Documents`:** File registry paths, sizes, formats, and uploader IDs.
5.  **`ComplianceRecords`:** Separate slots for the **9 required safety licenses** (Road Permit, PUC, Fitness Certificate, Explosives License/PESO, Insurance, Tax Token, Driver License, Gate Pass, and RC), expiry dates, and alert status flags.
6.  **`RenewalHistories`:** Historical logs of renewals recording previous expiry dates, new expiry dates, uploader details, and old/new document references.
7.  **`Notifications`:** Unread warnings, critical alerts, and expiry notifications.
8.  **`AuditLogs`:** Security trail logging operator mutations, IP addresses, timestamps, and serialized JSON payloads.
9.  **`Reports`:** Registry of compiled PDF and Excel reports.

---

## Compliance Alert Trigger Logic

Vehicle compliance and status are monitored dynamically based on the remaining days until the license expires:

*   **Remaining Days > 30:** `ACTIVE` (Green Indicator) — Vehicle is fully cleared.
*   **Remaining Days 16 - 30:** `WARNING` (Yellow Indicator) — Emits a real-time SignalR notification.
*   **Remaining Days 8 - 15:** `MEDIUM_CRITICAL` (Orange Indicator) — Generates notifications and emails department admins.
*   **Remaining Days 1 - 7:** `HIGH_CRITICAL` (Dark Orange Indicator) — Sends high-priority warnings and flags vehicles at the gate check.
*   **Remaining Days <= 0:** `EXPIRED` (Red Alarm) — Vehicle is automatically blocked from refinery entry.

---

## API Endpoints Documentation

All requests except Login and Public Gate QR Verification require an `Authorization: Bearer <token>` header.

### 1. Authentication
*   `POST /api/auth/login` — Login credentials verification. Returns JWT token and operator role.
*   `GET /api/auth/me` — Verifies active session context.

### 2. Vehicle Fleet
*   `GET /api/vehicles` — List vehicles (applies keyword searches, status filters, and RBAC department scopes).
*   `GET /api/vehicles/:id` — Load detailed compliance records for a vehicle.
*   `POST /api/vehicles` — Register vehicle (Initializes 9 blank compliance slots).
*   `PUT /api/vehicles/:id` — Edit driver or contractor.
*   `DELETE /api/vehicles/:id` — Decommission vehicle and purge compliance records.

### 3. Public QR Clearance (No Auth Required)
*   `GET /api/vehicles/verify/plate/:plateNumber` — Fetches gate clearance card showing green "OK TO ENTER" or red "ENTRY DENIED" based on expiry checks.

### 4. Renewals & History
*   `GET /api/compliance` — List compliance licenses.
*   `PUT /api/compliance/renew/:id` — Submits a new certificate document (accepts Multi-part Form Data file upload along with new dates), archives to `RenewalHistory`, and triggers socket alerts.
*   `GET /api/compliance/history` — Audits compliance renewal transition logs.

### 5. Administration
*   `GET /api/departments` — List refinery divisions.
*   `POST /api/departments` — Register division.
*   `GET /api/users` — List operator accounts.
*   `POST /api/users` — Create operator account.
*   `GET /api/audit` — Inspect security audit trails. Shows mutation details and serialized JSON payloads.

---

## Local Setup & Installation

To run the application locally without Docker, follow these steps:

### Prerequisites
*   [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) or higher.

### Step 1: Database Setup and Seeding
Navigate to the `backend-dotnet` folder and run the database seeder to create and populate the SQLite database:
```bash
cd backend-dotnet
dotnet run --seed
```
*Note: This creates the SQLite database file and seeds initial departments, users, vehicles, and compliance records.*

### Step 2: Run the Backend API
In the `backend-dotnet` folder:
```bash
dotnet run
```
The backend API boots on port `5000` (http://localhost:5000).

### Step 3: Run the Blazor Frontend
Open a new terminal window in the `frontend-dotnet` folder and start the Blazor Server application:
```bash
cd frontend-dotnet
dotnet run
```
The Blazor frontend will run on port `5173` (http://localhost:5173).

---

## Docker Compose Setup & Hosting

A production-ready `docker-compose.yml` configures Nginx and public routing tunnels.

### 1. Configure SMTP Credentials
Update your Gmail SMTP credentials as environment variables under the `backend-api` service in `docker-compose.yml`:
*   `Email__User=your-email@gmail.com`
*   `Email__Pass=your-app-password` (Gmail 16-character App Password)
*   `Email__FromAddress=your-email@gmail.com`

### 2. Start the Stack
From the root directory, start all services:
```powershell
docker compose up -d --build
```
This builds the .NET backend/frontend images, sets up Nginx to proxy traffic on port `8090`, and starts a `localtunnel` container requesting the subdomain `indianoilfleetmanagement`.

Once the stack is running, the portal is immediately accessible on the internet at:
👉 **`https://indianoilfleetmanagement.loca.lt`**

---

## Operator Access Matrix (RBAC Logins)

Use these credentials to demonstrate and test role scoping:

1.  **Super Admin:**
    *   **Username:** `superadmin`
    *   **Password:** `password123`
    *   **Access:** Global access across all departments, CRUD departments and users, exports refinery reports, inspects audit trails, and triggers manual compliance alerts.
2.  **Department Admin (Logistics):**
    *   **Username:** `logisticsadmin`
    *   **Password:** `password123`
    *   **Access:** Scoped strictly to the *Logistics & Transport* department. Can register department vehicles, execute renewals, and view department-specific audits. Cannot read or write safety/production files.
3.  **Department Admin (Safety):**
    *   **Username:** `safetyadmin`
    *   **Password:** `password123`
    *   **Access:** Scoped strictly to the *Safety & Fire* department.
4.  **Viewer:**
    *   **Username:** `viewer`
    *   **Password:** `password123`
    *   **Access:** Read-only access to search vehicles and download compliance reports. Cannot execute renewals or register vehicles.
