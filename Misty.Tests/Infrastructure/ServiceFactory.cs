using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Misty.Application.DTOs;
using Misty.Application.DTOs.Channels;
using Misty.Application.Interfaces;
using Misty.Application.Services;
using Misty.Infrastructure;
using Misty.Infrastructure.Data.Repositories;

namespace Misty.Tests.Infrastructure;

public class ServiceFactory
{
    private readonly ApplicationDbContext _db;
    public IBlobStorageProvider BlobStorage { get; } = Substitute.For<IBlobStorageProvider>();
    public IIdentityService IdentityService { get; } = Substitute.For<IIdentityService>();

    public UserRepository UserRepo { get; }
    public ChannelRepository ChannelRepo { get; }
    public MessageRepository MessageRepo { get; }
    public ConversationRepository ConversationRepo { get; }
    public AttachmentRepository AttachmentRepo { get; }

    public ServiceFactory(ApplicationDbContext db)
    {
        _db = db;
        UserRepo = new UserRepository(db);
        ChannelRepo = new ChannelRepository(db);
        MessageRepo = new MessageRepository(db);
        ConversationRepo = new ConversationRepository(db);
        AttachmentRepo = new AttachmentRepository(db);

        // Default blob stub: return a fake URL
        BlobStorage.GetDownloadUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => $"https://blob.test/{ci.Arg<string>()}");
        BlobStorage.UploadAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<string>(1));

        IdentityService.GetEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => $"{ci.Arg<string>()}@test.local");
    }

    public UserBlockService CreateUserBlockService() => new(
        UserRepo,
        BlobStorage,
        PassThroughValidator<CreateUserBlockRequest>(),
        NullLogger<UserBlockService>.Instance);

    public UserService CreateUserService() => new(
        UserRepo,
        IdentityService,
        BlobStorage,
        PassThroughValidator<UpdateProfileRequest>(),
        NullLogger<UserService>.Instance);

    public ChannelService CreateChannelService() => new(
        ChannelRepo,
        BlobStorage,
        PassThroughValidator<CreateChannelRequest>(),
        PassThroughValidator<UpdateChannelRequest>(),
        PassThroughValidator<TransferOwnershipRequest>(),
        NullLogger<ChannelService>.Instance);

    public ChannelMemberService CreateChannelMemberService() => new(
        ChannelRepo,
        BlobStorage,
        PassThroughValidator<MarkChannelReadRequest>(),
        PassThroughValidator<UpdateChannelMemberRolesRequest>(),
        NullLogger<ChannelMemberService>.Instance);

    public ChannelRoleService CreateChannelRoleService() => new(
        ChannelRepo,
        PassThroughValidator<CreateChannelRoleRequest>(),
        PassThroughValidator<UpdateChannelRoleRequest>(),
        NullLogger<ChannelRoleService>.Instance);

    public ModerationService CreateModerationService() => new(
        ChannelRepo,
        BlobStorage,
        PassThroughValidator<CreateModerationActionRequest>(),
        PassThroughValidator<RevokeModerationActionRequest>(),
        NullLogger<ModerationService>.Instance);

    public MessageService CreateMessageService() => new(
        MessageRepo,
        CreateUserBlockService(),
        BlobStorage,
        PassThroughValidator<SendMessageRequest>(),
        PassThroughValidator<UpdateMessageRequest>(),
        PassThroughValidator<AddReactionRequest>(),
        NullLogger<MessageService>.Instance);

    public ConversationService CreateConversationService() => new(
        ConversationRepo,
        UserRepo,
        CreateUserBlockService(),
        BlobStorage,
        NullLogger<ConversationService>.Instance);

    public AttachmentService CreateAttachmentService() => new(
        AttachmentRepo,
        BlobStorage,
        PassThroughValidator<UploadAttachmentRequest>(),
        NullLogger<AttachmentService>.Instance);

    private static IValidator<T> PassThroughValidator<T>()
    {
        var v = Substitute.For<IValidator<T>>();
        v.ValidateAsync(Arg.Any<IValidationContext>(), Arg.Any<CancellationToken>())
            .Returns(new FluentValidation.Results.ValidationResult());
        return v;
    }
}
