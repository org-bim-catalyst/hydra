# Ask Lucy

An enterprise AI Workspace. This repository is mid-migration from a legacy .NET 7
ASP.NET Core MVC application ("ChatGPT Client") to the Clean Architecture / CQRS / React
stack defined in [`.specify/memory/constitution.md`](.specify/memory/constitution.md)
and [`docs/`](docs/) — see
[`specs/000-legacy-modernization/spec.md`](specs/000-legacy-modernization/spec.md) for
the full migration specification.

## Solution structure

```text
Ask Lucy.sln              # legacy solution — kept building/deployable until parity is confirmed
Ask Lucy/                 # legacy ASP.NET Core MVC project (.NET 7) — do not add new features here

src/
├── AskLucy.Domain/           # entities, value objects — no external dependencies
├── AskLucy.Application/      # CQRS commands/queries/handlers, interfaces (MediatR, FluentValidation)
├── AskLucy.Infrastructure/   # IAIProvider→OpenAIProvider, JWT, file storage, email
├── AskLucy.Persistence/      # EF Core DbContext, ASP.NET Identity, repositories
└── AskLucy.WebAPI/           # controllers, JWT auth, rate limiting, Problem Details, OpenAPI

tests/
├── AskLucy.Domain.Tests/
├── AskLucy.Application.Tests/
├── AskLucy.Infrastructure.Tests/
├── AskLucy.Persistence.Tests/
├── AskLucy.WebAPI.Tests/
└── AskLucy.E2E.Tests/         # Playwright — requires a live deployment, see its own header comment

frontend/                  # React 19 + TypeScript + Vite + Material UI
```

## Running locally

### Backend

```sh
dotnet user-secrets set "ConnectionStrings:ChatGPT_ClientContextConnection" "<your SQL Server connection string>" --project src/AskLucy.WebAPI
dotnet user-secrets set "Jwt:SigningKey" "<a random 32+ character string>" --project src/AskLucy.WebAPI
dotnet user-secrets set "OpenAI:ApiKey" "<your OpenAI API key>" --project src/AskLucy.WebAPI
dotnet user-secrets set "SendGrid:ApiKey" "<your SendGrid API key>" --project src/AskLucy.WebAPI

dotnet build "Ask Lucy.sln"
dotnet run --project src/AskLucy.WebAPI
```

Never put real secrets in `appsettings.json` — see the `_comment_secrets` note in
`src/AskLucy.WebAPI/appsettings.json` and constitution §8.

### Frontend

```sh
cd frontend
npm install
npm run dev
```

### Tests

```sh
dotnet test                          # all backend test projects
cd frontend && npm run test          # frontend unit tests
```

## Documentation

- [`.specify/memory/constitution.md`](.specify/memory/constitution.md) — the project's
  engineering constitution; supersedes all other guidance.
- [`docs/`](docs/) — target architecture, database, API, security, testing, and design
  system standards.
- [`docs/adr/`](docs/adr/) — Architecture Decision Records.
- [`specs/`](specs/) — feature specifications, plans, and tasks (Spec Kit workflow).
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — branch protection and required CI secrets.
