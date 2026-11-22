BEGIN;

DROP PROCEDURE IF EXISTS ai_orchestration.api_list_platform_setting_drafts(REFCURSOR);
DROP PROCEDURE IF EXISTS ai_orchestration.api_get_platform_setting_draft(UUID, REFCURSOR);
DROP PROCEDURE IF EXISTS ai_orchestration.api_create_platform_setting_draft(
    VARCHAR, JSONB, TEXT, BOOLEAN, UUID, REFCURSOR);
DROP PROCEDURE IF EXISTS ai_orchestration.api_create_platform_setting_update_draft(
    VARCHAR, JSONB, BOOLEAN, TEXT, BOOLEAN, BOOLEAN, BOOLEAN, UUID, REFCURSOR);
DROP PROCEDURE IF EXISTS ai_orchestration.api_review_platform_setting_draft(UUID, UUID, REFCURSOR);
DROP PROCEDURE IF EXISTS ai_orchestration.api_apply_platform_setting_draft(UUID, UUID, REFCURSOR);
DROP TABLE IF EXISTS ai_orchestration.platform_setting_draft;

ALTER TABLE ai_orchestration.platform_setting
    ADD COLUMN IF NOT EXISTS is_applied BOOLEAN NOT NULL DEFAULT TRUE;

ALTER TABLE ai_orchestration.platform_setting
    ALTER COLUMN is_applied SET DEFAULT FALSE;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_list_platform_settings(
    IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR
        SELECT setting_key, setting_value::TEXT, description, is_sensitive, is_applied
        FROM ai_orchestration.platform_setting
        ORDER BY setting_key;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_create_platform_setting(
    IN p_setting_key VARCHAR(120),
    IN p_setting_value JSONB,
    IN p_description TEXT,
    IN p_is_sensitive BOOLEAN,
    IN p_actor UUID,
    IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
DECLARE
    v_setting_key VARCHAR(120);
BEGIN
    INSERT INTO ai_orchestration.platform_setting (
        setting_key, setting_value, description, is_sensitive, updated_by_user_id)
    VALUES (
        p_setting_key, p_setting_value, p_description, p_is_sensitive, p_actor)
    ON CONFLICT (setting_key) DO NOTHING
    RETURNING setting_key INTO v_setting_key;

    IF FOUND THEN
        OPEN p_rows FOR
            SELECT setting_key, setting_value::TEXT, description, is_sensitive, is_applied
            FROM ai_orchestration.platform_setting
            WHERE setting_key = v_setting_key;
    ELSE
        OPEN p_rows FOR
            SELECT setting_key, setting_value::TEXT, description, is_sensitive, is_applied
            FROM ai_orchestration.platform_setting
            WHERE FALSE;
    END IF;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_update_platform_setting(
    IN p_setting_key VARCHAR(120),
    IN p_setting_value JSONB,
    IN p_update_value BOOLEAN,
    IN p_description TEXT,
    IN p_update_description BOOLEAN,
    IN p_is_sensitive BOOLEAN,
    IN p_update_sensitivity BOOLEAN,
    IN p_actor UUID,
    IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
DECLARE
    v_setting_key VARCHAR(120);
BEGIN
    UPDATE ai_orchestration.platform_setting AS setting
    SET setting_value = CASE
            WHEN p_update_value THEN p_setting_value
            ELSE setting.setting_value
        END,
        description = CASE
            WHEN p_update_description THEN p_description
            ELSE setting.description
        END,
        is_sensitive = CASE
            WHEN p_update_sensitivity THEN p_is_sensitive
            ELSE setting.is_sensitive
        END,
        updated_by_user_id = p_actor
    WHERE setting.setting_key = p_setting_key
    RETURNING setting_key INTO v_setting_key;

    IF FOUND THEN
        OPEN p_rows FOR
            SELECT setting_key, setting_value::TEXT, description, is_sensitive, is_applied
            FROM ai_orchestration.platform_setting
            WHERE setting_key = v_setting_key;
    ELSE
        OPEN p_rows FOR
            SELECT setting_key, setting_value::TEXT, description, is_sensitive, is_applied
            FROM ai_orchestration.platform_setting
            WHERE FALSE;
    END IF;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_set_platform_setting_applied(
    IN p_setting_key VARCHAR(120),
    IN p_is_applied BOOLEAN,
    IN p_actor UUID,
    IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
DECLARE
    v_setting_key VARCHAR(120);
BEGIN
    UPDATE ai_orchestration.platform_setting AS setting
    SET is_applied = p_is_applied,
        updated_by_user_id = p_actor
    WHERE setting.setting_key = p_setting_key
    RETURNING setting_key INTO v_setting_key;

    IF FOUND THEN
        OPEN p_rows FOR
            SELECT setting_key, setting_value::TEXT, description, is_sensitive, is_applied
            FROM ai_orchestration.platform_setting
            WHERE setting_key = v_setting_key;
    ELSE
        OPEN p_rows FOR
            SELECT setting_key, setting_value::TEXT, description, is_sensitive, is_applied
            FROM ai_orchestration.platform_setting
            WHERE FALSE;
    END IF;
END;
$$;

COMMIT;
