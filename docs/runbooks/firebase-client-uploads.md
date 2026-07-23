# Firebase client uploads

## Backend configuration

Identity requires these secret-backed environment variables in Production:

- `FIREBASE_PROJECT_ID`
- `FIREBASE_CLIENT_EMAIL`
- `FIREBASE_PRIVATE_KEY` (PEM; real line breaks or escaped `\n`)
- `FIREBASE_WEB_STORAGE_BUCKET` (the exact bucket name used in Firebase download URLs)

Never commit credentials or log credentials/custom tokens.

Deploy the Storage Rules from the repository root:

```bash
firebase deploy --only storage --project "$FIREBASE_PROJECT_ID" --config infra/firebase/firebase.json
```

## Purpose and path matrix

Call `POST /v1/firebase/custom-token` with the VietRide access token. An empty body remains
backward-compatible and means `VEHICLE_IMAGE`; otherwise send `{ "purpose": "..." }`.

| Purpose | Allowed role | Object prefix |
|---|---|---|
| `VEHICLE_IMAGE` | `OPERATOR_ADMIN` | `vehicles/{operatorId}/` |
| `OPERATOR_LOGO` | `OPERATOR_ADMIN` | `operators/{operatorId}/logo/` |
| `PARCEL_PHOTO` | `PASSENGER` | `parcels/{userId}/` |
| `INCIDENT_PHOTO` | `DRIVER`, `ASSISTANT` | `incidents/{operatorId}/{userId}/` |
| `USER_AVATAR` | any active user | `avatars/{userId}/` |

Rules require the matching Firebase custom claims, a matching owner/operator path, non-empty
JPEG/PNG/WebP content smaller than 5 MiB, and deny every other client path. Operator-scoped
users must belong to an active, approved Operator when the token is minted.

After `getDownloadURL()`, submit the URL to the owning API. The backend validates the exact
bucket and owner prefix before saving Vehicle image URLs, Operator logo, Parcel photo,
Incident photo, or the avatar through `PATCH /v1/users/me/avatar`.

RAG documents continue to use server-side Cloudinary upload. Invoice PDFs remain backend-owned
storage and never receive a Firebase client token.

## Frontend flow

1. Authenticate with VietRide and request the purpose-specific custom token.
2. Keep Firebase Auth persistence in memory; call `signInWithCustomToken(data.token)`.
3. Generate a UUID filename with a safe image extension and upload under `data.uploadPath`.
4. Call `getDownloadURL()` and submit it to the owning VietRide endpoint.
5. Call Firebase `signOut()` in a `finally` block and on VietRide logout.

Custom tokens expire after one hour. Identity publishes a Firebase-session revoke request when
a user is locked or an Operator is suspended; already issued Firebase ID tokens can remain
usable for a residual window of about one hour.

## Staging acceptance

- Valid active caller, correct purpose/path, supported non-empty image under 5 MiB: allow.
- Anonymous, inactive, wrong-role, suspended-operator, cross-user, or cross-operator upload: deny.
- Empty file, size at or above 5 MiB, or unsupported MIME: deny.
- API rejects a URL outside the configured bucket or caller-owned prefix.
- End to end: custom token -> Firebase sign-in -> upload -> download URL -> owning API -> sign-out.
