# TransCard Exam | Payments API

A secure, idempotent Payments API built with ASP.NET Core 9.0, Entity Framework Core, and JWT authentication.

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

No database setup required — the app uses SQLite, which is created automatically on first run (`transcard.db`). If you make schema changes, delete the existing `transcard.db` file and restart — it will be recreated with the updated schema.

To browse the database:
- **VS Code:** Install [SQLite Viewer](https://marketplace.visualstudio.com/items?itemName=qwtel.sqlite-viewer), then click on `TransCard/transcard.db` in the file explorer
- **Visual Studio 2022+:** Install the [SQLite and SQL Server Compact Toolbox](https://marketplace.visualstudio.com/items?itemName=ErikEJ.SQLServerCompactSQLiteToolbox) extension, then connect to `transcard.db` via the toolbox panel

## Quick Start

```bash
cd TransCard
dotnet restore
dotnet run
```

The API will be available at **http://localhost:5279**.

Swagger UI is available in development mode at **http://localhost:5279/swagger**.

### Testing with REST Client

The file [`.docs/api-tests.http`](.docs/api-tests.http) contains pre-built requests covering every API flow (auth, payments, validation errors, idempotency, event history). To use it:

1. Install the REST Client extension:
   - **VS Code:** Install [REST Client](https://marketplace.visualstudio.com/items?itemName=humao.rest-client) by Huachao Mao
   - **Visual Studio 2022+:** Built-in support — no extension needed (`.http` files are supported natively)
2. Start the app with `dotnet run`
3. Open `.docs/api-tests.http` and click **Send Request** above each request

Run request **#1** first — it captures the JWT token and reuses it automatically in all subsequent requests.

## Architecture & Design

The application follows **Clean Architecture** with **CQRS** (Command Query Responsibility Segregation) and **Event Sourcing** — separating read and write operations, with an append-only audit trail for every payment state transition.

For full details, see:
- [Architecture, Design Decisions & Assessment Criteria](.docs/architecture.md) — Clean Architecture layers, CQRS, event sourcing, idempotency strategy, database schema, and how the app addresses the assessment criteria
- [Sequence Diagrams](.docs/sequence-diagrams.md) — Mermaid diagrams covering every API flow (auth, payments, events, idempotency, race conditions, error handling). To view the diagrams graphically:
  - **VS Code:** Install the [Markdown Preview Mermaid Support](https://marketplace.visualstudio.com/items?itemName=bierner.markdown-mermaid) extension, then open the file and press `Ctrl+Shift+V` to preview
  - **Visual Studio 2022+:** Open the file with the built-in Markdown editor — Mermaid rendering is supported natively from v17.12+
  - **GitHub:** Diagrams render automatically when viewing the file on GitHub

## Authentication

The API uses JWT Bearer tokens. To get a token, call the auth endpoint with the test credentials:

```bash
curl -X POST http://localhost:5279/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{"clientId": "test-client", "clientSecret": "test-secret"}'
```

Use the returned token in subsequent requests:

```bash
curl http://localhost:5279/api/payments/{id} \
  -H "Authorization: Bearer <your-token>"
```

### Test Credentials

| Field | Value |
|-------|-------|
| Client ID | `test-client` |
| Client Secret | `test-secret` |

## API Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/auth/token` | No | Generate a JWT token |
| POST | `/api/payments` | Yes | Process a payment |
| GET | `/api/payments/{id}` | Yes | Retrieve a payment by ID |
| GET | `/api/payments/{id}/events` | Yes | Retrieve payment event history |

### POST /api/payments

**Request body:**
```json
{
  "amount": 100.00,
  "currency": "USD",
  "referenceId": "order-12345"
}
```

**Supported currencies:** AUD, CAD, CHF, CNY, EUR, GBP, JPY, NZD, SEK, USD

**Responses:**
- `201 Created` — New payment processed successfully
- `200 OK` — Idempotent retry (same ReferenceId returns the original result)
- `400 Bad Request` — Validation error (invalid amount, currency, or missing fields)
- `401 Unauthorized` — Missing or invalid JWT token

**Response body:**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "amount": 100.00,
  "currency": "USD",
  "status": "Completed",
  "referenceId": "order-12345",
  "createdAt": "2026-02-26T05:00:00Z",
  "processedAt": "2026-02-26T05:00:01Z"
}
```

Payment status will be one of: `Completed`, `Failed`, or `Pending`.

### GET /api/payments/{id}/events

Returns the append-only event history (audit trail) for a payment, ordered chronologically.

**Responses:**
- `200 OK` — Event list (may be empty if payment has no events)
- `404 Not Found` — Payment does not exist
- `401 Unauthorized` — Missing or invalid JWT token

**Response body:**
```json
[
  {
    "id": "a1b2c3d4-...",
    "paymentId": "3fa85f64-...",
    "eventType": "PaymentCreated",
    "payload": "{\"amount\":100.00,\"currency\":\"USD\",\"status\":\"Pending\",\"referenceId\":\"order-12345\"}",
    "occurredAt": "2026-02-26T05:00:00Z"
  },
  {
    "id": "e5f6g7h8-...",
    "paymentId": "3fa85f64-...",
    "eventType": "PaymentCompleted",
    "payload": "{\"amount\":100.00,\"currency\":\"USD\",\"status\":\"Completed\",\"referenceId\":\"order-12345\"}",
    "occurredAt": "2026-02-26T05:00:01Z"
  }
]
```

Event types: `PaymentCreated`, `PaymentCompleted`, `PaymentFailed`, `PaymentPending`