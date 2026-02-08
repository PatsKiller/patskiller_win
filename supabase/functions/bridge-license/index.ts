/// <reference lib="deno.ns" />

import { serve } from "https://deno.land/std@0.224.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2.49.1";
import { corsHeaders } from "../_shared/cors.ts";

/**
 * PatsKiller Pro — Licensing Edge Function (Phase 3)
 *
 * Contract goals:
 * - Machine-readable `error` + internal `code` enum in responses (future-proofing).
 * - Strict policy: `user_email` is required for validate/activate/heartbeat/deactivate
 *   and must match the authenticated JWT email. (SSO identity is mandatory.)
 * - Machine binding = CombinedId (MachineId:SIID) with legacy fallback (MachineId only).
 *
 * Request (JSON):
 * {
 *   "action": "validate" | "activate" | "heartbeat" | "deactivate",
 *   "license_key": "XXXX-XXXX-XXXX-XXXX",
 *   "machine_id": "MACHINEID:SIID" | "MACHINEID",
 *   "siid": "SIID",               // optional (combined_id preferred)
 *   "machine_name": "DESKTOP-123",// optional
 *   "user_email": "user@domain.com",
 *   "version": "21.0.0"           // optional
 * }
 *
 * Response (JSON):
 * {
 *   "valid": boolean,
 *   "error": string | null,
 *   "code": string,               // enum
 *   "message": string,
 *   "licensedTo": string | null,
 *   "email": string | null,
 *   "licenseType": string | null,
 *   "expiresAt": string | null,
 *   "maxMachines": number,
 *   "machinesUsed": number,
 *   "nextCheckBy": string | null,
 *   "serverTime": string
 * }
 */

type Action = "validate" | "activate" | "heartbeat" | "deactivate";

type Payload = {
  action?: Action;
  license_key?: string;
  machine_id?: string;
  siid?: string;
  machine_name?: string;
  user_email?: string;
  version?: string;
};

type BridgeLicenseRow = {
  id: string;
  license_key: string;
  customer_name: string | null;
  customer_email: string | null;
  license_type: string | null;
  expires_at: string | null;
  max_machines: number | null;
  is_enabled: boolean | null;
};

type BridgeActivationRow = {
  id: string;
  license_id: string;
  machine_id: string;
  siid: string;
  combined_id: string | null;
  machine_name: string | null;
  app_version: string | null;
  first_seen: string;
  last_seen: string;
  last_validated_at: string;
  is_active: boolean;
};

const CODES = {
  OK: "OK",
  INVALID_PAYLOAD: "INVALID_PAYLOAD",
  AUTH_REQUIRED: "AUTH_REQUIRED",
  AUTH_INVALID: "AUTH_INVALID",
  INVALID_KEY: "INVALID_KEY",
  LICENSE_DISABLED: "LICENSE_DISABLED",
  LICENSE_EXPIRED: "LICENSE_EXPIRED",
  MACHINE_LIMIT: "MACHINE_LIMIT",
  EMAIL_MISMATCH: "EMAIL_MISMATCH",
  NOT_ACTIVATED: "NOT_ACTIVATED",
  SERVER_ERROR: "SERVER_ERROR",
} as const;

function jsonResponse(body: Record<string, unknown>, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json", ...corsHeaders },
  });
}

function nowIso() {
  return new Date().toISOString();
}

function addDaysIso(days: number) {
  const d = new Date();
  d.setDate(d.getDate() + days);
  return d.toISOString();
}

function normalizeKey(key: string) {
  return key.trim().toUpperCase();
}

function normalizeEmail(email: string) {
  return email.trim().toLowerCase();
}

function parseBearer(authHeader: string | null): string | null {
  if (!authHeader) return null;
  const m = authHeader.match(/^Bearer\s+(.+)$/i);
  return m ? m[1].trim() : null;
}

function splitCombined(machineId: string, siid?: string) {
  const raw = machineId.trim();
  if (raw.includes(":")) {
    const [hw, s] = raw.split(":");
    return {
      combinedId: raw,
      hwId: hw,
      siid: s ?? "",
    };
  }
  return {
    combinedId: raw,
    hwId: raw,
    siid: siid ?? "",
  };
}

serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response("ok", { headers: corsHeaders });
  }

  if (req.method !== "POST") {
    return jsonResponse(
      {
        valid: false,
        error: "invalid_method",
        code: CODES.INVALID_PAYLOAD,
        message: "POST required",
        serverTime: nowIso(),
      },
      405,
    );
  }

  const url = Deno.env.get("SUPABASE_URL");
  const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");

  if (!url || !serviceKey) {
    return jsonResponse(
      {
        valid: false,
        error: "server_misconfig",
        code: CODES.SERVER_ERROR,
        message: "Missing SUPABASE_URL or SUPABASE_SERVICE_ROLE_KEY",
        serverTime: nowIso(),
      },
      500,
    );
  }

  let payload: Payload;
  try {
    payload = await req.json();
  } catch {
    return jsonResponse(
      {
        valid: false,
        error: "invalid_json",
        code: CODES.INVALID_PAYLOAD,
        message: "Body must be valid JSON",
        serverTime: nowIso(),
      },
      400,
    );
  }

  // Accept both snake_case and camelCase fields (desktop clients vary by version).
  const p: any = payload as any;

  const action: Action | undefined = (p.action ?? p.Action) as Action | undefined;

  const licenseKeyRaw = p.license_key ?? p.licenseKey ?? p.license_key_text ?? p.licenseKeyText;
  const licenseKey = licenseKeyRaw ? normalizeKey(String(licenseKeyRaw)) : "";

  const machineId = String(
    p.machine_id ?? p.machineId ?? req.headers.get("x-machine-id") ?? "",
  ).trim();

  const userEmailRaw = p.user_email ?? p.userEmail ?? p.customerEmail ?? p.customer_email;
  const userEmail = userEmailRaw ? normalizeEmail(String(userEmailRaw)) : "";

  const siidRaw = p.siid ?? p.SIID ?? p.siId;

  if (
    !action || !["validate", "activate", "heartbeat", "deactivate"].includes(action)
  ) {
    return jsonResponse(
      {
        valid: false,
        error: "invalid_action",
        code: CODES.INVALID_PAYLOAD,
        message: "Invalid or missing action",
        serverTime: nowIso(),
      },
      400,
    );
  }

  // Strict policy: user_email is required for ALL operations
  if (!userEmail) {
    return jsonResponse(
      {
        valid: false,
        error: "invalid_email",
        code: CODES.INVALID_PAYLOAD,
        message: "Missing user_email",
        serverTime: nowIso(),
      },
      400,
    );
  }

  if (!licenseKey) {
    return jsonResponse(
      {
        valid: false,
        error: "invalid_key",
        code: CODES.INVALID_PAYLOAD,
        message: "Missing license_key",
        serverTime: nowIso(),
      },
      400,
    );
  }

  if (!machineId) {
    return jsonResponse(
      {
        valid: false,
        error: "invalid_machine",
        code: CODES.INVALID_PAYLOAD,
        message: "Missing machine_id",
        serverTime: nowIso(),
      },
      400,
    );
  }

  // Strict policy: must have Authorization: Bearer <jwt>
  const bearer = parseBearer(req.headers.get("Authorization"));
  if (!bearer) {
    return jsonResponse(
      {
        valid: false,
        error: "auth_required",
        code: CODES.AUTH_REQUIRED,
        message: "Missing Authorization bearer token",
        serverTime: nowIso(),
      },
      401,
    );
  }

  const sbAdmin = createClient(url, serviceKey, {
    auth: { persistSession: false, autoRefreshToken: false },
  });

  // Verify JWT and get canonical email from Supabase Auth
  const { data: userData, error: userErr } = await sbAdmin.auth.getUser(bearer);
  if (userErr || !userData?.user) {
    return jsonResponse(
      {
        valid: false,
        error: "auth_invalid",
        code: CODES.AUTH_INVALID,
        message: "Invalid/expired session",
        serverTime: nowIso(),
      },
      401,
    );
  }

  const jwtEmail = userData.user.email ? normalizeEmail(userData.user.email) : "";
  if (!jwtEmail) {
    return jsonResponse(
      {
        valid: false,
        error: "auth_invalid",
        code: CODES.AUTH_INVALID,
        message: "Authenticated user has no email",
        serverTime: nowIso(),
      },
      401,
    );
  }

  if (jwtEmail !== userEmail) {
    return jsonResponse(
      {
        valid: false,
        error: "email_mismatch",
        code: CODES.EMAIL_MISMATCH,
        message: `Payload email (${userEmail}) does not match authenticated email (${jwtEmail})`,
        serverTime: nowIso(),
      },
      403,
    );
  }

  // Fetch license
  const { data: lic, error: licErr } = await sbAdmin
    .from("bridge_licenses")
    .select("*")
    .eq("license_key", licenseKey)
    .limit(1)
    .maybeSingle<BridgeLicenseRow>();

  if (licErr) {
    return jsonResponse(
      {
        valid: false,
        error: "server_error",
        code: CODES.SERVER_ERROR,
        message: licErr.message,
        serverTime: nowIso(),
      },
      500,
    );
  }

  if (!lic) {
    return jsonResponse(
      {
        valid: false,
        error: "invalid_key",
        code: CODES.INVALID_KEY,
        message: "License key not found",
        serverTime: nowIso(),
      },
      200,
    );
  }

  const maxMachines = lic.max_machines ?? 1;

  if (lic.is_enabled === false) {
    return jsonResponse(
      {
        valid: false,
        error: "disabled",
        code: CODES.LICENSE_DISABLED,
        message: "License disabled",
        licensedTo: lic.customer_name,
        email: lic.customer_email,
        licenseType: lic.license_type,
        expiresAt: lic.expires_at,
        maxMachines,
        machinesUsed: 0,
        nextCheckBy: addDaysIso(7),
        serverTime: nowIso(),
      },
      200,
    );
  }

  if (lic.expires_at) {
    const exp = new Date(lic.expires_at);
    if (!isNaN(exp.getTime()) && exp.getTime() < Date.now()) {
      return jsonResponse(
        {
          valid: false,
          error: "expired",
          code: CODES.LICENSE_EXPIRED,
          message: "License expired",
          licensedTo: lic.customer_name,
          email: lic.customer_email,
          licenseType: lic.license_type,
          expiresAt: lic.expires_at,
          maxMachines,
          machinesUsed: 0,
          nextCheckBy: addDaysIso(7),
          serverTime: nowIso(),
        },
        200,
      );
    }
  }

  // Email binding on first use: if null, bind to this email.
  // If already set, enforce match.
  if (lic.customer_email) {
    const bound = normalizeEmail(lic.customer_email);
    if (bound !== userEmail) {
      return jsonResponse(
        {
          valid: false,
          error: "email_mismatch",
          code: CODES.EMAIL_MISMATCH,
          message: `License is bound to ${bound}. Signed in as ${userEmail}.`,
          licensedTo: lic.customer_name,
          email: lic.customer_email,
          licenseType: lic.license_type,
          expiresAt: lic.expires_at,
          maxMachines,
          machinesUsed: 0,
          nextCheckBy: addDaysIso(7),
          serverTime: nowIso(),
        },
        200,
      );
    }
  } else {
    // Bind on activate OR validate (backward compatibility)
    if (action === "activate" || action === "validate") {
      const { error: upErr } = await sbAdmin
        .from("bridge_licenses")
        .update({ customer_email: userEmail })
        .eq("id", lic.id);

      if (upErr) {
        return jsonResponse(
          {
            valid: false,
            error: "server_error",
            code: CODES.SERVER_ERROR,
            message: upErr.message,
            serverTime: nowIso(),
          },
          500,
        );
      }
      lic.customer_email = userEmail;
    }
  }

  const { combinedId, hwId, siid } = splitCombined(machineId, String(siidRaw ?? ""));
  const machineName = String(p.machine_name ?? p.machineName ?? "").trim() || null;
  const version = String(p.version ?? "").trim() || null;

  // Helper: count active activations
  const countActive = async () => {
    const { count, error } = await sbAdmin
      .from("bridge_activations")
      .select("id", { count: "exact", head: true })
      .eq("license_id", lic.id)
      .eq("is_active", true);
    if (error) throw error;
    return count ?? 0;
  };

  // Helper: find activation (prefer combined_id, fallback to legacy machine_id)
  const findActivation = async (): Promise<BridgeActivationRow | null> => {
    const { data, error } = await sbAdmin
      .from("bridge_activations")
      .select("*")
      .eq("license_id", lic.id)
      .eq("is_active", true)
      .or(`combined_id.eq.${combinedId},machine_id.eq.${hwId}`)
      .order("last_seen", { ascending: false })
      .limit(1)
      .maybeSingle<BridgeActivationRow>();

    if (error) throw error;
    return data ?? null;
  };

  const upsertActivation = async (existing: BridgeActivationRow | null) => {
    const ts = nowIso();

    if (existing) {
      const { error } = await sbAdmin
        .from("bridge_activations")
        .update({
          machine_id: hwId,
          siid,
          combined_id: combinedId,
          machine_name: machineName,
          app_version: version,
          last_seen: ts,
          last_validated_at: ts,
          is_active: true,
        })
        .eq("id", existing.id);

      if (error) throw error;
      return existing.id;
    }

    // New activation: enforce limit
    const used = await countActive();
    if (used >= maxMachines) {
      return null; // caller maps to machine_limit
    }

    const { data, error } = await sbAdmin
      .from("bridge_activations")
      .insert({
        license_id: lic.id,
        machine_id: hwId,
        siid,
        combined_id: combinedId,
        machine_name: machineName,
        app_version: version,
        first_seen: ts,
        last_seen: ts,
        last_validated_at: ts,
        is_active: true,
      })
      .select("id")
      .limit(1)
      .maybeSingle<{ id: string }>();

    if (error) throw error;
    return data?.id ?? null;
  };

  try {
    const activation = await findActivation();

    if (action === "deactivate") {
      if (!activation) {
        const used = await countActive();
        return jsonResponse({
          valid: false,
          error: "not_activated",
          code: CODES.NOT_ACTIVATED,
          message: "Machine is not activated",
          licensedTo: lic.customer_name,
          email: lic.customer_email,
          licenseType: lic.license_type,
          expiresAt: lic.expires_at,
          maxMachines,
          machinesUsed: used,
          nextCheckBy: addDaysIso(7),
          serverTime: nowIso(),
        });
      }

      const { error } = await sbAdmin
        .from("bridge_activations")
        .update({ is_active: false, last_seen: nowIso() })
        .eq("id", activation.id);

      if (error) throw error;

      const used = await countActive();
      return jsonResponse({
        valid: true,
        error: null,
        code: CODES.OK,
        message: "Deactivated",
        licensedTo: lic.customer_name,
        email: lic.customer_email,
        licenseType: lic.license_type,
        expiresAt: lic.expires_at,
        maxMachines,
        machinesUsed: used,
        nextCheckBy: addDaysIso(7),
        serverTime: nowIso(),
      });
    }

    if (action === "heartbeat") {
      if (!activation) {
        const used = await countActive();
        return jsonResponse({
          valid: false,
          error: "not_activated",
          code: CODES.NOT_ACTIVATED,
          message: "Machine is not activated",
          licensedTo: lic.customer_name,
          email: lic.customer_email,
          licenseType: lic.license_type,
          expiresAt: lic.expires_at,
          maxMachines,
          machinesUsed: used,
          nextCheckBy: addDaysIso(7),
          serverTime: nowIso(),
        });
      }

      await upsertActivation(activation);
      const used = await countActive();

      return jsonResponse({
        valid: true,
        error: null,
        code: CODES.OK,
        message: "Heartbeat OK",
        licensedTo: lic.customer_name,
        email: lic.customer_email,
        licenseType: lic.license_type,
        expiresAt: lic.expires_at,
        maxMachines,
        machinesUsed: used,
        nextCheckBy: addDaysIso(7),
        serverTime: nowIso(),
      });
    }

    // validate / activate
    // Backward compatibility: validate may create an activation if none exists and slots remain.
    const activatedId = await upsertActivation(activation);

    if (!activatedId) {
      const used = await countActive();
      return jsonResponse({
        valid: false,
        error: "machine_limit",
        code: CODES.MACHINE_LIMIT,
        message: `Machine limit reached (${used}/${maxMachines}). Deactivate another machine first.`,
        licensedTo: lic.customer_name,
        email: lic.customer_email,
        licenseType: lic.license_type,
        expiresAt: lic.expires_at,
        maxMachines,
        machinesUsed: used,
        nextCheckBy: addDaysIso(7),
        serverTime: nowIso(),
      });
    }

    const used = await countActive();
    return jsonResponse({
      valid: true,
      error: null,
      code: CODES.OK,
      message: action === "activate" ? "Activated" : "Valid",
      licensedTo: lic.customer_name,
      email: lic.customer_email,
      licenseType: lic.license_type,
      expiresAt: lic.expires_at,
      maxMachines,
      machinesUsed: used,
      nextCheckBy: addDaysIso(7),
      serverTime: nowIso(),
    });
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err);
    return jsonResponse(
      {
        valid: false,
        error: "server_error",
        code: CODES.SERVER_ERROR,
        message: msg,
        serverTime: nowIso(),
      },
      500,
    );
  }
});
