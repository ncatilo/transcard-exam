# Architecture: Clean Architecture with CQRS

This application follows **Clean Architecture** principles — organising code into layers with clear dependency rules so that business logic remains independent of frameworks, databases, and HTTP infrastructure. Each layer has a defined responsibility, and dependencies point inward:

```
┌──────────────────────────────────────────────────────┐
│  Presentation Layer (Controllers, Middleware)         │  ← HTTP concerns only
├──────────────────────────────────────────────────────┤
│  Application Layer (Handlers/Commands, Handlers/Queries) │  ← Business use cases
├──────────────────────────────────────────────────────┤
│  Domain Layer (Models/Entities)                      │  ← Core domain objects
├──────────────────────────────────────────────────────┤
│  Infrastructure Layer (Data, Services/External)      │  ← EF Core, JWT, processors
└──────────────────────────────────────────────────────┘
```

## How the layers map to the codebase

| Layer | Folder(s) | Responsibility |
|---|---|---|
| **Presentation** | `Controllers/`, `Middleware/`, `Models/Requests/`, `Models/Responses/` | HTTP routing, request/response shaping, validation, status codes, exception handling |
| **Application** | `Handlers/Commands/`, `Handlers/Queries/` | Business use cases expressed as CQRS command and query handlers |
| **Domain** | `Models/Entities/` | Core `Payment` entity — framework-agnostic, no HTTP or persistence logic |
| **Infrastructure** | `Data/`, `Services/`, `Configuration/` | EF Core persistence, JWT token generation, simulated payment gateway |

## CQRS (Command Query Responsibility Segregation)

Within the Application layer, the CQRS pattern separates operations that change state (commands) from operations that read state (queries). In a payment processing system this separation is particularly valuable:

- **Auditability:** Payment mutations (charges, refunds) and payment lookups have fundamentally different risk profiles. Isolating commands into dedicated handlers makes it straightforward to add logging, authorization checks, or retry policies to write operations without affecting reads.
- **Scalability:** Read traffic (checking payment status) typically far exceeds write traffic (submitting payments). CQRS makes it possible to scale and optimise each path independently — for example, adding read replicas or caching to query handlers without touching the transactional command path.
- **Clarity of intent:** Each handler has a single responsibility. `ProcessPaymentCommandHandler` owns the full mutation lifecycle (idempotency, transaction, processing), while `GetPaymentByIdQueryHandler` is a pure read with no side effects. A new developer can immediately tell whether a piece of code can cause a charge.

The implementation is intentionally minimal — two generic interfaces (`ICommandHandler<TCommand, TResult>`, `IQueryHandler<TQuery, TResult>`) with concrete handlers registered in DI. No MediatR or pipeline framework is needed at this scale; the pattern provides the structural benefit without the overhead.

```
Handlers/
  ICommandHandler.cs              — generic command interface
  IQueryHandler.cs                — generic query interface
  IProduceEvents.cs               — interface for results that carry pending events
  EventSourcingDecorator.cs       — generic decorator (transaction + event persistence)
  Commands/
    ProcessPaymentCommand.cs      — command input (Amount, Currency, ReferenceId)
    ProcessPaymentResult.cs       — command output (Response, IsExistingPayment, PendingEvents)
    ProcessPaymentCommandHandler.cs — mutation logic (idempotency, processing)
  Queries/
    GetPaymentByIdQuery.cs        — query input (Id)
    GetPaymentByIdQueryHandler.cs — read-only lookup
    GetPaymentEventsQuery.cs      — query input (PaymentId)
    GetPaymentEventsQueryHandler.cs — read-only event history
```

## Event Sourcing (Decorator Pattern)

Event sourcing is implemented as a **cross-cutting concern** using the decorator pattern — separating audit trail logic from business logic. The `EventSourcingDecorator<TCommand, TResult>` wraps any command handler whose result implements `IProduceEvents`, providing automatic transaction management and event persistence.

**How it works:**
1. The command handler builds `PaymentEvent` objects in-memory during execution and returns them via `PendingEvents` on the result — but does not persist them
2. The `EventSourcingDecorator` wraps the handler call in a database transaction, reads the `PendingEvents`, persists them to the `PaymentEvents` table, and commits
3. If the handler or persistence fails, the decorator rolls back the entire transaction — no orphaned events or partial state

**Flow:**
```
Controller → EventSourcingDecorator → ProcessPaymentCommandHandler
                  │                            │
                  │  BEGIN TRANSACTION          │
                  │──────────────────────────▶  │ Execute business logic
                  │                            │ Build events in-memory
                  │  ◀──────────────────────── │ Return result + PendingEvents
                  │  Persist PendingEvents      │
                  │  COMMIT                     │
```

**Why the decorator pattern:**
- **Separation of concerns:** The handler contains only business logic (idempotency, processing). Transaction management and event persistence are handled by the decorator.
- **Reusability:** Any future command handler that implements `IProduceEvents` gets event sourcing automatically — just register it with the decorator in DI.
- **Testability:** The handler can be tested in isolation (asserting on `PendingEvents` in the result), while the decorator can be tested separately with a mock inner handler.

**Why this matters for payments:**
- **Regulatory compliance:** Financial systems often require a tamper-evident log of every state change. The append-only event store satisfies this by never updating or deleting event records.
- **Dispute resolution:** When a customer disputes a charge, the event history shows exactly when the payment was created and what the processor returned, with JSON payload snapshots at each step.
- **Debugging:** If a payment is stuck in "Pending" or unexpectedly "Failed", the event timeline reveals exactly where in the lifecycle the issue occurred.

## Clean Architecture enforces these by design:

- **Controllers never touch business logic.** They translate HTTP requests into commands/queries, pass them to handlers, and translate results back into HTTP responses. Swapping from controller-based to Minimal API would not require changing a single handler.
- **Handlers never touch HTTP.** They depend on `PaymentsDbContext` and `PaymentProcessor` — infrastructure that is injected, not imported. The `ProcessPaymentCommandHandler` could run in a message queue consumer without modification.
- **Infrastructure is replaceable.** Swapping SQLite for SQL Server or other changes one line in `Program.cs`. Replacing the simulated `PaymentProcessor` with a real Stripe integration means implementing one class. Nothing else in the application needs to change.

## Design Decisions & Trade-offs

### Controller-based API (vs. Minimal API)
Chose controller-based architecture for the Presentation layer. Controllers handle HTTP concerns (validation, status codes) and delegate to Application-layer handlers for business logic. This makes the separation of concerns explicit and aligns with the assessment requirement for clean service/controller separation.

### SQLite
SQLite was chosen for zero setup — clone and run immediately without installing a database server. The schema and constraints are identical to what a full relational database would use. The database file (`transcard.db`) is created automatically on first startup.

### Idempotency Strategy

Idempotency guarantees that processing the same request repeatedly has the same effect as processing it once — no matter how many times a client retries, only one payment is created.

**How it works in this app:**
The client provides a `ReferenceId` with each payment request. This acts as an idempotency key — a unique identifier chosen by the caller to represent a single intended payment.

Two layers of protection enforce this:

1. **Application layer:** Before creating a new payment, the command handler queries the database for an existing payment with the same `ReferenceId`. If one is found, the original result is returned immediately with `200 OK` instead of `201 Created`, and no new payment is processed.

2. **Database layer:** A unique constraint (`IX_Payments_ReferenceId`) on the `ReferenceId` column acts as a safety net for race conditions. If two identical requests arrive simultaneously and both pass the application-level check, the second insert will fail with a constraint violation. The handler catches this, looks up the existing payment, and returns it — so the caller still gets a consistent response.

**Example flow:**
```
Request 1: POST /api/payments { referenceId: "order-123", amount: 50, currency: "USD" }
  → Payment created, status "Completed", returns 201 Created

Request 2: POST /api/payments { referenceId: "order-123", amount: 50, currency: "USD" }
  → Existing payment found, returns 200 OK with the same payment from Request 1
  → No second charge is created
```

### JWT Secret Management
The JWT signing key is stored only in `appsettings.Development.json`, not in the base `appsettings.json`. In a deployed environment, secrets would come from environment variables, Azure Key Vault, or `dotnet user-secrets`.

### Simulated Payment Processing
The `PaymentProcessor` class (Infrastructure layer) simulates a payment gateway with randomized outcomes (~70% success, ~20% failure, ~10% pending). This would be replaced with a real gateway integration (Stripe, Adyen, etc.) when connecting to actual payment infrastructure. The class is registered as a singleton and marked `virtual` for easy mocking in tests.

### Error Response Format
All errors use a consistent JSON format inspired by RFC 7807 Problem Details:
```json
{
  "type": "ValidationError",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "Additional context...",
  "traceId": "...",
  "errors": { "Amount": ["Amount must be greater than zero."] }
}
```
This applies to validation errors, auth failures, not-found responses, and unhandled exceptions.

### Database Schema

**Payments table:**

| Column | Type | Constraints |
|--------|------|-------------|
| Id | GUID | Primary Key |
| Amount | decimal(18,2) | Required |
| Currency | varchar(3) | Required |
| Status | varchar(50) | Required |
| ReferenceId | varchar(256) | Required, Unique Index |
| CreatedAt | datetime | Indexed |
| ProcessedAt | datetime | Nullable |

**Indexes:**
- `IX_Payments_ReferenceId` — Unique, enforces idempotency at DB level
- `IX_Payments_Status` — For filtering by payment status
- `IX_Payments_CreatedAt` — For time-range queries and ordering

**PaymentEvents table (Event Store):**

| Column | Type | Constraints |
|--------|------|-------------|
| Id | GUID | Primary Key |
| PaymentId | GUID | Required, Indexed |
| EventType | varchar(50) | Required |
| Payload | text | Required (JSON snapshot) |
| OccurredAt | datetime | Indexed |

**Indexes:**
- `IX_PaymentEvents_PaymentId` — For retrieving all events for a payment
- `IX_PaymentEvents_OccurredAt` — For chronological ordering

## How This App Addresses the Assessment Criteria

### API Design
- RESTful endpoints with clear resource-based routing (`POST /api/payments`, `GET /api/payments/{id}`, `POST /api/auth/token`)
- Correct HTTP status codes for each scenario: `201 Created` for new payments, `200 OK` for idempotent retries and lookups, `400` for validation, `401` for auth failures, `404` for missing resources
- Consistent RFC 7807-inspired error response format across all failure modes (validation, auth, not-found, server errors) with `type`, `title`, `status`, `detail`, and `traceId`
- Swagger/OpenAPI documentation auto-generated for interactive testing

### Business Logic Separation
- **Presentation layer** (Controllers) handles only HTTP concerns: request binding, input validation (currency codes), choosing the correct status code, and shaping responses
- **Application layer** separates commands from queries via CQRS — `ProcessPaymentCommandHandler` owns all mutation logic (idempotency, transaction, processing), `GetPaymentByIdQueryHandler` owns read-only lookups with no side effects
- **Infrastructure layer** (`PaymentProcessor`, `TokenService`, `PaymentsDbContext`) provides replaceable implementations injected via DI — swapping SQLite for PostgreSQL or the simulated processor for Stripe requires changing one class each
- Generic interfaces (`ICommandHandler<,>`, `IQueryHandler<,>`) decouple the Presentation layer from Application-layer implementations, enabling testability

### Idempotency
- Two-layer protection prevents duplicate charges: application-level `ReferenceId` lookup followed by a database unique constraint as a race-condition safety net
- Idempotent retries return `200 OK` with the original payment result — the caller cannot accidentally create a second charge
- The `DbUpdateException` catch block handles the concurrent-insert edge case gracefully, rolling back and returning the existing payment

### Reliability
- Database transactions wrap the full payment lifecycle (insert + processing + status update), ensuring atomicity — a failure mid-process does not leave orphaned records
- Global exception middleware catches all unhandled exceptions and returns structured JSON instead of stack traces or HTML error pages
- JWT validation with tight clock skew (`30s`), proper expired/invalid token handling returning consistent error JSON
- Input validation at multiple levels: DataAnnotation attributes on request DTOs, custom currency validation in the controller, unique constraints in the database

### Code Clarity
- Clean Architecture folder structure maps directly to layers: `Controllers/` and `Middleware/` (Presentation), `Handlers/` (Application), `Models/Entities/` (Domain), `Data/` and `Services/` (Infrastructure)
- Primary constructors throughout for concise dependency injection
- Small, focused classes — each file has a single responsibility
- No unnecessary abstractions or over-engineering — the simulated processor is a single class, not a strategy pattern with factories
- Consistent naming conventions and C# 12/.NET 9 idioms (primary constructors, pattern matching, file-scoped namespaces)
