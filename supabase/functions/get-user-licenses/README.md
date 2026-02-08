# get-user-licenses (Secure)

This Supabase Edge Function returns **license metadata** for the authenticated user **without leaking full license keys**.

## Why
If a user’s Google account (SSO) is compromised, returning full license keys would enable an attacker to copy/activate them elsewhere. This endpoint mitigates that by returning **masked keys only**.

## Request

- Method: `POST`
- Path: `/functions/v1/get-user-licenses`
- Auth: `Authorization: Bearer <JWT>`
- Body: optional (ignored)

## Response

```json
{
  "licenses": [
    {
      "license_id": "uuid",
      "license_key_masked": "XXXX-XXXX-XXXX-1234",
      "license_type": "professional",
      "expires_at": "2027-02-07T00:00:00Z",
      "is_active": true,
      "max_machines": 3,
      "machines_used": 1,
      "created_at": "2026-02-07T12:00:00Z"
    }
  ],
  "email": "user@example.com",
  "count": 1
}
```

## Activation policy
This endpoint is **read-only**. Activation still requires the user to enter their license key and call the existing `bridge-license` function.
