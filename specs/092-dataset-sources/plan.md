# Implementation Plan: Open Data Source Evaluation

**Feature**: Open Data Source Evaluation (specs/092-dataset-sources)

**Created**: 2026-11-03

**Status**: Draft

**Tech Stack**:
- **Backend**: ASP.NET Core (.NET 10), C#, Clean Architecture, Entity Framework Core, MediatR (CQRS), FluentValidation, AutoMapper, Serilog, ASP.NET Identity, JWT Authentication
- **Frontend**: React, TypeScript, Vite, Material UI (MUI), React Router, TanStack Query, Zustand, React Hook Form, Zod, Axios
- **Database**: SQL Server, Code-First Migrations
- **Spatial/Mapping**: Google Maps 2D/3D viewer integration

**Architecture**:
- Clean Architecture with four concentric layers: Domain, Application, Infrastructure, Presentation/API
- Dependencies point inward only (Domain → nothing, Application → Domain, Infrastructure → Application/Domain, Api → Application/Infrastructure/Domain)
- All external dependencies hidden behind interfaces owned by Application/Domain layers
- CQRS: All behavior expressed as MediatR IRequest commands (state-changing) or queries (read-only)

**Feature Overview**:
- Evaluate 6 provided open data source URLs for relevance
- Categorize each source as Oman-specific, General, or Mixed relevance
- Present results in table format with source name, URL, relevance category, and key metadata
- Identify sources most relevant for Oman-focused queries vs general international coverage
- Collect and catalog relevant sources for future use in Spatial Request Bundle
- Prioritize datasets by recency and category match

**User Stories** (from spec.md):
- **US1** (P1): Dataset Source Evaluation - User provides 6 URLs, system evaluates and categorizes relevance
- **US2** (P2): Dataset Collection & Cataloging - User collects relevant sources into catalog for future use
- **US3** (P3): Latest Dataset Prioritization - System prioritizes most up-to-date datasets based on timestamps and category match

**Task Organization** (from tasks.md):
- Phase 1: Setup (3 tasks) - project initialization, configuration, API client setup
- Phase 2: Foundational (2 tasks) - relevance classifier, data source model entity
- Phase 3: User Story 1 (5 tasks) - URL fetch/parsing, relevance assessment, evaluation result DTO, UI display
- Phase 4: User Story 2 (3 tasks) - catalog storage, catalog display, metadata persistence
- Phase 5: User Story 3 (3 tasks) + Polish - prioritization engine, prioritization display, finalization

**Key Interfaces**:
- IDataSourceRepository (Application layer) - repository for source catalog entries
- IRelevanceClassifier (Application layer) - interface for source categorization logic
- IOpenDataClient (Infrastructure layer) - HTTP client for fetching source data

**Dependencies**:
- ASP.NET Core Identity for user authentication (if catalog requires authenticated users)
- SQL Server for persistent storage of catalog entries and metadata
- Google Maps API for potential map visualization integration (future)

**Success Criteria** (from spec.md SC-001 through SC-005):
- All 6 provided URLs evaluated and categorized
- Oman-specific sources clearly identified
- General/international sources identified
- Results presented in table format with source name, URL, and relevance category
- User can quickly identify which sources to use for Oman vs general country queries

**Constitution Compliance**:
- Clean Architecture dependency rules enforced (inward-only)
- No silent failures - all exceptions caught and surfaced per §8
- Provider neutrality - no specific data source vendor hardcoded
- All sensitive operations require authenticated users
- Validation via FluentValidation for all input
- Structured logging via Serilog