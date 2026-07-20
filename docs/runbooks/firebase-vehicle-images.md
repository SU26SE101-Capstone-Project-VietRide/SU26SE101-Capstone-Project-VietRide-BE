# Firebase vehicle-image uploads

## Backend configuration

Identity requires these secret-backed environment variables in Production and fails startup when
any value is missing:

- `FIREBASE_PROJECT_ID`
- `FIREBASE_CLIENT_EMAIL`
- `FIREBASE_PRIVATE_KEY` (PEM; either real line breaks or escaped `\n` are accepted)

Never commit the credential values and never log credentials or custom tokens.

Deploy the Storage Rules from the repository root:

```bash
firebase deploy --only storage --project "$FIREBASE_PROJECT_ID" --config infra/firebase/firebase.json
```

The rules make `vehicles/{operatorId}/{fileName}` publicly readable. Writes require Firebase Auth
claims `role=OPERATOR_ADMIN` and a matching `operatorId`, a non-empty image smaller than 5 MiB,
and MIME `image/jpeg`, `image/png`, or `image/webp`. Every other object path remains denied to
Firebase clients by default.

## Frontend flow

1. Authenticate with VietRide and call `POST /v1/firebase/custom-token` with the VietRide access token.
2. Keep Firebase Auth persistence in memory and call `signInWithCustomToken()` with `data.token`.
3. Generate `<uuid-v4>.<ext>` and upload to `vehicles/{operatorId}/...` with the correct MIME metadata.
4. Call `getDownloadURL()` and submit that HTTPS URL in the Vehicle API `imageUrls` field.
5. Call Firebase `signOut()` in a `finally` block after upload and when logging out of VietRide.

Custom tokens expire after one hour. Exchanging one creates a longer Firebase session, so Identity
also publishes a Firebase-session revoke request when a user is locked or its Operator is suspended.
Already issued Firebase ID tokens can remain usable for a residual window of about one hour.

## Staging acceptance

- Valid active admin, correct operator path, supported non-empty image under 5 MiB: allow.
- Anonymous, non-admin, or mismatched operator: deny.
- Empty file, size at or above 5 MiB, or unsupported MIME: deny.
- Public download/read: allow.
- End to end: custom token -> Firebase sign-in -> upload -> download URL -> Vehicle `imageUrls` update -> sign-out.
