BEGIN;

ALTER TABLE ai_orchestration.app_user
    ADD COLUMN IF NOT EXISTS avatar_data bytea;

DO $$
BEGIN
    IF to_regclass('ai_orchestration.user_avatar') IS NOT NULL THEN
        LOCK TABLE ai_orchestration.user_avatar IN ACCESS EXCLUSIVE MODE;

        UPDATE ai_orchestration.app_user AS u
        SET avatar_data = a.image_data
        FROM ai_orchestration.user_avatar AS a
        WHERE a.user_id = u.user_id AND u.avatar_data IS NULL;

        DROP TABLE ai_orchestration.user_avatar;
    END IF;
END;
$$;

COMMIT;
