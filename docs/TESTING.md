# TESTING.md

> **Project:** Ask Lucy AI Workspace
> **Version:** 1.0
> **Status:** Mandatory Engineering Standard
> **Applies To:** All Contributors (Human & AI)
> **Last Updated:** July 2026

---

# 1. Purpose

This document defines the testing strategy and quality assurance standards for the Ask Lucy platform.

Testing is a core engineering activity—not a final verification step.

Every feature, bug fix, refactoring, and architectural change MUST include appropriate testing.

---

# 2. Testing Principles

The testing strategy is based on the following principles:

* Test behavior, not implementation.
* Prevent regressions.
* Automate wherever practical.
* Keep tests fast and deterministic.
* Prefer many small tests over a few large ones.
* Every bug should result in a new regression test.
* Tests are part of the product.

---

# 3. Testing Goals

The testing strategy aims to ensure:

* Functional correctness
* Architectural integrity
* Security
* Performance
* Accessibility
* Reliability
* Maintainability

---

# 4. Testing Pyramid

The project follows the Testing Pyramid.

```text
                E2E
               /   \
        Integration
       /            \
      Unit Tests
```

Approximate distribution:

| Test Type   | Target |
| ----------- | -----: |
| Unit        |    70% |
| Integration |    20% |
| End-to-End  |    10% |

---

# 5. Test Categories

The project includes:

* Unit Tests
* Integration Tests
* API Tests
* Component Tests
* UI Tests
* End-to-End Tests
* Accessibility Tests
* Performance Tests
* Regression Tests
* Smoke Tests
* Security Tests

---

# 6. Testing Stack

## Backend

* xUnit
* FluentAssertions
* Moq (or NSubstitute)
* ASP.NET Core Test Host
* Testcontainers (preferred for integration tests)

## Frontend

* Vitest
* React Testing Library
* Mock Service Worker (MSW)

## End-to-End

* Playwright

---

# 7. Test Project Structure

```text
tests/

AskLucy.Domain.Tests

AskLucy.Application.Tests

AskLucy.Infrastructure.Tests

AskLucy.Persistence.Tests

AskLucy.WebAPI.Tests

AskLucy.Frontend.Tests

AskLucy.E2E.Tests
```

Mirror the production project structure whenever possible.

---

# 8. Unit Testing

Unit tests validate isolated business behavior.

A unit test should:

* Execute quickly
* Avoid external resources
* Have no network access
* Have no filesystem dependency
* Have no database dependency

---

# 9. Unit Test Naming

Preferred format:

```text
Method_Should_ExpectedBehavior_WhenCondition
```

Example

```text
CreateConversation_ShouldCreateConversation_WhenTitleIsValid
```

---

# 10. Domain Tests

The Domain layer should receive the highest testing priority.

Test:

* Entities
* Value Objects
* Domain Services
* Business Rules
* Aggregate behavior

Never mock domain objects unnecessarily.

---

# 11. Application Tests

Test:

* Command Handlers
* Query Handlers
* Validators
* Mapping
* Authorization rules
* Workflow orchestration

Mock external dependencies.

---

# 12. Infrastructure Tests

Test:

* AI providers
* SMTP
* File storage
* Logging integrations
* External APIs

Avoid testing third-party implementations.

Focus on integration behavior.

---

# 13. Persistence Tests

Verify:

* Entity mappings
* Relationships
* Constraints
* Migrations
* Repository behavior

Use SQL Server through Testcontainers where practical.

Avoid the EF Core InMemory provider for relational behavior.

---

# 14. API Tests

Every API endpoint should verify:

* Success response
* Validation errors
* Authentication
* Authorization
* Error handling
* Problem Details responses

---

# 15. Frontend Component Tests

Every reusable component should verify:

* Rendering
* User interaction
* State changes
* Accessibility attributes
* Error states

Avoid testing implementation details.

---

# 16. React Hook Tests

Custom hooks should verify:

* Initial state
* State updates
* Error handling
* Cleanup
* Async behavior

---

# 17. End-to-End Testing

Playwright validates complete user workflows.

Critical scenarios include:

* Registration
* Login
* Email verification
* Chat
* File upload
* Translation
* Voice
* Image generation
* User settings
* Theme switching

---

# 18. Smoke Tests

Executed after deployment.

Verify:

* Application starts
* Login works
* AI chat responds
* Database connectivity
* Health endpoint
* Static assets load

---

# 19. Regression Testing

Every bug fix must include a regression test.

The regression test should fail before the fix and pass after the fix.

---

# 20. Accessibility Testing

Verify:

* Keyboard navigation
* Focus order
* Screen reader labels
* Color contrast
* Form labels
* ARIA attributes

Accessibility regressions block release.

---

# 21. Performance Testing

Measure:

Backend

* API latency
* Database query time
* Memory usage
* CPU usage

Frontend

* Initial load
* Bundle size
* Render time
* Interaction responsiveness

Performance should be measured before optimization.

---

# 22. Security Testing

Verify:

* Authentication
* Authorization
* File uploads
* Input validation
* SQL Injection resistance
* XSS protection
* Prompt injection handling
* Rate limiting

---

# 23. AI Testing

Validate:

* Streaming responses
* Provider abstraction
* Token accounting
* Error handling
* Timeout handling
* Retry logic
* Fallback providers (future)

Do not rely on live provider responses for deterministic automated tests.

---

# 24. RAG Testing

When implemented, verify:

* Document ingestion
* Parsing
* Chunking
* Embedding generation
* Retrieval accuracy
* Citation generation
* Authorization boundaries

---

# 25. Mocking

Mock only external dependencies.

Examples:

* OpenAI
* Anthropic
* SMTP
* PayPal
* Filesystem
* Current time (via abstraction)

Avoid mocking business logic.

---

# 26. Test Data

Use builders or factories.

Avoid hardcoded duplicated test data.

Each test should create only the data it requires.

---

# 27. Test Isolation

Tests must be:

* Independent
* Repeatable
* Parallel-safe
* Order-independent

One failing test must not affect another.

---

# 28. Database Testing

Integration tests should:

* Create isolated databases
* Apply migrations
* Seed required data
* Clean up automatically

Avoid shared databases.

---

# 29. File Testing

Temporary files should:

* Be isolated
* Be deleted automatically
* Never rely on production folders

---

# 30. Snapshot Testing

Use snapshot tests sparingly.

Prefer behavior assertions.

Update snapshots only after intentional UI changes.

---

# 31. Code Coverage

Coverage is a metric—not the goal.

Recommended minimums:

| Layer          | Target |
| -------------- | -----: |
| Domain         |    95% |
| Application    |    90% |
| Infrastructure |    80% |
| Persistence    |    80% |
| Web API        |    85% |
| Frontend       |    85% |

Critical business logic should approach 100%.

---

# 32. Mutation Testing

Where practical, perform mutation testing on critical business logic to evaluate test quality.

A high code coverage percentage alone does not guarantee effective tests.

---

# 33. Continuous Integration

Every Pull Request must execute:

* Restore
* Build
* Static Analysis
* Backend Unit Tests
* Frontend Unit Tests
* Integration Tests
* Playwright Tests
* Coverage Report
* Security Scans

No failing pipeline may be merged.

---

# 34. Test Execution Order

Preferred pipeline:

```text
Restore

↓

Build

↓

Lint

↓

Unit Tests

↓

Integration Tests

↓

Component Tests

↓

API Tests

↓

Playwright

↓

Coverage

↓

Security Scans

↓

Artifacts
```

---

# 35. Feature Testing Checklist

Every new feature should include:

* Unit tests
* Validation tests
* Error tests
* Authorization tests
* UI tests
* Documentation updates

---

# 36. Migration Testing

During the migration phase, verify that every legacy feature behaves identically after modernization.

Maintain a regression matrix covering:

* AI Chat
* Voice
* Speech-to-Text
* PDF Upload
* PDF Extraction
* Translation
* Pronunciation
* Image Generation
* Authentication
* User Settings

No feature should regress during migration.

---

# 37. AI Coding Agent Testing Rules

AI coding assistants MUST:

* Write tests alongside production code.
* Never remove tests without justification.
* Update affected tests when behavior changes.
* Add regression tests for bug fixes.
* Keep tests readable and maintainable.
* Avoid brittle assertions.
* Avoid disabling failing tests to make the pipeline pass.

---

# 38. Pull Request Testing Checklist

Every Pull Request should answer:

* Are new behaviors tested?
* Are regressions covered?
* Are authorization paths tested?
* Are validation rules tested?
* Are error conditions tested?
* Are documentation changes included?
* Does the CI pipeline pass?

---

# 39. Definition of Tested

A feature is considered tested only when:

* Unit tests pass.
* Integration tests pass.
* API tests pass.
* UI tests pass (where applicable).
* End-to-end tests pass for critical workflows.
* Accessibility requirements are verified.
* Security-related behavior is validated.
* Regression tests exist for changed behavior.
* CI completes successfully.

---

# 40. Definition of Quality

Quality is achieved when:

* The feature satisfies its specification.
* The implementation follows the architecture.
* The code complies with coding standards.
* Tests provide confidence against regressions.
* Security requirements are met.
* Documentation is updated.
* Performance remains acceptable.
* The implementation is maintainable and understandable.

Testing is the primary mechanism by which the Ask Lucy platform maintains reliability as it evolves. Every contributor—human or AI—is responsible for preserving that reliability through comprehensive, automated, and maintainable tests.
