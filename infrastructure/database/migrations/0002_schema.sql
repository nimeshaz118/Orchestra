-- AI Orchestration Platform
-- Recommended PostgreSQL schema for the current non-RAG release.
--
-- Architecture decisions represented here:
--   * Single organization / single tenant.
--   * Exactly three application roles: USER, ADMIN, SUPER_ADMIN.
--   * At most one active ADMIN and one active SUPER_ADMIN.
--   * Both privileged accounts are bootstrapped by .NET with hashed passwords.
--   * Each normal user belongs to exactly one department.
--   * Email verification and rotating refresh-token sessions are supported.
--   * Password-reset codes are bound to the user's security-stamp snapshot.
--   * Refresh-token lineage cannot cross users or token families.
--   * Conversations are soft deleted.
--   * AI requests and credit operations are idempotent.
--   * Model pricing is versioned and usage records retain the pricing version.
--   * CREDIT_LEDGER_ENTRY is the authoritative, append-only credit history.
--   * RAG/document tables are intentionally deferred to a later migration.
--
-- IMPORTANT IMPLEMENTATION NOTES:
--   1. The current workflow is database-first: run this file once against a
--      new PostgreSQL database, then scaffold the EF Core model. Record later
--      schema changes in numbered SQL scripts and re-scaffold deliberately.
--   2. Do not add DROP SCHEMA to normal migrations.
--   3. Password hashes must be produced by ASP.NET Core PasswordHasher/Identity.
--   4. Raw refresh tokens, verification codes, API keys, and provider secrets
--      must never be stored in the database.
--   5. Role permission JSON is descriptive. ASP.NET authorization policies are
--      the final enforcement point.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE SCHEMA IF NOT EXISTS ai_orchestration;
SET search_path TO ai_orchestration, public;

-- =========================================================
-- Shared trigger functions
-- =========================================================

CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at := NOW();
    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION prevent_append_only_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION '% is append-only; UPDATE and DELETE are not allowed.', TG_TABLE_NAME;
END;
$$;

-- =========================================================
-- 1. Single-organization configuration and access control
-- =========================================================

-- The CHECK constraint makes this a singleton table. There is no organization
-- creation API in the current single-tenant product.
CREATE TABLE organization_profile (
    organization_id SMALLINT PRIMARY KEY DEFAULT 1,
    organization_name VARCHAR(150) NOT NULL,
    timezone VARCHAR(80) NOT NULL DEFAULT 'Asia/Colombo',
    status VARCHAR(20) NOT NULL DEFAULT 'ACTIVE',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_organization_singleton CHECK (organization_id = 1),
    CONSTRAINT ck_organization_status CHECK (status IN ('ACTIVE', 'INACTIVE'))
);

CREATE TABLE role (
    role_code VARCHAR(30) PRIMARY KEY,
    display_name VARCHAR(100) NOT NULL UNIQUE,
    description TEXT NULL,
    permissions JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_role_code CHECK (role_code IN ('USER', 'ADMIN', 'SUPER_ADMIN')),
    CONSTRAINT ck_role_permissions_object CHECK (jsonb_typeof(permissions) = 'object')
);

-- Department is added as a foreign key after DEPARTMENT is created, which
-- avoids the APP_USER <-> DEPARTMENT creation-order cycle.
CREATE TABLE app_user (
    user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email VARCHAR(255) NOT NULL,
    password_hash TEXT NOT NULL,
    role_code VARCHAR(30) NOT NULL REFERENCES role(role_code),
    department_id UUID NULL,
    first_name VARCHAR(100) NOT NULL,
    last_name VARCHAR(100) NOT NULL,
    display_name VARCHAR(150) NULL,
    phone_number VARCHAR(30) NULL,
    job_title VARCHAR(120) NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    email_verified_at TIMESTAMPTZ NULL,
    must_change_password BOOLEAN NOT NULL DEFAULT FALSE,
    password_changed_at TIMESTAMPTZ NULL,
    security_stamp UUID NOT NULL DEFAULT gen_random_uuid(),
    failed_login_attempts INTEGER NOT NULL DEFAULT 0,
    locked_until TIMESTAMPTZ NULL,
    last_login_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at TIMESTAMPTZ NULL,
    created_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    updated_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    CONSTRAINT ck_app_user_status
        CHECK (status IN ('PENDING', 'ACTIVE', 'INACTIVE', 'SUSPENDED')),
    CONSTRAINT ck_app_user_failed_attempts CHECK (failed_login_attempts >= 0),
    CONSTRAINT ck_app_user_role_department CHECK (
        (role_code = 'USER' AND department_id IS NOT NULL)
        OR
        (role_code IN ('ADMIN', 'SUPER_ADMIN') AND department_id IS NULL)
    )
);

CREATE TABLE department (
    department_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    department_name VARCHAR(150) NOT NULL,
    description TEXT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'ACTIVE',
    configuration JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    updated_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    CONSTRAINT uq_department_name UNIQUE (department_name),
    CONSTRAINT ck_department_status CHECK (status IN ('ACTIVE', 'INACTIVE', 'ARCHIVED')),
    CONSTRAINT ck_department_configuration_object
        CHECK (jsonb_typeof(configuration) = 'object')
);

ALTER TABLE app_user
    ADD CONSTRAINT fk_app_user_department
    FOREIGN KEY (department_id) REFERENCES department(department_id);

-- PostgreSQL text uniqueness is case-sensitive, so normalize through LOWER.
CREATE UNIQUE INDEX ux_app_user_email_ci ON app_user (LOWER(email));

-- Only one active organization-wide Admin is permitted. An old Admin can be
-- deactivated before a replacement Admin is activated.
CREATE UNIQUE INDEX ux_one_active_admin
    ON app_user (role_code)
    WHERE role_code = 'ADMIN' AND status = 'ACTIVE' AND deleted_at IS NULL;

-- The current release also uses one prebuilt technical Super Admin account.
CREATE UNIQUE INDEX ux_one_active_super_admin
    ON app_user (role_code)
    WHERE role_code = 'SUPER_ADMIN' AND status = 'ACTIVE' AND deleted_at IS NULL;

CREATE TABLE approved_email_domain (
    domain_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    domain_name VARCHAR(255) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    updated_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL
);

CREATE UNIQUE INDEX ux_approved_email_domain_ci
    ON approved_email_domain (LOWER(domain_name));

CREATE TABLE platform_setting (
    setting_key VARCHAR(120) PRIMARY KEY,
    setting_value JSONB NOT NULL,
    description TEXT NULL,
    is_sensitive BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL
);

-- =========================================================
-- 2. Authentication and application API keys
-- =========================================================

-- Store a SHA-256 or stronger hash of the refresh token, never its raw value.
-- FAMILY_ID allows reuse detection to revoke a complete rotation chain.
CREATE TABLE refresh_token (
    refresh_token_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_user(user_id) ON DELETE CASCADE,
    token_hash CHAR(64) NOT NULL UNIQUE,
    family_id UUID NOT NULL,
    parent_token_id UUID NULL,
    replaced_by_token_id UUID NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ NULL,
    revoked_reason VARCHAR(150) NULL,
    created_by_ip INET NULL,
    last_used_at TIMESTAMPTZ NULL,
    user_agent TEXT NULL,
    CONSTRAINT uq_refresh_token_identity_family
        UNIQUE (refresh_token_id, user_id, family_id),
    CONSTRAINT fk_refresh_token_parent_same_user_family
        FOREIGN KEY (parent_token_id, user_id, family_id)
        REFERENCES refresh_token(refresh_token_id, user_id, family_id),
    CONSTRAINT fk_refresh_token_replacement_same_user_family
        FOREIGN KEY (replaced_by_token_id, user_id, family_id)
        REFERENCES refresh_token(refresh_token_id, user_id, family_id),
    CONSTRAINT ck_refresh_token_expiry CHECK (expires_at > created_at),
    CONSTRAINT ck_refresh_token_revocation
        CHECK (revoked_at IS NULL OR revoked_at >= created_at),
    CONSTRAINT ck_refresh_token_parent_not_self
        CHECK (parent_token_id IS NULL OR parent_token_id <> refresh_token_id),
    CONSTRAINT ck_refresh_token_replacement_not_self
        CHECK (replaced_by_token_id IS NULL OR replaced_by_token_id <> refresh_token_id)
);

CREATE TABLE verification_code (
    verification_code_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_user(user_id) ON DELETE CASCADE,
    purpose VARCHAR(30) NOT NULL,
    code_hash TEXT NOT NULL,
    security_stamp_snapshot UUID NULL,
    delivery_status VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    failed_attempts INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NOT NULL,
    used_at TIMESTAMPTZ NULL,
    invalidated_at TIMESTAMPTZ NULL,
    CONSTRAINT ck_verification_purpose
        CHECK (purpose IN ('EMAIL_VERIFICATION', 'PASSWORD_RESET')),
    CONSTRAINT ck_verification_password_reset_stamp CHECK (
        purpose <> 'PASSWORD_RESET' OR security_stamp_snapshot IS NOT NULL
    ),
    CONSTRAINT ck_verification_delivery_status
        CHECK (delivery_status IN ('PENDING', 'SENT', 'FAILED')),
    CONSTRAINT ck_verification_attempts CHECK (failed_attempts BETWEEN 0 AND 5),
    CONSTRAINT ck_verification_expiry CHECK (expires_at > created_at),
    CONSTRAINT ck_verification_used_time
        CHECK (used_at IS NULL OR used_at >= created_at)
);

CREATE TABLE api_key (
    api_key_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_user(user_id) ON DELETE CASCADE,
    key_name VARCHAR(150) NOT NULL,
    key_prefix VARCHAR(20) NOT NULL,
    key_hash CHAR(64) NOT NULL UNIQUE,
    status VARCHAR(20) NOT NULL DEFAULT 'ACTIVE',
    last_used_at TIMESTAMPTZ NULL,
    expires_at TIMESTAMPTZ NULL,
    revoked_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_api_key_name_per_user UNIQUE (user_id, key_name),
    CONSTRAINT ck_api_key_status CHECK (status IN ('ACTIVE', 'REVOKED', 'EXPIRED')),
    CONSTRAINT ck_api_key_expiry CHECK (expires_at IS NULL OR expires_at > created_at)
);

-- =========================================================
-- 3. Provider, model, pricing, and routing governance
-- =========================================================

CREATE TABLE ai_provider (
    provider_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_name VARCHAR(150) NOT NULL UNIQUE,
    base_url TEXT NOT NULL,
    credential_reference VARCHAR(150) NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'ACTIVE',
    timeout_seconds INTEGER NOT NULL DEFAULT 60,
    configuration JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    updated_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    CONSTRAINT ck_ai_provider_status
        CHECK (status IN ('ACTIVE', 'INACTIVE', 'MAINTENANCE')),
    CONSTRAINT ck_ai_provider_timeout CHECK (timeout_seconds BETWEEN 1 AND 600),
    CONSTRAINT ck_ai_provider_configuration_object
        CHECK (jsonb_typeof(configuration) = 'object')
);

-- CREDENTIAL_REFERENCE contains only the environment/secret-store key name.
-- Example: NVIDIA_API_KEY. It must never contain the secret itself.
CREATE TABLE ai_model (
    model_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_id UUID NOT NULL REFERENCES ai_provider(provider_id),
    provider_model_key VARCHAR(200) NOT NULL,
    display_name VARCHAR(150) NOT NULL,
    model_type VARCHAR(30) NOT NULL DEFAULT 'CHAT',
    status VARCHAR(20) NOT NULL DEFAULT 'ACTIVE',
    context_window_tokens INTEGER NULL,
    quality_tier SMALLINT NOT NULL DEFAULT 1,
    capabilities JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    updated_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    CONSTRAINT uq_ai_model_provider_key UNIQUE (provider_id, provider_model_key),
    CONSTRAINT ck_ai_model_type CHECK (model_type IN ('CHAT', 'EMBEDDING')),
    CONSTRAINT ck_ai_model_status
        CHECK (status IN ('ACTIVE', 'INACTIVE', 'MAINTENANCE')),
    CONSTRAINT ck_ai_model_context_window
        CHECK (context_window_tokens IS NULL OR context_window_tokens > 0),
    CONSTRAINT ck_ai_model_quality_tier CHECK (quality_tier BETWEEN 1 AND 5),
    CONSTRAINT ck_ai_model_capabilities_object
        CHECK (jsonb_typeof(capabilities) = 'object')
);

CREATE TABLE department_model (
    department_id UUID NOT NULL REFERENCES department(department_id),
    model_id UUID NOT NULL REFERENCES ai_model(model_id),
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    PRIMARY KEY (department_id, model_id)
);

-- Prices are stored per one million tokens to avoid extremely small decimals.
CREATE TABLE model_pricing (
    pricing_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    model_id UUID NOT NULL REFERENCES ai_model(model_id),
    version_number INTEGER NOT NULL,
    currency_code CHAR(3) NOT NULL DEFAULT 'USD',
    input_cost_per_million_tokens NUMERIC(20,8) NOT NULL,
    output_cost_per_million_tokens NUMERIC(20,8) NOT NULL,
    effective_from TIMESTAMPTZ NOT NULL,
    effective_to TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    CONSTRAINT uq_model_pricing_version UNIQUE (model_id, version_number),
    CONSTRAINT ck_model_pricing_version CHECK (version_number > 0),
    CONSTRAINT ck_model_pricing_costs CHECK (
        input_cost_per_million_tokens >= 0
        AND output_cost_per_million_tokens >= 0
    ),
    CONSTRAINT ck_model_pricing_period
        CHECK (effective_to IS NULL OR effective_to > effective_from)
);

-- Only one open-ended/current price is allowed per model. The application must
-- also reject overlapping closed historical ranges.
CREATE UNIQUE INDEX ux_model_pricing_one_current
    ON model_pricing (model_id)
    WHERE effective_to IS NULL;

-- A NULL department means the organization-wide default policy. A department
-- policy overrides the default for that department.
CREATE TABLE model_selection_policy (
    policy_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    department_id UUID NULL REFERENCES department(department_id),
    policy_name VARCHAR(150) NOT NULL,
    cost_weight NUMERIC(5,4) NOT NULL DEFAULT 0.4000,
    quality_weight NUMERIC(5,4) NOT NULL DEFAULT 0.4000,
    latency_weight NUMERIC(5,4) NOT NULL DEFAULT 0.2000,
    maximum_cost_per_request NUMERIC(20,8) NULL,
    fallback_model_id UUID NULL REFERENCES ai_model(model_id),
    rules JSONB NOT NULL DEFAULT '{}'::jsonb,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    updated_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    CONSTRAINT ck_selection_policy_weights CHECK (
        cost_weight BETWEEN 0 AND 1
        AND quality_weight BETWEEN 0 AND 1
        AND latency_weight BETWEEN 0 AND 1
        AND cost_weight + quality_weight + latency_weight = 1.0000
    ),
    CONSTRAINT ck_selection_policy_max_cost
        CHECK (maximum_cost_per_request IS NULL OR maximum_cost_per_request >= 0),
    CONSTRAINT ck_selection_policy_rules_object CHECK (jsonb_typeof(rules) = 'object')
);

CREATE UNIQUE INDEX ux_one_active_default_selection_policy
    ON model_selection_policy ((1))
    WHERE department_id IS NULL AND is_active = TRUE;

CREATE UNIQUE INDEX ux_one_active_department_selection_policy
    ON model_selection_policy (department_id)
    WHERE department_id IS NOT NULL AND is_active = TRUE;

-- =========================================================
-- 4. Conversations, AI processing, routing attempts, and usage
-- =========================================================

CREATE TABLE conversation (
    conversation_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_user(user_id),
    department_id UUID NOT NULL REFERENCES department(department_id),
    title VARCHAR(255) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_at TIMESTAMPTZ NULL,
    CONSTRAINT ck_conversation_deleted_time
        CHECK (deleted_at IS NULL OR deleted_at >= created_at)
);

CREATE TABLE ai_request (
    request_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID NOT NULL REFERENCES conversation(conversation_id),
    idempotency_key VARCHAR(150) NOT NULL UNIQUE,
    correlation_id UUID NOT NULL UNIQUE DEFAULT gen_random_uuid(),
    prompt_text TEXT NOT NULL,
    intent VARCHAR(100) NULL,
    selected_model_id UUID NULL REFERENCES ai_model(model_id),
    provider_request_id VARCHAR(255) NULL,
    status VARCHAR(30) NOT NULL DEFAULT 'QUEUED',
    failure_code VARCHAR(100) NULL,
    failure_message TEXT NULL,
    requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    started_at TIMESTAMPTZ NULL,
    completed_at TIMESTAMPTZ NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_ai_request_status CHECK (status IN (
        'QUEUED', 'PROCESSING', 'COMPLETED', 'FAILED', 'CANCELLED',
        'TIMED_OUT', 'RECONCILIATION_REQUIRED'
    )),
    CONSTRAINT ck_ai_request_started_time
        CHECK (started_at IS NULL OR started_at >= requested_at),
    CONSTRAINT ck_ai_request_completed_time
        CHECK (completed_at IS NULL OR completed_at >= requested_at)
);

CREATE TABLE ai_response (
    response_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    request_id UUID NOT NULL UNIQUE REFERENCES ai_request(request_id),
    response_text TEXT NULL,
    finish_reason VARCHAR(50) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- One request can have multiple attempts because of fallback or provider retry.
CREATE TABLE request_routing_attempt (
    routing_attempt_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    request_id UUID NOT NULL REFERENCES ai_request(request_id),
    attempt_number INTEGER NOT NULL,
    model_id UUID NOT NULL REFERENCES ai_model(model_id),
    provider_request_id VARCHAR(255) NULL,
    routing_reason TEXT NULL,
    status VARCHAR(30) NOT NULL,
    error_code VARCHAR(100) NULL,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ NULL,
    CONSTRAINT uq_routing_attempt UNIQUE (request_id, attempt_number),
    CONSTRAINT ck_routing_attempt_number CHECK (attempt_number > 0),
    CONSTRAINT ck_routing_attempt_status CHECK (status IN (
        'SELECTED', 'SENT', 'SUCCEEDED', 'FAILED', 'TIMED_OUT', 'UNKNOWN'
    )),
    CONSTRAINT ck_routing_attempt_completed_time
        CHECK (completed_at IS NULL OR completed_at >= started_at)
);

-- One final usage/settlement record is allowed per AI request. PRICING_ID makes
-- old reports reproducible even after model prices change.
CREATE TABLE usage_record (
    usage_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    request_id UUID NOT NULL UNIQUE REFERENCES ai_request(request_id),
    pricing_id UUID NULL REFERENCES model_pricing(pricing_id),
    input_tokens INTEGER NOT NULL DEFAULT 0,
    output_tokens INTEGER NOT NULL DEFAULT 0,
    monetary_cost NUMERIC(20,10) NOT NULL DEFAULT 0,
    credits_consumed NUMERIC(20,8) NOT NULL DEFAULT 0,
    is_estimated BOOLEAN NOT NULL DEFAULT FALSE,
    settlement_status VARCHAR(30) NOT NULL DEFAULT 'PENDING',
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    settled_at TIMESTAMPTZ NULL,
    CONSTRAINT ck_usage_values CHECK (
        input_tokens >= 0
        AND output_tokens >= 0
        AND monetary_cost >= 0
        AND credits_consumed >= 0
    ),
    CONSTRAINT ck_usage_settlement_status CHECK (settlement_status IN (
        'PENDING', 'SETTLED', 'RECONCILIATION_REQUIRED', 'REVERSED'
    )),
    CONSTRAINT ck_usage_settled_time
        CHECK (settled_at IS NULL OR settled_at >= recorded_at)
);

-- =========================================================
-- 5. Credit accounts, immutable ledger, and quota policies
-- =========================================================

-- There is one organization account and at most one account per department.
-- VERSION is an optimistic concurrency token for EF Core.
CREATE TABLE credit_account (
    credit_account_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    scope_type VARCHAR(20) NOT NULL,
    department_id UUID NULL REFERENCES department(department_id),
    available_credits NUMERIC(20,8) NOT NULL DEFAULT 0,
    reserved_credits NUMERIC(20,8) NOT NULL DEFAULT 0,
    version INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_credit_account_scope CHECK (
        (scope_type = 'ORGANIZATION' AND department_id IS NULL)
        OR
        (scope_type = 'DEPARTMENT' AND department_id IS NOT NULL)
    ),
    CONSTRAINT ck_credit_account_balances
        CHECK (available_credits >= 0 AND reserved_credits >= 0),
    CONSTRAINT ck_credit_account_version CHECK (version >= 0)
);

CREATE UNIQUE INDEX ux_one_organization_credit_account
    ON credit_account ((1))
    WHERE scope_type = 'ORGANIZATION';

CREATE UNIQUE INDEX ux_one_credit_account_per_department
    ON credit_account (department_id)
    WHERE scope_type = 'DEPARTMENT';

-- This is the only permanent credit transaction/history table. AMOUNT is the
-- business amount; deltas describe exactly how both balances changed.
CREATE TABLE credit_ledger_entry (
    ledger_entry_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    credit_account_id UUID NOT NULL REFERENCES credit_account(credit_account_id),
    request_id UUID NULL REFERENCES ai_request(request_id),
    reservation_id UUID NULL,
    entry_type VARCHAR(30) NOT NULL,
    amount NUMERIC(20,8) NOT NULL,
    available_delta NUMERIC(20,8) NOT NULL DEFAULT 0,
    reserved_delta NUMERIC(20,8) NOT NULL DEFAULT 0,
    available_balance_after NUMERIC(20,8) NOT NULL,
    reserved_balance_after NUMERIC(20,8) NOT NULL,
    idempotency_key VARCHAR(150) NOT NULL UNIQUE,
    description TEXT NULL,
    correlation_id UUID NOT NULL,
    performed_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_credit_ledger_entry_type CHECK (entry_type IN (
        'ALLOCATION', 'RESERVE', 'CONSUME', 'RELEASE',
        'ADJUSTMENT', 'REFUND', 'EXPIRY'
    )),
    CONSTRAINT ck_credit_ledger_amount CHECK (amount > 0),
    CONSTRAINT ck_credit_ledger_balances CHECK (
        available_balance_after >= 0 AND reserved_balance_after >= 0
    )
);

CREATE TRIGGER trg_credit_ledger_append_only
BEFORE UPDATE OR DELETE ON credit_ledger_entry
FOR EACH ROW EXECUTE FUNCTION prevent_append_only_mutation();

-- A NULL department means the organization-wide policy. One active policy is
-- allowed at each scope. The backend performs the hard stop at 100 percent.
CREATE TABLE quota_policy (
    quota_policy_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    scope_type VARCHAR(20) NOT NULL,
    department_id UUID NULL REFERENCES department(department_id),
    credit_limit NUMERIC(20,8) NOT NULL,
    period_type VARCHAR(20) NOT NULL DEFAULT 'MONTHLY',
    warning_threshold_percent NUMERIC(5,2) NOT NULL DEFAULT 80.00,
    critical_threshold_percent NUMERIC(5,2) NOT NULL DEFAULT 95.00,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    effective_from TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    effective_to TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    updated_by_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    CONSTRAINT ck_quota_policy_scope CHECK (
        (scope_type = 'ORGANIZATION' AND department_id IS NULL)
        OR
        (scope_type = 'DEPARTMENT' AND department_id IS NOT NULL)
    ),
    CONSTRAINT ck_quota_policy_limit CHECK (credit_limit >= 0),
    CONSTRAINT ck_quota_policy_period
        CHECK (period_type IN ('DAILY', 'WEEKLY', 'MONTHLY')),
    CONSTRAINT ck_quota_policy_thresholds CHECK (
        warning_threshold_percent > 0
        AND warning_threshold_percent < critical_threshold_percent
        AND critical_threshold_percent <= 100
    ),
    CONSTRAINT ck_quota_policy_effective_period
        CHECK (effective_to IS NULL OR effective_to > effective_from)
);

CREATE UNIQUE INDEX ux_one_active_organization_quota
    ON quota_policy ((1))
    WHERE scope_type = 'ORGANIZATION' AND is_active = TRUE;

CREATE UNIQUE INDEX ux_one_active_department_quota
    ON quota_policy (department_id)
    WHERE scope_type = 'DEPARTMENT' AND is_active = TRUE;

CREATE TABLE additional_credit_request (
    additional_credit_request_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    department_id UUID NOT NULL REFERENCES department(department_id),
    requested_by_user_id UUID NOT NULL REFERENCES app_user(user_id),
    requested_credits NUMERIC(20,8) NOT NULL,
    reason TEXT NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'PENDING',
    reviewed_by_user_id UUID NULL REFERENCES app_user(user_id),
    reviewed_at TIMESTAMPTZ NULL,
    decision_note TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_additional_credit_amount CHECK (requested_credits > 0),
    CONSTRAINT ck_additional_credit_status
        CHECK (status IN ('PENDING', 'APPROVED', 'REJECTED', 'CANCELLED')),
    CONSTRAINT ck_additional_credit_review CHECK (
        (status = 'PENDING' AND reviewed_at IS NULL)
        OR
        (status = 'CANCELLED')
        OR
        (status IN ('APPROVED', 'REJECTED')
            AND reviewed_by_user_id IS NOT NULL
            AND reviewed_at IS NOT NULL)
    )
);

-- =========================================================
-- 6. Notifications, technical logs, and immutable audit trail
-- =========================================================

CREATE TABLE notification (
    notification_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_user(user_id),
    notification_type VARCHAR(50) NOT NULL,
    title VARCHAR(255) NOT NULL,
    message TEXT NOT NULL,
    data JSONB NOT NULL DEFAULT '{}'::jsonb,
    deduplication_key VARCHAR(180) NULL UNIQUE,
    read_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_notification_data_object CHECK (jsonb_typeof(data) = 'object'),
    CONSTRAINT ck_notification_read_time
        CHECK (read_at IS NULL OR read_at >= created_at)
);

CREATE TABLE system_log (
    system_log_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_code VARCHAR(100) NOT NULL,
    category VARCHAR(50) NOT NULL,
    severity VARCHAR(20) NOT NULL,
    source_service VARCHAR(100) NOT NULL,
    message TEXT NOT NULL,
    actor_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    request_id UUID NULL REFERENCES ai_request(request_id),
    correlation_id UUID NOT NULL,
    error_code VARCHAR(100) NULL,
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_system_log_severity
        CHECK (severity IN ('DEBUG', 'INFO', 'WARNING', 'ERROR', 'CRITICAL')),
    CONSTRAINT ck_system_log_metadata_object CHECK (jsonb_typeof(metadata) = 'object')
);

-- ACTOR_ROLE_CODE is stored as a snapshot, because a user's role can change.
-- ACTOR_USER_ID is nullable so background/system actions can also be audited.
CREATE TABLE audit_log (
    audit_log_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    actor_user_id UUID NULL REFERENCES app_user(user_id) ON DELETE SET NULL,
    actor_role_code VARCHAR(30) NULL,
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
    CONSTRAINT ck_audit_actor_role CHECK (
        actor_role_code IS NULL
        OR actor_role_code IN ('USER', 'ADMIN', 'SUPER_ADMIN', 'SYSTEM')
    ),
    CONSTRAINT ck_audit_outcome CHECK (outcome IN ('SUCCESS', 'FAILURE', 'DENIED')),
    CONSTRAINT ck_audit_old_values_object
        CHECK (old_values IS NULL OR jsonb_typeof(old_values) = 'object'),
    CONSTRAINT ck_audit_new_values_object
        CHECK (new_values IS NULL OR jsonb_typeof(new_values) = 'object')
);

CREATE TRIGGER trg_audit_log_append_only
BEFORE UPDATE OR DELETE ON audit_log
FOR EACH ROW EXECUTE FUNCTION prevent_append_only_mutation();

-- =========================================================
-- 7. Indexes for common API and reporting queries
-- =========================================================

CREATE INDEX ix_app_user_department_status
    ON app_user (department_id, status)
    WHERE deleted_at IS NULL;

CREATE INDEX ix_refresh_token_user_family
    ON refresh_token (user_id, family_id);

CREATE INDEX ix_refresh_token_expiry
    ON refresh_token (expires_at)
    WHERE revoked_at IS NULL;

CREATE INDEX ix_verification_user_purpose_expiry
    ON verification_code (user_id, purpose, expires_at DESC);

CREATE INDEX ix_api_key_user_status
    ON api_key (user_id, status);

CREATE INDEX ix_ai_model_provider_status
    ON ai_model (provider_id, status);

CREATE INDEX ix_department_model_enabled
    ON department_model (department_id, is_enabled);

CREATE INDEX ix_conversation_user_updated
    ON conversation (user_id, updated_at DESC)
    WHERE deleted_at IS NULL;

CREATE INDEX ix_conversation_department_created
    ON conversation (department_id, created_at DESC);

CREATE INDEX ix_ai_request_conversation_requested
    ON ai_request (conversation_id, requested_at DESC);

CREATE INDEX ix_ai_request_status_requested
    ON ai_request (status, requested_at);

CREATE INDEX ix_routing_attempt_request
    ON request_routing_attempt (request_id, attempt_number);

CREATE INDEX ix_usage_record_recorded
    ON usage_record (recorded_at DESC);

CREATE INDEX ix_credit_ledger_account_created
    ON credit_ledger_entry (credit_account_id, created_at DESC);

CREATE INDEX ix_credit_ledger_request
    ON credit_ledger_entry (request_id)
    WHERE request_id IS NOT NULL;

CREATE INDEX ix_additional_credit_status_created
    ON additional_credit_request (status, created_at);

CREATE INDEX ix_notification_user_unread
    ON notification (user_id, created_at DESC)
    WHERE read_at IS NULL;

CREATE INDEX ix_system_log_occurred
    ON system_log (occurred_at DESC);

CREATE INDEX ix_system_log_correlation
    ON system_log (correlation_id);

CREATE INDEX ix_audit_actor_occurred
    ON audit_log (actor_user_id, occurred_at DESC);

CREATE INDEX ix_audit_target
    ON audit_log (target_type, target_id);

CREATE INDEX ix_audit_correlation
    ON audit_log (correlation_id);

-- =========================================================
-- 8. Automatic UPDATED_AT triggers
-- =========================================================

CREATE TRIGGER trg_organization_profile_updated_at
BEFORE UPDATE ON organization_profile
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_role_updated_at
BEFORE UPDATE ON role
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_app_user_updated_at
BEFORE UPDATE ON app_user
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_department_updated_at
BEFORE UPDATE ON department
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_approved_email_domain_updated_at
BEFORE UPDATE ON approved_email_domain
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_platform_setting_updated_at
BEFORE UPDATE ON platform_setting
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_ai_provider_updated_at
BEFORE UPDATE ON ai_provider
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_ai_model_updated_at
BEFORE UPDATE ON ai_model
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_department_model_updated_at
BEFORE UPDATE ON department_model
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_model_selection_policy_updated_at
BEFORE UPDATE ON model_selection_policy
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_conversation_updated_at
BEFORE UPDATE ON conversation
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_ai_request_updated_at
BEFORE UPDATE ON ai_request
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_credit_account_updated_at
BEFORE UPDATE ON credit_account
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_quota_policy_updated_at
BEFORE UPDATE ON quota_policy
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TRIGGER trg_additional_credit_request_updated_at
BEFORE UPDATE ON additional_credit_request
FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- =========================================================
-- 9. Safe initial lookup/configuration data
-- =========================================================

INSERT INTO organization_profile (organization_id, organization_name)
VALUES (1, 'Your Organization')
ON CONFLICT (organization_id) DO NOTHING;

INSERT INTO role (role_code, display_name, description, permissions)
VALUES
(
    'SUPER_ADMIN',
    'Super Admin',
    'Technical platform administrator.',
    '{
      "scope": "platform",
      "providers": ["manage"],
      "models": ["manage"],
      "pricing": ["manage"],
      "platform_settings": ["manage"],
      "system_logs": ["read", "export"],
      "audit_logs": ["read", "export"],
      "service_health": ["manage"]
    }'::jsonb
),
(
    'ADMIN',
    'Admin',
    'The single organization-wide manager.',
    '{
      "scope": "organization",
      "departments": ["create", "read", "update", "archive"],
      "users": ["create", "read", "update", "suspend"],
      "department_models": ["manage"],
      "quotas": ["manage"],
      "credit_accounts": ["read", "allocate", "adjust"],
      "credit_requests": ["read", "approve", "reject"],
      "usage": ["read_all", "export"],
      "notifications": ["create", "read_all"],
      "audit_logs": ["read"]
    }'::jsonb
),
(
    'USER',
    'User',
    'Standard employee account assigned to one department.',
    '{
      "scope": "self",
      "ai_requests": ["create"],
      "conversations": ["create", "read_own", "update_own", "delete_own"],
      "models": ["use_department_enabled"],
      "usage": ["read_own"],
      "quota": ["read_department"],
      "credit_requests": ["create", "read_own"],
      "api_keys": ["create_own", "read_own", "revoke_own"],
      "notifications": ["read_own", "mark_read"],
      "profile": ["read_own", "update_own"]
    }'::jsonb
)
ON CONFLICT (role_code) DO UPDATE
SET display_name = EXCLUDED.display_name,
    description = EXCLUDED.description,
    permissions = EXCLUDED.permissions,
    updated_at = NOW();

-- The roles above are safe static seed data. The actual ADMIN and SUPER_ADMIN
-- accounts are created by the .NET bootstrap service after the schema exists.
-- Their emails/passwords come from User Secrets or environment variables,
-- PasswordHasher<AppUser> creates their hashes, and both accounts are inserted
-- with ACTIVE status, verified email, no department, and must_change_password
-- enabled. Do not insert plaintext or manually generated password hashes here.

COMMIT;

-- =========================================================
-- Post-run verification queries
-- =========================================================

SELECT COUNT(*) AS created_table_count
FROM information_schema.tables
WHERE table_schema = 'ai_orchestration'
  AND table_type = 'BASE TABLE';

SELECT role_code, display_name, permissions
FROM ai_orchestration.role
ORDER BY role_code;

SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'ai_orchestration'
  AND table_type = 'BASE TABLE'
ORDER BY table_name;

-- Future RAG migration (not part of this release):
--   CREATE EXTENSION IF NOT EXISTS vector;
--   Add knowledge_document, document_chunk, ingestion_job, and embedding tables.


-- =========================================================
-- Migration: Add user preferences
-- =========================================================

BEGIN;

SET search_path TO ai_orchestration, public;

CREATE TABLE user_preference (
    preference_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    user_id UUID NOT NULL
        REFERENCES app_user(user_id)
        ON DELETE CASCADE,

    preference_key VARCHAR(100) NOT NULL,

    preference_value JSONB NOT NULL,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_user_preference_key
        UNIQUE (user_id, preference_key)
);

-- Automatically update updated_at when a preference changes.
CREATE TRIGGER trg_user_preference_updated_at
BEFORE UPDATE ON user_preference
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();

COMMIT;
