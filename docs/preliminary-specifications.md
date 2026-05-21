# Misty – System Specifications

**Version:** 2.0  
**Project:** Diploma Thesis: Scalable Cloud-Native Messaging Platform

---

## 1. Introduction

Misty is a cloud-native messaging platform enabling real-time group and direct communication. It is built as a **modular monolith** following Clean Architecture principles, deployed on Azure as a containerized, horizontally scalable application.

The system is intentionally scoped as a modular monolith rather than a microservices architecture. The internal module boundaries are designed so that individual modules could be extracted into independent services as scale demands, but the operational cost of distribution is not paid until it is genuinely necessary. This is the architecturally mature position for a system at this scale and stage, and the primary subject of academic analysis in the thesis.

**Core capabilities:**
- Channel-based group messaging with roles and permissions
- One-to-one direct messaging
- Real-time delivery via SignalR with horizontal scalability
- AI assistant integration per channel
- File attachments and user avatars
- Moderation tools (mute, ban, warn)

**Explicitly out of scope:**
- Voice and video communication
- End-to-end encryption
- Microservices decomposition
- Kubernetes orchestration
- Event sourcing or full CQRS

---

## 2. Requirements

### 2.1 Functional Requirements

#### Users
- Register, authenticate, and manage profiles
- Set and update profile avatars
- Block and unblock other users
- Blocked users cannot message each other or interact with each other's content in shared channels

#### Channels
- Create, update, and delete channels
- Join channels via invite code or direct join (public channels)
- View and retrieve channel details and member lists
- Channel creator is automatically assigned the Owner role

#### Membership, Roles & Permissions
- Membership links a user to a channel and carries one or more roles
- Roles are channel-scoped and carry a set of permission flags (`ChannelPermission` flags enum)
- Permissions are enforced server-side on every operation
- Each channel has an Owner role with full permissions; exactly one member holds it
- Roles and permissions can be assigned and revoked by authorized members

#### Messaging
- Send, edit, and delete messages in channels and direct conversations
- Cursor-based pagination for message history
- Flat replies: a message may reference one top-level message as a reply target; replies to replies are not permitted
- Reply metadata (target message ID and content preview) is included in message history responses; if the reply target has been tombstoned, its ID is preserved and the content preview reflects the cleared or placeholder state
- Client-provided idempotency key on send to prevent duplicate messages

#### Direct Messaging
- Initiate one-to-one conversations; each pair of users has at most one conversation
- Retrieve conversation list and message history
- Mark messages and conversations as read

#### Reactions
- Add and remove emoji reactions on messages
- Each user may apply a given reaction type once per message
- Aggregated reaction counts are included in message responses

#### Attachments
- Upload files associated with messages, user avatars, or channel icons
- Retrieve attachments via CDN-served URLs
- Delete attachments in accordance with deletion rules below

#### Moderation
- Apply mute, ban, and warn actions to users within a channel
- Actions may be time-limited (with an expiry duration) or indefinite (until explicitly revoked)
- Only one active action of a given type per user per channel
- Authorized members may revoke active moderation actions
- Moderation actions are enforced during permission validation

#### Deletion Behavior

| Entity | Behavior |
|---|---|
| Users | Soft-deleted; profile data anonymized, content preserved |
| Channels, conversations, memberships, roles, moderation actions | Soft-deleted |
| Messages | Hard-deleted; if the message has been replied to, its content is cleared (or replaced with a placeholder), `IsDeleted` is set to `true`, and the record is retained as a tombstone. Tombstones remain visible in message history responses to preserve stream order and reply relationships. They are excluded from edit, reaction, and moderation operations, but are returned by all message history and pagination queries. |
| Attachments | Hard-deleted |

### 2.2 Non-Functional Requirements

#### Scalability
The stateless application tier must support horizontal scaling to handle **10,000 concurrent active users** and **1,000 messages per second** at initial scale, without architectural changes. State that must be shared across instances (permissions, presence, SignalR routing) is externalized to Redis.

#### Performance

| Operation | Target |
|---|---|
| Message send (p99) | ≤ 500 ms |
| Message history retrieval, first page (p99) | ≤ 300 ms |
| Permission validation, cache hit (p99) | ≤ 20 ms |
| Permission validation, cache miss (p99) | ≤ 150 ms |
| Real-time event delivery to client (p95) | ≤ 1 s |

#### Availability
- Core messaging and channel operations: **99.9% monthly uptime**
- Real-time delivery: **99.5% monthly uptime**
- Planned maintenance windows excluded from calculations
- Non-critical features degrade gracefully; core messaging is never blocked by failures in asynchronous subsystems

#### Security
- JWT authentication required on all endpoints except registration and login
- Access token validity: 15 minutes; refresh token validity: 7 days, single-use, rotated on each use
- Channel permissions enforced server-side on every write and read operation
- Rate limiting applied per authenticated user (see §5.3)
- User data anonymized on soft deletion

#### Consistency
- Strong consistency within individual operations (message writes, permission checks)
- Eventual consistency acceptable for cache updates and async event propagation
- Cache misses fall back to authoritative SQL reads; stale cache data cannot produce incorrect authorization outcomes

#### Observability
- Structured logging with Serilog (console + Application Insights sink)
- Distributed tracing via OpenTelemetry -> Application Insights
- Custom metrics: message throughput, SignalR connection count, Service Bus queue depth
- Health check endpoint (`/health`) covering SQL, Redis, and Service Bus connectivity
- Alerting configured for: Redis backplane failure, Service Bus dead-letter queue growth, application error rate

---

## 3. Architecture

### 3.1 Architectural Style

Misty is a **modular monolith** - a single deployable process organized into four internal modules with enforced interface boundaries. Each module encapsulates its own domain logic, application services, and data access. Cross-module dependencies are expressed through defined interfaces or events, never by direct repository or DbSet access across module boundaries.

This style is chosen deliberately. The system's data access patterns are highly relational (messages reference users, channels, and attachments), and forcing physical database separation would introduce distributed transaction complexity with no scalability benefit at this scale. The module structure is designed so that any single module can be extracted into an independent service in the future without rewrites: data ownership is clear, event contracts are designed as if crossing a network boundary, and cross-module interfaces are stable.

Cross-module reads must go through a dedicated interface (`IUserQueryService`, `IChannelQueryService`) rather than direct DbSet access, even when the underlying implementation uses the same DbContext. This rule applies equally to read-path queries: a module that needs data owned by another module must consume it via a query service, a projection, or a named contract interface, not by joining across module table boundaries in application code.

### 3.2 Modules

**User Management:** User accounts, authentication, profile data, and avatars. Issues and validates JWTs. Exposes block/unblock actions but delegates block relationship storage to Communication.

**Communication:** Channels, direct conversations, memberships, roles, permissions, moderation actions, and block relationships. This module is the **authority for access control** across the system. It owns all data that determines whether a user may perform an action.

**Messaging:** High-frequency content operations: sending, editing, deleting, and paginating messages; reactions; attachment metadata. Validates permissions through the Redis cache on the critical path, with synchronous fallback to Communication on cache miss. Publishes domain events to Service Bus after successful writes.

**Realtime Delivery:** Maintains the SignalR hub and consumes Service Bus events to push updates to connected clients. Has no business logic and no database access. Its sole responsibility is connection management and event fan-out.

Integration services between modules must remain small and focused on a specific responsibility. Cross-module communication occurs only through defined interfaces or events to preserve clear module boundaries and reducing coupling. Avoiding a large shared integration layer helps maintain the modular structure and simplifies potential future extraction of modules into independent services.

### 3.3 Clean Architecture Layering

Each module follows a four-layer Clean Architecture structure with a strict unidirectional dependency rule:

```
Domain -> Application -> Infrastructure -> API/Presentation
```

- **Domain:** Entities, enums, value objects. No framework dependencies.
- **Application:** Use cases, service interfaces, DTOs, validators (FluentValidation). No I/O.
- **Infrastructure:** EF Core repositories, Redis client, Service Bus publisher, blob storage, SignalR hub, background workers.
- **API / Presentation:** ASP.NET Core controllers, Blazor WASM frontend, DI composition root.

The project layout mirrors this: `Misty.Domain`, `Misty.Application`, `Misty.Infrastructure`, `Misty.Api`, `Misty.Web` (Blazor WASM), `Misty.Tests`.

### 3.4 Synchronous vs. Asynchronous Communication

**Synchronous (HTTP):** All user-facing operations: message sends, channel management, profile updates, and authentication. Permission validation is synchronous, served from the Redis cache on the fast path.

**Asynchronous (Service Bus):** Side effects that do not block the user's perceived outcome.

| Event | Producers | Consumers |
|---|---|---|
| `MessageCreated` | Messaging | Realtime Delivery (SignalR push), AIResponseWorker |
| `MessageEdited` | Messaging | Realtime Delivery |
| `MessageDeleted` | Messaging | Realtime Delivery |
| `ReactionChanged` | Messaging | Realtime Delivery |
| `MembershipChanged` | Communication | CacheInvalidationWorker |
| `RoleChanged` | Communication | CacheInvalidationWorker |
| `ModerationActionApplied` | Communication | CacheInvalidationWorker |

The message **write path** is synchronous: `POST /messages` -> validate -> write to SQL -> publish event -> `201 Created`. The SignalR delivery is a downstream side effect of that event and is not awaited by the write path.

### 3.5 Message Send Flow

```
Client
  └─► POST /api/v1/channels/{id}/messages
        └─► Permission check -> Redis cache (hit: ≤20ms / miss: SQL ≤150ms)
              └─► Idempotency check (Message.IdempotencyKey)
                    └─► Persist to Azure SQL
                          └─► Publish MessageCreated -> Service Bus
                                └─► 201 Created ← client
                                      │
                    (async, ≤1s p95)  └─► Realtime Delivery consumes event
                                            └─► IHubContext -> SignalR push
                                            └─► AIResponseWorker (if AI enabled)
```

### 3.6 Real-Time Architecture

SignalR is used for persistent WebSocket connections. The **Redis backplane** (`AddSignalR().AddStackExchangeRedis()`) enables any application instance to deliver messages to clients connected to any other instance, fixing the single-instance limitation in the prototype.

Clients connect on login and join SignalR groups for their channels and conversations. Group naming convention: `channel:{id}`, `conversation:{id}`.

Connection tracking (presence / online status) is backed by Redis rather than in-process memory, eliminating the instance-locality problem that affected the prototype.

Delivery is best-effort. If a client is disconnected, it recovers missed messages via the REST API on reconnection using cursor-based pagination.

### 3.7 Frontend Architecture

The frontend is a **Blazor WebAssembly** SPA. It runs entirely in the browser, communicates with the ASP.NET Core API over HTTPS, and maintains a single SignalR WebSocket connection.

This replaces Blazor Server, which was the primary scalability bottleneck in the prototype:
- Blazor Server requires a persistent server-side circuit per user -> sticky sessions -> no stateless horizontal scaling
- Each Blazor Server user consumed two WebSocket connections (circuit + ChatHub)
- Blazor Server server-side UI state prevents true multi-instance deployment

With Blazor WASM, the API tier is fully stateless. All per-user state lives in the browser. The server holds no UI state.

---

## 4. Data Model

### 4.1 Database

A single **Azure SQL Database** instance holds all application data. EF Core 8 (Code First) manages the schema via incremental migrations. Tables are organized by SQL schema to reflect module ownership:

| SQL Schema | Module | Tables |
|---|---|---|
| `users` | User Management | `Users`, `RefreshTokens` |
| `comm` | Communication | `Channels`, `Conversations`, `Memberships`, `ChannelRoles`, `MemberRoles`, `ModerationActions`, `UserBlocks`, `AuditLog` |
| `msg` | Messaging | `Messages`, `Reactions`, `Attachments` |

Cross-module foreign keys are permitted (e.g., `msg.Messages.AuthorId` -> `users.Users.Id`) because the system is a monolith with one database. The schema separation enforces logical ownership and documents the extractability boundary: if Messaging were ever extracted to its own service, the tables it owns are already identified.

A single `ApplicationDbContext` is used, with `AddDbContextFactory<ApplicationDbContext>` registered alongside `AddDbContext` to support Blazor WASM's scoping model. Both the factory and the scoped registration target the same context type.

### 4.2 Key Entities

**User:** Profile data, Version (`rowversion` for optimistic concurrency).

**Channel:** Name, IsPrivate, InviteCode, IsAiAssistantEnabled, DefaultPermissions, denormalized MemberCount and LastMessageAt, Version.

**Membership:** UserId + ChannelId join; carries one or more ChannelRoles.

**ChannelRole:** Name, Permissions (`ChannelPermission` flags enum stored as `long`).

**Conversation:** UserId pair; unique constraint enforces one conversation per pair.

**Message:** ChannelId or ConversationId (exactly one set); AuthorId; Content; IdempotencyKey; ParentMessageId (self-reference for flat replies); EditedAt; IsDeleted (tombstone flag). When a replied-to message is deleted, its Content is cleared (or replaced with a placeholder), `IsDeleted` is set to `true`, and the record is retained in the database. Tombstoned messages remain in all message history and pagination responses, preserving stream order and reply chain integrity. They are excluded from edit, reaction, and moderation operations.

**MessageReaction:** MessageId + UserId + EmojiCode; unique constraint per (MessageId, UserId, EmojiCode).

**Attachment:** BlobUrl, Purpose (message / avatar / channel icon), associated entity reference.

**ModerationAction:** Type (mute / ban / warn), ChannelId, TargetUserId, IssuedByUserId, ExpiresAt (nullable), RevokedAt (nullable).

**UserBlock:** BlockerId + BlockedId; directional.

### 4.3 Concurrency and Idempotency

Optimistic concurrency is enforced on `User` and `Channel` via `rowversion` tokens (`Version` property mapped as EF Core concurrency token). Concurrent conflicting updates produce a `DbUpdateConcurrencyException`, which is translated to `409 Conflict` at the API layer.

`Message.IdempotencyKey` is a client-provided UUID. A unique constraint on `(ChannelId or ConversationId, AuthorId, IdempotencyKey)` prevents duplicate message creation from retried requests.

`OutboxMessage` uses a `rowversion` concurrency token to support optimistic concurrency. If multiple relay workers attempt to process the same entry concurrently, one succeeds while the others safely retry or skip the entry after receiving a `DbUpdateConcurrencyException`.

### 4.4 Migrations

Migrations are incremental and code-first. `database.MigrateAsync()` is called at startup (not via `UseMigrationsEndPoint`). A separate `ApplicationDbContextFactory` supports running `dotnet ef migrations add` from the CLI.

---

## 5. API Design

### 5.1 Style

RESTful HTTP API with resource-oriented endpoints. All endpoints are prefixed `/api/v1`. URI-based versioning; future breaking changes introduced as `/api/v2` with prior version support during transition.

### 5.2 Authentication

JWT Bearer tokens. Access token: 15-minute validity. Refresh token: 7-day validity, single-use, rotated on each use, revoked on logout or account deletion. SignalR connections authenticate using the access token passed in the connection query string.

Refresh token rotation is performed atomically within a single database transaction:

1. Validate the presented refresh token
2. Confirm the token has not been revoked or previously used
3. Mark the existing token as revoked
4. Generate a new refresh token
5. Persist both changes
6. Commit the transaction
7. Return the new access token and refresh token

This guarantees that refresh tokens remain single-use and prevents any period in which both the old and new tokens are valid simultaneously.

### 5.3 Rate Limiting

Enforced per authenticated user at the ASP.NET Core middleware level. Responses over the limit return `429 Too Many Requests` with a `Retry-After` header.

| Endpoint group | Limit |
|---|---|
| Message send | 30 req/min |
| Attachment upload | 10 req/min |
| All other endpoints | 300 req/min |

### 5.4 Pagination

Cursor-based pagination on all list endpoints. Clients provide an opaque cursor; the server returns a page and the next cursor. Cursors encode a stable sort position (typically creation timestamp + ID) and must not be constructed by clients. Supports both forward and backward traversal.

Tombstoned messages (`IsDeleted = true`) are included in paginated message history responses to preserve the continuity of reply threads and message stream ordering. Clients are expected to render them with placeholder content and without interactive controls (edit, react, moderate).

### 5.5 Error Responses

Consistent error envelope: `{ "type", "title", "status", "detail", "errors" }` (RFC 7807 Problem Details). Validation errors enumerate field-level failures. Asynchronous processing failures do not affect the HTTP response of the originating operation.

---

## 6. Infrastructure & Deployment

### 6.1 Infrastructure Stack

| Concern | Service | Notes |
|---|---|---|
| Application hosting | Azure App Service | 2–3 instances, Linux containers |
| Relational data | Azure SQL Database | General Purpose tier; retry-on-failure configured |
| Cache + SignalR backplane | Azure Cache for Redis | C1 Standard minimum |
| Async messaging | Azure Service Bus | Standard tier; topics with subscriptions |
| File storage | Azure Blob Storage | Private containers; CDN for delivery |
| Observability | Application Insights | OpenTelemetry SDK; structured logs + traces |
| Infrastructure as Code | Terraform | Separate workspaces for dev and prod |
| CI/CD | GitHub Actions | Build -> test -> Docker push -> App Service deploy |

### 6.2 Containerization

The ASP.NET Core host is packaged as a Docker image using `Dockerfile` (SDK build stage -> ASP.NET runtime stage). Images are tagged with the Git commit SHA and pushed to Azure Container Registry.

### 6.3 CI/CD Pipeline

```
Push to main
  └─► Build & restore
        └─► Run tests (unit + integration with Testcontainers)
              └─► Build Docker image
                    └─► Push to Azure Container Registry
                          └─► Deploy to Azure App Service (rolling update)
```

Pull requests run build and test only. Deployment triggers only on merge to `main`.

### 6.4 Configuration

All environment-specific configuration (connection strings, secrets, feature flags) is managed via Azure App Service Application Settings and Azure Key Vault references. No secrets are stored in source control. The application reads configuration via `IConfiguration` using the standard .NET host; no code changes are required between environments.

### 6.5 Health Checks

`/health` endpoint (registered via `AddHealthChecks()`) covers:
- EF Core `DbContext` connectivity (SQL Database)
- Redis connectivity
- Service Bus namespace reachability

Used by App Service for instance health monitoring.
### 6.6 Scalability Validation

The primary validation of the system’s horizontal scalability is the cross-instance SignalR scenario. Multiple App Service instances run with Session Affinity disabled, while clients connected to different instances exchange real-time events through the Redis backplane.

Validation consists of deploying two or more application instances, establishing SignalR connections to different instances, sending messages between connected clients, and verifying that real-time delivery occurs within the target latency (p95 ≤ 1 s) without dropped or duplicated events.

Successful execution confirms that:

* SignalR backplane distribution functions correctly across instances
* the application tier remains stateless
* no sticky sessions are required
* real-time delivery operates correctly under horizontal scaling

This scenario is the primary validation of the architectural claim that the redesigned system eliminates the scalability limitations of the prototype.

---

## 7. Technology Stack

| Layer | Technology |
|---|---|
| Language & runtime | C# / .NET 9 |
| API framework | ASP.NET Core Web API |
| Frontend | Blazor WebAssembly |
| Real-time | ASP.NET Core SignalR + Redis backplane |
| ORM | Entity Framework Core 9 |
| Validation | FluentValidation |
| In-process events | MediatR |
| Background workers | `IHostedService` / `BackgroundService` |
| Database | Azure SQL Database |
| Cache + backplane | Azure Cache for Redis (StackExchange.Redis) |
| File storage | Azure Blob Storage + CDN |
| Async messaging | Azure Service Bus |
| Observability | Serilog + OpenTelemetry + Application Insights |
| Testing | xUnit + Testcontainers + Respawn |
| Load testing | k6 |
| Containerization | Docker |
| CI/CD | GitHub Actions |
| Infrastructure as Code | Terraform |
| Auth | JWT (ASP.NET Core Identity + custom token service) |

---

## 8. Architectural Decisions

### ADR-1: Modular Monolith over Microservices

**Decision:** Single deployable process with four internal modules.

**Rationale:** Microservices trade simplicity for independent deployability and fault isolation. At this scale and team size (one developer), the operational cost (separate CI/CD pipelines, distributed tracing across services, network-level contract management, independent deployment coordination) exceeds the benefit. The system's data model is highly relational; forcing separate databases would require distributed transactions for operations that are currently a single SQL write.

The modular structure is designed for extractability: module boundaries are enforced by interfaces and event contracts, SQL schemas clearly assign table ownership, and no cross-module DbSet access is permitted. Extraction of any module into an independent service is a defined and low-cost path, not a rewrite.

### ADR-2: Single Azure SQL Database

**Decision:** One database instance, logical separation via SQL schemas.

**Rationale:** Polyglot persistence per module would require distributed transactions or eventual consistency for operations that span module boundaries (e.g., retrieving a message with author and channel metadata). These are standard JOIN operations in a relational model; making them eventually consistent adds complexity with no scalability benefit at this scale. SQL schemas (`users.`, `comm.`, `msg.`) achieve logical ownership separation and document the extractability boundary without paying the operational cost of multiple databases.

### ADR-3: Redis Scope

**Decision:** Redis is used exclusively for the permission/membership/block cache and as the SignalR backplane.

**Rationale:** These are the two roles where Redis earns its keep: permission checks are on every critical path (cache hit ≤20ms vs. SQL ≤150ms), and the SignalR backplane is the mechanism that makes real-time delivery horizontally scalable. Redis is not used as a general-purpose cache for message content (SQL is the source of truth and is fast enough with indexes), not as a queue (Service Bus handles that), and not for session state (JWT is stateless).

### ADR-4: Azure Service Bus over Kafka

**Decision:** Azure Service Bus Standard tier with topics and subscriptions.

**Rationale:** Kafka is optimized for very high-throughput stream processing with consumer group semantics and log compaction. The event volume this system will generate (hundreds to low thousands of events per second) is well within Service Bus capacity and does not require Kafka's operational complexity. Service Bus topics with subscriptions provide the fan-out pattern needed (one `MessageCreated` event -> Realtime Delivery + AIResponseWorker). Service Bus is fully managed, Azure-native, and requires no additional infrastructure.

### ADR-5: Blazor WASM over Blazor Server

**Decision:** Blazor WebAssembly SPA communicating with the API over HTTP.

**Rationale:** Blazor Server requires a stateful server-side circuit per connected user. This circuit cannot be load-balanced without sticky sessions (affinity routing), which prevents true stateless horizontal scaling of the web tier. Additionally, each Blazor Server user maintains two WebSocket connections: the Blazor circuit and the SignalR ChatHub connection. Blazor WASM eliminates both problems: the application tier is fully stateless, each user has exactly one WebSocket connection, and the browser holds all UI state. The migration cost from the prototype is manageable because the component model is similar.

### ADR-6: Synchronous Write Path, Asynchronous Side Effects

**Decision:** Message persistence is synchronous HTTP; real-time delivery and AI processing are async via Service Bus.

**Rationale:** The user's request completes when the message is durably written. Real-time delivery and AI responses are side effects; the user does not need to wait for them. Decoupling these via Service Bus ensures that SignalR unavailability or OpenAI latency cannot block message sends. This is also the correct pattern for avoiding the prototype's anti-pattern of calling OpenAI synchronously on the message write path.

### ADR-7: In-Process Events via MediatR

**Decision:** MediatR for in-process domain event dispatch within the application layer.

**Rationale:** The prototype's large `MessageService.cs` (29KB) handled send, edit, delete, reactions, AI triggering, and realtime notification in a single class. MediatR handlers decompose this into focused single-responsibility handlers (`SendMessageHandler`, `NotifyRealtimeHandler`, `TriggerAIHandler`) wired through a common notification pipeline. This is not CQRS, but an internal event dispatch within the monolith, using the same process and memory, without the complexity of a full CQRS read-model separation.

### ADR-8: Outbox Pattern for Service Bus Publishing

**Decision:** Transactional outbox pattern for publishing events to Service Bus.

**Rationale:** Publishing to Service Bus after a SQL write introduces a failure window: the write succeeds but the publish fails, leaving downstream consumers (Realtime Delivery, cache invalidation) unaware of the change. The outbox pattern persists the event to SQL in the same transaction as the write, then a background relay reads unpublished events and forwards them to Service Bus. When multiple relay instances run concurrently, they race to update each outbox row; the `rowversion` concurrency token on `OutboxMessage` ensures only one instance succeeds per entry, the other receives a `DbUpdateConcurrencyException` and retries or skips safely. This is consistent with the optimistic concurrency approach used for `User` and `Channel`.

This guarantees at-least-once delivery. Service Bus delivers events at-least-once; consumer idempotency is achieved by design rather than by a dedicated processed-events store. Cache invalidation operations are delete-only and naturally idempotent; SignalR fan-out tolerates duplicate delivery without side effects; AI response processing uses message-level uniqueness to avoid duplicate responses. Consumers do not maintain a separate deduplication table.
