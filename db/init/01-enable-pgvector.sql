-- Runs automatically the first time the container is created (empty data dir).
-- Enables the pgvector extension in the `documind` database.
CREATE EXTENSION IF NOT EXISTS vector;
