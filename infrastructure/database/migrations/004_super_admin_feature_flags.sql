BEGIN;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_list_feature_flags(IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
BEGIN
    OPEN p_rows FOR
        SELECT substr(s.setting_key, length('feature_flag.') + 1) AS feature_key,
               s.setting_value = 'true'::jsonb AS enabled,
               s.description,
               s.updated_at
        FROM ai_orchestration.platform_setting AS s
        WHERE starts_with(s.setting_key, 'feature_flag.')
          AND s.setting_key <> 'feature_flag.'
          AND NOT s.is_sensitive
          AND jsonb_typeof(s.setting_value) = 'boolean'
        ORDER BY s.setting_key;
END;
$$;

COMMIT;
