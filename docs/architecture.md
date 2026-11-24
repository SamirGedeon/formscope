# Architecture decisions

## A fixed schema after publication

Forms begin in draft, where the full schema can be replaced under a version check. Publication freezes that schema. Closing and reopening change availability, not question definitions. This keeps responses interpretable without introducing schema-version mapping or per-response question snapshots.

The validator bounds question counts, IDs, label lengths, types and option lists. Multiple-choice options are trimmed and must remain distinct. Every submitted key must correspond to a published question; the validator checks required values, email syntax and exact option membership. Optional omitted answers normalize to an empty string.

## Transaction boundaries

Draft updates and state changes run in SQLite transactions with compare-and-swap version predicates. Another account's form returns 404. An old version returns 409 instead of overwriting a newer state. Published/closed schemas cannot be edited even when the version matches.

Submission transactions load the saved form, validate the answers and check for a previous request ID. A matching fingerprint returns the original receipt; a different fingerprint returns 409. Only genuinely new submissions check that the form is still open. Closure therefore blocks new records without breaking safe receipt replay.

SQLite's transaction boundary serializes publication/closure against submission writes. The database uniqueness constraint on `(SurveyId, RequestId)` reinforces duplicate protection. Foreign keys enforce account/form ownership relationships. The connection uses a five-second busy timeout and WAL is enabled at startup.

## Canonical request identity

Request UUIDs are normalized to a standard lowercase representation, so casing or alternate UUID formatting cannot produce a second record for the same ID. Answer values are trimmed, missing optional fields become empty strings, and the fingerprint sorts keys using ordinal comparison before SHA-256 hashing.

The fingerprint is an integrity comparison, not encryption. The actual answers remain plaintext JSON in the private database. The API returns only response ID, timestamp and answers to the owning account; fingerprints and request IDs are not exposed on public form pages.

## Authentication and browser requests

ASP.NET Core handles password hashing, protected authentication cookies and antiforgery tokens. The authentication cookie carries an opaque session token; its digest and expiration are also stored in SQLite. Every authenticated request verifies the database session, enabling server-side logout revocation and expiration checks.

The frontend obtains a request token from `/api/session` and sends it in `X-CSRF-TOKEN` for mutations. After login/logout it obtains a new identity-bound request token. This also protects anonymous form submission against cross-site browser posting. Public form respondents do not need an account.

Data Protection keys are persisted outside the web root. HttpOnly and SameSite=Strict cookies are used; Production requires Secure cookies/HTTPS. No global CORS allowance or forwarded-header trust relaxation is configured.

## Frontend

The client uses plain ES modules, delegated event handlers and native forms. A single escaping helper protects all user-provided labels, titles, option values and response text inserted into templates. The content security policy restricts scripts and styles to the same origin and disallows framing.

Forms have visible labels, native input validation and live error/status regions. Published questions remain visible in the owner interface while edit controls disappear. The public page does not display account information or response controls belonging to the owner. Browser tests exercise real anonymous and owner contexts and check narrow-layout overflow. No formal accessibility audit has been performed.

## Limits

Owner response lists show the latest 100 entries; total response counts include all accepted submissions. There is no pagination/export implementation yet. Rate limiting is per direct IP in the current process and has no shared distributed store.

EF Core `EnsureCreated` initializes a fresh installation; it is not a migration system. Existing production schemas require a proper migration plan before structural changes. This release targets a single local SQLite-backed application instance.
