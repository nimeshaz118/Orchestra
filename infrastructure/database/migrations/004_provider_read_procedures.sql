BEGIN;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_list_providers(
    IN p_rows REFCURSOR
)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR
        SELECT provider_id, provider_name, base_url,
               credential_reference, status, timeout_seconds,
               configuration, created_at, updated_at
        FROM ai_orchestration.ai_provider
        ORDER BY provider_name;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_get_provider(
    IN p_provider_id UUID,
    IN p_rows REFCURSOR
)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR
        SELECT provider_id, provider_name, base_url,
               credential_reference, status, timeout_seconds,
               configuration, created_at, updated_at
        FROM ai_orchestration.ai_provider
        WHERE provider_id = p_provider_id;
END;
$$;

COMMIT;