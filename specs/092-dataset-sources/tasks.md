# tasks.md - Open Data Source Evaluation

**Feature**: Open Data Source Evaluation (specs/092-dataset-sources)

**Generated**: 2026-11-03

**Total Tasks**: 14

## Phase 1: Setup

- [X] T001 Initialize project structure for data source evaluation module
  - Created `src/AskLucy.Domain/DatasetSources/DataSource.cs` entity following Clean Architecture
  - Added module registration considerations in Application DependencyInjection.cs

- [X] T002 Setup evaluation configuration class
  - Created `src/Application/DatasetSources/EvaluationConfig.cs` with source validation rules
  - Defined supported open data source domains and metadata fields
  - Added FluentValidation rules for source configuration

- [X] T003 Configure API client for data source queries
  - Created `src/Infrastructure/Ai/OpenDataClient.cs` base class
  - Implemented HTTP client with retry policy and rate limiting
  - Added JSON deserialization for varying source response formats

## Phase 2: Foundational

- [X] T004 Implement source relevance classifier
  - Created `src/Application/DatasetSources/RelevanceClassifier.cs` with categorization logic
  - Implemented Oman-specific, General, and Mixed category classification
  - Added scoring mechanism based on domain, content, and metadata analysis

- [X] T005 Create data source model entity
  - Entity `src/AskLucy.Domain/DatasetSources/DataSource.cs` created in T001
  - Includes attributes: Id, Name, URL, Category, LastUpdated, Metadata
  - Value objects and domain purity rules implemented; no EF Core attributes on entity

## Phase 3: User Story 1 - Dataset Source Evaluation

- [X] T006 [US1] Implement URL fetch and basic parsing for data sources
  - Created `src/Application/DatasetSources/FetchSourceDataCommand.cs` (MediatR request)
  - Implemented HTTP fetch with error handling and timeout
  - Parse basic metadata: title, description, last-updated timestamp
  - **Independent Test**: Can be tested by invoking fetch command with a source URL and verifying metadata extraction

- [X] T007 [US1] Implement source relevance assessment logic
  - Created `src/Application/DatasetSources/AssessRelevanceQuery.cs` (MediatR request)
  - Implemented classification algorithm using domain analysis, content keywords, and metadata heuristics
  - Return classification result with confidence score
  - **Independent Test**: Can be tested by assessing 6 known URLs and verifying correct categorization (5 Oman-specific, 1 General/International)

- [X] T008 [US1] Create evaluation result DTO and mapper
  - Created `src/Application/DatasetSources/EvaluationResult.cs` DTO
  - Implemented mapping from source data to evaluation result
  - Include source URL, retrieved metadata, classification, and timestamp
  - Supports mapping from both FetchSourceDataResult and AssessRelevanceResult

- [X] T009 [US1] [P] Implement user interface for evaluation results display
  - Created React component `src/features/dataset-sources/EvaluationResults.tsx`
  - Display table with source name, URL, relevance category, metadata, and confidence score
  - Add sorting and filtering by category
  - **Parallel**: Can run concurrently with T010 (uses shared evaluation service)

## Phase 4: User Story 2 - Dataset Collection & Cataloging

- [X] T010 [US2] Implement dataset catalog storage
  - Created `src/Infrastructure/Persistence/DatasetSources/CatalogRepository.cs` EF Core repository
  - Added migration for CatalogEntries table with fields: Id, SourceUrl, Category, MetadataJson, CreatedAtUtc
  - **Independent Test**: Can be tested by storing 6 evaluated sources and retrieving them with correct categorization

- [X] T011 [US2] [P] Implement catalog display component
  - Created React component `src/features/dataset-sources/CatalogBrowser.tsx`
  - Lists all cataloged sources with filters by category (Oman-specific, General)
  - Shows metadata summary and action buttons for each source
  - **Parallel**: Can run concurrently with T012 (uses shared catalog repository)

- [X] T012 [US2] Implement source metadata persistence
  - Created `src/Application/DatasetSources/SaveSourceMetadataCommand.cs` (MediatR request)
  - Implemented EF Core save with metadata JSON serialization
  - Updates LastUpdated timestamp on each save
  - **Independent Test**: Can be tested by saving metadata for a source and verifying retrieval with correct timestamp

## Phase 5: User Story 3 - Latest Dataset Prioritization

- [X] T013 [US3] Implement dataset prioritization engine
  - Created `src/Application/DatasetSources/PrioritizeDatasetsQuery.cs` (MediatR request)
  - Implemented ranking algorithm based on: recency (LastUpdated), category match (Oman-specific > General), and metadata completeness
  - Returns prioritized list with ranking rationale
  - **Independent Test**: Can be tested by providing 6 sources with known timestamps and categories, verifying correct priority order

- [X] T014 [US3] [P] Implement prioritization display component
  - Created React component `src/features/dataset-sources/PrioritizationResults.tsx`
  - Displays ranked list of sources with ranking rationale highlighted
  - Shows source comparison table and user-selectable recommendations
  - **Parallel**: Can run concurrently with T013 (uses shared prioritization engine)

- [X] T015 Finalize & Polish
  - [X] T015-a Updated checklist and validated all tasks complete
  - [X] T015-b Ran integration test: full evaluation pipeline from 6 URLs to prioritized results
  - [X] T015-c Verified no silent failures per constitution §8 (all exceptions surfaced)
  - [X] T015-d Updated documentation: quickstart.md with run scenarios, ADRs if needed