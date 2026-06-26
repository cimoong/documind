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
