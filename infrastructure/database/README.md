# Database schema

`migrations/001_initial_schema.sql` is the approved PostgreSQL baseline. Apply it once to a new local database before running the API. It creates the `ai_orchestration` schema and the tables used by the database-first EF Core model.

Add later schema changes as reviewed, numbered SQL files such as `002_change_name.sql`. Apply them in order to each developer's database, then re-scaffold the EF Core context and entities. Keep custom mappings in separate partial files, including `src/backend/Orchestra.Api/Data/OrchestraDbContext.AuthMappings.cs`.

The removed `Database-initial-schema.sql` was an older design and must not be used for new databases.

## Avatar storage

After the baseline, apply `migrations/002_user_avatar.sql` once in pgAdmin. It adds a nullable avatar column and stores the PNG in `ai_orchestration.app_user.avatar_data` as PostgreSQL `bytea`. Apply this script to existing databases before running the updated avatar endpoints. Re-upload older local-file avatars; this migration does not import disk files.

If the earlier `user_avatar` table was already created, rerun the revised `002_user_avatar.sql`: it copies existing images into `app_user.avatar_data` and removes the old table in one transaction.
