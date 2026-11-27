BEGIN;

-- The approved baseline predates preferences. Keep existing preference rows on upgrades.
CREATE TABLE IF NOT EXISTS ai_orchestration.user_preference (
    preference_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES ai_orchestration.app_user(user_id) ON DELETE CASCADE,
    preference_key VARCHAR(100) NOT NULL,
    preference_value JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_user_preference_key UNIQUE (user_id, preference_key)
);

CREATE OR REPLACE TRIGGER trg_user_preference_updated_at
BEFORE UPDATE ON ai_orchestration.user_preference
FOR EACH ROW EXECUTE FUNCTION ai_orchestration.set_updated_at();

CREATE OR REPLACE PROCEDURE ai_orchestration.api_list_departments(
    IN p_active_only BOOLEAN, IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR
        SELECT d.department_id, d.department_name, d.description, d.status
        FROM ai_orchestration.department AS d
        WHERE NOT p_active_only OR d.status = 'ACTIVE'
        ORDER BY d.department_name;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_get_department(
    IN p_department_id UUID, IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR
        SELECT d.department_id, d.department_name, d.description, d.status
        FROM ai_orchestration.department AS d
        WHERE d.department_id = p_department_id;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_list_domains(IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR
        SELECT d.domain_id, d.domain_name, d.is_active, d.created_at, d.updated_at
        FROM ai_orchestration.approved_email_domain AS d
        ORDER BY d.domain_name;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_get_profile(
    IN p_user_id UUID, IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR
        SELECT u.user_id, u.email, u.first_name, u.last_name, u.display_name,
               u.phone_number, u.job_title, u.role_code, u.department_id, u.status,
               u.must_change_password, u.created_at, u.updated_at
        FROM ai_orchestration.app_user AS u
        WHERE u.user_id = p_user_id AND u.deleted_at IS NULL AND u.status = 'ACTIVE';
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_get_avatar(
    IN p_user_id UUID, IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR
        SELECT u.avatar_data
        FROM ai_orchestration.app_user AS u
        WHERE u.user_id = p_user_id AND u.status = 'ACTIVE'
            AND u.deleted_at IS NULL AND u.email_verified_at IS NOT NULL;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_list_preferences(
    IN p_user_id UUID, IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR
        SELECT p.preference_key, p.preference_value
        FROM ai_orchestration.user_preference AS p
        WHERE p.user_id = p_user_id;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_list_sessions(
    IN p_user_id UUID, IN p_token_hash TEXT, IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
BEGIN
    -- Return no rows if the supplied token isn't a live session for this user.
    OPEN p_rows FOR
        SELECT t.refresh_token_id, t.created_at, t.expires_at, t.last_used_at, t.user_agent,
               t.refresh_token_id = current_token.refresh_token_id AS is_current
        FROM ai_orchestration.refresh_token AS current_token
        JOIN ai_orchestration.refresh_token AS t ON t.user_id = current_token.user_id
        WHERE current_token.user_id = p_user_id
            AND current_token.token_hash = p_token_hash::CHAR(64)
            AND current_token.revoked_at IS NULL AND current_token.expires_at > statement_timestamp()
            AND t.revoked_at IS NULL AND t.expires_at > statement_timestamp()
        ORDER BY t.created_at DESC
        LIMIT 50;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_get_password_state(
    IN p_user_id UUID, IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR
        SELECT u.password_hash, u.security_stamp
        FROM ai_orchestration.app_user AS u
        WHERE u.user_id = p_user_id AND u.status = 'ACTIVE'
            AND u.deleted_at IS NULL AND u.email_verified_at IS NOT NULL;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_change_password(
    IN p_user_id UUID, IN p_expected_stamp UUID, IN p_password_hash TEXT,
    IN p_new_stamp UUID, OUT changed BOOLEAN)
LANGUAGE plpgsql AS $$
BEGIN
    -- A competing password or account change must not overwrite the state .NET verified.
    -- The update also locks the same user row as refresh rotation and logout.
    UPDATE ai_orchestration.app_user AS u
    SET password_hash = p_password_hash,
        must_change_password = FALSE,
        password_changed_at = clock_timestamp(),
        security_stamp = p_new_stamp
    WHERE u.user_id = p_user_id AND u.security_stamp = p_expected_stamp AND u.status = 'ACTIVE'
        AND u.deleted_at IS NULL AND u.email_verified_at IS NOT NULL;

    changed := FOUND;
    IF changed THEN
        UPDATE ai_orchestration.refresh_token AS t
        SET revoked_at = GREATEST(clock_timestamp(), t.created_at), revoked_reason = 'PASSWORD_CHANGED'
        WHERE t.user_id = p_user_id AND t.revoked_at IS NULL;
    END IF;
END;
$$;

COMMIT;
