/// <reference lib="deno.ns" />

import { serve } from "https://deno.land/std@0.224.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2.49.1";
import { corsHeaders } from "../_shared/cors.ts";

/**
 * PatsKiller Pro — get-user-licenses (Secure)
 *
 * Goal:
 * - Allow the desktop app to list licenses for the signed-in user after Google SSO.
 * - Prevent a "Google account compromise → license key leak": DO NOT return full license keys.
 * - Return only masked key + metadata needed for UI selection (type/expiry/seats).
 *
 * Request:
 * POST /functions/v1/get-user-licenses
 * Authorization: Bearer <JWT>
 * Body: optional (ignored)
 *
 * Response:
 * {
 *   "licenses": [{
 *     "license_id": "uuid",
 *     "license_key_masked": "XXXX-XXXX-XXXX-1234",
 *     "license_type": "professional",
 *     "expires_at": "2027-02-07T00:00:00Z",
 *     "is_active": true,
 *     "max_machines": 3,
 *     "machines_used": 1,
 *     "created_at": "2026-02-07T12:00:00Z"
 *   }],
 *   "email": "user@example.com",
 *   "count": 1
 * }
 */

type LicenseRow = {
  id: string;
  license_key: string;
  license_key_text?: string | null; // optional in some schemas
  license_type: string | null;
  expires_at: string | null;
  max_machines: number | null;
  is_active?: boolean | null; // optional in some schemas
  is_enabled?: boolean | null; // legacy in some schemas
  created_at: string;
  // Optional columns in some schemas
  user_id?: string | null;
  email?: string | null;
  customer_email?: string | null;
};

type LicenseResponse = {
  license_id: string;
  license_key_masked: string;
  license_type: string | null;
  expires_at: string | null;
  is_active: boolean;
  max_machines: number;
  machines_used: number;
  created_at: string;
};

const CODES = {
  OK: "OK",
  AUTH_REQUIRED: "AUTH_REQUIRED",
  AUTH_INVALID: "AUTH_INVALID",
  DB_ERROR: "DB_ERROR",
  SERVER_ERROR: "SERVER_ERROR",
} as const;

function jsonResponse(body: Record<string, unknown>, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json", ...corsHeaders },
  });
}

function parseBearer(authHeader: string | null): string | null {
  if (!authHeader) return null;
  const m = authHeader.match(/^Bearer\s+(.+)$/i);
  return m ? m[1].trim() : null;
}

function normalizeEmail(email: string) {
  return email.trim().toLowerCase();
}

function maskKey(key: string) {
  const raw = String(key ?? "").trim().toUpperCase();
  const compact = raw.replace(/[^A-Z0-9]/g, "");
  const last4 = (compact.slice(-4) || "XXXX").padStart(4, "X");
  return `XXXX-XXXX-XXXX-${last4}`;
}

async function fetchLicensesResilient(
  sbAdmin: ReturnType<typeof createClient>,
  userId: string,
  userEmail: string,
): Promise<{ data: LicenseRow[] | null; error: { message: string } | null }> {
  // Try the richest (newer) schema first.
  const selectCols =
    "id, license_key, license_key_text, license_type, expires_at, max_machines, is_active, is_enabled, created_at";

  // 1) user_id OR email OR customer_email
  const attempt = async (orClause: string) => {
    return await sbAdmin
      .from("bridge_licenses")
      .select(selectCols)
      .or(orClause)
      .order("created_at", { ascending: false });
  };

  // Newest expected schema
  let resp = await attempt(
    `user_id.eq.${userId},email.ilike.${userEmail},customer_email.ilike.${userEmail}`,
  );

  // Fallbacks for older schemas where some columns may not exist.
  if (resp.error) {
    const msg = resp.error.message || "";

    // Remove email
    if (msg.includes("email") && msg.includes("does not exist")) {
      resp = await attempt(
        `user_id.eq.${userId},customer_email.ilike.${userEmail}`,
      );
    }

    // Remove user_id
    if (resp.error) {
      const msg2 = resp.error.message || "";
      if (msg2.includes("user_id") && msg2.includes("does not exist")) {
        resp = await attempt(`customer_email.ilike.${userEmail}`);
      }
    }

    // Older schema: no license_key_text / is_active / is_enabled selection may still work if missing
    // but select would fail; try minimal select.
    if (resp.error) {
      const minimal =
        "id, license_key, license_type, expires_at, max_machines, created_at";
      resp = await sbAdmin
        .from("bridge_licenses")
        .select(minimal)
        .or(`customer_email.ilike.${userEmail}`)
        .order("created_at", { ascending: false });
    }
  }

  if (resp.error) return { data: null, error: { message: resp.error.message } };
  return { data: (resp.data as LicenseRow[]) ?? [], error: null };
}

serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response(null, { headers: corsHeaders });
  }

  if (req.method !== "POST") {
    return jsonResponse(
      { error: "POST required", code: CODES.SERVER_ERROR },
      405,
    );
  }

  const url = Deno.env.get("SUPABASE_URL");
  const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");

  if (!url || !serviceKey) {
    return jsonResponse(
      {
        error: "Missing SUPABASE_URL or SUPABASE_SERVICE_ROLE_KEY",
        code: CODES.SERVER_ERROR,
      },
      500,
    );
  }

  const bearer = parseBearer(req.headers.get("Authorization"));
  if (!bearer) {
    return jsonResponse(
      { error: "Authorization required", code: CODES.AUTH_REQUIRED },
      401,
    );
  }

  // Ignore request body (keeps the contract stable)
  await req.json().catch(() => ({}));

  const sbAdmin = createClient(url, serviceKey, {
    auth: { persistSession: false, autoRefreshToken: false },
  });

  const { data: userData, error: authErr } = await sbAdmin.auth.getUser(bearer);
  if (authErr || !userData?.user) {
    return jsonResponse(
      { error: "Invalid or expired token", code: CODES.AUTH_INVALID },
      401,
    );
  }

  const userId = userData.user.id;
  const userEmail = userData.user.email ? normalizeEmail(userData.user.email) : "";

  if (!userEmail) {
    return jsonResponse(
      { error: "User email not found", code: CODES.AUTH_INVALID },
      400,
    );
  }

  try {
    const { data: licenses, error: licErr } = await fetchLicensesResilient(
      sbAdmin,
      userId,
      userEmail,
    );

    if (licErr) {
      return jsonResponse(
        { error: "Failed to fetch licenses", code: CODES.DB_ERROR },
        500,
      );
    }

    if (!licenses || licenses.length === 0) {
      return jsonResponse({ licenses: [], email: userEmail, count: 0 });
    }

    // Activation counts (active only)
    const licenseIds = licenses.map((l) => l.id);

    const { data: activations, error: actErr } = await sbAdmin
      .from("bridge_activations")
      .select("license_id")
      .in("license_id", licenseIds)
      .eq("is_active", true);

    if (actErr) {
      return jsonResponse(
        { error: "Failed to fetch activation counts", code: CODES.DB_ERROR },
        500,
      );
    }

    const counts: Record<string, number> = {};
    (activations ?? []).forEach((a: { license_id: string }) => {
      counts[a.license_id] = (counts[a.license_id] || 0) + 1;
    });

    const formatted: LicenseResponse[] = licenses.map((l) => {
      const keyForMask = l.license_key_text ?? l.license_key;
      const isActive =
        typeof l.is_active === "boolean" ? l.is_active : (l.is_enabled ?? true);

      return {
        license_id: l.id,
        license_key_masked: maskKey(keyForMask),
        license_type: l.license_type,
        expires_at: l.expires_at,
        is_active: Boolean(isActive),
        max_machines: l.max_machines ?? 1,
        machines_used: counts[l.id] || 0,
        created_at: l.created_at,
      };
    });

    return jsonResponse({ licenses: formatted, email: userEmail, count: formatted.length, code: CODES.OK });
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err);
    return jsonResponse(
      { error: "Internal server error", code: CODES.SERVER_ERROR, message: msg },
      500,
    );
  }
});
