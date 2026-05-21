# Misty – Database Schema

---

## `users.User`

| Column | Type | Notes |
|---|---|---|
| Id | `uniqueidentifier` | PK |
| Username | `nvarchar(32)` | Unique |
| DisplayName | `nvarchar(64)` | |
| Bio | `nvarchar(max)` | Nullable |
| AvatarUrl | `nvarchar(512)` | Nullable |
| IsDeleted | `bit` | Soft delete flag |
| DeletedAt | `datetime2` | Nullable |
| Version | `rowversion` | Optimistic concurrency token |

**Indexes**

- `UX_User_Username`

---

## `users.RefreshToken`

| Column | Type | Notes |
|---|---|---|
| Id | `uniqueidentifier` | PK |
| UserId | `uniqueidentifier` | FK → `users.User.Id` |
| TokenHash | `nvarchar(512)` | Stored hashed |
| CreatedAt | `datetime2` | |
| ExpiresAt | `datetime2` | |
| RevokedAt | `datetime2` | Nullable |

**Indexes**

- `IX_RefreshToken_UserId`

---

## `comm.Channel`

| Column | Type | Notes |
|---|---|---|
| Id | `uniqueidentifier` | PK |
| Name | `nvarchar(100)` | |
| IsPrivate | `bit` | |
| InviteCode | `nvarchar(32)` | Nullable |
| IsAiAssistantEnabled | `bit` | |
| DefaultPermissions | `bigint` | `ChannelPermission` flags |
| MemberCount | `int` | Denormalized |
| LastMessageAt | `datetime2` | Nullable |
| IsDeleted | `bit` | Soft delete flag |
| Version | `rowversion` | Optimistic concurrency token |

---

## `comm.Membership`

| Column | Type | Notes |
|---|---|---|
| Id | `uniqueidentifier` | PK |
| UserId | `uniqueidentifier` | FK → `users.User.Id` |
| ChannelId | `uniqueidentifier` | FK → `comm.Channel.Id` |
| IsDeleted | `bit` | Soft delete flag |

**Indexes**

- `IX_Membership_UserId_ChannelId`

---

## `comm.ChannelRole`

| Column | Type | Notes |
|---|---|---|
| Id | `uniqueidentifier` | PK |
| ChannelId | `uniqueidentifier` | FK → `comm.Channel.Id` |
| Name | `nvarchar(64)` | |
| Permissions | `bigint` | `ChannelPermission` flags |
| IsDeleted | `bit` | Soft delete flag |

---

## `comm.MemberRole`

| Column | Type | Notes |
|---|---|---|
| MembershipId | `uniqueidentifier` | FK → `comm.Membership.Id` |
| RoleId | `uniqueidentifier` | FK → `comm.ChannelRole.Id` |

**Primary Key**

- `(MembershipId, RoleId)`

---

## `comm.Conversation`

| Column | Type | Notes |
|---|---|---|
| Id | `uniqueidentifier` | PK |
| UserAId | `uniqueidentifier` | FK → `users.User.Id` |
| UserBId | `uniqueidentifier` | FK → `users.User.Id` |
| IsDeleted | `bit` | Soft delete flag |

**Constraints**

- `UserAId < UserBId`
- Unique `(UserAId, UserBId)`

---

## `comm.ModerationAction`

| Column | Type | Notes |
|---|---|---|
| Id | `uniqueidentifier` | PK |
| ChannelId | `uniqueidentifier` | FK → `comm.Channel.Id` |
| TargetUserId | `uniqueidentifier` | FK → `users.User.Id` |
| IssuedByUserId | `uniqueidentifier` | FK → `users.User.Id` |
| Type | `nvarchar(32)` | `ModerationActionType` |
| ExpiresAt | `datetime2` | Nullable |
| RevokedAt | `datetime2` | Nullable |
| IsDeleted | `bit` | Soft delete flag |

---

## `comm.UserBlock`

| Column | Type | Notes |
|---|---|---|
| Id | `uniqueidentifier` | PK |
| BlockerId | `uniqueidentifier` | FK → `users.User.Id` |
| BlockedId | `uniqueidentifier` | FK → `users.User.Id` |

**Indexes**

- `IX_UserBlock_BlockerId_BlockedId`

---

## `comm.AuditLog`

| Column | Type | Notes |
|---|---|---|
| Id | `uniqueidentifier` | PK |
| ChannelId | `uniqueidentifier` | FK → `comm.Channel.Id` |
| ActorUserId | `uniqueidentifier` | FK → `users.User.Id` |
| Action | `nvarchar(128)` | |
| OccurredAt | `datetime2` | |

---

## `msg.Message`

| Column | Type | Notes |
|---|---|---|
| Id | `uniqueidentifier` | PK |
| ChannelId | `uniqueidentifier` | Nullable FK → `comm.Channel.Id` |
| ConversationId | `uniqueidentifier` | Nullable FK → `comm.Conversation.Id` |
| AuthorId | `uniqueidentifier` | FK → `users.User.Id` |
| ParentMessageId | `uniqueidentifier` | Nullable FK → `msg.Message.Id` |
| Content | `nvarchar(max)` | Nullable for tombstones |
| IdempotencyKey | `uniqueidentifier` | |
| IsDeleted | `bit` | Tombstone flag |
| EditedAt | `datetime2` | Nullable |
| CreatedAt | `datetime2` | |

**Constraints**

- Exactly one of `ChannelId` or `ConversationId` must be non-null

**Indexes**

- `IX_Message_ChannelId_CreatedAt_Id`
- `IX_Message_ConversationId_CreatedAt_Id`

---

## `msg.MessageReaction`

| Column | Type | Notes |
|---|---|---|
| MessageId | `uniqueidentifier` | FK → `msg.Message.Id` |
| UserId | `uniqueidentifier` | FK → `users.User.Id` |
| EmojiCode | `nvarchar(32)` | |

**Primary Key**

- `(MessageId, UserId, EmojiCode)`

---

## `msg.Attachment`

| Column | Type | Notes |
|---|---|---|
| Id | `uniqueidentifier` | PK |
| MessageId | `uniqueidentifier` | Nullable FK → `msg.Message.Id` |
| UserId | `uniqueidentifier` | Nullable FK → `users.User.Id` |
| ChannelId | `uniqueidentifier` | Nullable FK → `comm.Channel.Id` |
| BlobUrl | `nvarchar(512)` | |
| Purpose | `nvarchar(32)` | `AttachmentPurpose` |
| CreatedAt | `datetime2` | |

**Constraints**

- Exactly one of `MessageId`, `UserId`, or `ChannelId` must be non-null

---

## `msg.OutboxMessage`

| Column | Type | Notes |
|---|---|---|
| Id | `uniqueidentifier` | PK |
| EventType | `nvarchar(128)` | |
| Payload | `nvarchar(max)` | Serialized event payload |
| OccurredAt | `datetime2` | |
| ProcessedAt | `datetime2` | Nullable |
| AttemptCount | `int` | |
| Version | `rowversion` | Optimistic concurrency token |

**Indexes**

- `IX_OutboxMessage_ProcessedAt_OccurredAt`

---

## Enums

### `ChannelPermission` (flags, stored as `bigint`)

| Value | Name | Description |
|---|---|---|
| `0` | `None` | No permissions |
| `1` | `SendMessages` | Post messages in the channel |
| `2` | `DeleteAnyMessage` | Delete any member's messages |
| `4` | `ManageChannel` | Update channel name, settings, and icon |
| `8` | `ManageRoles` | Create, edit, and assign channel roles |
| `16` | `ManageMembers` | Remove members from the channel |
| `32` | `MuteMembers` | Apply and revoke mute actions |
| `64` | `BanMembers` | Apply and revoke ban actions |
| `128` | `WarnMembers` | Issue warnings to members |
| `256` | `ManageAttachments` | Delete any member's attachments |

The `Owner` role is assigned full permissions on channel creation and is the only role that may transfer ownership or delete the channel.

---

### `ModerationActionType` (stored as `nvarchar(32)`)

| Value | Description |
|---|---|
| `Mute` | Prevents the target user from sending messages in the channel |
| `Ban` | Removes and bars the target user from the channel |
| `Warn` | Issues a formal warning; no access restriction applied |

---

### `AttachmentPurpose` (stored as `nvarchar(32)`)

| Value | Description |
|---|---|
| `Message` | File attached to a message |
| `Avatar` | User profile picture |
| `ChannelIcon` | Channel icon image |
