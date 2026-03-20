using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Misty.Domain.Entities;
using Misty.Infrastructure.Identity;

namespace Misty.Infrastructure
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<User> DomainUsers => Set<User>();
        public DbSet<Channel> Channels => Set<Channel>();
        public DbSet<ChannelMember> ChannelMembers => Set<ChannelMember>();
        public DbSet<ChannelMemberRole> ChannelMemberRoles => Set<ChannelMemberRole>();
        public DbSet<ChannelRole> ChannelRoles => Set<ChannelRole>();
        public DbSet<Conversation> Conversations => Set<Conversation>();
        public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();
        public DbSet<Message> Messages => Set<Message>();
        public DbSet<MessageReaction> MessageReactions => Set<MessageReaction>();
        public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();
        public DbSet<Attachment> Attachments => Set<Attachment>();
        public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
        public DbSet<ChannelAuditLog> ChannelAuditLogs => Set<ChannelAuditLog>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // User + ApplicationUser are soft-deleted via anonymization. PII is scrubbed across both tables, account disabled. Rows are never physically removed.
            // Pre-condition: user must not own any channels (must transfer ownership first).
            // Anonymization Service will do these:
            //   1. ApplicationUser: scrub Email (deleted_{id}@removed.invalid), NormalizedEmail (DELETED_{ID}@REMOVED.INVALID), PasswordHash (null), SecurityStamp (new Guid), PhoneNumber (null), disable account (LockoutEnabled = true, LockoutEnd = DateTimeOffset.MaxValue)
            //   2. User: scrub Username (deleted_{id}), NormalizedUsername (DELETED_{ID}), DisplayName ("Deleted User"), Bio (null), AvatarAttachmentId (null), DeletedAt (now)
            //   3. Hard-delete avatar Attachment row + blob storage file
            //   4. Hard-delete all UserBlocks (in both directions)
            //   5. Hard-delete all MessageReactions by user
            //   6. Set LeftAt on all active ChannelMember records, hard-delete their ChannelMemberRole assignments
            //   7. Null out ChannelAuditLog.IpAddress for all entries by the user (IP is high level PII (CJEU C-582/14), keeping it adds little value).
            //   8. ModerationAction, Messages, Attachments, ConversationParticipants remain but they reference the anonymized row
            //   9. ChannelAuditLog entries remain. ActorDisplayName snapshot preserves readability (Art. 6(1)(f))
            builder.Entity<User>(e =>
            {
                e.HasKey(u => u.UserId);
                e.Property(u => u.UserId).HasMaxLength(450);

                e.Property(u => u.Username)
                    .IsRequired()
                    .HasMaxLength(256);

                e.Property(u => u.NormalizedUsername)
                    .IsRequired()
                    .HasMaxLength(256);

                e.HasIndex(u => u.NormalizedUsername)
                    .IsUnique()
                    .HasFilter("[DeletedAt] IS NULL");

                e.Property(u => u.DisplayName)
                    .IsRequired()
                    .HasMaxLength(100);

                e.Property(u => u.Bio).HasMaxLength(500);

                e.Property(u => u.DeletedAt);
                e.HasIndex(u => u.DeletedAt);

                e.Property(u => u.Version)
                    .IsRowVersion();

                e.HasOne(u => u.Avatar)
                    .WithOne()
                    .HasForeignKey<User>(u => u.AvatarAttachmentId)
                    // Service handles attachment cleanup; detach reference only
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Channel is soft-deleted first (query-filtered out). Background process later permanently deletes the row, cascading to roles, memberships, messages, reactions, and moderation actions.
            // OwnerUserId Ownership can be transferred by updating OwnerUserId.
            // DefaultPermissions stores the @everyone base permissions (bitfield). Members without explicit role assignments get these permissions.
            builder.Entity<Channel>(e =>
            {
                e.HasKey(c => c.ChannelId);
                e.HasQueryFilter(c => c.DeletedAt == null);

                e.Property(c => c.Name).HasMaxLength(100);
                e.Property(c => c.Description).HasMaxLength(500);
                e.Property(c => c.InviteCode).HasMaxLength(50);
                e.Property(c => c.CreatedByUserId).HasMaxLength(450);
                e.Property(c => c.OwnerUserId).HasMaxLength(450);
                e.Property(c => c.Version).IsRowVersion();

                e.HasOne(c => c.Icon)
                    .WithOne()
                    .HasForeignKey<Channel>(c => c.IconAttachmentId)
                    // Service handles attachment cleanup; detach reference only
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasIndex(c => c.InviteCode)
                    .IsUnique()
                    .HasFilter("[InviteCode] IS NOT NULL");

                e.HasIndex(c => c.DeletedAt)
                    .HasFilter("[DeletedAt] IS NOT NULL");

                e.HasIndex(c => c.LastMessageAt)
                    .HasFilter("[LastMessageAt] IS NOT NULL");

                e.HasOne(c => c.Creator)
                    .WithMany(u => u.CreatedChannels)
                    .HasForeignKey(c => c.CreatedByUserId)
                    // User rows are never hard-deleted
                    .OnDelete(DeleteBehavior.NoAction);

                e.HasOne(c => c.Owner)
                    .WithMany(u => u.OwnedChannels)
                    .HasForeignKey(c => c.OwnerUserId)
                    // User rows are never hard-deleted
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // ChannelMember is soft-deleted via LeftAt timestamp. On leave/kick: roles removed by service, LeftAt set. On user anonymization: LeftAt set for all active memberships, roles removed by service. On channel permanent deletion: hard-deleted via cascade. User can rejoin, creating a new record.
            builder.Entity<ChannelMember>(e =>
            {
                e.HasKey(cm => cm.ChannelMemberId);
                e.HasQueryFilter(cm => cm.LeftAt == null);

                e.Property(cm => cm.UserId).HasMaxLength(450);

                e.HasIndex(cm => new { cm.ChannelId, cm.UserId })
                    .IsUnique()
                    .HasFilter("[LeftAt] IS NULL");

                e.HasIndex(cm => cm.UserId);

                e.HasOne(cm => cm.User)
                    .WithMany(u => u.Memberships)
                    .HasForeignKey(cm => cm.UserId)
                    // User rows are never hard-deleted
                    .OnDelete(DeleteBehavior.NoAction);

                e.HasIndex(cm => cm.LeftAt)
                    .HasFilter("[LeftAt] IS NULL");

                e.HasOne(cm => cm.Channel)
                    .WithMany(c => c.Members)
                    .HasForeignKey(cm => cm.ChannelId)
                    // If channel deleted, remove all memberships
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ChannelRole is hard-deleted. Role assignments cascade-delete. Permissions stored as a bitfield. Position determines hierarchy. Higher position = more authority. Users can only manage roles below their highest role's position.
            builder.Entity<ChannelRole>(e =>
            {
                e.HasKey(cr => cr.ChannelRoleId);
                e.HasQueryFilter(cr => cr.Channel.DeletedAt == null);

                e.Property(cr => cr.Name).HasMaxLength(100);
                e.Property(cr => cr.Version).IsRowVersion();

                e.HasIndex(cr => new { cr.ChannelId, cr.Name }).IsUnique();
                e.HasIndex(cr => new { cr.ChannelId, cr.Position });

                e.HasOne(cr => cr.Channel)
                    .WithMany(c => c.Roles)
                    .HasForeignKey(cr => cr.ChannelId)
                    // If channel deleted, remove all roles
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ChannelMemberRole (join table) is hard-deleted when: (1) role is deleted (cascade), (2) member leaves/kicked (service), (3) user account is anonymized (service), (4) channel permanently deleted (cascade via channel to role).
            builder.Entity<ChannelMemberRole>(e =>
            {
                e.HasKey(cmr => new { cmr.ChannelMemberId, cmr.ChannelRoleId });
                e.HasQueryFilter(cmr => cmr.Member.LeftAt == null);

                e.HasOne(cmr => cmr.Member)
                    .WithMany(cm => cm.AssignedRoles)
                    .HasForeignKey(cmr => cmr.ChannelMemberId)
                    // NoAction avoids multiple cascade paths from Channel. Service removes assignments when member leaves
                    .OnDelete(DeleteBehavior.NoAction);

                e.HasOne(cmr => cmr.Role)
                    .WithMany(cr => cr.MemberAssignments)
                    .HasForeignKey(cmr => cmr.ChannelRoleId)
                    // If role deleted, remove all assignments
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Conversation: MaxParticipants enforced at service layer (SQL Server check constraints cannot reference other tables).
            // When a new message is sent, the service MUST null HiddenAt for the other participant to resurface the conversation in their inbox.
            builder.Entity<Conversation>(e =>
            {
                e.HasKey(c => c.ConversationId);

                e.HasIndex(c => c.LastMessageAt);
            });

            // ConversationParticipant
            builder.Entity<ConversationParticipant>(e =>
            {
                e.HasKey(cp => cp.ConversationParticipantId);

                e.Property(cp => cp.UserId).HasMaxLength(450);

                e.HasIndex(cp => new { cp.ConversationId, cp.UserId }).IsUnique();
                e.HasIndex(cp => cp.UserId);

                e.HasOne(cp => cp.Conversation)
                    .WithMany(c => c.Participants)
                    .HasForeignKey(cp => cp.ConversationId)
                    // Conversations are never deleted in practice (hidden via HiddenAt); cascade exists as a safety net
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(cp => cp.HiddenAt)
                    .HasFilter("[HiddenAt] IS NULL");

                e.HasOne(cp => cp.User)
                    .WithMany(u => u.ConversationParticipants)
                    .HasForeignKey(cp => cp.UserId)
                    // User rows are never hard-deleted
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // Messages are hard-deleted. On delete: reactions + attachments cascade-delete via DB. The service layer will collect Attachment.StoragePath values and delete the corresponding blob storage files before deleting the message (cascade will remove the DB rows, but not the blobs). Replies are preserved with ParentMessageId set to null (IsReply stays true, so the UI shows "Replied to a deleted message"). On channel/conversation permanent deletion: hard-deleted via cascade. Blob cleanup handled by the channel deletion background job.
            builder.Entity<Message>(e =>
            {
                e.HasKey(m => m.MessageId);

                e.Property(m => m.Content).HasMaxLength(4000);
                e.Property(m => m.AuthorUserId).HasMaxLength(450);

                // Chronological retrieval with cursor pagination
                e.HasIndex(m => new { m.ChannelId, m.SentAt, m.MessageId })
                    .HasFilter("[ChannelId] IS NOT NULL");
                e.HasIndex(m => new { m.ConversationId, m.SentAt, m.MessageId })
                    .HasFilter("[ConversationId] IS NOT NULL");
                e.HasIndex(m => m.AuthorUserId);
                e.HasIndex(m => m.ParentMessageId)
                    .HasFilter("[ParentMessageId] IS NOT NULL");

                e.ToTable(t => t.HasCheckConstraint(
                    "CK_Message_Target",
                    "([ChannelId] IS NOT NULL AND [ConversationId] IS NULL) OR ([ChannelId] IS NULL AND [ConversationId] IS NOT NULL)"));

                e.HasOne(m => m.Author)
                    .WithMany(u => u.Messages)
                    .HasForeignKey(m => m.AuthorUserId)
                    .IsRequired()
                    // User rows are never hard-deleted
                    .OnDelete(DeleteBehavior.NoAction);

                e.HasOne(m => m.Channel)
                    .WithMany(c => c.Messages)
                    .HasForeignKey(m => m.ChannelId)
                    // If channel deleted, remove all messages
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(m => m.Conversation)
                    .WithMany(c => c.Messages)
                    .HasForeignKey(m => m.ConversationId)
                    // If conversation deleted, remove all messages
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(m => m.ParentMessage)
                    .WithMany(m => m.Replies)
                    .HasForeignKey(m => m.ParentMessageId)
                    // Nulls child's ParentMessageId; IsReply preserves reply context
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // MessageReactions are hard-deleted. Removed when: (1) user unreacts, (2) parent message deleted (cascade), (3) user account is anonymized (service), (4) channel/conversation permanently deleted (cascade via message).
            builder.Entity<MessageReaction>(e =>
            {
                e.HasKey(mr => mr.MessageReactionId);

                e.Property(mr => mr.Emoji).HasMaxLength(64);
                e.Property(mr => mr.ReactedByUserId).HasMaxLength(450);

                e.HasIndex(mr => new { mr.MessageId, mr.ReactedByUserId, mr.Emoji }).IsUnique();
                e.HasIndex(mr => mr.ReactedByUserId);

                e.HasOne(mr => mr.Message)
                    .WithMany(m => m.Reactions)
                    .HasForeignKey(mr => mr.MessageId)
                    // If message deleted, remove all reactions
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(mr => mr.User)
                    .WithMany(u => u.Reactions)
                    .HasForeignKey(mr => mr.ReactedByUserId)
                    // User rows are never hard-deleted
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // ModerationAction cascades with channel permanent deletion. Active sanctions deactivated in service when revoked. On user anonymization: FK references the anonymized user row; display names are resolved at query time via the navigation property (Art. 6(1)(f) legitimate interest: moderation record integrity, abuse prevention).
            builder.Entity<ModerationAction>(e =>
            {
                e.HasKey(ma => ma.ModerationActionId);

                e.Property(ma => ma.Reason).HasMaxLength(1000);
                e.Property(ma => ma.TargetUserId).HasMaxLength(450);
                e.Property(ma => ma.CreatedByUserId).HasMaxLength(450);
                e.Property(ma => ma.UpdatedByUserId).HasMaxLength(450);
                e.Property(ma => ma.Version).IsRowVersion();

                e.HasIndex(ma => new { ma.ChannelId, ma.TargetUserId, ma.Type })
                    .IsUnique()
                    .HasFilter("[IsActive] = 1");

                e.HasIndex(ma => ma.ExpiresAt)
                    .HasFilter("[IsActive] = 1 AND [ExpiresAt] IS NOT NULL");

                e.HasOne(ma => ma.Channel)
                    .WithMany(c => c.ModerationActions)
                    .HasForeignKey(ma => ma.ChannelId)
                    // If channel deleted, remove all moderation history
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(ma => ma.TargetUser)
                    .WithMany(u => u.TargetedModerationActions)
                    .HasForeignKey(ma => ma.TargetUserId)
                    // User rows are never hard-deleted
                    .OnDelete(DeleteBehavior.NoAction);

                e.HasOne(ma => ma.CreatedBy)
                    .WithMany(u => u.CreatedModerationActions)
                    .HasForeignKey(ma => ma.CreatedByUserId)
                    // User rows are never hard-deleted
                    .OnDelete(DeleteBehavior.NoAction);

                e.HasOne(ma => ma.UpdatedBy)
                    .WithMany(u => u.UpdatedModerationActions)
                    .HasForeignKey(ma => ma.UpdatedByUserId)
                    // User rows are never hard-deleted
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // Attachments are hard-deleted when parent message is deleted (cascade). Avatar/icon references SetNull'd, attachment + blob cleaned up by service. On user anonymization: UploadedByUserId nullified by service.
            builder.Entity<Attachment>(e =>
            {
                e.HasKey(a => a.AttachmentId);

                e.Property(a => a.UploadedByUserId).HasMaxLength(450);
                e.Property(a => a.FileName).HasMaxLength(256);
                e.Property(a => a.StoragePath).HasMaxLength(2048);
                e.Property(a => a.ContentType).HasMaxLength(256);

                e.HasIndex(a => a.UploadedByUserId);
                e.HasIndex(a => a.MessageId)
                    .HasFilter("[MessageId] IS NOT NULL");
                e.HasIndex(a => a.UploadedAt)
                    .HasFilter("[MessageId] IS NULL");

                e.HasOne(a => a.UploadedBy)
                    .WithMany(u => u.UploadedAttachments)
                    .HasForeignKey(a => a.UploadedByUserId)
                    // User rows are never hard-deleted
                    .OnDelete(DeleteBehavior.NoAction);

                e.HasOne(a => a.Message)
                    .WithMany(m => m.Attachments)
                    .HasForeignKey(a => a.MessageId)
                    // If message hard-deleted, remove its attachments
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // UserBlock is hard-deleted when either user's account is anonymized, since the account can no longer interact.
            builder.Entity<UserBlock>(e =>
            {
                e.HasKey(ub => ub.UserBlockId);

                e.Property(ub => ub.BlockingUserId).HasMaxLength(450);
                e.Property(ub => ub.BlockedUserId).HasMaxLength(450);

                e.HasIndex(ub => new { ub.BlockingUserId, ub.BlockedUserId }).IsUnique();
                e.HasIndex(ub => ub.BlockedUserId);

                e.ToTable(t => t.HasCheckConstraint(
                    "CK_UserBlock_NoSelfBlock",
                    "[BlockingUserId] <> [BlockedUserId]"));

                e.HasOne(ub => ub.BlockingUser)
                    .WithMany(u => u.InitiatedBlocks)
                    .HasForeignKey(ub => ub.BlockingUserId)
                    // Blocks hard-deleted by service on account anonymization
                    .OnDelete(DeleteBehavior.NoAction);

                e.HasOne(ub => ub.BlockedUser)
                    .WithMany(u => u.ReceivedBlocks)
                    .HasForeignKey(ub => ub.BlockedUserId)
                    // Blocks hard-deleted by service on account anonymization
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // ChannelAuditLog: append-only audit trail for channel administrative actions (GDPR Art. 30). Retention: 90 days (a background job should periodically delete entries older than 90 days (GDPR Art. 5(1)(e) storage limitation)). On channel permanent deletion: cascade-deleted. On user anonymization: IpAddress nullified by service (step 8); ActorDisplayName snapshot preserves readability.
            builder.Entity<ChannelAuditLog>(e =>
            {
                e.HasKey(a => a.ChannelAuditLogId);

                e.Property(a => a.TargetType).HasMaxLength(100);
                e.Property(a => a.TargetId).HasMaxLength(256);
                e.Property(a => a.Details).HasMaxLength(4000);
                e.Property(a => a.ActorDisplayName).HasMaxLength(100);
                e.Property(a => a.IpAddress).HasMaxLength(45);
                e.Property(a => a.ActorUserId).HasMaxLength(450);

                e.HasIndex(a => new { a.ChannelId, a.CreatedAt });
                e.HasIndex(a => a.ActorUserId);

                e.HasOne(a => a.Channel)
                    .WithMany(c => c.AuditLogs)
                    .HasForeignKey(a => a.ChannelId)
                    // If channel permanently deleted, remove all audit history
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(a => a.Actor)
                    .WithMany(u => u.AuditLogEntries)
                    .HasForeignKey(a => a.ActorUserId)
                    // User rows are never hard-deleted
                    .OnDelete(DeleteBehavior.NoAction);
            });
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            OnBeforeSave();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            OnBeforeSave();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void OnBeforeSave()
        {
            var now = DateTimeOffset.UtcNow;
            var memberCountDeltas = new Dictionary<Guid, int>();

            foreach (var entry in ChangeTracker.Entries())
            {
                // Portable timestamp defaults replace SQL Server-specific GETUTCDATE()
                if (entry.State == EntityState.Added)
                {
                    foreach (var prop in entry.Properties)
                    {
                        if (prop.Metadata.ClrType == typeof(DateTimeOffset)
                            && !prop.Metadata.IsNullable
                            && prop.CurrentValue is DateTimeOffset dt
                            && dt == default)
                        {
                            prop.CurrentValue = now;
                        }
                    }
                }

                if (entry.State == EntityState.Modified)
                {
                    if (entry.Entity is ModerationAction ma)
                        ma.UpdatedAt = now;

                    if (entry.Entity is Message msg && entry.Property(nameof(Message.Content)).IsModified)
                        msg.EditedAt = now;
                }

                // Collect MemberCount deltas: applied after iteration to avoid modifying tracked entities during enumeration
                if (entry.Entity is ChannelMember member)
                {
                    if (entry.State == EntityState.Added)
                    {
                        memberCountDeltas.TryGetValue(member.ChannelId, out var d);
                        memberCountDeltas[member.ChannelId] = d + 1;
                    }
                    else if (entry.State == EntityState.Modified
                        && member.LeftAt != null
                        && entry.Property(nameof(ChannelMember.LeftAt)).IsModified)
                    {
                        memberCountDeltas.TryGetValue(member.ChannelId, out var d);
                        memberCountDeltas[member.ChannelId] = d - 1;
                    }
                }
            }

            // Apply MemberCount deltas to already-tracked Channel entities only. The service layer will(!) pre-load the Channel entity before adding/removing members. If the Channel is not tracked, the delta is silently skipped. The count will be reconciled by a periodic background job or on next Channel load. This avoids issuing DB queries inside SaveChanges and eliminates the RowVersion race condition on concurrent joins.
            foreach (var (channelId, delta) in memberCountDeltas)
            {
                var channel = Set<Channel>().Local.FirstOrDefault(c => c.ChannelId == channelId);
                if (channel != null)
                    channel.MemberCount += delta;
            }
        }
    }
}
