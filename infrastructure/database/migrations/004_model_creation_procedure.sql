BEGIN;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_create_model(
    IN p_provider_id UUID,
    IN p_provider_model_key VARCHAR(200),
    IN p_display_name VARCHAR(150),
    IN p_model_type VARCHAR(30),
    IN p_context_window_tokens INTEGER,
    IN p_quality_tier SMALLINT,
    IN p_capabilities JSONB,
    IN p_actor_user_id UUID,
    OUT p_model_id UUID)
LANGUAGE plpgsql AS $$
DECLARE
    actor_role VARCHAR(30);
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM ai_orchestration.ai_provider
        WHERE provider_id = p_provider_id
    ) THEN
        RAISE EXCEPTION 'Referenced provider does not exist'
            USING ERRCODE = 'P0003';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM ai_orchestration.ai_model
        WHERE provider_id = p_provider_id
          AND provider_model_key = p_provider_model_key
    ) THEN
        RAISE EXCEPTION 'Model already exists for provider'
            USING ERRCODE = 'P0002';
    END IF;

    SELECT role_code INTO actor_role
    FROM ai_orchestration.app_user
    WHERE user_id = p_actor_user_id;

    INSERT INTO ai_orchestration.ai_model (
        provider_id, provider_model_key, display_name, model_type, status,
        context_window_tokens, quality_tier, capabilities,
        created_by_user_id, updated_by_user_id
    )
    VALUES (
        p_provider_id, p_provider_model_key, p_display_name, p_model_type, 'ACTIVE',
        p_context_window_tokens, p_quality_tier, p_capabilities,
        p_actor_user_id, p_actor_user_id
    )
    RETURNING model_id INTO p_model_id;

    INSERT INTO ai_orchestration.audit_log (
        actor_user_id, actor_role_code, action, target_type, target_id,
        new_values, outcome, correlation_id
    )
    VALUES (
        p_actor_user_id, actor_role, 'MODEL_CREATED', 'AI_MODEL', p_model_id,
        jsonb_build_object(
            'provider_id', p_provider_id,
            'provider_model_key', p_provider_model_key,
            'display_name', p_display_name,
            'model_type', p_model_type,
            'status', 'ACTIVE',
            'context_window_tokens', p_context_window_tokens,
            'quality_tier', p_quality_tier,
            'capabilities', p_capabilities
        ),
        'SUCCESS', gen_random_uuid()
    );
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_get_model(
    IN p_model_id UUID, IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR
        SELECT m.model_id, m.provider_id, m.provider_model_key, m.display_name,
               m.model_type, m.status, m.context_window_tokens, m.quality_tier,
               m.capabilities, m.created_at, m.updated_at
        FROM ai_orchestration.ai_model AS m
        WHERE m.model_id = p_model_id;
END;
$$;

COMMIT;
