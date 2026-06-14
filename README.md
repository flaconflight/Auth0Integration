# Auth0 Passwordless Integration — POC

A proof-of-concept for Auth0 passwordless authentication where an **admin system** initiates a magic link for an **end user**, with custom credit application context linked to the user's profile.

## Architecture

```
Admin System  ──POST /api/passwordless/start──▶  Azure Function (Backend)
                                                      │
                                                      ├─▶ Auth0 /passwordless/start (send=code)
                                                      │       └─▶ Auth0 emails OTC to end user
                                                      │
End User clicks link ──▶ React App (future) ──POST /api/auth/verify-otc──▶  Backend
                                                                              │
                                                                              ├─▶ Auth0 /oauth/token (server-side, no browser session)
                                                                              ├─▶ Auth0 Management API → writes credit context to app_metadata
                                                                              └─▶ Returns tokens to frontend
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local)
- [Postman](https://www.postman.com/) (for testing)

## Auth0 Tenant Setup

### Step 1: Create Auth0 Account & Tenant

1. Go to [https://auth0.com](https://auth0.com) → **Sign Up**
2. Sign up with your email, Google, or GitHub account
3. After sign-up, a default tenant is created — name format: `dev-xxx.us.auth0.com`
4. **Copy your tenant domain** — visible in the top-left dropdown and in **Settings → Domain**

### Step 2: Configure Passwordless Connection

1. Auth0 Dashboard → left menu → **Authentication** → **Passwordless**
2. Toggle **Email** ON
3. Click **Email** → **Settings** tab
4. Configure:
   | Setting | Value |
   |---------|-------|
   | **OTP Expiry** | `5` minutes |
   | **OTP Length** | `8` characters |
   | **Disable Sign Ups** | OFF (new users auto-register) |
   | **From** | Your verified sender email or leave default |
   | **Subject** | `Your verification code` |
   | **Message** | `Your verification code is: {{code}}` |
5. Click **Save**

### Step 3: Create a Regular Web Application (for backend Auth0 calls)

1. **Applications** → **Applications** → **Create Application**
2. Name: `Auth0Integration - Backend`
3. Type: **Regular Web Application**
4. Click **Create**
5. On the **Settings** tab, copy:
   - **Client ID** (→ `Auth0:BackendClientId`)
   - **Client Secret** (→ `Auth0:BackendClientSecret`)
6. Scroll to **Advanced Settings** → **Grant Types** tab
7. Enable:
   - ✅ **Passwordless OTP** (`http://auth0.com/oauth/grant-type/passwordless/otp`)
   - ✅ **Client Credentials**
8. Click **Save Changes**

### Step 4: Create a Machine-to-Machine Application (for Management API)

1. **Applications** → **Applications** → **Create Application**
2. Name: `Auth0Integration - Management`
3. Type: **Machine to Machine**
4. Click **Create**
5. In the dialog:
   - Select **Auth0 Management API**
   - Select scopes:
     - `read:users`
     - `update:users`
     - `read:roles`
     - `create:roles`
     - `read:users_app_metadata`
     - `update:users_app_metadata`
6. Click **Authorize**
7. Copy:
   - **Client ID** (→ `Auth0:ManagementClientId`)
   - **Client Secret** (→ `Auth0:ManagementClientSecret`)

### Step 5: (Optional) Create a Custom API

1. **Applications** → **APIs** → **Create API**
2. Name: `Auth0Integration API`
3. Identifier: `https://auth0integration.api`
4. Signing Algorithm: `RS256`
5. Click **Create**
6. Copy the **Identifier** (→ `Auth0:Audience`)

If you skip this step, use `https://{your-tenant-domain}/api/v2/` as the audience.

## Local Setup

### 1. Configure `local.settings.json`

Copy the template file:
```powershell
copy backend\Auth0Integration.Functions\local.settings.json.example backend\Auth0Integration.Functions\local.settings.json
```

Edit `local.settings.json` and fill in your Auth0 values:

```json
{
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "Auth0:Domain": "dev-xxx.us.auth0.com",
    "Auth0:BackendClientId": "<from Step 3>",
    "Auth0:BackendClientSecret": "<from Step 3>",
    "Auth0:ManagementClientId": "<from Step 4>",
    "Auth0:ManagementClientSecret": "<from Step 4>",
    "Auth0:PasswordlessConnection": "email",
    "Auth0:Audience": "https://auth0integration.api",
    "Auth0:ManagementAudience": "https://dev-xxx.us.auth0.com/api/v2/"
  }
}
```

### 2. Run the Backend

```powershell
cd backend\Auth0Integration.Functions
func start
```

You should see:
```
Functions:
    PasswordlessStart: POST /api/passwordless/start
    UserProfile: GET /api/user/profile
    VerifyOtc: POST /api/auth/verify-otc
```

## Testing with Postman

### Import the Collection

1. Open Postman → **File** → **Import**
2. Select `backend/Auth0Integration.Functions/Auth0Integration.postman_collection.json`
3. Set the collection variables (click collection → **Variables** tab):
   | Variable | Value |
   |----------|-------|
   | `baseUrl` | `http://localhost:7071` |
   | `endUserEmail` | Your email address |

### Test Flow

#### Step 1: Initiate Passwordless
```
POST http://localhost:7071/api/passwordless/start
{
  "email": "your-email@test.com",
  "creditContext": {
    "applicationId": "APP-001",
    "loanAmount": 50000,
    "purpose": "home_improvement",
    "stage": "initiated"
  }
}
```
✅ Expected: `200 { "message": "Verification code sent to ...", "correlationId": "..." }`

Check your email — you'll receive an 8-character code from Auth0.

#### Step 2: Verify OTC
```
POST http://localhost:7071/api/auth/verify-otc
{
  "email": "your-email@test.com",
  "otc": "<8-char code from email>"
}
```
✅ Expected: `200 { "access_token": "eyJ...", "user": { "sub": "auth0|...", "app_metadata": {...} } }`

Response includes the credit context written to Auth0 `app_metadata`.

#### Step 3: Get User Profile
```
GET http://localhost:7071/api/user/profile
Authorization: Bearer <access_token from Step 2>
```
✅ Expected: `200 { "email": "...", "app_metadata": { "creditApplications": [...] } }`

### Testing Multiple Credit Applications

1. Call Step 1 with `APP-001` → verify → user gets APP-001
2. Call Step 1 again with `APP-002` → verify → user gets both APP-001 and APP-002
3. Call Step 1 again with `APP-001` → verify → returns `409 Conflict` (duplicate)

## API Endpoints

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/passwordless/start` | POST | Function Key | Admin-triggered. Sends OTC email to end user, stores credit context |
| `/api/auth/verify-otc` | POST | None | End user verifies OTC. Backend exchanges with Auth0, links credit context to user |
| `/api/user/profile` | GET | Bearer Token | Returns user profile with `app_metadata.creditApplications` and roles |

---

## OAuth & Auth0 Concepts

### Core Concepts

**Claims** are key-value pairs of information about a user embedded inside a **JWT token** (JSON Web Token). When a user authenticates, Auth0 issues an ID token and an Access token — both contain claims.

A JWT payload looks like:
```json
{
  "sub": "auth0|63abc123",
  "email": "user@example.com",
  "email_verified": true,
  "iss": "https://dev-xxx.us.auth0.com/",
  "aud": "https://auth0integration.api",
  "iat": 1718000000,
  "exp": 1718086400
}
```

These are **standard claims**. You can also add **custom claims** — your own key-value data (e.g., `credit_app_id`, `org_id`, `can_approve`).

---

**Roles** are a type of claim — a named label assigned to a user. In Auth0:
- Roles are defined in **User Management → Roles**
- A role like `CreditAppViewer` gets assigned to users
- When requested with the right scope (`read:roles`), the role name appears as a claim in the token

A role is fundamentally just a claim like:
```json
{
  "roles": ["credit_app_viewer"]
}
```

But Auth0 gives them their own management UI, CRUD API, and permission scoping.

---

**Permissions** are the actions a role allows. In Auth0's **RBAC** (Role-Based Access Control):
- A **Role** (e.g., `CreditAppManager`) contains **Permissions** (e.g., `read:credit-app`, `approve:credit-app`, `reject:credit-app`)
- Permissions are granular action-level strings
- They appear in the Access Token as:
```json
{
  "permissions": ["read:credit-app", "approve:credit-app"]
}
```

---

**`user_metadata` vs `app_metadata`** — two JSON objects attached to every Auth0 user:

| Metadata | Who writes it | Typical use |
|----------|-------------|-------------|
| `user_metadata` | The end user (via profile editor) | Preferences, display name, theme |
| `app_metadata` | Your application (via Management API) | Role assignments, credit app context, internal state |

---

### How Data Flows in Auth0 Tokens

```
┌────────────────────────────────────────────────────────────────┐
│  Auth0 Management API                                         │
│  The user's Auth0 profile contains:                           │
│    user_metadata: { "preferredName": "John" }                │
│    app_metadata:  { "creditApplications": [...], "role": ... }│
└─────────────────────────┬──────────────────────────────────────┘
                          │
                          ▼
┌────────────────────────────────────────────────────────────────┐
│  Auth0 Action (Post-Login pipeline)                           │
│  A serverless function that runs every time a user logs in.   │
│  Reads app_metadata → writes custom claims into the token:    │
│                                                                │
│  api.accessToken.setCustomClaim("credit_app_state", "approved")│
│  api.accessToken.setCustomClaim("can_view_approval", true)    │
└─────────────────────────┬──────────────────────────────────────┘
                          │
                          ▼
┌────────────────────────────────────────────────────────────────┐
│  Auth0 issues Access Token with custom claims:                │
│  {                                                            │
│    "sub": "auth0|...",                                        │
│    "credit_app_id": "APP-001",                                │
│    "roles": ["viewer"],                                       │
│    "permissions": ["read:status"],                            │
│    "credit_app_state": "approved",                            │
│    "can_view_approval": true                                  │
│  }                                                            │
└─────────────────────────┬──────────────────────────────────────┘
                          │
                          ▼
┌────────────────────────────────────────────────────────────────┐
│  Your Backend validates the token (using Auth0's JWKS public  │
│  keys), reads the claims, and uses them for authorization:    │
│  - "sub" → identifies the user                                │
│  - "permissions" → checks if user can call this endpoint      │
│  - "credit_app_state" → returns appropriate data              │
└────────────────────────────────────────────────────────────────┘
```

**How this applies to the POC:**
Currently, the POC writes credit application data to `app_metadata` via the Management API. The `UserProfile` endpoint returns this data directly. In a full production setup, you'd add an **Auth0 Action** that reads `app_metadata` and injects fine-grained claims (like `can_edit_documents`, `can_approve`) into the access token so the frontend and backend can make authorization decisions without calling the Management API on every request.

---

### Other Auth0 Concepts Useful for Your Credit App

| Concept | What it is | How to use it |
|---------|-----------|--------------|
| **Auth0 Actions** | Serverless functions that run during the login pipeline | Enrich tokens with custom claims from `app_metadata`, apply access rules, block logins |
| **Organizations** | Built-in multi-tenancy. Users belong to orgs with their own IdP connections | Each lender/credit union is an org with its own user directory and branding |
| **Token Exchange** | Exchange one token for another with different claims/scopes | A processor's token can be exchanged for an approver token after manager approval |
| **Refresh Tokens** | Long-lived tokens to get new Access Tokens without re-authentication | Keep the user logged in during long credit application sessions |
| **MFA / Step-Up Auth** | Require additional verification for sensitive operations | "This loan exceeds $100,000 — please confirm your identity with MFA" |
| **Connections** | Identity providers (email/password, Google, SAML, LDAP, etc.) | Credit union employees log in via corporate SSO, end users use passwordless email |

---

## Practical Examples for Your Credit App

### Example 1: Simple Role-Based Access

Define 3 roles in Auth0 (**User Management → Roles**):

| Role | Permissions | What the user sees |
|------|------------|-------------------|
| `credit_app_viewer` | `read:credit-app` | Can view the application status only |
| `credit_app_processor` | `read:credit-app`, `update:credit-app` | Can view + update documents, add notes |
| `credit_app_approver` | `read:credit-app`, `approve:credit-app`, `reject:credit-app` | Can make final approval decisions |

**Backend authorization logic:**
```
GET /api/credit-app/{id}           → requires "read:credit-app" permission
POST /api/credit-app/{id}/approve  → requires "approve:credit-app" permission
```

**Frontend rendering:**
```
if roles includes "credit_app_approver"  → show Approve/Reject buttons
if roles includes "credit_app_viewer"    → show read-only dashboard
```

**Implementation via Management API:**
```csharp
// Assign role to user after OTC verification
await _auth0Mgmt.AssignRolesAsync(sub, new List<string> { "role_id_for_viewer" });

// Read roles in UserProfile endpoint
var roles = await _auth0Mgmt.GetUserRolesAsync(sub);
```

---

### Example 2: State-Based Access via `app_metadata`

Credit applications move through states: `draft → submitted → under_review → approved → disbursed`. The `app_metadata` tracks both the applications and the user's permissions within each state.

**Data structure:**
```json
{
  "creditApplications": [
    {
      "applicationId": "APP-001",
      "state": "under_review",
      "loanAmount": 50000,
      "assignedProcessor": "auth0|processor-id"
    }
  ],
  "accessLevels": {
    "canEditDocuments": true,
    "canViewApproval": false,
    "canAddNotes": true
  }
}
```

**Auth0 Action** (post-login) reads `app_metadata` and injects claims into the token:

```javascript
// Auth0 Dashboard → Actions → Library → Build Custom
// Trigger: Login / Post Login
exports.onExecutePostLogin = async (event, api) => {
  const metadata = event.user.app_metadata;
  const app = metadata?.creditApplications?.[0];

  if (app?.state === "under_review") {
    api.accessToken.setCustomClaim("credit_app_state", "under_review");
    api.accessToken.setCustomClaim("can_view_approval", false);
    api.accessToken.setCustomClaim("can_edit_documents", true);
  }

  if (app?.state === "approved") {
    api.accessToken.setCustomClaim("credit_app_state", "approved");
    api.accessToken.setCustomClaim("can_view_approval", true);
    api.accessToken.setCustomClaim("can_edit_documents", false);
  }
};
```

**Frontend reads claims directly from the token** (no extra API calls):
```
if token.can_edit_documents → show "Upload Document" button
if token.can_view_approval  → show approval letter download
```

---

### Example 3: Multi-Tenant / Organization Context

If the credit app serves multiple lenders/credit unions:

```json
{
  "orgId": "org_creditcorp",
  "orgRole": "underwriter",
  "creditApplications": [
    {
      "id": "APP-001",
      "orgId": "org_creditcorp"
    }
  ]
}
```

**Auth0 Action** enriches the token with org context:
```javascript
exports.onExecutePostLogin = async (event, api) => {
  const metadata = event.user.app_metadata;
  api.accessToken.setCustomClaim("org_id", metadata?.orgId);
  api.accessToken.setCustomClaim("org_role", metadata?.orgRole);
};
```

**Backend validates org isolation:**
```csharp
// Only return data if the user's org matches the application's org
if (token.OrgId != application.OrgId)
    return Forbidden;
```

This prevents users from seeing credit applications from other organizations.

---

### How This Fits the End Goal

**"Allow the actual end user to view the credit app in different states with different levels of access"** — the architecture:

```
1. User authenticates via passwordless OTC → Auth0 issues Access Token
2. Auth0 Action runs at login:
   - Reads app_metadata.creditApplications[0].state
   - Writes claims: { credit_app_state, role, permissions }
3. Frontend receives token, renders UI based on state + role:
   - Draft:       user can edit all fields
   - Submitted:   user can view but not edit, sees "Under Review" banner
   - Approved:    user sees approval letter, disbursement info
4. Backend validates permissions per endpoint:
   - GET    /api/credit-app/{id}   → requires "read:credit-app"
   - PATCH  /api/credit-app/{id}   → requires "update:credit-app" AND state != "approved"
   - POST   /api/credit-app/{id}/approve → requires "approve:credit-app"
```

The **state** determines the available actions. The **role** determines who can perform them. The **claims** carry both to the frontend and backend.

---

## Data Flow

```
app_metadata.creditApplications (stored in Auth0):
{
  "creditApplications": [
    {
      "applicationId": "APP-001",
      "loanAmount": 50000,
      "purpose": "home_improvement",
      "stage": "initiated",
      "linkedAt": "2026-06-14T12:00:00Z",
      "linkedByOtc": true
    },
    {
      "applicationId": "APP-002",
      "loanAmount": 75000,
      "purpose": "debt_consolidation",
      "stage": "submitted",
      "linkedAt": "2026-06-14T14:00:00Z",
      "linkedByOtc": true
    }
  ]
}
```

## Project Structure

```
backend/Auth0Integration.Functions/
├── Functions/
│   ├── PasswordlessStart.cs     POST /api/passwordless/start
│   ├── VerifyOtc.cs              POST /api/auth/verify-otc
│   └── UserProfile.cs            GET  /api/user/profile
├── Models/                       Request/response DTOs
├── Services/
│   ├── Auth0AuthenticationService.cs  Auth0 passwordless API calls
│   ├── Auth0ManagementService.cs      Auth0 Management API calls
│   └── InMemoryCreditContextStore.cs  Temporary OTC storage (30min TTL)
├── Configuration/Auth0Options.cs     Strongly-typed config
├── Program.cs                        DI setup
├── host.json
├── local.settings.json               (ignored by git — contains secrets)
└── local.settings.json.example       (tracked — template with placeholders)
```

## Important Notes

- **No cross-browser issue**: OTC exchange happens server-to-server (`POST /oauth/token` from backend to Auth0). No browser session dependency.
- **Email validation**: If the wrong email is entered, the backend catches it with `email_mismatch` error. Admin re-initiates with the correct email.
- **Duplicate prevention**: Each `applicationId` can only be linked once per user. Returns `409 Conflict` on duplicate.
- **Security**: Auth0 client secret is only stored in the backend (`local.settings.json`), never exposed to the frontend.
- **Email delivery**: For POC, Auth0 sends the email. For production, the backend would send custom-branded emails.

## Production Considerations

| Concern | POC Approach | Production Improvement |
|---------|-------------|----------------------|
| Token delivery | Response body | HTTP-only secure session cookie |
| OTC storage | In-memory dictionary | Azure Table Storage / Cosmos DB |
| Admin auth | Function Key | Auth0-protected admin endpoints |
| Email sending | Auth0 sends OTC | Backend sends custom magic link |
| Frontend | Postman | React app with `@auth0/auth0-react` |
