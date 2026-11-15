# Authentication implementation notes

This page records authentication details that are easy to break during maintenance. Verify names and options against the current implementation and tests before changing them.

## Sign-in and second factors

- Password sign-in requiring a second factor returns `{ status: "twoFactorRequired", loginTicket }` without setting JWT cookies. TOTP or a recovery code completes the login.
- Passkey sign-in is passwordless and does not require TOTP.
- TOTP issuer is `Delegat`, not the hostname. QR codes use the local `qrcode` SVG helper.

## Refresh tokens

- `POST /auth/refresh` rotates the presented refresh token: the old row gets `revokedAt` and `replacedByTokenId` pointing at its replacement.
- A revoked token is accepted only within `RefreshToken.RotationGracePeriod` (30 seconds) of rotation, and only to return the still-active token at the end of its own rotation chain (concurrent tabs share one cookie; at most three hops, each inside its grace window). Logout revokes without a replacement, so logout-revoked tokens are never accepted.
- Any other revoked-token use is treated as theft: all of the user's active refresh tokens are revoked, cookies are cleared, and the request returns 401. Covered by `backend.Tests/JwtAuthRefreshTokenTests.cs`.

## Passkeys

- WebAuthn uses discoverable credentials (`ResidentKey = Required`; keep `RequireResidentKey = true` for Bitwarden). Do not set `AuthenticatorAttachment`.
- Relying-party ID is the current hostname from `Origin`, not a shared parent domain.
- Challenge cookie: `delegat.webauthn`, path `/api/auth`.
- Identity authenticator keys persist in `UserTokens`; credentials live in `webAuthnCredentials`.
- Frontend uses `@simplewebauthn/browser` v13 `optionsJSON` APIs. Conditional UI uses `useBrowserAutofill: true` and `autocomplete="username webauthn"`.

## Read-only demo workspace

The built-in **Viewer** role template grants only `reference.read`: neither `ai.use` nor `workspace.view` is included. A default Viewer therefore has no assistant or Python access. `workspace.view` is an extra grant for a Viewer who is a member of the demo store; do not add it to the role template. With `workspace.view` and at most `reference.read`, the employee gets read-only assistant access, but no Python, uploads, memory writes, connected MCP calls, or cabinet writes. Plugins and database dumps remain unavailable.

The assistant host evaluates **effective permissions, not the role name**. Python permission requires `ai.use`; enabling AI or configuring a Python helper does not supply that grant. If `ai.use` is explicitly added to a Viewer, the resulting effective permission set is no longer the view-only set, even if it also includes `workspace.view`. Python still requires enabled AI and a configured helper; every cabinet-chat run also needs a separate confirmed plan. Confirmation does not grant missing permissions. A role label alone cannot establish an employee's effective access.

Implementation: `AppRoles.RolePermissions`, `Permissions.IsCabinetViewOnly`, and `AgentAccess.CanUseAgent` / `CanRunPython`. The cases are covered by `backend.Tests/Ai/AgentAccessTests.cs`. The [workspace-view design record](../plans/2026-08-25-workspace-view-design.md) is historical, not the current permission contract.
