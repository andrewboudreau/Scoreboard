using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

using Scoreboard.Models;

using System.Security.Cryptography;
using System.Text.Json;

namespace Scoreboard.Services;

/// <summary>
/// Group and membership management APIs backed by Azure Blob Storage.
/// </summary>
public interface IGroupService
{
    /// <summary>
    /// Creates a new group with the provided display name and a new admin access code.
    /// </summary>
    /// <param name="name">The friendly name of the group.</param>
    /// <returns>The newly created group.</returns>
    Task<Group> CreateGroupAsync(string name);

    /// <summary>
    /// Gets a group by its unique identifier.
    /// </summary>
    /// <param name="groupId">The group identifier.</param>
    /// <returns>The group if found; otherwise <see langword="null"/>.</returns>
    Task<Group?> GetGroupByIdAsync(string groupId);

    /// <summary>
    /// Gets a group by its admin access code.
    /// </summary>
    /// <param name="adminCode">The admin code provided to the caller.</param>
    /// <returns>The group if found; otherwise <see langword="null"/>.</returns>
    Task<Group?> GetGroupByAdminCodeAsync(string adminCode);

    /// <summary>
    /// Gets a group and member entry by a member access code.
    /// </summary>
    /// <param name="memberCode">The member code provided to the caller.</param>
    /// <returns>The group and matching member entry if found; otherwise <see langword="null"/>.</returns>
    Task<(Group Group, MemberAccess Member)?> GetGroupByMemberCodeAsync(string memberCode);

    /// <summary>
    /// Adds a new member access entry to a group.
    /// </summary>
    /// <param name="groupId">The group identifier.</param>
    /// <param name="label">A friendly label for the member.</param>
    /// <returns>The created member access entry.</returns>
    Task<MemberAccess> AddMemberAsync(string groupId, string label);

    /// <summary>
    /// Revokes (deactivates) a member access code for a group.
    /// </summary>
    /// <param name="groupId">The group identifier.</param>
    /// <param name="memberCode">The member code to revoke.</param>
    /// <returns><see langword="true"/> if the member was found and revoked; otherwise <see langword="false"/>.</returns>
    Task<bool> RevokeMemberAsync(string groupId, string memberCode);

    /// <summary>
    /// Generates SAS URLs for the backing blob container.
    /// </summary>
    /// <param name="canWrite">Whether the returned token set should include write access.</param>
    /// <returns>A read-only SAS URL and (optionally) a write-enabled SAS URL.</returns>
    SasTokenSet GenerateSasUrls(bool canWrite);
}

/// <summary>
/// Stores and retrieves group metadata from Azure Blob Storage and generates SAS URLs for client access.
/// </summary>
public class GroupService : IGroupService
{
    private readonly BlobContainerClient _containerClient;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Dictionary<string, Group> _groups = new();
    private bool _loaded;

    private const string GroupBlobPrefix = "_groups/";
    // Exclude ambiguous chars (I/O/0/1) for readability
    private const string AdminCodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const string MemberCodeChars = "abcdefghjkmnpqrstuvwxyz23456789";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public GroupService(BlobContainerClient containerClient)
    {
        _containerClient = containerClient;
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        await _semaphore.WaitAsync();
        try
        {
            if (_loaded) return;

            await foreach (var blobItem in _containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, GroupBlobPrefix, CancellationToken.None))
            {
                try
                {
                    var blobClient = _containerClient.GetBlobClient(blobItem.Name);
                    var response = await blobClient.DownloadContentAsync();
                    var group = JsonSerializer.Deserialize<Group>(response.Value.Content);
                    if (group != null)
                    {
                        _groups[group.Id] = group;
                    }
                }
                catch
                {
                    // Skip corrupted group files
                }
            }

            _loaded = true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task SaveGroupAsync(Group group)
    {
        var blobClient = _containerClient.GetBlobClient($"{GroupBlobPrefix}{group.Id}.json");
        var json = JsonSerializer.Serialize(group, JsonOptions);
        await blobClient.UploadAsync(BinaryData.FromString(json), overwrite: true);
    }

    public async Task<Group> CreateGroupAsync(string name)
    {
        await EnsureLoadedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var group = new Group
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                AdminCode = GenerateCode(AdminCodeChars, 8),
                CreatedAt = DateTimeOffset.UtcNow
            };

            // Ensure unique admin code
            while (_groups.Values.Any(g => g.AdminCode == group.AdminCode))
            {
                group.AdminCode = GenerateCode(AdminCodeChars, 8);
            }

            await SaveGroupAsync(group);
            _groups[group.Id] = group;

            return group;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Group?> GetGroupByIdAsync(string groupId)
    {
        await EnsureLoadedAsync();

        await _semaphore.WaitAsync();
        try
        {
            return _groups.GetValueOrDefault(groupId);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Group?> GetGroupByAdminCodeAsync(string adminCode)
    {
        await EnsureLoadedAsync();

        await _semaphore.WaitAsync();
        try
        {
            return _groups.Values.FirstOrDefault(g =>
                string.Equals(g.AdminCode, adminCode, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<(Group Group, MemberAccess Member)?> GetGroupByMemberCodeAsync(string memberCode)
    {
        await EnsureLoadedAsync();

        await _semaphore.WaitAsync();
        try
        {
            foreach (var group in _groups.Values)
            {
                var member = group.Members.FirstOrDefault(m =>
                    m.Active && string.Equals(m.Code, memberCode, StringComparison.OrdinalIgnoreCase));
                if (member != null)
                {
                    return (group, member);
                }
            }

            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<MemberAccess> AddMemberAsync(string groupId, string label)
    {
        await EnsureLoadedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var group = _groups.GetValueOrDefault(groupId)
                ?? throw new KeyNotFoundException($"Group {groupId} not found");

            var member = new MemberAccess
            {
                Code = GenerateCode(MemberCodeChars, 6),
                Label = label,
                Active = true
            };

            // Ensure unique member code across all groups
            while (_groups.Values.SelectMany(g => g.Members).Any(m => m.Code == member.Code))
            {
                member.Code = GenerateCode(MemberCodeChars, 6);
            }

            group.Members.Add(member);
            await SaveGroupAsync(group);

            return member;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> RevokeMemberAsync(string groupId, string memberCode)
    {
        await EnsureLoadedAsync();

        await _semaphore.WaitAsync();
        try
        {
            var group = _groups.GetValueOrDefault(groupId);
            if (group == null) return false;

            var member = group.Members.FirstOrDefault(m =>
                string.Equals(m.Code, memberCode, StringComparison.OrdinalIgnoreCase));
            if (member == null) return false;

            member.Active = false;
            await SaveGroupAsync(group);

            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public SasTokenSet GenerateSasUrls(bool canWrite)
    {
        if (!_containerClient.CanGenerateSasUri)
        {
            throw new InvalidOperationException(
                "Cannot generate SAS tokens. The blob client was not created with shared key credentials.");
        }

        var expiresOn = DateTimeOffset.UtcNow.AddHours(3);

        // Read SAS
        var readBuilder = new BlobSasBuilder
        {
            BlobContainerName = _containerClient.Name,
            Resource = "c",
            ExpiresOn = expiresOn
        };
        readBuilder.SetPermissions(BlobContainerSasPermissions.Read | BlobContainerSasPermissions.List);

        var readUri = _containerClient.GenerateSasUri(readBuilder);

        string? writeUrl = null;
        if (canWrite)
        {
            var writeBuilder = new BlobSasBuilder
            {
                BlobContainerName = _containerClient.Name,
                Resource = "c",
                ExpiresOn = expiresOn
            };
            writeBuilder.SetPermissions(
                BlobContainerSasPermissions.Read |
                BlobContainerSasPermissions.Write |
                BlobContainerSasPermissions.Create |
                BlobContainerSasPermissions.List);

            writeUrl = _containerClient.GenerateSasUri(writeBuilder).ToString();
        }

        return new SasTokenSet
        {
            ReadUrl = readUri.ToString(),
            WriteUrl = writeUrl,
            ExpiresAt = expiresOn
        };
    }

    private static string GenerateCode(string chars, int length)
    {
        return new string(Enumerable.Range(0, length)
            .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)])
            .ToArray());
    }
}
