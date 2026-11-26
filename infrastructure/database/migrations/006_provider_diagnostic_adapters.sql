-- Preserve the embedding behavior of records created before adapters were configurable.
UPDATE ai_orchestration.ai_provider
SET configuration = configuration || jsonb_build_object(
    'adapter', CASE WHEN rtrim(base_url, '/') = 'https://integrate.api.nvidia.com/v1'
        THEN 'nvidia' ELSE 'openai-compatible' END)
WHERE NOT (configuration ? 'adapter');
