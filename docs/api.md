# HTTP API

All routes are under `/api` and use JSON. Private routes require the session cookie. Every mutation also requires the matching antiforgery cookie and `X-CSRF-TOKEN` header.

Start with `GET /api/session`. It returns `{csrf,user}` and sets the antiforgery cookie. After login/logout, request a fresh token because request tokens are bound to the current identity.

| Method | Path | Request | Result |
| --- | --- | --- | --- |
| GET | `/health` | — | `{status:"ok"}` |
| GET | `/session` | — | Current account summary or null, plus CSRF token |
| POST | `/auth/signup` | `{email,password}` | Account summary and session cookie |
| POST | `/auth/login` | `{email,password}` | Account summary and session cookie |
| POST | `/logout` | `{}` | Revokes session |
| GET | `/forms` | Cookie | Owned forms with counts |
| POST | `/forms` | Survey input | Created draft, HTTP 201 |
| GET | `/forms/:id` | Cookie | Owned form and count |
| PUT | `/forms/:id` | Full survey input + current version | Updated draft |
| POST | `/forms/:id/state` | `{state,version}` | Published/closed/reopened form |
| GET | `/forms/:id/responses` | Cookie | Latest 100 private responses |
| GET | `/public/:id` | — | Published form questions |
| POST | `/public/:id/responses` | `{requestId,answers}` | `{accepted:true,id}` |

## Survey input

```json
{
  "title": "Product feedback",
  "description": "Help us shape what comes next.",
  "version": 1,
  "questions": [
    {"id":"email","label":"Your email","kind":"email","required":true,"options":[]},
    {"id":"priority","label":"What should we improve?","kind":"choice","required":true,"options":["Onboarding","Performance"]}
  ]
}
```

Titles: 1–100 trimmed characters. Descriptions: 0–400. Question IDs: 1–48 ASCII letters, digits, underscores or hyphens. Labels: 1–100. At most 10 questions. Kinds: `text`, `email`, `choice`. Choices require 2–8 unique trimmed options, each 1–60 characters. Non-choice questions must have no options.

Draft creation starts at version 1. Saves and state changes increment the version. Publishing requires at least one question. Once published, only `published → closed → published` transitions are allowed.

## Submission

```json
{
  "requestId": "0b4f2076-a8b9-40dc-a9fc-378c1a1b0fc4",
  "answers": {"email":"reader@example.com","priority":"Performance"}
}
```

Text answers are bounded to 2000 characters; email answers to 254. Required answers cannot be blank after trimming. Choice answers must exactly match a published option. Omitted optional answers become empty strings. Unknown answer IDs and unknown JSON properties are rejected. Total JSON body limit: 64 KiB.

Retry an ambiguous request with the **same request ID and answers**. Exact retries return the same receipt ID. Reusing the ID with different answers returns 409. A new request ID represents a new response, even from the same person. A closed form rejects new submissions but can replay a previously accepted receipt.

## Responses and errors

Successful requests return 200 except draft creation, which returns 201. Application errors contain `{message:string}`. Framework-level malformed JSON, size-limit and rate-limit responses may use their standard HTTP body format.

- **400:** invalid schema/answers/credentials fields, forbidden state transition or invalid antiforgery token.
- **401:** missing/expired/revoked session or invalid login.
- **404:** unavailable draft/public form, missing resource or another account's private form.
- **409:** stale version, fixed published schema, duplicate account email or conflicting request ID.
- **410:** public form closed for new submissions.
- **413:** body limit exceeded.
- **429:** authentication/submission rate limit.

Authentication passwords require 10–128 characters. Email addresses are normalized to lowercase. Public schema and receipt endpoints do not return owner email or private responses.
