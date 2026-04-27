# Misty – Preliminary Specifications

## 1. Overview

Misty is a cloud-native messaging platform built with .NET and Azure. It enables real-time communication through channels and direct messages, organized around core entities: users, channels, conversations, messages, memberships, and roles.

Users participate in channel-based group discussions and one-to-one direct conversations. Interactions include sending and reacting to messages, managing channel membership, and applying moderation actions. The platform is built as a modular monolith following Clean Architecture principles, with a design that supports horizontal scaling and event-driven communication.

**Goals:**
- Scalable system design
- Event-driven architecture
- Cloud deployment practices

---

## 2. Scope

### 2.1 In Scope

- **User Management** — registration, authentication, profile management, avatars, and user blocking
- **Channels** — creation, management, membership, and retrieval
- **Messaging** — sending, editing, deleting, and retrieving messages in channels and direct conversations; message reactions; flat replies to messages
- **Direct Messaging** — one-to-one conversations with message history and read/unread state
- **Membership, Roles, and Permissions** — channel membership management, role assignment, and role-based access control
- **Attachments** — file upload, retrieval, and association with messages and profiles
- **Moderation** — mute, ban, warn actions within channels, enforced via roles and permissions

### 2.2 Out of Scope

- Voice and video communication
- End-to-end encryption
- Advanced automated moderation (e.g., AI-based filtering)
- Federation between multiple deployments
- Full-featured mobile or desktop clients

### 2.3 System Boundaries

Misty is responsible for managing users, channels, conversations, messages, memberships, roles, permissions, reactions, and moderation. Infrastructure concerns, such as storage, messaging bus, hosting, are handled through external services and are not part of the application domain.

---

## 3. Terminology

**User:** An individual account within the system. Users participate in channels and conversations, send messages, and interact with other users.

**Channel:** A group communication context containing a shared stream of messages.

**Conversation:** A one-to-one communication context between two users, corresponding to a direct message thread.

**Direct Message (DM):** The user-facing term for a conversation. Internally represented as a Conversation entity.

**Message:** A unit of communication sent within a channel or conversation. May contain text, reactions, and attachments.

**Reaction:** A user interaction on a message, typically represented by an emoji.

**Membership:** The relationship between a user and a channel, carrying the user's roles and permissions within that channel.

**Role:** A named set of permissions scoped to a channel and assigned to memberships.

**Permission:** An enum value representing an allowed action within a channel. Permissions are not stored as independent records but they are defined in code and assigned to Roles as a collection of enum values.

**Attachment:** A file associated with a message, user profile, or channel. Stored externally and referenced within the system.

**Moderation Action:** A restriction applied to a user within a channel (e.g., mute, ban). Enforced based on roles and permissions.

---

## 4. Domain Model

### 4.1 Entities

**User**
- Participates in Channels through Memberships
- Participates in Conversations
- Sends Messages and performs Reactions

**Channel**
- Contains many Messages
- Has many Members via Memberships
- Defines Roles and associated Permissions

**Conversation**
- Involves exactly two Users
- Contains many Messages
- *Constraint:* Unique per pair of users

**Message**
- Belongs to either a Channel or a Conversation (never both)
- Sent by a User
- Can have many Reactions and zero or more Attachments
- May reference another Message as a reply target
- *Constraints:* A reply must reference a Message within the same communication context. Replies are flat — a reply target must itself be a top-level message; replies to replies are not permitted.

**Reaction**
- Belongs to a Message; created by a User
- *Constraint:* A user may apply a given reaction type only once per message

**Membership**
- Links a User to a Channel; carries one or more Roles
- *Constraint:* A user can have only one membership per channel

**Role**
- Assigned to Memberships; defines a set of Permissions
- Permissions are enum values stored as a collection on the Role — they are not a separate entity or database table
- *Constraint:* Scoped to a single channel; examples of permission values: `SendMessage`, `DeleteMessage`, `ManageChannel`, `ModerateUsers`

**Attachment**
- Primarily associated with a Message
- May also be associated with a User (avatar) or Channel (icon)

**Moderation Action**
- Targets a User within a Channel; issued by another User
- Examples: `mute`, `ban`, `warn`
- *Constraints:* Only one active action of a given type per user per channel. Actions may be time-limited (expiring automatically) or indefinite (until explicitly revoked).

### 4.2 Key Relationships

| Relationship | Description |
|---|---|
| User <-> Channel | via Membership |
| Channel -> Messages | one-to-many |
| Conversation -> Messages | one-to-many |
| Message -> Reactions, Attachments | one-to-many |
| Message -> Message (reply) | optional self-reference, one level deep |
| Membership -> Roles | one-to-many |
| Role -> Permissions | many-to-many |
| ModerationAction -> User within Channel | scoped restriction |

---

## 5. Requirements

### 5.1 Functional Requirements

#### 5.1.1 User Management

1. The system shall allow users to register and authenticate.
2. The system shall allow users to create, retrieve, update, and delete their profiles in accordance with the deletion behavior defined in §5.1.8.
3. The system shall allow users to set and update profile avatars.
4. The system shall allow users to block and unblock other users.
5. The system shall prevent blocked users from initiating direct messages with each other.
6. The system shall prevent blocked users from seeing or interacting with each other in shared channels.

#### 5.1.2 Channels

1. The system shall allow users to create channels.
2. The system shall allow users to join and leave channels.
3. The system shall allow users to view channels they are members of.
4. The system shall allow retrieval of channel details, including members and metadata.
5. The system shall allow channel owners or authorized users to update or delete channels in accordance with §5.1.8.

#### 5.1.3 Membership, Roles, and Permissions

1. The system shall associate users with channels through memberships.
2. The system shall allow assignment and revocation of roles within a channel.
3. The system shall enforce permissions based on roles assigned to a user within a channel.
4. The system shall restrict actions (e.g., messaging, moderation) based on those permissions.
5. The system shall ensure that each channel has an Owner role with full permissions.
6. Upon channel creation, the creating user shall be automatically assigned the Owner role.

#### 5.1.4 Messaging

1. The system shall allow users to send messages in channels they are members of.
2. The system shall allow users to send messages in conversations they participate in.
3. The system shall allow users to edit and delete their own messages in accordance with §5.1.8.
4. The system shall allow authorized users to delete messages of other users.
5. The system shall provide message history retrieval using cursor-based pagination.
6. The system shall allow users to reply to an existing message within the same channel or conversation.
7. The system shall associate a reply with its target message and preserve this reference in message history.
8. The system shall return reply metadata — including the target message identifier and a preview of the target message content — when retrieving messages.
9. Replies are flat: a reply may only target a top-level message. Replying to an existing reply is not permitted.

#### 5.1.5 Direct Messaging

1. The system shall allow users to initiate conversations with other users.
2. The system shall ensure conversations exist uniquely between pairs of users.
3. The system shall allow users to retrieve their conversations.
4. The system shall allow users to view message history within a conversation.
5. The system shall allow users to mark messages or conversations as read.

#### 5.1.6 Reactions

1. The system shall allow users to add reactions to messages.
2. The system shall allow users to remove their own reactions.
3. The system shall ensure a user can apply a given reaction type only once per message.
4. The system shall provide aggregated reaction data for messages.

#### 5.1.7 Attachments

1. The system shall allow users to upload attachments associated with messages.
2. The system shall allow retrieval of attachments via accessible URLs.
3. The system shall allow deletion of attachments in accordance with §5.1.8.

#### 5.1.8 Deletion Behavior

The system supports both soft and hard deletion depending on entity type.

- **Soft deletion** makes an entity inaccessible to users while retaining underlying data for consistency and auditing.
- **Hard deletion** permanently removes data from the system.

Entity-specific rules:
- **Soft deleted:** users, channels, conversations, memberships, roles, moderation actions. User data is anonymized upon deletion while related content is preserved.
- **Hard deleted:** messages and attachments, with one exception: if a message has been replied to, its content is removed but the message record is retained as a tombstone to preserve reply context. The tombstone is visible in reply references but excluded from standard message history retrieval.

#### 5.1.9 Blocking Behavior

1. Blocking is bidirectional: neither party can initiate a direct message with the other.
2. Blocked users shall not see each other's messages in shared channels.
3. Blocked users shall not interact with each other's content (e.g., reactions).
4. Existing messages sent before a block are not retroactively removed.
5. Unblocking restores normal interaction between the two users.

Block enforcement is applied server-side at the application layer during permission and access validation. The Communication component is the authority for block relationships. When a request involves interaction between two users — sending a direct message, retrieving messages, or adding a reaction — the system checks for an active block before any state change is persisted. Block relationships are cached alongside permission data to minimize latency on the critical path.

#### 5.1.10 Moderation

1. The system shall allow authorized users to apply moderation actions within a channel.
2. The system shall support moderation actions: mute, ban, and warn.
3. The system shall enforce restrictions imposed by active moderation actions.
4. The system shall allow revocation of moderation actions by authorized users.
5. Only users with the appropriate permissions may perform moderation actions.
6. Moderation actions may be time-limited or indefinite. Time-limited actions require an expiry duration at the time of application and expire automatically. Indefinite actions remain in effect until explicitly revoked.
7. Moderation actions are enforced during permission validation and may prevent actions such as sending messages or interacting with content.

---

### 5.2 Non-Functional Requirements

The specified targets represent initial performance and scalability objectives derived from anticipated usage patterns. These targets are intended to guide system design and validation and may be refined based on testing and observed system behavior.

#### 5.2.1 Scalability

The system shall support horizontal scaling to handle up to 10.000 concurrent active users and 1.000 messages per second at initial scale. Horizontal scaling shall accommodate growth beyond these targets without architectural changes. Stateless components shall be capable of being replicated across multiple instances.

#### 5.2.2 Performance

The following latency targets apply under normal load:

| Operation | Target |
|---|---|
| Message send (p99) | ≤ 500ms (API receipt to persistence) |
| Message retrieval, first page (p99) | ≤ 300ms |
| Permission validation, cache hit (p99) | ≤ 20ms |
| Permission validation, cache miss (p99) | ≤ 150ms |
| Real-time delivery, event to client (p95) | ≤ 1 second |

Frequently accessed data — permissions, memberships, block relationships — shall be cached in Redis to reduce latency and database load. The number of synchronous cross-component calls on the critical path shall be minimized.

#### 5.2.3 Availability

The system targets **99,9% monthly availability** for core messaging and channel operations (approximately 44 minutes downtime per month). Real-time delivery targets **99,5% monthly availability**. Planned maintenance windows are excluded from availability calculations.

In the event of partial degradation, the system shall prioritize core messaging functionality and allow non-critical features to degrade gracefully.

#### 5.2.4 Reliability

Failures in non-critical components or asynchronous processes shall not prevent core functionality. Asynchronous communication shall be resilient to delays and temporary failures. Critical operations — message creation, permission validation — shall complete reliably.

#### 5.2.5 Security

Authentication is required for all endpoints except registration and login, implemented via JWT. Authorization is enforced using role-based access control within communication contexts. Permissions are validated for all operations that modify or access protected resources. User data is anonymized upon account deletion.

#### 5.2.6 Consistency

Strong consistency is maintained for critical operations such as message creation and permission validation. Eventual consistency is acceptable for secondary processes — cache updates, asynchronous event handling — provided it does not compromise correctness of user-facing operations.

#### 5.2.7 Maintainability

The system is structured using Clean Architecture with clear component boundaries, enabling independent development and modification. The codebase shall be organized to facilitate testing, refactoring, and future extension.

#### 5.2.8 Rate Limiting

Rate limiting is enforced at the API level per authenticated user. Requests exceeding the limit receive a `429 Too Many Requests` response with a `Retry-After` header.

| Operation | Limit |
|---|---|
| Message send | 30 requests/minute |
| Attachment upload | 10 requests/minute |
| General API endpoints | 300 requests/minute |

#### 5.2.9 Error Handling

The API returns standard HTTP status codes: `4xx` for client errors, `5xx` for server-side failures. Validation errors include sufficient detail for clients to identify and correct the issue. Error responses follow a consistent structure. Failures in asynchronous processing do not affect the success of the originating operation. All errors are logged and surfaced through the observability layer.

---

## 6. System Architecture

### 6.1 Architectural Style

Misty is a modular monolith built on Clean Architecture principles. The system is organized into distinct components with clear boundaries and well-defined interfaces. Each component encapsulates its own domain logic, application behavior, and data, and interacts with others only through those interfaces.

Each component follows a four-layer structure:

- **Domain Layer** — core entities and business rules
- **Application Layer** — use cases and orchestration logic
- **Infrastructure Layer** — data access and external integrations
- **Interface Layer** — API endpoints and entry points

### 6.2 Components

The system is organized into four components:

**User Management** handles user accounts, authentication, profile data, and user-related assets such as avatars. It exposes block and unblock as user-facing actions but does not own the block relationship data.

**Communication** manages communication contexts like channels and direct conversations, along with memberships, roles, permissions, moderation actions, and block relationships. It is the authority for access control across the system. When the Messaging component needs to validate a permission or check a block, it goes through Communication.

**Messaging** handles high-frequency operations: creating, retrieving, editing, and deleting messages, managing reactions, and tracking attachment metadata. It depends on Communication for permission validation but manages that dependency through the caching layer, avoiding synchronous cross-component calls on the critical path wherever possible.

**Realtime Delivery** maintains persistent client connections and delivers events to connected users in real time. It has no business logic and performs no data persistence — its sole responsibility is connection management and event fan-out. It is driven entirely by events published to the messaging infrastructure by other components.

### 6.3 Component Separation: Messaging vs. Communication

The Messaging and Communication components are intentionally separated. Communication owns access control and context, the slower-changing, consistency-critical data. Messaging owns the high-frequency content operations. Separating them prevents the throughput requirements of messaging from being constrained by the consistency requirements of access control.

The cross-component dependency (Messaging validating against Communication) is managed through Redis. Permission, membership, and block data are cached close to the Messaging component and invalidated by events when the underlying data changes. Synchronous fallback to Communication occurs only on cache misses.

### 6.4 Data Ownership

Each component owns and is solely responsible for its data. No component directly reads or writes another component's data store.

| Component | Owns |
|---|---|
| User Management | User accounts, profile data |
| Communication | Channels, conversations, memberships, roles, moderation actions, block relationships |
| Messaging | Messages, reactions, attachment metadata |

### 6.5 Scaling

Messaging operations are expected to handle high throughput and frequent access. Communication operations are less frequent but require stronger consistency. Stateless components can be scaled horizontally and independently. The Realtime Delivery component uses Redis as a SignalR backplane, allowing any application instance to deliver events to clients connected to any other instance.

### 6.6 Consistency Model

Strong consistency is maintained within individual operations like message creation, permission validation. Eventual consistency is applied across components where appropriate, including cache updates and derived data propagated via events. Stale cache data cannot compromise correctness because the system falls back to authoritative validation on cache misses.

---

## 7. API Design

### 7.1 Style

The system exposes a RESTful HTTP API following resource-oriented design principles:

- `GET` — retrieve a resource or collection
- `POST` — create a new resource
- `PUT` / `PATCH` — update an existing resource
- `DELETE` — remove a resource

### 7.2 Versioning

The API uses URI-based versioning. All endpoints are prefixed with `/api/v1`. Future breaking changes are introduced under a new version prefix (e.g., `/api/v2`) while prior versions remain available during transition periods.

### 7.3 Authentication

All endpoints except registration and login require a valid JWT in the `Authorization: Bearer` header. Tokens are stateless and self-contained, allowing any application instance to validate them without shared session state.

The system issues two token types on successful authentication:

- **Access token** — valid for 15 minutes; included in every API request and used to authenticate SignalR connections.
- **Refresh token** — valid for 7 days; used to obtain a new access token without re-authenticating. Refresh tokens are single-use and rotated on each use — the prior token is invalidated immediately. Refresh tokens are revoked on explicit logout or account deletion. If a refresh token is expired or revoked, the client must re-authenticate.

If a SignalR connection drops and the access token has expired, the client must refresh before reconnecting.

### 7.4 Pagination

List endpoints that may return large result sets use cursor-based pagination. The client provides an opaque cursor representing a position in the result set; the server returns the next page alongside a new cursor. Cursors must not be constructed or interpreted by clients. Pagination supports directional retrieval (before or after a cursor) and results are ordered by creation time to ensure stable behavior on live datasets.

---

## 8. Communication Model

### 8.1 Synchronous Communication

Synchronous communication is used for operations requiring immediate validation: permission checks, membership verification, and data retrieval. The Messaging component is the primary consumer of synchronous cross-component calls — specifically, calls to Communication for permission validation on cache misses.

### 8.2 Asynchronous Communication

Asynchronous communication propagates state changes across components via Azure Service Bus. Events include message creation and deletion, reaction updates, membership changes, and moderation actions. Consumers react to events to update derived data, invalidate cache entries, or trigger side effects. Event handling is designed to tolerate delays and partial failures without affecting the originating operation.

### 8.3 Real-Time Delivery

Real-time delivery is driven by the event stream. When a component publishes an event, the Realtime Delivery component consumes it and pushes the update to connected clients subscribed to the relevant context. Clients establish authenticated SignalR connections on login and subscribe to the channels and conversations they participate in. Delivery is best-effort — the API is the source of truth, and clients retrieve missed data upon reconnection.

### 8.4 Message Flow

The following describes the lifecycle of a sent message:

1. The client sends a POST request to the Messaging component.
2. The Messaging component checks Redis for cached permission and block data for the requesting user.
3. On a cache miss, the Messaging component calls the Communication component synchronously to validate membership, roles, and block status.
4. If validation fails, the request is rejected with an appropriate error response.
4a. If the message includes a reply reference, the Messaging component validates that the target message exists within the same communication context and is a top-level message.
5. If validation succeeds, the message is persisted.
6. A message-created event is published to Azure Service Bus.
7. The Realtime Delivery component consumes the event and pushes the message to all clients subscribed to the relevant channel or conversation.
8. Disconnected clients retrieve the message via the API on reconnection.
9. Other components (e.g., cache invalidation handlers) may also consume the event.

### 8.5 Message Ordering

Messages within a communication context are ordered by creation time, applied consistently in API responses and used as the basis for real-time delivery sequencing. No global ordering is guaranteed across different channels or conversations. Strict ordering under concurrent high-load conditions is subject to the limitations of distributed processing.

### 8.6 Realtime Delivery: Failure Behavior

If the Realtime Delivery component becomes unavailable, clients lose persistent connections. Core messaging functionality via the REST API is unaffected. Clients detect connection loss and attempt reconnection with exponential backoff, then retrieve missed data through the API.

Events published to Azure Service Bus are retained for 1 hour if the Realtime Delivery component cannot consume them. Events not consumed within this window are moved to a dead-letter queue and are not replayed — clients retrieve missed data through the API. If the Redis backplane becomes unavailable, cross-instance real-time delivery is disrupted; clients on the same instance as the publishing operation may still receive events, but others will not. Core messaging is unaffected in either case.

Monitoring and alerting shall be configured for Realtime Delivery component health, Service Bus dead-letter queue depth, and Redis backplane connectivity.

---

## 9. Data Management

### 9.1 Storage

Structured data, such as users, channels, messages, memberships, and all relational data, is stored in Azure SQL Database, accessed via Entity Framework Core. File and media assets are stored in Azure Blob Storage and served through a CDN. The storage layer is abstracted from domain and application logic.

### 9.2 Caching

Redis (Azure Cache for Redis) serves two roles: an application cache for frequently accessed data, and the SignalR backplane for cross-instance real-time delivery.

As a cache, Redis stores permission data, membership data, block relationships, and recent message data. Cached data is an optimization layer, not a source of truth. Cache entries are invalidated by events when underlying data changes. On a cache miss, the system falls back to authoritative validation.

As a SignalR backplane, Redis enables any application instance to deliver events to clients connected to any other instance, supporting horizontal scaling of the Realtime Delivery component.

### 9.3 Data Lifecycle

Entities are subject to soft or hard deletion based on their type, as defined in §5.1.8. Soft-deleted entities are excluded from active operations but retained in the database. Hard-deleted content is permanently removed. User data is anonymized on soft deletion while preserving associated content.

---

## 10. Deployment

### 10.1 Hosting

The application is hosted on Azure App Service in a containerized environment. Multiple instances can be deployed behind a load balancer to handle increased load. Configuration is managed externally and varies per environment (development, production) without requiring code changes.

### 10.2 Infrastructure

| Concern | Service |
|---|---|
| Application hosting | Azure App Service |
| Structured data | Azure SQL Database |
| File storage | Azure Blob Storage + CDN |
| Caching + SignalR backplane | Azure Cache for Redis |
| Async messaging | Azure Service Bus |
| Infrastructure as Code | Terraform |

### 10.3 Observability

The system includes logging, monitoring, and distributed tracing to support troubleshooting and provide visibility into performance and reliability. Alerting is configured for critical infrastructure components including the Service Bus dead-letter queue and Redis backplane connectivity.

---

## 11. Technology Stack

| Layer | Technology |
|---|---|
| Language & runtime | C# / .NET 8 |
| Web framework | ASP.NET Core Web API |
| Real-time | ASP.NET Core SignalR |
| ORM | Entity Framework Core |
| Database | Azure SQL Database |
| Cache + backplane | Redis (Azure Cache for Redis) |
| File storage | Azure Blob Storage |
| Asset delivery | CDN |
| Async messaging | Azure Service Bus |
| Hosting | Azure App Service |
| Infrastructure as Code | Terraform |
| Authentication | JWT |
| Version control | Git / GitHub |
| Project management | GitHub Projects |