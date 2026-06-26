# DocuMind

DocuMind is a Retrieval-Augmented Generation (RAG) document Q&A application built on .NET 10 and Blazor Server. Users upload documents, the system indexes their content into a searchable knowledge base, and an LLM answers natural-language questions grounded in the retrieved passages — returning answers with citations back to the source material. This README is a placeholder and will be expanded with setup, configuration, and usage instructions as the project evolves.

## Solution structure

| Project | Type | Responsibility |
| --- | --- | --- |
| `DocuMind.Web` | ASP.NET Core + Blazor Server | Application host: UI, API endpoints, dependency injection, composition root. |
| `DocuMind.Core` | Class library | Domain models, interfaces, and service abstractions (no external dependencies). |
| `DocuMind.Infrastructure` | Class library | Implementation details: EF Core `DbContext`, Gemini access, concrete services. |

## Getting started

```bash
# Restore & build the whole solution
dotnet build DocuMind.sln

# Run the web app
dotnet run --project src/DocuMind.Web
```

## Database (PostgreSQL + pgvector via Docker)

The database runs in Docker — no manual PostgreSQL install required. It uses the
official `pgvector/pgvector:pg17` image and enables the `vector` extension on first
startup via `db/init/01-enable-pgvector.sql`.

| Setting | Value |
| --- | --- |
| Container | `documind-db` |
| Database | `documind` |
| User / password | `postgres` / `postgres` (local only) |
| Host port | `5432` |
| Connection string | `Host=localhost;Port=5432;Database=documind;Username=postgres;Password=postgres` |

### Start / stop

```bash
# Start the database in the background
docker compose up -d

# Follow logs (optional)
docker compose logs -f db

# Stop the container (keeps data)
docker compose down

# Stop AND delete all data (removes the named volume)
docker compose down -v
```

### Verify the pgvector extension is active

```bash
docker exec -it documind-db psql -U postgres -d documind -c "SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';"
```

You should see a row for `vector` with its version. Data persists across restarts in
the named volume `documind-db-data`.
