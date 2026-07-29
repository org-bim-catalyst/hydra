# coding-standards.md

> **Project:** Ask Lucy AI Workspace
> **Applies To:** All contributors (human and AI)
> **Version:** 1.0
> **Status:** Mandatory Standard
> **Last Updated:** July 2026

---

# 1. Purpose

This document defines the mandatory coding standards for the Ask Lucy project.

These standards exist to ensure:

* Consistent code quality
* Long-term maintainability
* Readability
* Testability
* Security
* Performance
* Collaboration between developers and AI coding agents

All code committed to the repository **MUST** comply with this document.

---

# 2. General Principles

Every change MUST:

* Be simple.
* Be maintainable.
* Be testable.
* Be secure.
* Be documented when necessary.
* Avoid unnecessary complexity.

Always prefer code that is easy to understand over code that is clever.

---

# 3. Engineering Principles

Always follow:

* SOLID
* DRY
* KISS
* YAGNI
* Separation of Concerns
* Dependency Inversion
* Composition over Inheritance
* Explicit over Implicit

---

# 4. Readability

Code is read more often than it is written.

Optimize for readability.

Prefer:

```text
Clear names

Small methods

Small classes

Explicit logic
```

Avoid nested logic whenever possible.

---

# 5. File Organization

One primary responsibility per file.

Avoid large files.

Recommended limits:

| Item            |     Target |
| --------------- | ---------: |
| Class           | <300 lines |
| Method          |  <40 lines |
| Controller      |  Thin only |
| React Component | <250 lines |

Split large classes into smaller responsibilities.

---

# 6. Naming Conventions

Names should describe intent.

Good

```text
ConversationService

GenerateImageCommand

UserSettings

KnowledgeBase
```

Bad

```text
Helper

Utils

Manager

Thing

Data
```

Avoid abbreviations unless universally understood.

---

# 7. C# Naming

Classes

PascalCase

Interfaces

Prefix with `I`

```text
IChatProvider
```

Methods

PascalCase

Properties

PascalCase

Private fields

```text
_chatRepository
```

Local variables

camelCase

Constants

PascalCase

Enums

PascalCase

Enum members

PascalCase

---

# 8. TypeScript Naming

Components

PascalCase

Hooks

```text
useConversation
```

Variables

camelCase

Interfaces

```text
ConversationDto
```

Types

PascalCase

Files

Prefer feature-based names.

---

# 9. Folder Naming

Use lowercase with hyphens where appropriate.

Example

```text
features/

chat/

knowledge-base/

prompt-library/
```

Avoid ambiguous folder names.

---

# 10. One Responsibility

Each class should have one responsibility.

Each method should perform one task.

If a method requires extensive comments to explain its behavior, it likely needs to be refactored.

---

# 11. Method Size

Methods should generally:

* Return one result
* Perform one action
* Avoid nested branching

Prefer early returns.

---

# 12. Comments

Code should be self-explanatory.

Avoid comments like

```text
Increment counter
```

Good comments explain:

* Why
* Trade-offs
* Non-obvious behavior

Never comment obvious code.

---

# 13. XML Documentation

Public APIs require XML documentation.

Include:

* Summary
* Parameters
* Returns
* Exceptions (when applicable)

Private methods generally do not require XML comments.

---

# 14. Nullable Reference Types

Nullable reference types MUST be enabled.

Never suppress warnings without justification.

Avoid using the null-forgiving operator (`!`) unless there is a well-documented reason.

---

# 15. Async Programming

Use async/await consistently.

Avoid:

```text
.Result

.Wait()

Task.Run() for I/O
```

Never block asynchronous code.

Always pass `CancellationToken` where appropriate.

---

# 16. Exception Handling

Throw exceptions only for exceptional situations.

Never use exceptions for normal control flow.

Catch exceptions only when you can:

* Recover
* Add context
* Translate to a meaningful response
* Log appropriately

---

# 17. Logging

Use Serilog.

Never log:

* Passwords
* Tokens
* Secrets
* Personal data
* Connection strings

Log:

* Correlation ID
* Request
* Duration
* Errors
* Warnings
* Significant business events

---

# 18. Magic Values

Never hardcode:

* URLs
* Keys
* Timeouts
* Limits
* Role names
* File paths

Move them to configuration or named constants.

---

# 19. Configuration

Use strongly typed configuration classes.

Avoid reading configuration directly throughout the application.

Centralize configuration access.

---

# 20. Dependency Injection

All services should be registered through DI.

Never instantiate services with `new` inside business logic unless they are simple value objects.

---

# 21. Domain Rules

Business logic belongs in the Domain layer.

Never place domain logic in:

* Controllers
* API endpoints
* React components
* EF entities
* Repositories

---

# 22. Validation

Validation belongs in FluentValidation.

Do not duplicate validation across layers.

Client-side validation improves UX but does not replace server validation.

---

# 23. DTOs

Never expose entities directly.

Use DTOs for:

* Requests
* Responses
* Events

Keep DTOs immutable where practical.

---

# 24. AutoMapper

Use AutoMapper only for straightforward mappings.

Avoid complex mapping logic in profiles.

Complex transformations belong in application services.

---

# 25. Entity Framework

Never expose `DbContext` outside the Persistence layer.

Prefer LINQ.

Avoid raw SQL unless necessary for performance or supported features.

Review every query for efficiency.

---

# 26. SQL

Avoid:

* SELECT *
* N+1 queries
* Unbounded queries

Use appropriate indexes.

Paginate large result sets.

---

# 27. Transactions

Use transactions only when multiple operations must succeed or fail together.

Keep transaction scope as small as possible.

---

# 28. API Controllers

Controllers should:

* Validate requests
* Call MediatR
* Return responses

Controllers should not contain business logic.

---

# 29. CQRS

Commands

Change state.

Queries

Read state.

Do not mix responsibilities.

---

# 30. MediatR

Handlers should remain focused.

One handler should process one request.

Avoid handlers that orchestrate unrelated workflows.

---

# 31. React Components

Components should:

* Be small
* Be reusable
* Be focused

Move business logic into hooks or services.

---

# 32. React Hooks

Create custom hooks for reusable behavior.

Hooks should not perform unrelated responsibilities.

---

# 33. State Management

Use:

**Zustand**

For client state.

Examples:

* Theme
* Authentication
* Sidebar
* Preferences

Use:

**TanStack Query**

For server state.

Examples:

* Conversations
* Documents
* Users
* Models

Do not duplicate server state in Zustand.

---

# 34. Styling

Never use inline styles unless absolutely necessary.

Use:

* Material UI Theme
* Theme tokens
* Shared design system

Never hardcode colors.

---

# 35. Forms

Use:

React Hook Form

Validation

Zod (client)

FluentValidation (server)

Display validation inline.

---

# 36. Accessibility

Every UI component must support:

* Keyboard navigation
* Focus management
* Screen readers
* Proper labels
* Sufficient contrast

Accessibility is not optional.

---

# 37. Performance

Prefer:

* Lazy loading
* Memoization where beneficial
* Virtualization for large lists
* Efficient rendering

Measure before optimizing.

Avoid premature optimization.

---

# 38. Security

Never trust client input.

Always validate:

* Requests
* Files
* IDs
* Permissions

Follow least-privilege principles.

---

# 39. Secrets

Secrets must never be stored:

* In source code
* In Git
* In client-side code
* In logs

Use secure configuration providers.

---

# 40. Feature Flags

Experimental functionality should be guarded by feature flags when appropriate.

Remove obsolete flags after rollout.

---

# 41. Testing

Every new feature should include:

* Unit tests
* Integration tests where appropriate
* Regression tests for changed behavior

Bug fixes should include tests that reproduce the original issue.

---

# 42. Error Messages

User-facing messages should:

* Be understandable
* Avoid internal implementation details
* Suggest recovery when appropriate

Technical details belong in logs.

---

# 43. Code Reviews

Every pull request should verify:

* Architecture compliance
* Coding standards
* Security
* Performance
* Test coverage
* Documentation updates

---

# 44. Refactoring

Improve code when working in an area.

However:

Do not perform unrelated large refactors within feature branches.

Keep pull requests focused.

---

# 45. Git Commits

Use Conventional Commits.

Examples

```text
feat(chat): add streaming responses

fix(auth): refresh token expiration

refactor(api): simplify command handler

docs: update architecture

test(chat): add conversation service tests
```

---

# 46. TODO Policy

Avoid permanent TODOs.

Every TODO should:

* Explain why
* Reference an issue or specification
* Include a clear follow-up action

Example

```text
TODO(SPEC-015): Replace temporary SQL implementation with provider abstraction.
```

---

# 47. AI Coding Agent Rules

AI coding assistants MUST:

* Read the relevant specification before coding.
* Follow Clean Architecture.
* Follow this coding standard.
* Avoid duplicated logic.
* Explain architectural trade-offs before major changes.
* Update tests when behavior changes.
* Update documentation when architecture changes.
* Never invent requirements.
* Ask for clarification when requirements are ambiguous or conflicting.

---

# 48. Definition of Ready

A task is ready for implementation only when:

* Requirements are approved.
* Acceptance criteria are defined.
* Dependencies are identified.
* Architectural impact is understood.

---

# 49. Definition of Done

A task is complete only when:

* Acceptance criteria are satisfied.
* Code complies with this standard.
* Tests pass.
* Documentation is updated.
* No critical warnings remain.
* Code review is complete.
* CI pipeline succeeds.

---

# 50. Continuous Improvement

These standards are living guidelines.

Changes require:

* Architectural review
* Team agreement
* Documentation updates

The objective is to improve consistency without introducing unnecessary complexity.

Every contributor—human or AI—is expected to uphold these standards to ensure Ask Lucy remains maintainable, scalable, secure, and production-ready throughout its lifetime.
