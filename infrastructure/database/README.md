# Database schema

Apply these scripts in order to a new PostgreSQL database before running the API:

1. [`001_initial_schema.sql`](migrations/001_initial_schema.sql) creates the approved baseline.
2. [`002_user_avatar.sql`](migrations/002_user_avatar.sql) ensures the avatar column exists.
3. [`003_api_stored_procedures.sql`](migrations/003_api_stored_procedures.sql) installs the API procedures and creates the preferences table if it is missing.
4. [`004_model_creation_procedure.sql`](migrations/004_model_creation_procedure.sql) installs model creation and model read procedures.

Docker Compose runs these scripts when it initializes an empty volume. For an existing database or Docker volume, apply any missing scripts in that order using pgAdmin or `psql` before starting the updated API. `0002_schema.sql` is an alternate full schema script; it is not part of this sequence.

Add later schema changes as reviewed, numbered SQL files such as `002_change_name.sql`. Apply them in order to each developer's database, then re-scaffold the EF Core context and entities. Keep custom mappings in separate partial files, including `src/backend/Orchestra.Api/Data/OrchestraDbContext.AuthMappings.cs`.

The removed `Database-initial-schema.sql` was an older design and must not be used for new databases.

## Avatar storage

After the baseline, apply `migrations/002_user_avatar.sql` once in pgAdmin. It adds a nullable avatar column and stores the PNG in `ai_orchestration.app_user.avatar_data` as PostgreSQL `bytea`. Apply this script to existing databases before running the updated avatar endpoints. Re-upload older local-file avatars; this migration does not import disk files.

If your database still has the earlier `user_avatar` table, rerun `002_user_avatar.sql`. It fills empty `app_user.avatar_data` values from the legacy table, preserves any images already in `app_user`, and removes the legacy table in the same transaction.

## API procedures

The public and privileged department reads, admin domain list, current profile and session reads, avatar and preference reads, and password change use procedures in `ai_orchestration`. The API batches each read `CALL` with its cursor fetch so both run in the same transaction. The password change receives a new hash from the API and updates the account and revokes its refresh tokens in a single `CALL`.
