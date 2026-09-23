# ADR 0016: Custom Model Deployment Reads Its FTP Target from Configuration — Temporarily

**Status:** Accepted

**Date:** 2026-09-23

**Feature:** [specs/072-custom-model-deploy](../../specs/072-custom-model-deploy/spec.md)

## Context

Custom Models (specs/072) downloads a Hugging Face repository on the server and pushes it to the
production host over FTPS. That needs a deployment target: a host, port, credentials and a root
path.

The long-term design is a Connectors feature (spec 071): administrator-managed, encrypted
connections to external systems, chosen per use. That feature doesn't exist yet. Building a general
connector abstraction first would have delayed Custom Models by a whole feature, and designing it
around a single consumer would have locked in the wrong shape.

## Decision

Read the target from the `Ftp` configuration section for now, and let exactly one class know that:

* `IDeploymentTargetSettingsProvider` (Application) is the only way anything learns about the
  target. It returns `DeploymentTargetSettings`, or reports that deployment isn't configured.
* `ConfigurationDeploymentTargetSettingsProvider` (Infrastructure) is its only implementation. It
  binds `FtpOptions` (`Host`, `Port`, `Username`, `Password`, `RootPath`, `AllowPlainFtp`).
* The deployment job, the handlers, the uploader and the UI never touch `FtpOptions` or
  `IConfiguration`.

Real values live only in the untracked `appsettings.{Environment}.json`. The tracked `.example`
files carry an empty `Ftp` section.

The password, host and root path never appear in a log line, an API response or an exception
message. The API reports only whether deployment is configured and over which transport.

## Consequences

* When spec 071 lands, a connector-backed `IDeploymentTargetSettingsProvider` replaces the
  configuration one. That is a single DI registration change plus deleting `FtpOptions`.
  **This replacement is the plan. It is not a regression, and it should not be reviewed as one.**
* Until then, changing the target means editing a hand-deployed configuration file and restarting
  the app. There's no admin UI for it, and the credential sits in plain text in that file. This is
  the same trade-off the site's other hand-deployed secrets already make.
* The target is global: every deployment goes to the one configured host.

## Alternatives considered

* **Build Connectors first.** Rejected: it doubles the scope, and its shape is better decided
  with more than one consumer.
* **Store the FTP credential in the database now** (as `AIProviders` does). Rejected: it would
  need an admin UI and a data migration that spec 071 would then have to replace.
* **Let the job take `IOptions<FtpOptions>` directly.** Rejected: that spreads the configuration
  dependency across the job and the uploader, so the future swap stops being a one-class change.
