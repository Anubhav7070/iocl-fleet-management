# Smart Fleet Compliance & Renewal Management System for Panipat Refinery

A production-ready centralized enterprise web portal built for **Indian Oil Corporation Limited (IOCL) Panipat Refinery** to digitally manage refinery transport vehicles, department ownership scopes, automatic license validity tracking, alert notification triggers, renewal workflows, and gate safety verification.

---

## Technical Stack & Systems

- **Frontend Core:** React.js, Tailwind CSS (v3.4), Lucide Icons, Chart.js & React-Chartjs-2
- **Backend Core:** Node.js, Express.js, Socket.io
- **Database Engine:** SQLite (configured with foreign keys, normalization, and cascade index configurations)
- **Object Relational Mapper (ORM):** Sequelize ORM
- **Authentication Security:** JWT Auth, bcrypt password hashing, Role-Based Access Control (RBAC) middleware
- **Report Compiler:** jsPDF (with custom IOCL PDF branding headers), ExcelJS (for spreadsheets)
- **QR Utility:** `qrcode` (generates base64 data-URLs for gate inspection clearance check)

---

## Software Clean Architecture Layers

```
IOCL/
├── backend/
│   ├── config/             # DB connection & Sequelize configurations
│   ├── controllers/        # Express request routing controllers
│   ├── middleware/         # Security checks (JWT auth, RBAC, multer validators, error captures)
│   ├── models/             # Sequelize normalized schemas (User, Department, Vehicle, etc.)
│   ├── routes/             # REST route mapping indices
│   ├── seeders/            # Database initialization scripts
│   ├── services/           # Business logic engines (alerts, dynamic compliance scores)
│   ├── uploads/            # Local directory serving certificate attachments
│   └── utils/              # Helper libraries (response wrappers, JWT generators)
└── frontend/
    ├── public/             # Static public assets
    └── src/
        ├── components/     # Reusable layout UI (Sidebar, Header, protected path wrappers)
        ├── context/        # Global states (Auth persist, real-time Sockets notifications)
        ├── features/       # Feature views (Dashboard control, Vehicles table, User configuration)
        └── services/       # Fetch API client client wrappers
```

---

## Database Schemas & Normalization

The SQLite database uses 9 tables configured with relational mapping rules:

1. **`departments`:** Name, unique company code, description, and overall compliance score.
2. **`users`:** Operator credentials, role (`SUPER_ADMIN` | `DEPT_ADMIN` | `VIEWER`), status, and department scoping filters.
3. **`vehicles`:** Plate registration numbers, category type, driver/vendor, QR link, and aggregated health status.
4. **`documents`:** File registry paths, sizes, formats, and uploader IDs.
5. **`compliance_records`:** Separate slots for the **8 required safety licenses** (Road Permit, PUC, Fitness, explosives, etc.), expiry dates, and alert status flags.
6. **`renewal_history`:** Historical transitions logging previous expiry dates, new expiry dates, uploader, and old/new document references.
7. **`notifications`:** Unread warnings, critical alerts, and expiry notifications.
8. **`audit_logs`:** Security trail logging operator mutations, IP addresses, timestamps, and serialized JSON payloads.
9. **`reports`:** Register of compiled PDF and Excel reports.

---

## Compliance Alert Trigger Logic

Expiries are monitored dynamically:
- **Remaining Days > 30:** `ACTIVE` (Green Indicator)
- **Remaining Days 16 - 30:** `WARNING` (Yellow Indicator) - logs unread notification and emits socket updates.
- **Remaining Days 8 - 15:** `MEDIUM_CRITICAL` (Orange Indicator) - logs alert and emails department admins.
- **Remaining Days 1 - 7:** `HIGH_CRITICAL` (Dark Orange Indicator) - triggers alarms and pins alerts to gate checking.
- **Remaining Days <= 0:** `EXPIRED` (Red Alarm) - gates are locked for vehicle entry.

---

## API Endpoints Documentation

All requests except Login and Public Gate QR Verification require the header: `Authorization: Bearer <token>`

### 1. Authentication
- `POST /api/auth/login` - Login credentials verification. Returns JWT token and operator role.
- `GET /api/auth/me` - Verifies active session context.

### 2. Vehicle Fleet
- `GET /api/vehicles` - List vehicles. Applies search keywords, status filters, and RBAC department scopes.
- `GET /api/vehicles/:id` - Load detailed compliance records for a vehicle.
- `POST /api/vehicles` - Register vehicle (Initializes 8 blank compliance slots).
- `PUT /api/vehicles/:id` - Edit driver or contractor.
- `DELETE /api/vehicles/:id` - Decommission vehicle and purge compliance records.

### 3. Public QR Clearance (No Auth Required)
- `GET /api/vehicles/verify/:id` - Fetches gate clearance card showing green "OK TO ENTER" or red "ENTRY DENIED" based on expiry checks.

### 4. Renewals & History
- `GET /api/compliance` - List compliance licenses.
- `PUT /api/compliance/renew/:id` - Submits a new certificate document (accepts Multi-part Form Data file upload `file` along with new dates), archives to `RenewalHistory`, and triggers socket alerts.
- `GET /api/compliance/history` - Audits compliance renewal transition logs.

### 5. Administration
- `GET /api/departments` - List refinery divisions.
- `POST /api/departments` - Register division.
- `GET /api/users` - List operator accounts.
- `POST /api/users` - Create operator account.
- `GET /api/audit` - Inspect security audit trails. Shows uploader details and serial JSON payloads.

---

## Local Setup & Installation

### Step 1: Clone and Configure Environment

Ensure Node.js (v18+) is installed. Create `backend/.env` file with configurations:
```env
PORT=5000
NODE_ENV=development
JWT_SECRET=iocl_panipat_refinery_fleet_secret_key_2026_xyz
DB_STORAGE_PATH=./database/iocl_compliance.sqlite
UPLOAD_DIR=./uploads
FRONTEND_URL=http://localhost:5173
```

### Step 2: Install Backend Dependencies & Seed DB

In the `backend` folder:
```bash
# Install packages
npm install

# Run database seeder (seeds 4 users, 4 vehicles, and 32 compliance records)
npm run seed
```

### Step 3: Run Backend server

In the `backend` folder:
```bash
npm run dev
# Server boots on port 5000 and runs a daily cron compliance check scan
```

### Step 4: Install Frontend & Run Dev Client

Open a new terminal window in the `frontend` folder:
```bash
# Install React libraries
npm install

# Run Vite development client
npm run dev
# Client boots on local port 5173
```

---

## Operator Access Matrix (RBAC Logins)

Use these credentials to demonstrate and test role scoping:

1. **Super Admin:**
   - **Username:** `superadmin`
   - **Password:** `password123`
   - **Access:** Global access across all departments, CRUD departments and users, exports refinery reports, inspects audit trails.
2. **Department Admin (Logistics):**
   - **Username:** `logisticsadmin`
   - **Password:** `password123`
   - **Access:** Scoped strictly to the *Logistics & Transport* department. Can register department vehicles, execute renewals, and view department-specific audits. Cannot read or write safety/production files.
3. **Department Admin (Safety):**
   - **Username:** `safetyadmin`
   - **Password:** `password123`
   - **Access:** Scoped strictly to the *Safety & Fire* department.
4. **Viewer:**
   - **Username:** `viewer`
   - **Password:** `password123`
   - **Access:** Read-only access to search vehicles and download compliance reports. Cannot execute renewals or create records.
