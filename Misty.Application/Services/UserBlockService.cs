using FluentValidation;
using Microsoft.Extensions.Logging;
using Misty.Application.DTOs;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Domain.Entities;

namespace Misty.Application.Services;

public class UserBlockService : IUserBlockService
{
    private readonly IUserRepository _userRepository;
    private readonly IBlobStorageProvider _blobStorage;
    private readonly IValidator<CreateUserBlockRequest> _blockValidator;
    private readonly ILogger<UserBlockService> _logger;

    public UserBlockService(
        IUserRepository userRepository,
        IBlobStorageProvider blobStorage,
        IValidator<CreateUserBlockRequest> blockValidator,
        ILogger<UserBlockService> logger)
    {
        _userRepository = userRepository;
        _blobStorage = blobStorage;
        _blockValidator = blockValidator;
        _logger = logger;
    }

    // UC-9.1 Block User
    public async Task<UserBlockResponse> BlockUserAsync(
        string userId, CreateUserBlockRequest request, CancellationToken ct = default)
    {
        await _blockValidator.ValidateAndThrowAsync(request, ct);

        if (request.BlockedUserId == userId)
            throw new BusinessRuleException("A user cannot block themselves.");

        var blockedUser = await _userRepository.GetByIdAsync(request.BlockedUserId, ct)
            ?? throw new NotFoundException("User", request.BlockedUserId);

        var block = new UserBlock
        {
            UserBlockId = Guid.NewGuid(),
            BlockingUserId = userId,
            BlockedUserId = request.BlockedUserId,
            BlockedAt = DateTimeOffset.UtcNow
        };

        await _userRepository.AddBlockAsync(block, ct);
        await _userRepository.SaveChangesAsync(ct);

        _logger.LogInformation("User {UserId} blocked {BlockedUserId}", userId, request.BlockedUserId);

        return await ToResponse(block, blockedUser);
    }

    // UC-9.2 Unblock User
    public async Task UnblockUserAsync(string userId, Guid blockId, CancellationToken ct = default)
    {
        var block = await _userRepository.GetBlockByIdAsync(blockId, ct)
            ?? throw new NotFoundException("UserBlock", blockId);

        if (block.BlockingUserId != userId)
            throw new BusinessRuleException("A user can only remove blocks they initiated.");

        _userRepository.RemoveBlock(block);
        await _userRepository.SaveChangesAsync(ct);

        _logger.LogInformation("User {UserId} unblocked {BlockedUserId}", userId, block.BlockedUserId);
    }

    // UC-9.3 List Blocked Users
    public async Task<IReadOnlyList<UserBlockResponse>> GetBlockedUsersAsync(
        string userId, CancellationToken ct = default)
    {
        var blocks = await _userRepository.GetBlocksByUserAsync(userId, ct);

        var responses = new List<UserBlockResponse>(blocks.Count);
        foreach (var block in blocks)
        {
            responses.Add(await ToResponseAsync(block, ct));
        }

        return responses;
    }

    // UC-9.4 Ensure Not Blocked
    public async Task EnsureNotBlockedAsync(string userId, string otherUserId, CancellationToken ct = default)
    {
        if (await _userRepository.ExistsBlockBetweenAsync(userId, otherUserId, ct))
            throw new BusinessRuleException("Interaction is blocked between these users.");
    }

    private async Task<UserBlockResponse> ToResponseAsync(UserBlock block, CancellationToken ct)
    {
        string? avatarUrl = null;
        if (block.BlockedUser.Avatar is not null)
            avatarUrl = await _blobStorage.GetDownloadUrlAsync(block.BlockedUser.Avatar.StoragePath, ct);

        return new UserBlockResponse
        {
            UserBlockId = block.UserBlockId,
            BlockedUser = new UserSummary
            {
                Id = block.BlockedUser.UserId,
                DisplayName = block.BlockedUser.DisplayName,
                AvatarUrl = avatarUrl
            },
            BlockedAt = block.BlockedAt
        };
    }

    private async Task<UserBlockResponse> ToResponse(UserBlock block, User blockedUser)
    {
        string? avatarUrl = null;
        if (blockedUser.Avatar is not null)
            avatarUrl = await _blobStorage.GetDownloadUrlAsync(blockedUser.Avatar.StoragePath);

        return new UserBlockResponse
        {
            UserBlockId = block.UserBlockId,
            BlockedUser = new UserSummary
            {
                Id = blockedUser.UserId,
                DisplayName = blockedUser.DisplayName,
                AvatarUrl = avatarUrl
            },
            BlockedAt = block.BlockedAt
        };
    }
}
