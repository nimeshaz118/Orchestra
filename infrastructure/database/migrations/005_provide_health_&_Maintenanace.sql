BEGIN;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM ai_orchestration.platform_setting WHERE setting_key = 'system.maintenance'
               AND description IS DISTINCT FROM 'Internal maintenance state') THEN
        RAISE EXCEPTION 'The system.maintenance setting key is already in use';
    END IF;
END;
$$;

INSERT INTO ai_orchestration.platform_setting (setting_key, setting_value, description, is_sensitive)
VALUES ('system.maintenance', '{}'::JSONB, 'Internal maintenance state', TRUE)
ON CONFLICT (setting_key) DO NOTHING;

CREATE INDEX IF NOT EXISTS ix_audit_maintenance_start_key
    ON ai_orchestration.audit_log (actor_user_id, (new_values->>'startKeyHash'))
    WHERE target_type = 'maintenance_session' AND action IN ('MAINTENANCE_STARTED', 'MAINTENANCE_START_IMPORTED')
      AND new_values->>'startKeyHash' IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_system_log_provider_health
    ON ai_orchestration.system_log ((metadata->>'providerId'), occurred_at DESC, system_log_id DESC)
    WHERE event_code = 'PROVIDER_HEALTH_CHECK';

CREATE OR REPLACE PROCEDURE ai_orchestration.api_start_maintenance(
    IN p_reason TEXT, IN p_public_message TEXT, IN p_scope TEXT, IN p_policy TEXT,
    IN p_start_key TEXT, IN p_affected_count INTEGER, IN p_actor UUID, IN p_correlation_id UUID,
    IN p_started_at TIMESTAMPTZ, IN p_expected_end_at TIMESTAMPTZ, IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
DECLARE
    v_state JSONB;
    v_result JSONB;
    v_saved_reason TEXT;
    v_outcome TEXT;
BEGIN
    SELECT setting_value INTO STRICT v_state FROM ai_orchestration.platform_setting
    WHERE setting_key = 'system.maintenance' FOR UPDATE;

    IF p_start_key IS NOT NULL THEN
        SELECT new_values, reason INTO v_result, v_saved_reason FROM ai_orchestration.audit_log
        WHERE target_type = 'maintenance_session' AND action IN ('MAINTENANCE_STARTED', 'MAINTENANCE_START_IMPORTED')
          AND actor_user_id = p_actor AND new_values->>'startKeyHash' = p_start_key;
    END IF;

    IF v_result IS NOT NULL THEN
        IF ROW(v_saved_reason, v_result->>'publicMessage', v_result->>'scope', v_result->>'activeRequestPolicy',
               (v_result->>'expectedEndAt')::TIMESTAMPTZ)
            IS DISTINCT FROM ROW(p_reason, p_public_message, p_scope, p_policy, p_expected_end_at) THEN
            v_outcome := 'idempotency_conflict';
        ELSE
            v_outcome := 'replayed';
        END IF;
    ELSIF v_state->>'maintenanceId' IS NOT NULL AND v_state->>'endedAt' IS NULL THEN
        v_outcome := 'already_active';
    ELSE
        v_result := jsonb_build_object(
            'maintenanceId', gen_random_uuid(), 'active', TRUE, 'scope', p_scope,
            'publicMessage', p_public_message, 'activeRequestPolicy', p_policy, 'startedBy', p_actor,
            'startedAt', p_started_at, 'expectedEndAt', p_expected_end_at,
            'affectedActiveRequestCount', p_affected_count);
        UPDATE ai_orchestration.platform_setting
        SET setting_value = v_result, updated_by_user_id = p_actor
        WHERE setting_key = 'system.maintenance';

        INSERT INTO ai_orchestration.audit_log (
            actor_user_id, actor_role_code, action, target_type, target_id,
            outcome, reason, correlation_id, occurred_at, new_values)
        VALUES (
            p_actor, 'SUPER_ADMIN', 'MAINTENANCE_STARTED', 'maintenance_session',
            (v_result->>'maintenanceId')::UUID, 'SUCCESS', p_reason, p_correlation_id, p_started_at,
            v_result || jsonb_build_object('startKeyHash', p_start_key));
        v_outcome := 'started';
    END IF;

    OPEN p_rows FOR
        SELECT v_outcome, (v_result->>'maintenanceId')::UUID, v_result->>'scope', v_result->>'publicMessage',
               (v_result->>'startedBy')::UUID, (v_result->>'startedAt')::TIMESTAMPTZ,
               (v_result->>'expectedEndAt')::TIMESTAMPTZ, (v_result->>'affectedActiveRequestCount')::INTEGER;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_end_maintenance(
    IN p_maintenance_id UUID, IN p_summary TEXT, IN p_actor UUID,
    IN p_correlation_id UUID, IN p_ended_at TIMESTAMPTZ, IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
DECLARE
    v_state JSONB;
BEGIN
    UPDATE ai_orchestration.platform_setting
    SET setting_value = setting_value || jsonb_build_object('active', FALSE, 'endedAt', p_ended_at),
        updated_by_user_id = p_actor
    WHERE setting_key = 'system.maintenance' AND setting_value->>'endedAt' IS NULL
      AND (setting_value->>'maintenanceId')::UUID = p_maintenance_id
    RETURNING setting_value INTO v_state;

    IF FOUND THEN
        INSERT INTO ai_orchestration.audit_log (
            actor_user_id, actor_role_code, action, target_type, target_id,
            outcome, reason, correlation_id, occurred_at, new_values)
        VALUES (
            p_actor, 'SUPER_ADMIN', 'MAINTENANCE_ENDED', 'maintenance_session', p_maintenance_id,
            'SUCCESS', p_summary, p_correlation_id, p_ended_at, v_state);
    END IF;

    OPEN p_rows FOR SELECT p_maintenance_id WHERE v_state IS NOT NULL;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_get_maintenance_state(
    IN p_active_only BOOLEAN, IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR
        SELECT (setting_value->>'maintenanceId')::UUID, (setting_value->>'endedAt')::TIMESTAMPTZ,
               setting_value->>'scope', setting_value->>'publicMessage',
               (setting_value->>'startedAt')::TIMESTAMPTZ
        FROM ai_orchestration.platform_setting
        WHERE setting_key = 'system.maintenance' AND setting_value->>'maintenanceId' IS NOT NULL
          AND (NOT p_active_only OR setting_value->>'endedAt' IS NULL);
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_get_provider_health(
    IN p_provider_id UUID, IN p_as_of TIMESTAMPTZ, IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR
        WITH checks AS NOT MATERIALIZED (
            SELECT system_log_id, occurred_at, (metadata->>'providerSucceeded')::BOOLEAN AS succeeded,
                   (metadata->>'latencyMs')::BIGINT AS latency_ms, error_code
            FROM ai_orchestration.system_log
            WHERE event_code = 'PROVIDER_HEALTH_CHECK' AND metadata->>'providerId' = p_provider_id::TEXT
              AND occurred_at <= p_as_of
        ), recent AS (
            SELECT * FROM checks WHERE occurred_at >= p_as_of - INTERVAL '24 hours'
        ), metrics AS (
            SELECT COUNT(*) FILTER (WHERE succeeded IS TRUE) AS successful_checks,
                   COUNT(*) FILTER (WHERE succeeded IS FALSE) AS failed_checks,
                   AVG(latency_ms::DOUBLE PRECISION) AS average_latency_ms
            FROM recent
        )
        SELECT
            (SELECT MAX(occurred_at) FROM checks),
            (SELECT MAX(occurred_at) FROM checks WHERE succeeded IS TRUE),
            (SELECT succeeded FROM recent WHERE succeeded IS NOT NULL
             ORDER BY occurred_at DESC, system_log_id DESC LIMIT 1),
            metrics.successful_checks, metrics.failed_checks, metrics.average_latency_ms,
            ARRAY(SELECT error_code::TEXT FROM recent WHERE error_code IS NOT NULL
                  ORDER BY occurred_at DESC, system_log_id DESC LIMIT 20)
        FROM metrics
        WHERE EXISTS (SELECT 1 FROM ai_orchestration.ai_provider WHERE provider_id = p_provider_id);
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_record_provider_health_check(
    IN p_provider_id UUID, IN p_checked_at TIMESTAMPTZ, IN p_provider_succeeded BOOLEAN,
    IN p_latency_ms BIGINT, IN p_safe_error_category TEXT)
LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO ai_orchestration.system_log (
        event_code, category, severity, source_service, message, correlation_id, error_code, metadata, occurred_at)
    VALUES ('PROVIDER_HEALTH_CHECK', 'ai_diagnostics', CASE WHEN p_provider_succeeded IS TRUE THEN 'INFO' ELSE 'WARNING' END,
        'orchestra-api', 'Provider diagnostic completed.', gen_random_uuid(), p_safe_error_category,
        jsonb_build_object('providerId', p_provider_id, 'providerSucceeded', p_provider_succeeded, 'latencyMs', p_latency_ms),
        p_checked_at);
END;
$$;

-- Generic settings procedures leave the internal maintenance row to the maintenance commands.
CREATE OR REPLACE PROCEDURE ai_orchestration.api_list_platform_settings(IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR SELECT setting_key, setting_value::TEXT, description, is_sensitive, is_applied
    FROM ai_orchestration.platform_setting WHERE setting_key <> 'system.maintenance' ORDER BY setting_key;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_update_platform_setting(
    IN p_setting_key VARCHAR(120), IN p_setting_value JSONB, IN p_update_value BOOLEAN,
    IN p_description TEXT, IN p_update_description BOOLEAN, IN p_is_sensitive BOOLEAN,
    IN p_update_sensitivity BOOLEAN, IN p_actor UUID, IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
DECLARE
    v_key TEXT;
BEGIN
    UPDATE ai_orchestration.platform_setting
    SET setting_value = CASE WHEN p_update_value THEN p_setting_value ELSE setting_value END,
        description = CASE WHEN p_update_description THEN p_description ELSE description END,
        is_sensitive = CASE WHEN p_update_sensitivity THEN p_is_sensitive ELSE is_sensitive END,
        updated_by_user_id = p_actor
    WHERE setting_key = p_setting_key AND setting_key <> 'system.maintenance'
    RETURNING setting_key INTO v_key;

    OPEN p_rows FOR SELECT setting_key, setting_value::TEXT, description, is_sensitive, is_applied
    FROM ai_orchestration.platform_setting WHERE setting_key = v_key;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_set_platform_setting_applied(
    IN p_setting_key VARCHAR(120), IN p_is_applied BOOLEAN, IN p_actor UUID, IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
DECLARE
    v_key TEXT;
BEGIN
    UPDATE ai_orchestration.platform_setting
    SET is_applied = p_is_applied, updated_by_user_id = p_actor
    WHERE setting_key = p_setting_key AND setting_key <> 'system.maintenance'
    RETURNING setting_key INTO v_key;

    OPEN p_rows FOR SELECT setting_key, setting_value::TEXT, description, is_sensitive, is_applied
    FROM ai_orchestration.platform_setting WHERE setting_key = v_key;
END;
$$;

-- Move data for databases that received the earlier dedicated-table migrations.
DO $$
BEGIN
    IF to_regclass('ai_orchestration.maintenance_session') IS NOT NULL THEN
        LOCK TABLE ai_orchestration.maintenance_session IN ACCESS EXCLUSIVE MODE;

        IF EXISTS (SELECT 1 FROM ai_orchestration.maintenance_session) AND EXISTS (
            SELECT 1 FROM ai_orchestration.platform_setting
            WHERE setting_key = 'system.maintenance' AND setting_value <> '{}'::JSONB) THEN
            RAISE EXCEPTION 'Maintenance state exists in both storage locations';
        END IF;

        UPDATE ai_orchestration.platform_setting
        SET setting_value = jsonb_build_object(
                'maintenanceId', session.maintenance_id, 'active', session.ended_at IS NULL, 'scope', session.scope,
                'publicMessage', session.public_message, 'activeRequestPolicy', session.active_request_policy,
                'startedAt', session.started_at, 'startedBy', session.started_by_user_id,
                'expectedEndAt', session.expected_end_at, 'endedAt', session.ended_at,
                'affectedActiveRequestCount', session.affected_active_request_count),
            updated_by_user_id = COALESCE(session.ended_by_user_id, session.started_by_user_id)
        FROM (SELECT * FROM ai_orchestration.maintenance_session
              ORDER BY (ended_at IS NOT NULL), COALESCE(ended_at, started_at) DESC LIMIT 1) AS session
        WHERE setting_key = 'system.maintenance';

        INSERT INTO ai_orchestration.audit_log (
            actor_user_id, actor_role_code, action, target_type, target_id,
            outcome, reason, correlation_id, occurred_at, new_values)
        SELECT started_by_user_id, 'SUPER_ADMIN', 'MAINTENANCE_START_IMPORTED', 'maintenance_session', maintenance_id,
            'SUCCESS', reason, gen_random_uuid(), started_at, jsonb_build_object(
                'maintenanceId', maintenance_id, 'active', TRUE, 'scope', scope, 'publicMessage', public_message,
                'activeRequestPolicy', active_request_policy, 'startedAt', started_at, 'startedBy', started_by_user_id,
                'expectedEndAt', expected_end_at, 'affectedActiveRequestCount', affected_active_request_count,
                'startKeyHash', encode(sha256(convert_to(idempotency_key, 'UTF8')), 'hex'))
        FROM ai_orchestration.maintenance_session WHERE idempotency_key IS NOT NULL;

        DROP TABLE ai_orchestration.maintenance_session;
    END IF;

    IF to_regclass('ai_orchestration.provider_health_check') IS NOT NULL THEN
        LOCK TABLE ai_orchestration.provider_health_check IN ACCESS EXCLUSIVE MODE;
        INSERT INTO ai_orchestration.system_log (
            system_log_id, event_code, category, severity, source_service, message,
            correlation_id, error_code, metadata, occurred_at)
        SELECT provider_health_check_id, 'PROVIDER_HEALTH_CHECK', 'ai_diagnostics',
            CASE WHEN provider_succeeded IS TRUE THEN 'INFO' ELSE 'WARNING' END,
            'orchestra-api', 'Provider diagnostic completed.', gen_random_uuid(), safe_error_category,
            jsonb_build_object('providerId', provider_id, 'providerSucceeded', provider_succeeded, 'latencyMs', latency_ms),
            checked_at
        FROM ai_orchestration.provider_health_check;

        DROP TABLE ai_orchestration.provider_health_check;
    END IF;
END;
$$;

COMMIT;
