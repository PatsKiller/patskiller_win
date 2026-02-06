# Phase 3 — Strict Licensing Deployment Runbook

## What changed
- Licensing is now **strict**:
  - **Google SSO identity is required** for all license operations (activate/validate/heartbeat/deactivate)
  - Token-based features require **both**: (1) signed-in SSO user and (2) valid license bound to that email
- Machine binding is upgraded to **CombinedId**: `MachineId:SIID` with legacy fallback.
- License cache file renamed to `license.key` (legacy `license.dat` migrates automatically).

---

## Backend (Supabase) — one-time
### 1) Apply migration
Run migrations as usual:
- `supabase db push` (local)
- or apply `supabase/migrations/20260205_phase3_bridge_license_siid.sql` to your target DB.

### 2) Deploy Edge Function
Deploy:
- Function path: `supabase/functions/bridge-license`
- Required env vars:
  - `SUPABASE_URL`
  - `SUPABASE_SERVICE_ROLE_KEY`

CORS:
- Allows `authorization, apikey, content-type`

Endpoint:
- `POST /functions/v1/bridge-license`

---

## Desktop App — build & test
### 1) Login + License
1. Start app
2. Sign in with Google
3. Click **License: ...** in header → open License Management
4. Enter key and activate

Expected:
- License shows **Active**
- Header shows:
  - Tokens: <n>
  - License: <type>
  - <email>

### 2) Machine limit
- Activate the same license on multiple machines until limit reached.
Expected:
- Activation fails with `Machine limit reached (...)`

### 3) Wrong account
- Sign in with a different Google email and try validate/activate.
Expected:
- License shows “Attention needed” + message indicates mismatch.

---

## Troubleshooting quick hits
- **401 auth_required**: client isn’t sending Authorization header (SSO not set in LicenseService)
- **email_mismatch**: payload user_email doesn’t match JWT email, or license is bound to another email
- **machine_limit**: deactivate an old machine from License Management
