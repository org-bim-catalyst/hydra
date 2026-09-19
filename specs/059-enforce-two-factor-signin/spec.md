# Feature Specification: Enforce Two-Factor Authentication at Sign-In

**Feature Branch**: `059-enforce-two-factor-signin`

**Created**: 2026-09-19

**Status**: Draft

**Input**: User description: "Two-factor authentication is never enforced at sign-in. A user who enrols in TOTP two-factor authentication is still signed in with a password alone, because `IdentityService.ValidateCredentialsAsync` validates with `SignInManager.CheckPasswordSignInAsync`, which never reports `RequiresTwoFactor` (only `PasswordSignInAsync` does), so the `RequiresTwoFactor` branch is dead code; and the follow-on `ValidateTwoFactorCodeAsync` leans on `SignInManager.TwoFactorAuthenticatorSignInAsync`, which requires the `TwoFactorUserId` cookie that a JWT API never sets, so it could not work even if reached. Fix sign-in so that an account with two-factor enabled is challenged for its second factor, and so that the authenticator code and the recovery-code paths both verify statelessly without depending on Identity's two-factor cookie. Existing tests pass against the broken behaviour, so the fix needs its own coverage."

## Context

Ask Lucy already offers two-factor enrolment. A user can scan an authenticator key from Settings, and the product tells them their account is protected by a second factor. The sign-in page already knows how to present a code challenge, and a second-step endpoint already exists.

None of it is reached. Sign-in completes on the password alone, for every account, enrolled or not. The protection the product promises does not exist, and a user who believes a stolen password is survivable is wrong.

This is a correctness and security defect in an existing promised capability, not a new capability. It was found during the SPEC-058 password-recovery work and recorded in
[ADR 0009](../../docs/adr/0009-owned-password-reset-token.md) and
[SPEC-058's security review](../058-password-recovery/security-review.md) as out of that feature's scope.

There is a second defect behind the first, and it is more dangerous than the one that hides it. The second-step endpoint identifies the account from a user identifier supplied in the request body, and accepts it with no evidence that the password step ever succeeded. While the challenge is never issued, that endpoint is unreachable in practice. The moment the first defect is fixed in isolation, it becomes a complete authentication bypass: anyone holding a user identifier could obtain a session by guessing a six-digit code, with no password required. **Both defects must be fixed together; fixing only the first makes the product less safe than it is today.**

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An enrolled account is challenged for its second factor (Priority: P1)

A user who has enrolled in two-factor authentication signs in with their email address and password. Instead of landing in the workspace, they are asked for the current code from their authenticator app. The workspace opens only after that code is accepted.

**Why this priority**: This is the defect. Without it the feature's entire promise is unmet and every other story is decoration.

**Independent Test**: Enrol an account in two-factor authentication, sign out, and sign in again with the correct password. The workspace must not open until a valid authenticator code has been supplied.

**Acceptance Scenarios**:

1. **Given** an account with two-factor enabled, **When** the user submits a correct email and password, **Then** they are shown the code challenge and are not signed in.
2. **Given** an account with two-factor enabled and a pending challenge, **When** the user submits the current authenticator code, **Then** they are signed in and reach the workspace.
3. **Given** an account with two-factor enabled and a pending challenge, **When** the user submits an incorrect or expired code, **Then** they are told the code was not accepted and remain unsigned-in, with the challenge still open.
4. **Given** an account **without** two-factor enabled, **When** the user submits a correct email and password, **Then** they are signed in directly with no additional step — the existing experience is unchanged.
5. **Given** an account with two-factor enabled, **When** the user submits an **incorrect** password, **Then** they are told their credentials were rejected and are **not** advanced to the code challenge.

---

### User Story 2 - The second step cannot be reached without the password (Priority: P1)

The code challenge is only answerable by someone who has just proved they know the account's password. Presenting a code without having passed the password step cannot sign anyone in, however the request is constructed.

**Why this priority**: Equal to US1 and inseparable from it. Delivering US1 without US2 converts a missing protection into an authentication bypass, which is strictly worse than the current state.

**Independent Test**: Attempt to complete the second step for a known account identifier without first submitting a correct password. No combination of inputs may produce a signed-in session.

**Acceptance Scenarios**:

1. **Given** no prior password submission, **When** a code challenge answer is submitted for a known account, **Then** it is refused and no session is issued.
2. **Given** a password submission that was rejected, **When** a code challenge answer is submitted for that account, **Then** it is refused and no session is issued.
3. **Given** a successful password submission for account A, **When** a code challenge answer is submitted naming account B, **Then** it is refused and no session is issued for either account.
4. **Given** a successful password submission, **When** the user waits beyond the challenge's lifetime and then submits a valid code, **Then** it is refused and they must sign in again from the beginning.
5. **Given** a challenge that has already been answered successfully, **When** it is answered a second time, **Then** it is refused and no second session is issued.

---

### User Story 3 - A recovery code gets a user back in without their authenticator (Priority: P2)

A user who has lost the device holding their authenticator chooses to use a recovery code instead, enters one of the codes saved at enrolment, and is signed in. That code cannot be used again.

**Why this priority**: Enforcing a second factor without a recovery route converts a lost phone into a permanently lost account. It must ship with enforcement, but enforcement is what makes the product correct.

**Independent Test**: Enrol an account, capture its recovery codes, sign in with a password, choose the recovery-code route and redeem one code. Then confirm the same code is refused on a subsequent sign-in.

**Acceptance Scenarios**:

1. **Given** a pending challenge, **When** the user chooses the recovery-code route and submits an unused recovery code, **Then** they are signed in.
2. **Given** a recovery code that has already been redeemed, **When** it is submitted again, **Then** it is refused.
3. **Given** a pending challenge, **When** the user submits an authenticator code in the recovery-code field or vice versa, **Then** it is refused without revealing which route would have accepted it.
4. **Given** a user who has redeemed recovery codes, **When** they view their security settings, **Then** they can see how many unused recovery codes remain.

---

### User Story 4 - Guessing a code is not a viable attack (Priority: P2)

Someone who has stolen a password cannot simply try codes until one works.

**Why this priority**: A six-digit code is a small space. Unlimited attempts against an open challenge reduce the second factor to a delay rather than a defence.

**Independent Test**: Open a challenge with a correct password, then submit repeated wrong codes and confirm that attempts stop being accepted well before the code space could be explored.

**Acceptance Scenarios**:

1. **Given** an open challenge, **When** wrong codes are submitted repeatedly, **Then** further attempts are refused after a bounded number of failures and the user must start again from the password step.
2. **Given** repeated failures across many accounts from one origin, **When** the attempts continue, **Then** they are throttled.
3. **Given** a code challenge that is failing, **When** attempts are refused, **Then** the refusal does not reveal whether the account exists, whether it is enrolled, or which factor was wrong.

---

### Edge Cases

- A user disables two-factor authentication while a challenge for their account is open — the open challenge must not become a route to a session that skipped a factor now considered unnecessary; treat it as resolved by the account's state at the moment the challenge is answered.
- A user's password is changed or reset between the password step and the code step — the challenge must not survive, because it attests to a password that is no longer current.
- An account that is locked out during an open challenge must not be able to complete sign-in.
- A user with two-factor enabled signs in through an external provider (Google, Microsoft, Facebook, GitHub) — see Assumptions; this feature does not change the external-provider path.
- Clock drift between the server and the user's authenticator device — codes adjacent to the current window must be tolerated within the accepted standard's normal allowance, or users with slightly skewed devices will be locked out of their own accounts.
- A user abandons the challenge and navigates away, then returns — they must be able to start again from the password step without being stuck in a half-signed-in state.
- Two sign-in attempts for the same account running concurrently must not interfere, and answering one must not complete the other.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST determine, after a password is successfully verified, whether the account has two-factor authentication enabled, and MUST NOT issue a session to an enrolled account on the password alone.
- **FR-002**: The system MUST respond to a successful password submission for an enrolled account with an outcome that asks for a second factor and carries no session or access credential of any kind.
- **FR-003**: The system MUST NOT advance to the second factor when the password is wrong, when the account does not exist, when the address is unconfirmed, or when the account is locked out.
- **FR-004**: The system MUST verify an authenticator code without depending on any browser cookie or other server-held sign-in state established by the password step's underlying framework, because the application authenticates through bearer credentials and never establishes such state.
- **FR-005**: The system MUST verify a recovery code without depending on that same state, and MUST invalidate a recovery code once it has been redeemed.
- **FR-006**: The system MUST bind the second step to the specific password verification that preceded it, so that the second step cannot be completed by naming an account alone. An answer that is not accompanied by proof of a recent successful password verification for that same account MUST be refused.
- **FR-007**: The proof binding the two steps MUST be single-use, MUST expire after a short bounded lifetime, MUST be unforgeable by the client, and MUST NOT itself grant any access to the application.
- **FR-008**: The system MUST invalidate an outstanding challenge when the account's password changes, when the account's password is reset, when the account becomes locked out, or when two-factor enrolment is disabled.
- **FR-009**: The system MUST limit the number of failed second-factor attempts permitted against a single challenge, and MUST end the challenge once that limit is reached.
- **FR-010**: The system MUST rate-limit second-factor attempts by request origin, and MUST NOT partition those limits by account identifier or email address, because a per-account limit is itself an account-existence oracle.
- **FR-011**: The system MUST return one undifferentiated failure for every second-factor rejection cause — wrong code, expired code, already-redeemed recovery code, expired challenge, unknown challenge, attempt limit reached, mismatched account — and MUST record the specific cause to the security log only.
- **FR-012**: The system MUST tolerate reasonable clock drift between server and authenticator device when validating time-based codes.
- **FR-013**: Users MUST be able to choose the recovery-code route from the code challenge, and the interface MUST make that route discoverable to someone who has lost their authenticator.
- **FR-014**: Users MUST be able to see how many unused recovery codes remain, and MUST be able to generate a fresh set, which invalidates the previous set.
- **FR-015**: The system MUST log every second-factor challenge issued, every success and every failure with its cause, as security events attributable to an account, without recording any code, recovery code, or challenge proof value.
- **FR-016**: The system MUST NOT persist or log an authenticator code, a recovery code in redeemable form, or the value that binds the two sign-in steps.
- **FR-017**: A failing second factor MUST surface to the user as visible interface feedback explaining that the code was not accepted and what to do next, never as a silent failure or a console-only error.
- **FR-018**: Accounts without two-factor enrolment MUST continue to sign in exactly as they do today, with no additional step and no change to the response they receive.

### Key Entities

- **Two-factor sign-in challenge**: The short-lived, single-use evidence that a specific account's password was just verified and that only the second factor remains. Holds which account it is for, when it was created, when it expires, how many failed attempts it has absorbed, and whether it has been spent. It is not a credential — holding it grants nothing on its own.
- **Recovery code**: A pre-issued, single-use alternative to an authenticator code, stored so that it cannot be read back in usable form, and marked as spent once redeemed.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of sign-in attempts against an enrolled account require a second factor; none completes on a password alone.
- **SC-002**: 0% of second-step attempts succeed without a preceding successful password verification for the same account, across every attempted request shape.
- **SC-003**: Sign-in for accounts without two-factor enrolment is unchanged, evidenced by the existing sign-in coverage passing without modification to its expectations.
- **SC-004**: A user enrolled in two-factor authentication can complete sign-in in under 60 seconds, including reading a code from their authenticator.
- **SC-005**: A user who has lost their authenticator can regain access with a recovery code in under 2 minutes, without contacting support.
- **SC-006**: An attacker who knows a password and an account identifier cannot obtain a session; brute-forcing the code is ended by the attempt limit after no more than a low double-digit number of tries against one challenge.
- **SC-007**: Every rejection cause is externally indistinguishable, verified by comparing the responses for all seven causes named in FR-011.
- **SC-008**: New automated coverage fails against today's behaviour and passes after the fix, for both the enforcement gap and the unbound-second-step gap — so the tests demonstrably test the defect rather than the implementation.

## Assumptions

- Two-factor enrolment itself already works: the authenticator key is generated, stored and made scannable, and recovery codes are generated at enrolment. This feature fixes verification at sign-in, not enrolment.
- The sign-in interface already presents a code challenge when told one is required, and already posts an answer to a second step. The client changes here are the recovery-code route, the remaining-codes display, and whatever the new binding between the two steps requires — not a new page.
- Time-based one-time passwords remain the only supported authenticator factor. Hardware security keys, passkeys and SMS codes are out of scope.
- Two-factor enforcement applies to password sign-in. Sign-in through an external identity provider is out of scope for this feature: the external provider performs its own authentication and its own second factor, and layering a second challenge on top is a separate product decision.
- "Remember this device" is out of scope. Every password sign-in to an enrolled account is challenged.
- Administrator-mandated enrolment — requiring two-factor for a role or the whole tenant — is out of scope. Enrolment remains the user's choice; this feature only honours the choice they already made.
- Existing enrolled accounts, if any, will begin being challenged as soon as this ships. Anyone enrolled without having saved recovery codes should be prompted to generate them, since enforcement makes a lost authenticator consequential for the first time.
