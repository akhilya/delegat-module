# Workspace view (read-only cabinet)

Demo-cabinet users need to browse the whole workspace without changing data. Plugins stay closed.

## Permission

- Claim: `workspace.view` (assignable extra grant, not on Viewer).
- Owner picks it up via `Permissions.Assignable` when roles re-seed.
- Assign on the demo user in Administration → Users. Add that user to the demo store.

`IsCabinetViewOnly`: has `workspace.view` and no other claims except `reference.read`.

## API

`PermissionAuthorizationHandler`: GET/HEAD/OPTIONS succeed with `workspace.view` in place of the required permission, except `plugins.manage` and `settings.dump` (full MySQL dump is instance-owner only). Store-scoped routes still require store membership.

POST/PUT/PATCH/DELETE still need the original permission.

Cabinet-view-only accounts cannot:

- `PATCH /users/me` (profile / password)
- TOTP start/confirm/disable
- Passkey register/delete

They can still `POST /users/me/memberships/{id}/select` and refresh/sign-out.

## UI

`canAccessPath` treats `workspace.view` as read access to every route except plugins, product create, and setup.

Sidebar uses the same rule. Create-product stays hidden. Adapters / tools / settings (except plugins) show.

Axios blocks mutating requests (except store select, refresh, sign-out) and `GET /settings/database/dump`. User settings fields for profile, password, TOTP, and passkeys are disabled.
