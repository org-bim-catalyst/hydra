# Do-not-translate list (protected terms)

Canonical list of brand, vendor and product names and acronyms that are **never translated or
transliterated** in any language (specs/067, SC-016). They stay verbatim in Latin script,
and in right-to-left text they are wrapped so they keep their reading order (`<bdi>` in email,
isolated spans in the UI).

This file is the single source of truth. Three places mirror it and tests keep them in step:

- Backend: `src/AskLucy.Infrastructure/Notifications/Templates/ProtectedTerms.cs`
  (`ProtectedTermsTests` fails if it drifts from the list below).
- Frontend: `ClientApp/src/i18n/protectedTerms.ts`.
- Arabic notification templates: every term in an `en` seed appears verbatim in the `ar` seed.

To add a term, add one bullet below, then update the mirrors above. Keep exactly one term per
bullet and nothing else on the line.

## Protected terms

- OpenAI
- Anthropic
- Gemini
- OpenRouter
- Ask Lucy
- API
- MCP
- SMTP
- 2FA
- TOTP
- RAG
- OCR
- BIM
- PDF
