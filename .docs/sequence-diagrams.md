# Sequence Diagrams

## POST /api/auth/token

```mermaid
sequenceDiagram
    participant Client
    participant AuthController
    participant TokenService
    participant JwtSettings

    Client->>AuthController: POST /api/auth/token { clientId, clientSecret }
    AuthController->>TokenService: GenerateToken(clientId, clientSecret)
    TokenService->>JwtSettings: Read TestClientId, TestClientSecret

    alt Credentials match
        TokenService->>TokenService: Build JWT (claims, expiry, signing)
        TokenService-->>AuthController: TokenResponse { token, expiresAt }
        AuthController-->>Client: 200 OK + TokenResponse
    else Credentials invalid
        TokenService-->>AuthController: null
        AuthController-->>Client: 401 Unauthorized + ApiErrorResponse
    end
```

---

## POST /api/payments (Model Validation Failure)

```mermaid
sequenceDiagram
    participant Client
    participant JwtMiddleware
    participant ModelValidation
    participant PaymentsController

    Client->>JwtMiddleware: POST /api/payments + Bearer token

    alt Token missing or invalid
        JwtMiddleware-->>Client: 401 Unauthorized + ApiErrorResponse
    else Token valid
        JwtMiddleware->>ModelValidation: Authenticated request
    end

    ModelValidation->>ModelValidation: Validate DataAnnotations (Required, Range, StringLength)

    alt Validation fails (e.g. Amount ≤ 0, missing ReferenceId, Currency not 3 chars)
        Note over ModelValidation: InvalidModelStateResponseFactory (Program.cs)
        ModelValidation-->>Client: 400 Bad Request + { type, title, status, errors, traceId }
    else Validation passes
        ModelValidation->>PaymentsController: Valid request
        Note over PaymentsController: Controller action executes
    end
```

---

## POST /api/payments (New Payment)

```mermaid
sequenceDiagram
    participant Client
    participant JwtMiddleware
    participant PaymentsController
    participant Decorator as EventSourcingDecorator
    participant CommandHandler
    participant PaymentsDb
    participant PaymentProcessor

    Client->>JwtMiddleware: POST /api/payments + Bearer token

    alt Token missing or invalid
        JwtMiddleware-->>Client: 401 Unauthorized + ApiErrorResponse
    else Token valid
        JwtMiddleware->>PaymentsController: Authenticated request
    end

    PaymentsController->>PaymentsController: Validate currency code

    alt Currency invalid
        PaymentsController-->>Client: 400 Bad Request + ApiErrorResponse
    end

    PaymentsController->>Decorator: HandleAsync(ProcessPaymentCommand)
    Decorator->>PaymentsDb: BEGIN TRANSACTION
    Decorator->>CommandHandler: HandleAsync(command)
    CommandHandler->>PaymentsDb: Query by ReferenceId

    alt ReferenceId already exists (idempotent retry)
        PaymentsDb-->>CommandHandler: Existing Payment
        CommandHandler-->>Decorator: ProcessPaymentResult(isExisting: true, events: [])
        Decorator->>PaymentsDb: COMMIT (no events to persist)
        Decorator-->>PaymentsController: result
        PaymentsController-->>Client: 200 OK + PaymentResponse
    else ReferenceId is new
        CommandHandler->>PaymentsDb: INSERT Payment (status: Pending)
        CommandHandler->>PaymentProcessor: Process(amount, currency)
        PaymentProcessor-->>CommandHandler: "Completed" | "Failed" | "Pending"
        CommandHandler->>PaymentsDb: UPDATE Payment status + processedAt
        CommandHandler-->>Decorator: ProcessPaymentResult(isExisting: false, events: [Created, {Status}])
        Decorator->>PaymentsDb: INSERT PaymentEvents (from PendingEvents)
        Decorator->>PaymentsDb: COMMIT
        Decorator-->>PaymentsController: result
        PaymentsController-->>Client: 201 Created + PaymentResponse
    end
```

---

## POST /api/payments (Concurrent Race Condition)

```mermaid
sequenceDiagram
    participant Client A
    participant Client B
    participant CommandHandler
    participant PaymentsDb

    Note over Client A, Client B: Both send same ReferenceId simultaneously

    Client A->>CommandHandler: HandleAsync(ref: "order-1")
    Client B->>CommandHandler: HandleAsync(ref: "order-1")

    CommandHandler->>PaymentsDb: Query ReferenceId "order-1"
    PaymentsDb-->>CommandHandler: Not found (for both)

    CommandHandler->>PaymentsDb: INSERT Payment + Events (Client A) — succeeds
    CommandHandler->>PaymentsDb: INSERT Payment + Events (Client B) — UNIQUE constraint violation

    CommandHandler->>PaymentsDb: ROLLBACK (Client B transaction)
    CommandHandler->>PaymentsDb: Query ReferenceId "order-1" (AsNoTracking)
    PaymentsDb-->>CommandHandler: Payment from Client A

    CommandHandler-->>Client A: 201 Created + PaymentResponse
    CommandHandler-->>Client B: 200 OK + same PaymentResponse
```

---

## GET /api/payments/{id}

```mermaid
sequenceDiagram
    participant Client
    participant JwtMiddleware
    participant PaymentsController
    participant QueryHandler
    participant PaymentsDb

    Client->>JwtMiddleware: GET /api/payments/{id} + Bearer token

    alt Token missing or invalid
        JwtMiddleware-->>Client: 401 Unauthorized + ApiErrorResponse
    else Token valid
        JwtMiddleware->>PaymentsController: Authenticated request
    end

    PaymentsController->>QueryHandler: HandleAsync(GetPaymentByIdQuery)
    QueryHandler->>PaymentsDb: FindAsync(id)

    alt Payment found
        PaymentsDb-->>QueryHandler: Payment entity
        QueryHandler-->>PaymentsController: PaymentResponse
        PaymentsController-->>Client: 200 OK + PaymentResponse
    else Payment not found
        PaymentsDb-->>QueryHandler: null
        QueryHandler-->>PaymentsController: null
        PaymentsController-->>Client: 404 Not Found + ApiErrorResponse
    end
```

---

## GET /api/payments/{id}/events

```mermaid
sequenceDiagram
    participant Client
    participant JwtMiddleware
    participant PaymentsController
    participant GetPaymentHandler
    participant GetEventsHandler
    participant PaymentsDb

    Client->>JwtMiddleware: GET /api/payments/{id}/events + Bearer token

    alt Token missing or invalid
        JwtMiddleware-->>Client: 401 Unauthorized + ApiErrorResponse
    else Token valid
        JwtMiddleware->>PaymentsController: Authenticated request
    end

    PaymentsController->>GetPaymentHandler: HandleAsync(GetPaymentByIdQuery)
    GetPaymentHandler->>PaymentsDb: FindAsync(id)

    alt Payment not found
        PaymentsDb-->>GetPaymentHandler: null
        GetPaymentHandler-->>PaymentsController: null
        PaymentsController-->>Client: 404 Not Found + ApiErrorResponse
    else Payment found
        PaymentsDb-->>GetPaymentHandler: Payment entity
        GetPaymentHandler-->>PaymentsController: PaymentResponse
        PaymentsController->>GetEventsHandler: HandleAsync(GetPaymentEventsQuery)
        GetEventsHandler->>PaymentsDb: SELECT FROM PaymentEvents WHERE PaymentId = id ORDER BY OccurredAt
        PaymentsDb-->>GetEventsHandler: List<PaymentEvent>
        GetEventsHandler-->>PaymentsController: List<PaymentEventResponse>
        PaymentsController-->>Client: 200 OK + PaymentEventResponse[]
    end
```

---

## Unhandled Exception Flow

```mermaid
sequenceDiagram
    participant Client
    participant GlobalExceptionMiddleware
    participant Controller/Service

    Client->>GlobalExceptionMiddleware: Any request
    GlobalExceptionMiddleware->>Controller/Service: Forward request

    Controller/Service->>Controller/Service: Unhandled exception thrown

    Controller/Service-->>GlobalExceptionMiddleware: Exception bubbles up
    GlobalExceptionMiddleware->>GlobalExceptionMiddleware: Log error with TraceId
    GlobalExceptionMiddleware->>GlobalExceptionMiddleware: Build ApiErrorResponse (hide details if not Development)
    GlobalExceptionMiddleware-->>Client: 500 Internal Server Error + ApiErrorResponse (JSON)
```
