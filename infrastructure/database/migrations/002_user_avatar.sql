BEGIN;

ALTER TABLE ai_orchestration.app_user
    ADD COLUMN IF NOT EXISTS avatar_data bytea;

COMMIT;
