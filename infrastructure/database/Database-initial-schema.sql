-- Run this entire script once in pgAdmin Query Tool.
--
-- IMPORTANT:
-- 1) This script creates a dedicated schema named ai_orchestration.
-- 2) It drops/recreates ONLY that schema. It does not drop public or other schemas.
-- 3) Passwords below are TEST ONLY. Replace them and use your ASP.NET Core
--    Identity password hasher before using this database outside local testing.
-- 4) API key values are never stored; only hashes are stored.
-- 5) Initial roles: Super Admin (platform-wide), Admin (assigned department scope),
--    User (own account/conversations/usage and quota requests).
-- 6) RAG/document ingestion is intentionally not included in this initial phase.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

DROP SCHEMA IF EXISTS ai_orchestration CASCADE;
CREATE SCHEMA ai_orchestration;
SET search_path TO ai_orchestration, public;

-- =========================================================
-- 1. ORGANIZATION AND USER MANAGEMENT
-- =========================================================

CREATE TABLE organization (
    organization_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(150) NOT NULL
);

CREATE TABLE app_user (
    user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email VARCHAR(255) NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    status VARCHAR(30) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL,
    CONSTRAINT ck_app_user_status CHECK (status IN ('Active','Inactive','Suspended','Pending'))
);

CREATE TABLE approved_email_domain (
    domain_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID NOT NULL REFERENCES organization(organization_id),
    domain_name VARCHAR(255) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_by_user_id UUID NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    CONSTRAINT uq_approved_domain_per_org UNIQUE (organization_id, domain_name)
);

CREATE TABLE department (
    department_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    department_name VARCHAR(150) NOT NULL,
    department_status VARCHAR(30) NOT NULL,
    department_configuration JSONB NULL DEFAULT '{}'::jsonb,
    organization_id UUID NOT NULL REFERENCES organization(organization_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL,
    CONSTRAINT uq_department_name_per_org UNIQUE (organization_id, department_name),
    CONSTRAINT ck_department_status CHECK (department_status IN ('Active','Inactive','Archived'))
);

CREATE TABLE role (
    role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(100) NOT NULL UNIQUE,
    description TEXT NULL,
    permission JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL,
    CONSTRAINT ck_role_permission_object CHECK (jsonb_typeof(permission) = 'object')
);

CREATE TABLE user_department (
    user_id UUID NOT NULL REFERENCES app_user(user_id) ON DELETE CASCADE,
    department_id UUID NOT NULL REFERENCES department(department_id) ON DELETE CASCADE,
    membership_role VARCHAR(50) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL,
    PRIMARY KEY (user_id, department_id)
);

CREATE TABLE user_role (
    user_id UUID NOT NULL REFERENCES app_user(user_id) ON DELETE CASCADE,
    role_id UUID NOT NULL REFERENCES role(role_id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL,
    PRIMARY KEY (user_id, role_id)
);

-- =========================================================
-- 2. AUTHENTICATION AND API MANAGEMENT
-- =========================================================

CREATE TABLE verification_code (
    code_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_user(user_id) ON DELETE CASCADE,
    code_hash TEXT NOT NULL,
    purpose VARCHAR(50) NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    used_at TIMESTAMPTZ NULL,
    failed_attempts INTEGER NOT NULL DEFAULT 0,
    delivery_status VARCHAR(30) NULL,
    CONSTRAINT ck_verification_failed_attempts CHECK (failed_attempts >= 0)
);

CREATE TABLE api_key (
    api_key_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_user(user_id) ON DELETE CASCADE,
    name VARCHAR(150) NOT NULL,
    key_hash TEXT NOT NULL,
    status VARCHAR(30) NOT NULL,
    expires_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL,
    CONSTRAINT ck_api_key_status CHECK (status IN ('Active','Revoked','Expired'))
);

-- =========================================================
-- 3. AI PROVIDER AND MODEL MANAGEMENT
-- =========================================================

CREATE TABLE ai_provider (
    provider_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(150) NOT NULL,
    status VARCHAR(30) NOT NULL,
    base_url TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL
);

CREATE TABLE ai_model (
    model_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_id UUID NOT NULL REFERENCES ai_provider(provider_id),
    model_name VARCHAR(150) NOT NULL,
    model_type VARCHAR(50) NULL,
    skills JSONB NULL,
    status VARCHAR(30) NOT NULL,
    CONSTRAINT uq_model_per_provider UNIQUE (provider_id, model_name)
);

CREATE TABLE department_model (
    department_id UUID NOT NULL REFERENCES department(department_id) ON DELETE CASCADE,
    model_id UUID NOT NULL REFERENCES ai_model(model_id) ON DELETE CASCADE,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL,
    PRIMARY KEY (department_id, model_id)
);

CREATE TABLE model_pricing (
    pricing_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    model_id UUID NOT NULL REFERENCES ai_model(model_id),
    input_price_per_token NUMERIC(18,10) NOT NULL,
    output_price_per_token NUMERIC(18,10) NOT NULL,
    effective_from TIMESTAMPTZ NOT NULL,
    effective_to TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL,
    CONSTRAINT ck_model_pricing_effective_period
        CHECK (effective_to IS NULL OR effective_to > effective_from),
    CONSTRAINT ck_model_pricing_nonnegative
        CHECK (input_price_per_token >= 0 AND output_price_per_token >= 0)
);

-- =========================================================
-- 4. AI CONVERSATION AND REQUEST MANAGEMENT
-- =========================================================

CREATE TABLE conversation (
    conversation_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_user(user_id),
    department_id UUID NOT NULL REFERENCES department(department_id),
    title VARCHAR(255) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_by UUID NULL
);

CREATE TABLE ai_request (
    request_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID NOT NULL REFERENCES conversation(conversation_id),
    model_id UUID NULL REFERENCES ai_model(model_id),
    prompt TEXT NOT NULL,
    status VARCHAR(30) NOT NULL,
    requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL
);

CREATE TABLE ai_response (
    response_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    request_id UUID NOT NULL UNIQUE REFERENCES ai_request(request_id) ON DELETE CASCADE,
    response_text TEXT NULL,
    input_tokens INTEGER NOT NULL DEFAULT 0,
    output_tokens INTEGER NOT NULL DEFAULT 0,
    duration_ms BIGINT NULL,
    CONSTRAINT ck_ai_response_tokens CHECK (input_tokens >= 0 AND output_tokens >= 0),
    CONSTRAINT ck_ai_response_duration CHECK (duration_ms IS NULL OR duration_ms >= 0)
);

CREATE TABLE request_routing (
    routing_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    request_id UUID NOT NULL REFERENCES ai_request(request_id) ON DELETE CASCADE,
    model_id UUID NOT NULL REFERENCES ai_model(model_id),
    routing_reason TEXT NULL,
    selected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL
);

CREATE TABLE usage_record (
    usage_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    request_id UUID NOT NULL REFERENCES ai_request(request_id),
    input_tokens INTEGER NOT NULL DEFAULT 0,
    output_tokens INTEGER NOT NULL DEFAULT 0,
    estimated_cost NUMERIC(18,8) NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL,
    CONSTRAINT ck_usage_nonnegative CHECK
        (input_tokens >= 0 AND output_tokens >= 0 AND estimated_cost >= 0)
);

-- =========================================================
-- 5. CREDIT AND QUOTA MANAGEMENT
-- =========================================================

CREATE TABLE credit_account (
    credit_account_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID NOT NULL REFERENCES organization(organization_id),
    department_id UUID NULL REFERENCES department(department_id),
    available_credits NUMERIC(18,6) NOT NULL DEFAULT 0,
    reserved_credits NUMERIC(18,6) NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL,
    CONSTRAINT ck_credit_account_nonnegative CHECK
        (available_credits >= 0 AND reserved_credits >= 0)
);

CREATE TABLE credit_transaction (
    transaction_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    credit_account_id UUID NOT NULL REFERENCES credit_account(credit_account_id),
    request_id UUID NULL REFERENCES ai_request(request_id),
    transaction_type VARCHAR(30) NOT NULL,
    amount NUMERIC(18,6) NOT NULL,
    idempotency_key VARCHAR(150) NULL UNIQUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL
);

CREATE TABLE quota_policy (
    quota_policy_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID NOT NULL REFERENCES organization(organization_id),
    department_id UUID NULL REFERENCES department(department_id),
    credit_limit NUMERIC(18,6) NOT NULL,
    period VARCHAR(30) NOT NULL,
    warning_threshold NUMERIC(5,2) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL,
    CONSTRAINT ck_quota_policy_limit CHECK (credit_limit >= 0),
    CONSTRAINT ck_quota_warning_threshold CHECK
        (warning_threshold IS NULL OR (warning_threshold > 0 AND warning_threshold <= 100))
);

CREATE TABLE quota_notification (
    notification_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    credit_account_id UUID NOT NULL REFERENCES credit_account(credit_account_id),
    threshold_percent NUMERIC(5,2) NOT NULL,
    notification_type VARCHAR(50) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL,
    CONSTRAINT ck_quota_notification_threshold CHECK
        (threshold_percent > 0 AND threshold_percent <= 100)
);

CREATE TABLE additional_credit_request (
    additional_request_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    department_id UUID NOT NULL REFERENCES department(department_id),
    requested_by UUID NOT NULL REFERENCES app_user(user_id),
    requested_credits NUMERIC(18,6) NOT NULL,
    status VARCHAR(30) NOT NULL,
    reviewed_by UUID NULL REFERENCES app_user(user_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL,
    CONSTRAINT ck_additional_credit_positive CHECK (requested_credits > 0),
    CONSTRAINT ck_additional_credit_status CHECK
        (status IN ('Pending','Approved','Rejected','Cancelled'))
);

-- =========================================================
-- 6. NOTIFICATION
-- =========================================================

CREATE TABLE notification (
    notification_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_user(user_id),
    title VARCHAR(255) NOT NULL,
    message TEXT NOT NULL,
    read_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL
);

-- =========================================================
-- 7. SYSTEM LOGGING AND AUDITING
-- =========================================================

CREATE TABLE system_log (
    log_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_code VARCHAR(100) NOT NULL,
    log_category VARCHAR(30) NOT NULL,
    severity VARCHAR(20) NOT NULL,
    source_service VARCHAR(100) NOT NULL,
    message TEXT NOT NULL,
    actor_user_id UUID NULL REFERENCES app_user(user_id),
    entity_type VARCHAR(100) NULL,
    entity_id UUID NULL,
    action VARCHAR(50) NULL,
    outcome VARCHAR(20) NULL,
    correlation_id UUID NOT NULL,
    request_id UUID NULL REFERENCES ai_request(request_id),
    error_code VARCHAR(100) NULL,
    metadata JSONB NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL
);

CREATE TABLE audit_log (
    audit_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    actor_user_id UUID NOT NULL REFERENCES app_user(user_id),
    actor_role VARCHAR(30) NULL,
    action VARCHAR(100) NOT NULL,
    target_type VARCHAR(100) NOT NULL,
    target_id UUID NULL,
    department_id UUID NULL REFERENCES department(department_id),
    old_values JSONB NULL,
    new_values JSONB NULL,
    outcome VARCHAR(20) NOT NULL,
    reason TEXT NULL,
    correlation_id UUID NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL
);

-- =========================================================
-- 8. CREDIT LEDGER MANAGEMENT
-- =========================================================

CREATE TABLE credit_ledger_entry (
    ledger_entry_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    credit_account_id UUID NOT NULL REFERENCES credit_account(credit_account_id),
    transaction_type VARCHAR(30) NOT NULL,
    amount NUMERIC(18,6) NOT NULL,
    balance_after NUMERIC(18,6) NOT NULL,
    reference_type VARCHAR(50) NULL,
    reference_id UUID NULL,
    idempotency_key VARCHAR(150) NULL,
    request_id UUID NULL REFERENCES ai_request(request_id),
    description TEXT NULL,
    performed_by UUID NULL REFERENCES app_user(user_id),
    department_id UUID NULL REFERENCES department(department_id),
    correlation_id UUID NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by UUID NULL,
    updated_at TIMESTAMPTZ NULL,
    updated_by UUID NULL,
    CONSTRAINT ck_ledger_balance_nonnegative CHECK (balance_after >= 0)
);

-- =========================================================
-- Add the audit-user foreign keys after all tables exist.
-- This handles the self-reference and circular CreatedBy links.
-- =========================================================

ALTER TABLE app_user
    ADD CONSTRAINT fk_app_user_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_app_user_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE approved_email_domain
    ADD CONSTRAINT fk_approved_domain_created_by FOREIGN KEY (created_by_user_id) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_approved_domain_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

-- Keep the PDF's CreatedBy / UpdatedBy audit references consistently linked to app_user.
ALTER TABLE department
    ADD CONSTRAINT fk_department_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_department_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE role
    ADD CONSTRAINT fk_role_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_role_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE user_department
    ADD CONSTRAINT fk_user_department_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_user_department_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE user_role
    ADD CONSTRAINT fk_user_role_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_user_role_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE api_key
    ADD CONSTRAINT fk_api_key_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_api_key_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE ai_provider
    ADD CONSTRAINT fk_ai_provider_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_ai_provider_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE department_model
    ADD CONSTRAINT fk_department_model_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_department_model_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE model_pricing
    ADD CONSTRAINT fk_model_pricing_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_model_pricing_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE conversation
    ADD CONSTRAINT fk_conversation_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_conversation_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE ai_request
    ADD CONSTRAINT fk_ai_request_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_ai_request_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE request_routing
    ADD CONSTRAINT fk_request_routing_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_request_routing_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE usage_record
    ADD CONSTRAINT fk_usage_record_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_usage_record_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE credit_account
    ADD CONSTRAINT fk_credit_account_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_credit_account_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE credit_transaction
    ADD CONSTRAINT fk_credit_transaction_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_credit_transaction_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE quota_policy
    ADD CONSTRAINT fk_quota_policy_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_quota_policy_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE quota_notification
    ADD CONSTRAINT fk_quota_notification_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_quota_notification_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE additional_credit_request
    ADD CONSTRAINT fk_additional_credit_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_additional_credit_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE notification
    ADD CONSTRAINT fk_notification_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_notification_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE system_log
    ADD CONSTRAINT fk_system_log_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_system_log_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE audit_log
    ADD CONSTRAINT fk_audit_log_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_audit_log_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

ALTER TABLE credit_ledger_entry
    ADD CONSTRAINT fk_credit_ledger_created_by FOREIGN KEY (created_by) REFERENCES app_user(user_id),
    ADD CONSTRAINT fk_credit_ledger_updated_by FOREIGN KEY (updated_by) REFERENCES app_user(user_id);

-- =========================================================
-- Useful indexes for common lookups and filtering
-- =========================================================

CREATE INDEX ix_department_organization ON department(organization_id);
CREATE INDEX ix_user_department_department ON user_department(department_id);
CREATE INDEX ix_user_role_role ON user_role(role_id);
CREATE INDEX ix_verification_user_expiry ON verification_code(user_id, expires_at);
CREATE INDEX ix_api_key_user_status ON api_key(user_id, status);
CREATE INDEX ix_ai_model_provider ON ai_model(provider_id);
CREATE INDEX ix_conversation_user_created ON conversation(user_id, created_at DESC);
CREATE INDEX ix_ai_request_conversation_requested ON ai_request(conversation_id, requested_at DESC);
CREATE INDEX ix_usage_record_created ON usage_record(created_at DESC);
CREATE INDEX ix_credit_account_org_department ON credit_account(organization_id, department_id);
CREATE INDEX ix_additional_credit_status ON additional_credit_request(status, created_at);
CREATE INDEX ix_notification_user_read ON notification(user_id, read_at);
CREATE INDEX ix_system_log_occurred ON system_log(occurred_at DESC);
CREATE INDEX ix_system_log_category_severity ON system_log(log_category, severity);
CREATE INDEX ix_audit_log_actor_occurred ON audit_log(actor_user_id, occurred_at DESC);
CREATE INDEX ix_audit_log_target ON audit_log(target_type, target_id);
CREATE INDEX ix_credit_ledger_account_created ON credit_ledger_entry(credit_account_id, created_at DESC);

-- Credit ledger entries are an immutable financial history.
CREATE OR REPLACE FUNCTION prevent_credit_ledger_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'CREDIT_LEDGER_ENTRY is append-only; UPDATE and DELETE are not allowed.';
END;
$$;

CREATE TRIGGER trg_credit_ledger_append_only
BEFORE UPDATE OR DELETE ON credit_ledger_entry
FOR EACH ROW EXECUTE FUNCTION prevent_credit_ledger_mutation();

-- =========================================================
-- MOCK DATA
-- Fixed UUIDs make the test records easy to identify and reuse.
-- =========================================================

-- Organization
INSERT INTO organization (organization_id, name) VALUES
('10000000-0000-0000-0000-000000000001', 'Demo Organization');

-- Users (test passwords only; replace with ASP.NET Core Identity hashes)
INSERT INTO app_user
(user_id, email, password_hash, status, created_at)
VALUES
('20000000-0000-0000-0000-000000000001', 'superadmin@demo.example',
 crypt('SuperAdmin_Test_123!', gen_salt('bf')), 'Active', NOW()),
('20000000-0000-0000-0000-000000000002', 'admin@demo.example',
 crypt('Admin_Test_123!', gen_salt('bf')), 'Active', NOW()),
('20000000-0000-0000-0000-000000000003', 'alice@demo.example',
 crypt('User_Test_123!', gen_salt('bf')), 'Active', NOW()),
('20000000-0000-0000-0000-000000000004', 'bob@demo.example',
 crypt('User_Test_123!', gen_salt('bf')), 'Active', NOW());

-- Approved domains
INSERT INTO approved_email_domain
(domain_id, organization_id, domain_name, is_active, created_by_user_id, created_at)
VALUES
('30000000-0000-0000-0000-000000000001',
 '10000000-0000-0000-0000-000000000001', 'demo.example', TRUE,
 '20000000-0000-0000-0000-000000000001', NOW());

-- Departments
INSERT INTO department
(department_id, department_name, department_status, department_configuration,
 organization_id, created_at, created_by)
VALUES
('40000000-0000-0000-0000-000000000001', 'Engineering', 'Active',
 '{"monthly_credit_limit": 1000, "allow_external_models": true}'::jsonb,
 '10000000-0000-0000-0000-000000000001', NOW(),
 '20000000-0000-0000-0000-000000000001'),
('40000000-0000-0000-0000-000000000002', 'Finance', 'Active',
 '{"monthly_credit_limit": 500, "allow_external_models": false}'::jsonb,
 '10000000-0000-0000-0000-000000000001', NOW(),
 '20000000-0000-0000-0000-000000000001');

-- Roles and JSONB permissions
-- Super Admin: platform-wide administration and oversight.
-- Admin: department-scoped management only (scope comes from USER_DEPARTMENT).
-- User: own AI access, conversations, usage, notifications and quota requests.
INSERT INTO role (role_id, name, description, permission, created_at, created_by)
VALUES
(
 '50000000-0000-0000-0000-000000000001',
 'Super Admin',
 'Platform-level administrator with access across the organization.',
 '{
   "scope": "platform",
   "organization": ["read","update"],
   "departments": ["create","read","update","deactivate","assign_users"],
   "users": ["create","read","update","deactivate","assign_roles","reset_access"],
   "roles": ["create","read","update","assign"],
   "permissions": ["read","update"],
   "providers": ["create","read","update","activate","deactivate"],
   "models": ["create","read","update","activate","deactivate"],
   "department_models": ["enable","disable","read"],
   "pricing": ["create","read","update"],
   "quotas": ["create","read","update","set_department_limits","override"],
   "credit_accounts": ["read","create","adjust"],
   "credit_requests": ["read","approve","reject"],
   "usage": ["read_all","export"],
   "billing": ["read","manage"],
   "api_keys": ["read_all","revoke"],
   "notifications": ["create","read_all"],
   "system_logs": ["read","export"],
   "audit_logs": ["read","export"],
   "credit_ledger": ["read","reconcile"],
   "platform_settings": ["read","update"]
 }'::jsonb,
 NOW(), '20000000-0000-0000-0000-000000000001'
),
(
 '50000000-0000-0000-0000-000000000002',
 'Admin',
 'Department-scoped administrator. Can manage assigned department only.',
 '{
   "scope": "assigned_departments",
   "departments": ["read_own"],
   "users": ["create_in_own_department","read_own_department","update_own_department","deactivate_own_department","assign_user_role"],
   "roles": ["read_assignable"],
   "providers": ["read_available"],
   "models": ["read_available"],
   "department_models": ["read_own_department","request_enablement"],
   "quotas": ["read_own_department","set_department_quota_shared_by_department_users","request_additional_credits"],
   "credit_accounts": ["read_own_department"],
   "credit_requests": ["read_own_department","review_user_requests","submit_department_request_to_super_admin"],
   "usage": ["read_own_department","export_own_department"],
   "api_keys": ["read_own_department_metadata","revoke_own_department"],
   "notifications": ["create_for_own_department","read_own_department"],
   "system_logs": ["read_own_department_relevant"],
   "audit_logs": ["read_own_department"],
   "credit_ledger": ["read_own_department"]
 }'::jsonb,
 NOW(), '20000000-0000-0000-0000-000000000001'
),
(
 '50000000-0000-0000-0000-000000000003',
 'User',
 'Standard employee account.',
 '{
   "scope": "self",
   "ai_requests": ["create"],
   "models": ["read_enabled_for_department","use_enabled_for_department"],
   "conversations": ["create","read_own","update_own","delete_own"],
   "usage": ["read_own"],
   "quota": ["read_own_department_allocation"],
   "credit_requests": ["create_for_own_department","read_own"],
   "api_keys": ["create_own","read_own","revoke_own"],
   "notifications": ["read_own","mark_read"],
   "profile": ["read_own","update_own"]
 }'::jsonb,
 NOW(), '20000000-0000-0000-0000-000000000001'
);

-- Department membership. Super Admin is platform-wide and need not be in a department.
INSERT INTO user_department
(user_id, department_id, membership_role, created_at, created_by)
VALUES
('20000000-0000-0000-0000-000000000002',
 '40000000-0000-0000-0000-000000000001', 'Department Admin', NOW(),
 '20000000-0000-0000-0000-000000000001'),
('20000000-0000-0000-0000-000000000003',
 '40000000-0000-0000-0000-000000000001', 'Employee', NOW(),
 '20000000-0000-0000-0000-000000000002'),
('20000000-0000-0000-0000-000000000004',
 '40000000-0000-0000-0000-000000000002', 'Employee', NOW(),
 '20000000-0000-0000-0000-000000000001');

-- Role assignments
INSERT INTO user_role (user_id, role_id, created_at, created_by)
VALUES
('20000000-0000-0000-0000-000000000001',
 '50000000-0000-0000-0000-000000000001', NOW(),
 '20000000-0000-0000-0000-000000000001'),
('20000000-0000-0000-0000-000000000002',
 '50000000-0000-0000-0000-000000000002', NOW(),
 '20000000-0000-0000-0000-000000000001'),
('20000000-0000-0000-0000-000000000003',
 '50000000-0000-0000-0000-000000000003', NOW(),
 '20000000-0000-0000-0000-000000000002'),
('20000000-0000-0000-0000-000000000004',
 '50000000-0000-0000-0000-000000000003', NOW(),
 '20000000-0000-0000-0000-000000000001');

-- Verification codes (hash-like test values; do not use as real verification secrets)
INSERT INTO verification_code
(code_id, user_id, code_hash, purpose, expires_at, failed_attempts, delivery_status)
VALUES
('60000000-0000-0000-0000-000000000001',
 '20000000-0000-0000-0000-000000000003', crypt('123456', gen_salt('bf')),
 'EmailVerification', NOW() + INTERVAL '10 minutes', 0, 'Sent'),
('60000000-0000-0000-0000-000000000002',
 '20000000-0000-0000-0000-000000000004', crypt('654321', gen_salt('bf')),
 'PasswordReset', NOW() + INTERVAL '10 minutes', 1, 'Sent');

-- API keys: only hashes are stored.
INSERT INTO api_key
(api_key_id, user_id, name, key_hash, status, expires_at, created_at, created_by)
VALUES
('61000000-0000-0000-0000-000000000001',
 '20000000-0000-0000-0000-000000000003', 'Alice Development Key',
 encode(digest('TEST_ONLY_API_KEY_ALICE_DO_NOT_USE', 'sha256'), 'hex'),
 'Active', NOW() + INTERVAL '90 days', NOW(),
 '20000000-0000-0000-0000-000000000003');

-- Providers and models
INSERT INTO ai_provider
(provider_id, name, status, base_url, created_at, created_by)
VALUES
('70000000-0000-0000-0000-000000000001', 'NVIDIA NIM', 'Active',
 'https://integrate.api.nvidia.com/v1', NOW(),
 '20000000-0000-0000-0000-000000000001'),
('70000000-0000-0000-0000-000000000002', 'GitHub Models', 'Active',
 'https://models.inference.ai.azure.com', NOW(),
 '20000000-0000-0000-0000-000000000001');

INSERT INTO ai_model (model_id, provider_id, model_name, model_type, skills, status)
VALUES
('71000000-0000-0000-0000-000000000001',
 '70000000-0000-0000-0000-000000000001', 'meta/llama-3.1-8b-instruct',
 'Chat', '{"chat":true,"reasoning":"general","context_window":128000}'::jsonb, 'Active'),
('71000000-0000-0000-0000-000000000002',
 '70000000-0000-0000-0000-000000000001', 'nvidia/llama-3.1-nemotron-70b-instruct',
 'Chat', '{"chat":true,"reasoning":"advanced"}'::jsonb, 'Active'),
('71000000-0000-0000-0000-000000000003',
 '70000000-0000-0000-0000-000000000002', 'Phi-3.5-mini-instruct',
 'Chat', '{"chat":true,"reasoning":"general"}'::jsonb, 'Active');

INSERT INTO department_model
(department_id, model_id, is_enabled, created_at, created_by)
VALUES
('40000000-0000-0000-0000-000000000001',
 '71000000-0000-0000-0000-000000000001', TRUE, NOW(),
 '20000000-0000-0000-0000-000000000001'),
('40000000-0000-0000-0000-000000000001',
 '71000000-0000-0000-0000-000000000002', TRUE, NOW(),
 '20000000-0000-0000-0000-000000000001'),
('40000000-0000-0000-0000-000000000002',
 '71000000-0000-0000-0000-000000000003', TRUE, NOW(),
 '20000000-0000-0000-0000-000000000001');

INSERT INTO model_pricing
(pricing_id, model_id, input_price_per_token, output_price_per_token,
 effective_from, effective_to, created_at, created_by)
VALUES
('72000000-0000-0000-0000-000000000001',
 '71000000-0000-0000-0000-000000000001', 0.0000002000, 0.0000006000,
 NOW() - INTERVAL '30 days', NULL, NOW(), '20000000-0000-0000-0000-000000000001'),
('72000000-0000-0000-0000-000000000002',
 '71000000-0000-0000-0000-000000000002', 0.0000010000, 0.0000030000,
 NOW() - INTERVAL '30 days', NULL, NOW(), '20000000-0000-0000-0000-000000000001'),
('72000000-0000-0000-0000-000000000003',
 '71000000-0000-0000-0000-000000000003', 0.0000001500, 0.0000004000,
 NOW() - INTERVAL '30 days', NULL, NOW(), '20000000-0000-0000-0000-000000000001');

-- Conversations and AI request/response examples
INSERT INTO conversation
(conversation_id, user_id, department_id, title, created_at, created_by)
VALUES
('80000000-0000-0000-0000-000000000001',
 '20000000-0000-0000-0000-000000000003',
 '40000000-0000-0000-0000-000000000001',
 'API design discussion', NOW() - INTERVAL '1 day',
 '20000000-0000-0000-0000-000000000003'),
('80000000-0000-0000-0000-000000000002',
 '20000000-0000-0000-0000-000000000004',
 '40000000-0000-0000-0000-000000000002',
 'Monthly report summary', NOW() - INTERVAL '2 hours',
 '20000000-0000-0000-0000-000000000004');

INSERT INTO ai_request
(request_id, conversation_id, model_id, prompt, status, requested_at, created_at, created_by)
VALUES
('81000000-0000-0000-0000-000000000001',
 '80000000-0000-0000-0000-000000000001',
 '71000000-0000-0000-0000-000000000001',
 'Explain REST API versioning strategies.', 'Completed',
 NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day',
 '20000000-0000-0000-0000-000000000003'),
('81000000-0000-0000-0000-000000000002',
 '80000000-0000-0000-0000-000000000002',
 '71000000-0000-0000-0000-000000000003',
 'Summarize a sample monthly finance report.', 'Completed',
 NOW() - INTERVAL '2 hours', NOW() - INTERVAL '2 hours',
 '20000000-0000-0000-0000-000000000004');

INSERT INTO ai_response
(response_id, request_id, response_text, input_tokens, output_tokens, duration_ms)
VALUES
('82000000-0000-0000-0000-000000000001',
 '81000000-0000-0000-0000-000000000001',
 'REST APIs can be versioned through URI paths, query parameters, or headers. URI versioning is explicit and commonly used.', 18, 45, 820),
('82000000-0000-0000-0000-000000000002',
 '81000000-0000-0000-0000-000000000002',
 'The sample report indicates stable revenue, increased operating expenses, and a need to review discretionary spending.', 20, 32, 690);

INSERT INTO request_routing
(routing_id, request_id, model_id, routing_reason, selected_at, created_at, created_by)
VALUES
('83000000-0000-0000-0000-000000000001',
 '81000000-0000-0000-0000-000000000001',
 '71000000-0000-0000-0000-000000000001',
 'Selected an enabled general-purpose model with an appropriate cost profile.',
 NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day',
 '20000000-0000-0000-0000-000000000003'),
('83000000-0000-0000-0000-000000000002',
 '81000000-0000-0000-0000-000000000002',
 '71000000-0000-0000-0000-000000000003',
 'Selected the model enabled for the Finance department.',
 NOW() - INTERVAL '2 hours', NOW() - INTERVAL '2 hours',
 '20000000-0000-0000-0000-000000000004');

INSERT INTO usage_record
(usage_id, request_id, input_tokens, output_tokens, estimated_cost, created_at, created_by)
VALUES
('84000000-0000-0000-0000-000000000001',
 '81000000-0000-0000-0000-000000000001', 18, 45, 0.00003060,
 NOW() - INTERVAL '1 day', '20000000-0000-0000-0000-000000000003'),
('84000000-0000-0000-0000-000000000002',
 '81000000-0000-0000-0000-000000000002', 20, 32, 0.00001580,
 NOW() - INTERVAL '2 hours', '20000000-0000-0000-0000-000000000004');

-- Credit accounts: organization-level account and department-level accounts.
INSERT INTO credit_account
(credit_account_id, organization_id, department_id, available_credits, reserved_credits,
 created_at, created_by)
VALUES
('90000000-0000-0000-0000-000000000001',
 '10000000-0000-0000-0000-000000000001', NULL, 5000.000000, 0.000000,
 NOW(), '20000000-0000-0000-0000-000000000001'),
('90000000-0000-0000-0000-000000000002',
 '10000000-0000-0000-0000-000000000001',
 '40000000-0000-0000-0000-000000000001', 1000.000000, 5.000000,
 NOW(), '20000000-0000-0000-0000-000000000001'),
('90000000-0000-0000-0000-000000000003',
 '10000000-0000-0000-0000-000000000001',
 '40000000-0000-0000-0000-000000000002', 500.000000, 0.000000,
 NOW(), '20000000-0000-0000-0000-000000000001');

INSERT INTO credit_transaction
(transaction_id, credit_account_id, request_id, transaction_type, amount,
 idempotency_key, created_at, created_by)
VALUES
('91000000-0000-0000-0000-000000000001',
 '90000000-0000-0000-0000-000000000002', NULL, 'Allocation', 1000.000000,
 'seed-allocation-engineering-001', NOW(), '20000000-0000-0000-0000-000000000001'),
('91000000-0000-0000-0000-000000000002',
 '90000000-0000-0000-0000-000000000002',
 '81000000-0000-0000-0000-000000000001', 'Usage', 0.000031,
 'seed-usage-request-001', NOW() - INTERVAL '1 day',
 '20000000-0000-0000-0000-000000000003');

INSERT INTO quota_policy
(quota_policy_id, organization_id, department_id, credit_limit, period,
 warning_threshold, created_at, created_by)
VALUES
('92000000-0000-0000-0000-000000000001',
 '10000000-0000-0000-0000-000000000001', NULL, 5000.000000,
 'Monthly', 80.00, NOW(), '20000000-0000-0000-0000-000000000001'),
('92000000-0000-0000-0000-000000000002',
 '10000000-0000-0000-0000-000000000001',
 '40000000-0000-0000-0000-000000000001', 1000.000000,
 'Monthly', 80.00, NOW(), '20000000-0000-0000-0000-000000000001'),
('92000000-0000-0000-0000-000000000003',
 '10000000-0000-0000-0000-000000000001',
 '40000000-0000-0000-0000-000000000002', 500.000000,
 'Monthly', 80.00, NOW(), '20000000-0000-0000-0000-000000000001');

-- 80% notification and 95% warning examples. 100% hard stop is enforced by backend logic.
INSERT INTO quota_notification
(notification_id, credit_account_id, threshold_percent, notification_type, created_at, created_by)
VALUES
('93000000-0000-0000-0000-000000000001',
 '90000000-0000-0000-0000-000000000002', 80.00, 'UsageThresholdNotification',
 NOW(), '20000000-0000-0000-0000-000000000001'),
('93000000-0000-0000-0000-000000000002',
 '90000000-0000-0000-0000-000000000002', 95.00, 'CriticalUsageWarning',
 NOW(), '20000000-0000-0000-0000-000000000001');

INSERT INTO additional_credit_request
(additional_request_id, department_id, requested_by, requested_credits, status,
 reviewed_by, created_at, created_by)
VALUES
('94000000-0000-0000-0000-000000000001',
 '40000000-0000-0000-0000-000000000001',
 '20000000-0000-0000-0000-000000000002', 250.000000, 'Pending',
 NULL, NOW(), '20000000-0000-0000-0000-000000000002'),
('94000000-0000-0000-0000-000000000002',
 '40000000-0000-0000-0000-000000000002',
 '20000000-0000-0000-0000-000000000004', 50.000000, 'Pending',
 NULL, NOW(), '20000000-0000-0000-0000-000000000004');

-- Notifications
INSERT INTO notification
(notification_id, user_id, title, message, read_at, created_at, created_by)
VALUES
('95000000-0000-0000-0000-000000000001',
 '20000000-0000-0000-0000-000000000003',
 'Quota usage reached 80%',
 'Your department has reached the 80% quota notification threshold.',
 NULL, NOW(), '20000000-0000-0000-0000-000000000001'),
('95000000-0000-0000-0000-000000000002',
 '20000000-0000-0000-0000-000000000002',
 'Additional credits requested',
 'An additional credit request is waiting for platform-level review.',
 NULL, NOW(), '20000000-0000-0000-0000-000000000001'),
('95000000-0000-0000-0000-000000000003',
 '20000000-0000-0000-0000-000000000004',
 'Request completed',
 'Your AI request has been completed successfully.',
 NOW(), NOW(), '20000000-0000-0000-0000-000000000004');

-- System logs: technical events from backend/services.
INSERT INTO system_log
(log_id, event_code, log_category, severity, source_service, message,
 actor_user_id, entity_type, entity_id, action, outcome, correlation_id,
 request_id, error_code, metadata, occurred_at, created_at, created_by)
VALUES
('96000000-0000-0000-0000-000000000001',
 'AI_REQUEST_COMPLETED', 'AI_REQUEST', 'INFO', 'ASP.NET Core API',
 'AI request completed successfully.',
 '20000000-0000-0000-0000-000000000003', 'AI_REQUEST',
 '81000000-0000-0000-0000-000000000001', 'PROCESS', 'SUCCESS',
 '97000000-0000-0000-0000-000000000001',
 '81000000-0000-0000-0000-000000000001', NULL,
 '{"input_tokens":18,"output_tokens":45}'::jsonb, NOW() - INTERVAL '1 day',
 NOW() - INTERVAL '1 day', '20000000-0000-0000-0000-000000000003'),
('96000000-0000-0000-0000-000000000002',
 'MODEL_PROVIDER_TIMEOUT', 'PROVIDER', 'WARNING', 'AI Orchestration Worker',
 'A provider timeout was detected; retry policy was initiated.',
 NULL, 'AI_PROVIDER', '70000000-0000-0000-0000-000000000001',
 'CALL_PROVIDER', 'RETRYING',
 '97000000-0000-0000-0000-000000000002',
 NULL, 'PROVIDER_TIMEOUT',
 '{"retry_attempt":1,"timeout_ms":30000}'::jsonb, NOW(),
 NOW(), '20000000-0000-0000-0000-000000000001');

-- Audit logs: important user/admin actions for accountability.
INSERT INTO audit_log
(audit_id, actor_user_id, actor_role, action, target_type, target_id,
 department_id, old_values, new_values, outcome, reason, correlation_id,
 occurred_at, created_at, created_by)
VALUES
('98000000-0000-0000-0000-000000000001',
 '20000000-0000-0000-0000-000000000001', 'Super Admin',
 'CREATE_DEPARTMENT', 'Department', '40000000-0000-0000-0000-000000000001',
 NULL, NULL, '{"department_name":"Engineering","status":"Active"}'::jsonb,
 'SUCCESS', 'Initial organization setup',
 '97000000-0000-0000-0000-000000000003', NOW(), NOW(),
 '20000000-0000-0000-0000-000000000001'),
('98000000-0000-0000-0000-000000000002',
 '20000000-0000-0000-0000-000000000002', 'Admin',
 'CREATE_ADDITIONAL_CREDIT_REQUEST', 'AdditionalCreditRequest',
 '94000000-0000-0000-0000-000000000001',
 '40000000-0000-0000-0000-000000000001',
 NULL, '{"requested_credits":250,"status":"Pending"}'::jsonb,
 'SUCCESS', 'Department requires additional monthly credits',
 '97000000-0000-0000-0000-000000000004', NOW(), NOW(),
 '20000000-0000-0000-0000-000000000002'),
('98000000-0000-0000-0000-000000000003',
 '20000000-0000-0000-0000-000000000003', 'User',
 'CREATE_AI_REQUEST', 'AIRequest', '81000000-0000-0000-0000-000000000001',
 '40000000-0000-0000-0000-000000000001',
 NULL, '{"status":"Completed"}'::jsonb,
 'SUCCESS', NULL, '97000000-0000-0000-0000-000000000005',
 NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day',
 '20000000-0000-0000-0000-000000000003');

-- Credit ledger: append-only financial history examples.
INSERT INTO credit_ledger_entry
(ledger_entry_id, credit_account_id, transaction_type, amount, balance_after,
 reference_type, reference_id, idempotency_key, request_id, description,
 performed_by, department_id, correlation_id, created_at, created_by)
VALUES
('99000000-0000-0000-0000-000000000001',
 '90000000-0000-0000-0000-000000000002', 'ALLOCATION',
 1000.000000, 1000.000000, 'CreditTransaction',
 '91000000-0000-0000-0000-000000000001',
 'ledger-seed-allocation-001', NULL,
 'Initial credit allocation to Engineering department.',
 '20000000-0000-0000-0000-000000000001',
 '40000000-0000-0000-0000-000000000001',
 '97000000-0000-0000-0000-000000000006', NOW(),
 '20000000-0000-0000-0000-000000000001'),
('99000000-0000-0000-0000-000000000002',
 '90000000-0000-0000-0000-000000000002', 'USAGE',
 0.000031, 999.999969, 'AIRequest',
 '81000000-0000-0000-0000-000000000001',
 'ledger-seed-usage-001', '81000000-0000-0000-0000-000000000001',
 'Credit consumption for completed AI request.',
 '20000000-0000-0000-0000-000000000003',
 '40000000-0000-0000-0000-000000000001',
 '97000000-0000-0000-0000-000000000007', NOW() - INTERVAL '1 day',
 '20000000-0000-0000-0000-000000000003');

COMMIT;

-- =========================================================
-- POST-RUN VERIFICATION QUERIES
-- Run these after the script to verify the setup.
-- =========================================================

-- Table count (should return 27)
SELECT COUNT(*) AS created_table_count
FROM information_schema.tables
WHERE table_schema = 'ai_orchestration'
  AND table_type = 'BASE TABLE';

-- Roles and their permissions
SELECT r.name, r.permission
FROM ai_orchestration.role r
ORDER BY r.name;

-- Users with assigned roles and departments
SELECT u.email, r.name AS role_name, d.department_name,
       ud.membership_role
FROM ai_orchestration.app_user u
JOIN ai_orchestration.user_role ur ON ur.user_id = u.user_id
JOIN ai_orchestration.role r ON r.role_id = ur.role_id
LEFT JOIN ai_orchestration.user_department ud ON ud.user_id = u.user_id
LEFT JOIN ai_orchestration.department d ON d.department_id = ud.department_id
ORDER BY u.email;

-- Mock data row counts for all 27 tables
SELECT 'organization' AS table_name, COUNT(*) FROM ai_orchestration.organization
UNION ALL SELECT 'approved_email_domain', COUNT(*) FROM ai_orchestration.approved_email_domain
UNION ALL SELECT 'department', COUNT(*) FROM ai_orchestration.department
UNION ALL SELECT 'app_user', COUNT(*) FROM ai_orchestration.app_user
UNION ALL SELECT 'role', COUNT(*) FROM ai_orchestration.role
UNION ALL SELECT 'user_department', COUNT(*) FROM ai_orchestration.user_department
UNION ALL SELECT 'user_role', COUNT(*) FROM ai_orchestration.user_role
UNION ALL SELECT 'verification_code', COUNT(*) FROM ai_orchestration.verification_code
UNION ALL SELECT 'api_key', COUNT(*) FROM ai_orchestration.api_key
UNION ALL SELECT 'ai_provider', COUNT(*) FROM ai_orchestration.ai_provider
UNION ALL SELECT 'ai_model', COUNT(*) FROM ai_orchestration.ai_model
UNION ALL SELECT 'department_model', COUNT(*) FROM ai_orchestration.department_model
UNION ALL SELECT 'model_pricing', COUNT(*) FROM ai_orchestration.model_pricing
UNION ALL SELECT 'conversation', COUNT(*) FROM ai_orchestration.conversation
UNION ALL SELECT 'ai_request', COUNT(*) FROM ai_orchestration.ai_request
UNION ALL SELECT 'ai_response', COUNT(*) FROM ai_orchestration.ai_response
UNION ALL SELECT 'request_routing', COUNT(*) FROM ai_orchestration.request_routing
UNION ALL SELECT 'usage_record', COUNT(*) FROM ai_orchestration.usage_record
UNION ALL SELECT 'credit_account', COUNT(*) FROM ai_orchestration.credit_account
UNION ALL SELECT 'credit_transaction', COUNT(*) FROM ai_orchestration.credit_transaction
UNION ALL SELECT 'quota_policy', COUNT(*) FROM ai_orchestration.quota_policy
UNION ALL SELECT 'quota_notification', COUNT(*) FROM ai_orchestration.quota_notification
UNION ALL SELECT 'additional_credit_request', COUNT(*) FROM ai_orchestration.additional_credit_request
UNION ALL SELECT 'notification', COUNT(*) FROM ai_orchestration.notification
UNION ALL SELECT 'system_log', COUNT(*) FROM ai_orchestration.system_log
UNION ALL SELECT 'audit_log', COUNT(*) FROM ai_orchestration.audit_log
UNION ALL SELECT 'credit_ledger_entry', COUNT(*) FROM ai_orchestration.credit_ledger_entry
ORDER BY table_name;
