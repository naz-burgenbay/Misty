using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Misty.Core.Data.Entities;

namespace Misty.Core.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<Channel> Channels => Set<Channel>();
        public DbSet<ChannelMember> ChannelMembers => Set<ChannelMember>();
        public DbSet<ChannelMemberRole> ChannelMemberRoles => Set<ChannelMemberRole>();
        public DbSet<ChannelRole> ChannelRoles => Set<ChannelRole>();
        public DbSet<Conversation> Conversations => Set<Conversation>();
        public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();
        public DbSet<Message> Messages => Set<Message>();
        public DbSet<MessageReaction> MessageReactions => Set<MessageReaction>();
        public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();
        public DbSet<UserBlock> UserBlocks => Set<UserBlock>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ApplicationUser
            builder.Entity<ApplicationUser>(e =>
            {
                e.Property(u => u.DisplayName).HasMaxLength(100);
                e.Property(u => u.Bio).HasMaxLength(500);
                e.Property(u => u.AvatarUrl).HasMaxLength(2048);
            });

            // Channel
            builder.Entity<Channel>(e =>
            {
                e.HasKey(c => c.ChannelId);

                e.Property(c => c.Name).HasMaxLength(100);
                e.Property(c => c.Description).HasMaxLength(500);
                e.Property(c => c.IconUrl).HasMaxLength(2048);
                e.Property(c => c.InviteCode).HasMaxLength(50);

                e.HasIndex(c => c.InviteCode)
                    .IsUnique()
                    .HasFilter("[InviteCode] IS NOT NULL");

                e.HasOne(c => c.Creator)
                    .WithMany(u => u.CreatedChannels)
                    .HasForeignKey(c => c.CreatedByUserId)
                    // User rows are never deleted
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // ChannelMember
            builder.Entity<ChannelMember>(e =>
            {
                e.HasKey(cm => cm.ChannelMemberId);

                e.HasIndex(cm => new { cm.ChannelId, cm.UserId }).IsUnique();

                // Fast lookup of all channels for a given user
                e.HasIndex(cm => cm.UserId);

                e.HasOne(cm => cm.User)
                    .WithMany(u => u.Memberships)
                    .HasForeignKey(cm => cm.UserId)
                    // User rows are never deleted
                    .OnDelete(DeleteBehavior.NoAction);

                e.HasOne(cm => cm.Channel)
                    .WithMany(c => c.Members)
                    .HasForeignKey(cm => cm.ChannelId)
                    // If channel deleted, remove all memberships
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ChannelRole
            builder.Entity<ChannelRole>(e =>
            {
                e.HasKey(cr => cr.ChannelRoleId);

                e.Property(cr => cr.Name).HasMaxLength(100);

                e.HasIndex(cr => new { cr.ChannelId, cr.Name }).IsUnique();

                e.HasOne(cr => cr.Channel)
                    .WithMany(c => c.Roles)
                    .HasForeignKey(cr => cr.ChannelId)
                    // If channel deleted, remove all roles
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ChannelMemberRole
            builder.Entity<ChannelMemberRole>(e =>
            {
                e.HasKey(cmr => new { cmr.ChannelMemberId, cmr.ChannelRoleId });

                e.HasOne(cmr => cmr.Member)
                    .WithMany(cm => cm.AssignedRoles)
                    .HasForeignKey(cmr => cmr.ChannelMemberId)
                    // If member deleted, remove their role assignments
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(cmr => cmr.Role)
                    .WithMany(cr => cr.MemberAssignments)
                    .HasForeignKey(cmr => cmr.ChannelRoleId)
                    // Role deletion cleanup handled in service
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Conversation
            builder.Entity<Conversation>(e =>
            {
                e.HasKey(c => c.ConversationId);
            });

            // ConversationParticipant
            builder.Entity<ConversationParticipant>(e =>
            {
                e.HasKey(cp => cp.ConversationParticipantId);

                e.HasIndex(cp => new { cp.ConversationId, cp.UserId }).IsUnique();

                // Fast lookup of all conversations for a given user
                e.HasIndex(cp => cp.UserId);

                e.HasOne(cp => cp.Conversation)
                    .WithMany(c => c.Participants)
                    .HasForeignKey(cp => cp.ConversationId)
                    // If conversation deleted, remove all participants
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(cp => cp.User)
                    .WithMany(u => u.ConversationParticipants)
                    .HasForeignKey(cp => cp.UserId)
                    // User rows are never deleted
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // Message
            builder.Entity<Message>(e =>
            {
                e.HasKey(m => m.MessageId);

                e.Property(m => m.Content).HasMaxLength(4000);
                e.Property(m => m.ImageUrl).HasMaxLength(2048);

                // Fast chronological message retrieval within a channel
                e.HasIndex(m => new { m.ChannelId, m.SentAt })
                    .HasFilter("[ChannelId] IS NOT NULL");
                // Fast chronological message retrieval within a conversation
                e.HasIndex(m => new { m.ConversationId, m.SentAt })
                    .HasFilter("[ConversationId] IS NOT NULL");
                // Fast lookup of all messages by a given user
                e.HasIndex(m => m.AuthorUserId);
                // Fast lookup of replies to a message for thread loading
                e.HasIndex(m => m.ParentMessageId)
                    .HasFilter("[ParentMessageId] IS NOT NULL");

                // Exactly one of ChannelId or ConversationId must be set (mutually exclusive)
                e.ToTable(t => t.HasCheckConstraint(
                    "CK_Message_Target",
                    "([ChannelId] IS NOT NULL AND [ConversationId] IS NULL) OR ([ChannelId] IS NULL AND [ConversationId] IS NOT NULL)"));

                e.HasOne(m => m.Author)
                    .WithMany(u => u.Messages)
                    .HasForeignKey(m => m.AuthorUserId)
                    // User rows are never deleted
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
                    // Replies remain visible, orphans handled in service
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // MessageReaction
            builder.Entity<MessageReaction>(e =>
            {
                e.HasKey(mr => mr.MessageReactionId);

                e.Property(mr => mr.Emoji).HasMaxLength(64);

                e.HasIndex(mr => new { mr.MessageId, mr.ReactedByUserId, mr.Emoji }).IsUnique();

                // Fast lookup of all reactions by a given user
                e.HasIndex(mr => mr.ReactedByUserId);

                e.HasOne(mr => mr.Message)
                    .WithMany(m => m.Reactions)
                    .HasForeignKey(mr => mr.MessageId)
                    // If message deleted, remove all reactions
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(mr => mr.User)
                    .WithMany(u => u.Reactions)
                    .HasForeignKey(mr => mr.ReactedByUserId)
                    // User rows are never deleted
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // ModerationAction
            builder.Entity<ModerationAction>(e =>
            {
                e.HasKey(ma => ma.ModerationActionId);

                e.Property(ma => ma.Reason).HasMaxLength(1000);
                e.Property(ma => ma.TargetUserDisplayName).HasMaxLength(100);
                e.Property(ma => ma.CreatedByDisplayName).HasMaxLength(100);
                e.Property(ma => ma.UpdatedByDisplayName).HasMaxLength(100);

                // Concurrency control
                e.Property(ma => ma.RowVersion)
                    .IsRowVersion();

                // Prevent duplicate active sanctions of the same type for the same user in the same channel
                e.HasIndex(ma => new { ma.ChannelId, ma.TargetUserId, ma.Type })
                    .IsUnique()
                    .HasFilter("[IsActive] = 1");

                e.HasOne(ma => ma.Channel)
                    .WithMany(c => c.ModerationActions)
                    .HasForeignKey(ma => ma.ChannelId)
                    // If channel deleted, remove all moderation history
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(ma => ma.TargetUser)
                    .WithMany(u => u.TargetedModerationActions)
                    .HasForeignKey(ma => ma.TargetUserId)
                    // User rows are never deleted
                    .OnDelete(DeleteBehavior.NoAction);

                e.HasOne(ma => ma.CreatedBy)
                    .WithMany(u => u.CreatedModerationActions)
                    .HasForeignKey(ma => ma.CreatedByUserId)
                    // User rows are never deleted
                    .OnDelete(DeleteBehavior.NoAction);

                e.HasOne(ma => ma.UpdatedBy)
                    .WithMany(u => u.UpdatedModerationActions)
                    .HasForeignKey(ma => ma.UpdatedByUserId)
                    // User rows are never deleted
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // UserBlock
            builder.Entity<UserBlock>(e =>
            {
                e.HasKey(ub => ub.UserBlockId);

                e.Property(ub => ub.Reason).HasMaxLength(500);

                e.HasIndex(ub => new { ub.BlockingUserId, ub.BlockedUserId }).IsUnique();

                // Fast reverse lookup, "who blocked this user"
                e.HasIndex(ub => ub.BlockedUserId);

                e.HasOne(ub => ub.BlockingUser)
                    .WithMany(u => u.InitiatedBlocks)
                    .HasForeignKey(ub => ub.BlockingUserId)
                    // User rows are never deleted
                    .OnDelete(DeleteBehavior.NoAction);

                e.HasOne(ub => ub.BlockedUser)
                    .WithMany(u => u.ReceivedBlocks)
                    .HasForeignKey(ub => ub.BlockedUserId)
                    // User rows are never deleted
                    .OnDelete(DeleteBehavior.NoAction);
            });
        }
    }
}
