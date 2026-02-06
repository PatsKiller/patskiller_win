-- Phase 3 Licensing migration: SIID + CombinedId binding support
-- Date: 2026-02-05
--
-- Notes:
-- - This migration is written to be safe to apply to existing environments.
-- - The Edge Function uses SERVICE ROLE, so RLS policies are not required for it.
-- - Client now prefers `machine_id = "<MachineId>:<SIID>"` (combined_id).
--   Legacy MachineId-only rows are still recognized.

create extension if not exists pgcrypto;

-- Core tables (create if missing; no-op if you already have them)
create table if not exists public.bridge_licenses (
  id uuid primary key default gen_random_uuid(),
  license_key text unique not null,
  customer_name text,
  customer_email text,
  license_type text default 'standard',
  expires_at timestamptz,
  max_machines integer not null default 1,
  is_enabled boolean not null default true,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table if not exists public.bridge_activations (
  id uuid primary key default gen_random_uuid(),
  license_id uuid not null references public.bridge_licenses(id) on delete cascade,
  machine_id text not null,
  siid text not null default '',
  combined_id text,
  machine_name text,
  app_version text,
  first_seen timestamptz not null default now(),
  last_seen timestamptz not null default now(),
  last_validated_at timestamptz not null default now(),
  is_active boolean not null default true
);

-- Add new columns to existing deployments (idempotent)
alter table public.bridge_activations add column if not exists siid text not null default '';
alter table public.bridge_activations add column if not exists combined_id text;
alter table public.bridge_activations add column if not exists machine_name text;
alter table public.bridge_activations add column if not exists app_version text;
alter table public.bridge_activations add column if not exists first_seen timestamptz not null default now();
alter table public.bridge_activations add column if not exists last_seen timestamptz not null default now();
alter table public.bridge_activations add column if not exists last_validated_at timestamptz not null default now();
alter table public.bridge_activations add column if not exists is_active boolean not null default true;

-- Best-effort backfill of combined_id for legacy rows (MachineId-only)
update public.bridge_activations
set combined_id = coalesce(combined_id, nullif(machine_id, ''))
where combined_id is null;

-- Indexes for performance
create index if not exists idx_bridge_activations_combined_id
  on public.bridge_activations (combined_id);

create index if not exists idx_bridge_activations_license_active
  on public.bridge_activations (license_id, is_active);

-- Uniqueness: only one ACTIVE row per (license_id, combined_id)
-- (allows historical inactive duplicates)
create unique index if not exists uq_bridge_activations_license_combined_active
  on public.bridge_activations (license_id, combined_id)
  where is_active = true and combined_id is not null;

-- Optional: helpful view for admin dashboards
create or replace view public.bridge_license_overview as
select
  l.id as license_id,
  l.license_key,
  l.customer_name,
  l.customer_email,
  l.license_type,
  l.expires_at,
  l.max_machines,
  l.is_enabled,
  coalesce(a.machines_used, 0) as machines_used,
  l.created_at,
  l.updated_at
from public.bridge_licenses l
left join (
  select license_id, count(*)::int as machines_used
  from public.bridge_activations
  where is_active = true
  group by license_id
) a on a.license_id = l.id;
