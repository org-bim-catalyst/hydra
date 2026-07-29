# Ask Lucy

An enterprise AI Workspace, built on the Clean Architecture / CQRS / React stack defined
in [`.specify/memory/constitution.md`](.specify/memory/constitution.md) and
[`docs/`](docs/). Migrated from a legacy .NET 7 ASP.NET Core MVC application
("ChatGPT Client") — see
[`specs/000-legacy-modernization/spec.md`](specs/000-legacy-modernization/spec.md) for
the migration history; the legacy project has been decommissioned.

## Solution structure

```text
Ask Lucy.sln

src/
├── AskLucy.Domain/           # entities, value objects — no external dependencies
├── AskLucy.Application/      # CQRS commands/queries/handlers, interfaces (MediatR, FluentValidation)
├── AskLucy.Infrastructure/   # IAIProvider→OpenAIProvider, JWT, file storage, email
├── AskLucy.Persistence/      # EF Core DbContext, ASP.NET Identity, repositories
└── AskLucy.Web/              # controllers, JWT auth, rate limiting, Problem Details, OpenAPI
    └── ClientApp/             # React 19 + TypeScript + Vite + Material UI — built into
                                # wwwroot on every build (see AskLucy.Web.csproj); served
                                # by the same process as the API

tests/
├── AskLucy.Domain.Tests/
├── AskLucy.Application.Tests/
├── AskLucy.Infrastructure.Tests/
├── AskLucy.Persistence.Tests/
├── AskLucy.Web.Tests/
└── AskLucy.E2E.Tests/         # Playwright — requires a live deployment, see its own header comment
```

## Running locally

### Backend

```sh
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your SQL Server connection string>" --project src/AskLucy.Web
dotnet user-secrets set "Jwt:SigningKey" "<a random 32+ character string>" --project src/AskLucy.Web
dotnet user-secrets set "OpenAI:ApiKey" "<your OpenAI API key>" --project src/AskLucy.Web
dotnet user-secrets set "Smtp:Username" "<your SMTP username, if the host requires auth>" --project src/AskLucy.Web
dotnet user-secrets set "Smtp:Password" "<your SMTP password, if the host requires auth>" --project src/AskLucy.Web

cd src/AskLucy.Web/ClientApp && npm install && cd ../../..   # once, before the first build/run
dotnet build "Ask Lucy.sln"
dotnet run --project src/AskLucy.Web
```

`AskLucy.Web.csproj` runs `npm run build` and copies `ClientApp`'s output into `wwwroot`
before every build (including hitting Run in Visual Studio) — one process serves both the
API and the SPA. This needs `npm install` to have been run in `ClientApp` at least once;
it does not run install/ci itself, to keep every build fast.

In the `Development` environment, email is never actually sent — `ConsoleEmailSender` logs it
instead, so the `Smtp:*` secrets above are only needed once you run outside `Development`.

Never put real secrets in `appsettings.json` — see the `_comment_secrets` note in
`src/AskLucy.Web/appsettings.json` and constitution §8.

### Frontend (active UI development)

The build above always reflects ClientApp as of its last `npm run build` — for hot-reload
during active frontend work, run the Vite dev server separately instead:

```sh
cd src/AskLucy.Web/ClientApp
npm install
npm run dev
```

### Tests

```sh
dotnet test                                          # all backend test projects
cd src/AskLucy.Web/ClientApp && npm run test         # frontend unit tests
```

## Documentation

- [`.specify/memory/constitution.md`](.specify/memory/constitution.md) — the project's
  engineering constitution; supersedes all other guidance.
- [`docs/`](docs/) — target architecture, database, API, security, testing, and design
  system standards.
- [`docs/adr/`](docs/adr/) — Architecture Decision Records.
- [`specs/`](specs/) — feature specifications, plans, and tasks (Spec Kit workflow).
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — branch protection and required CI secrets.
