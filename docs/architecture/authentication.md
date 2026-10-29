# Authentication implementation notes

This page records authentication details that are easy to break during maintenance. Verify names and options against the current implementation and tests before changing them.

## Sign-in and second factors

- Password sign-in requiring a second factor returns `{ status: "twoFactorRequired", loginTicket }` without setting JWT cookies. TOTP or a recovery code completes the login.
- Passkey sign-in is passwordless and does not require TOTP.
- TOTP issuer is `Delegat`, not the hostname. QR codes use the local `qrcode` SVG helper.

## Passkeys

- WebAuthn uses discoverable credentials (`ResidentKey = Required`; keep `RequireResidentKey = true` for Bitwarden). Do not set `AuthenticatorAttachment`.
- Relying-party ID is the current hostname from `Origin`, not a shared parent domain.
- Challenge cookie: `delegat.webauthn`, path `/api/auth`.
- Identity authenticator keys persist in `UserTokens`; credentials live in `webAuthnCredentials`.
- Frontend uses `@simplewebauthn/browser` v13 `optionsJSON` APIs. Conditional UI uses `useBrowserAutofill: true` and `autocomplete="username webauthn"`.

## Read-only demo workspace

`workspace.view` is an extra grant for a Viewer who is a member of the demo store; do not add it to the Viewer role template. Plugins and database dumps remain unavailable. See [the workspace-view design record](../plans/2026-08-25-workspace-view-design.md) and `backend.Tests/` permission tests.
