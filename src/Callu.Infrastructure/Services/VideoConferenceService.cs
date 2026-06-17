using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Shared.Localization;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Conference;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>Video conference rooms: creation, participant tracking, SMS invites.</summary>
public class VideoConferenceService(
    IConferenceRoomRepository roomRepo,
    IConferenceParticipantRepository participantRepo,
    IIncidentRepository incidentRepo,
    ITeamMemberRepository teamMemberRepo,
    ITransactionManager transactionManager,
    ICommunicationProviderRegistry providerRegistry,
    IConfiguration configuration,
    IUserContactRepository userContacts,
    ILogger<VideoConferenceService> logger) : IVideoConferenceService
{
    public async Task<ConferenceRoomResult> CreateRoomAsync(Guid incidentId, CancellationToken ct = default)
    {
        ConferenceRoomResult result;
        List<ConferenceParticipant> participants;
        Incident? incident;
        ConferenceRoom? room;
        try
        {
            (result, participants, incident, room) = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var incident = await incidentRepo.GetQueryable()
                .Include(i => i.Team)
                .FirstOrDefaultAsync(i => i.Id == incidentId, ct);

            if (incident == null)
            {
                return (new ConferenceRoomResult { Success = false, Error = "Incident not found" },
                    new List<ConferenceParticipant>(), (Incident?)null, (ConferenceRoom?)null);
            }

            var existingRoom = await roomRepo.FindSingleAsync(
                r => r.IncidentId == incidentId && r.Status == ConferenceRoomStatus.Active, ct);

            if (existingRoom != null)
            {
                return (new ConferenceRoomResult
                {
                    Success = true,
                    RoomId = existingRoom.Id,
                    RoomToken = existingRoom.RoomToken,
                    ConferenceUrl = $"/conference",
                    ParticipantCount = await participantRepo.CountAsync(
                        p => p.ConferenceRoomId == existingRoom.Id, ct)
                }, new List<ConferenceParticipant>(), (Incident?)null, (ConferenceRoom?)null);
            }

            var room = new ConferenceRoom
            {
                IncidentId = incidentId,
                RoomToken = Guid.NewGuid().ToString("N"),
                VoximplantConferenceId = $"callu-incident-{incidentId:N}",
                Status = ConferenceRoomStatus.Active,
                MaxDurationMinutes = 60,
                RecordingEnabled = false,
                ExpiresAt = DateTime.UtcNow.AddMinutes(60)
            };

            await roomRepo.AddAsync(room, ct);

            List<ConferenceParticipant> participants = [];

            if (incident.TeamId.HasValue)
            {
                var teamMembers = await teamMemberRepo.GetQueryable()
                    .Where(tm => tm.TeamId == incident.TeamId.Value && !tm.IsDeleted)
                    .ToListAsync(ct);

                var userIds = teamMembers.Select(tm => tm.UserId).ToList();
                var contacts = await userContacts.GetContactsByIdsAsync(userIds, ct);
                var contactById = contacts.ToDictionary(c => c.Id, StringComparer.Ordinal);

                foreach (var tm in teamMembers)
                {
                    contactById.TryGetValue(tm.UserId, out var user);
                    var participant = new ConferenceParticipant
                    {
                        ConferenceRoomId = room.Id,
                        UserId = tm.UserId,
                        ParticipantToken = Guid.NewGuid().ToString("N"),
                        DisplayName = user?.DisplayName ?? tm.UserId,
                        PhoneNumber = user?.PhoneNumber
                    };

                    participants.Add(participant);
                    await participantRepo.AddAsync(participant, ct);
                }
            }

            logger.LogInformation(
                "Conference room {RoomId} created for incident {IncidentId} with {Count} participants",
                room.Id, incidentId, participants.Count);

            return (new ConferenceRoomResult
            {
                Success = true,
                RoomId = room.Id,
                RoomToken = room.RoomToken,
                ConferenceUrl = "/conference",
                ParticipantCount = participants.Count
            }, participants, (Incident?)incident, (ConferenceRoom?)room);
        }, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolationOn(ex, "IX_ConferenceRooms_IncidentId_Active"))
        {
            logger.LogInformation(
                "ConferenceRoom create race for incident {IncidentId} resolved; returning existing room",
                incidentId);
            var existing = await roomRepo.FindSingleAsync(
                r => r.IncidentId == incidentId && r.Status == ConferenceRoomStatus.Active, ct);
            if (existing is null)
            {
                logger.LogError(ex, "Unique violation on conference create but no Active room found");
                return new ConferenceRoomResult { Success = false, Error = "Conference create failed" };
            }
            var participantCount = await participantRepo.CountAsync(
                p => p.ConferenceRoomId == existing.Id, ct);
            return new ConferenceRoomResult
            {
                Success = true,
                RoomId = existing.Id,
                RoomToken = existing.RoomToken,
                ConferenceUrl = "/conference",
                ParticipantCount = participantCount
            };
        }

        if (!result.Success || room == null || incident == null || participants.Count == 0)
            return result;

        var calluApiUrl = configuration["CalluSettings:ApiUrl"] ?? "https://localhost:5001";

        foreach (var participant in participants.Where(p => !string.IsNullOrEmpty(p.PhoneNumber)))
        {
            var conferenceLink = $"{calluApiUrl}/conference/{participant.ParticipantToken}";
            var smsMessage =
                Messages.Get("conferenceSms.title") + "\n" +
                Messages.Get("conferenceSms.incident",
                    ("title", incident.Title),
                    ("severity", incident.Severity)) + "\n" +
                Messages.Get("conferenceSms.join", ("link", conferenceLink)) + "\n" +
                Messages.Get("conferenceSms.duration", ("minutes", room.MaxDurationMinutes));

            try
            {
                var smsProvider = providerRegistry.GetProvider(CommunicationCapability.Sms);
                if (smsProvider != null)
                {
                    await smsProvider.SendSmsAsync(new SendSmsRequest
                    {
                        To = participant.PhoneNumber!,
                        Message = smsMessage
                    });

                    logger.LogInformation("Conference SMS sent to {Phone} for room {RoomId}",
                        participant.PhoneNumber, room.Id);
                }
                else
                {
                    logger.LogWarning("No SMS provider configured, cannot send conference link to {Phone}",
                        participant.PhoneNumber);
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "Failed to send conference SMS to {Phone}", participant.PhoneNumber);
            }
        }

        return result;
    }

    public async Task<ParticipantInfoDto?> ValidateParticipantAsync(string participantToken, CancellationToken ct = default)
    {
        var participant = await participantRepo.GetQueryable()
            .Include(p => p.ConferenceRoom)
                .ThenInclude(r => r.Incident)
            .FirstOrDefaultAsync(p => p.ParticipantToken == participantToken, ct);

        if (participant == null) return null;

        var room = participant.ConferenceRoom;

        if (room.Status != ConferenceRoomStatus.Active)
            return null;

        if (DateTime.UtcNow > room.ExpiresAt)
        {
            await transactionManager.ExecuteInTransactionAsync(async () =>
            {
                room.Status = ConferenceRoomStatus.Expired;
                roomRepo.Update(room);
                
                
                var activeParticipants = await participantRepo.GetQueryable()
                    .Where(p => p.ConferenceRoomId == room.Id && p.IsActive)
                    .ToListAsync(ct);
                foreach (var p in activeParticipants)
                {
                    p.IsActive = false;
                    p.LeftAt = DateTime.UtcNow;
                    participantRepo.Update(p);
                }
            }, ct);
            return null;
        }

        var activeCount = await participantRepo.CountAsync(
            p => p.ConferenceRoomId == room.Id && p.IsActive, ct);

        var voxConfId = !string.IsNullOrWhiteSpace(room.VoximplantConferenceId)
            ? room.VoximplantConferenceId
            : $"callu-incident-{room.IncidentId:N}";

        return new ParticipantInfoDto
        {
            ParticipantToken = participantToken,
            DisplayName = participant.DisplayName,
            RoomId = room.Id,
            VoximplantConferenceId = voxConfId,
            IncidentId = room.IncidentId,
            IncidentTitle = room.Incident?.Title ?? "Unknown",
            IncidentSeverity = room.Incident?.Severity.ToString() ?? "Unknown",
            RoomStatus = room.Status.ToString(),
            ExpiresAt = room.ExpiresAt,
            ActiveParticipants = activeCount,
            IsAlreadyActive = participant.IsActive
        };
    }

    public async Task<JoinResultDto> JoinConferenceAsync(string participantToken, string? sourceIp = null, string? userAgent = null, CancellationToken ct = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var participant = await participantRepo.GetQueryable()
                .Include(p => p.ConferenceRoom)
                .FirstOrDefaultAsync(p => p.ParticipantToken == participantToken, ct);

            if (participant == null)
                return new JoinResultDto { Success = false, Error = "Invalid participant token" };

            var room = participant.ConferenceRoom;

            if (room.Status != ConferenceRoomStatus.Active)
                return new JoinResultDto { Success = false, Error = "Conference has ended" };

            if (DateTime.UtcNow > room.ExpiresAt)
                return new JoinResultDto { Success = false, Error = "Conference has expired" };

            var activeCount = await participantRepo.CountAsync(
                p => p.ConferenceRoomId == room.Id && p.IsActive, ct);
            if (activeCount >= 12)
            {
                return new JoinResultDto { Success = false, Error = "Conference is full (maximum 12 participants)" };
            }

            if (participant.IsActive)
            {
                logger.LogInformation(
                    "Participant {DisplayName} re-joining conference {RoomId}",
                    participant.DisplayName, room.Id);

                if (!string.IsNullOrEmpty(participant.LastJoinIp)
                    && !string.IsNullOrEmpty(sourceIp)
                    && participant.LastJoinIp != sourceIp)
                {
                    logger.LogWarning(
                        "Conference participant {DisplayName} re-joined room {RoomId} from a different source IP ({NewIp} vs {OldIp}) — possible forwarded link.",
                        participant.DisplayName, room.Id, sourceIp, participant.LastJoinIp);
                }
            }

            participant.IsActive = true;
            participant.JoinedAt = DateTime.UtcNow;
            participant.LeftAt = null;
            participant.JoinCount++;
            participant.LastJoinIp = sourceIp;
            participant.LastJoinUserAgent = userAgent;
            participantRepo.Update(participant);

            logger.LogInformation("Participant {DisplayName} joined conference {RoomId}",
                participant.DisplayName, room.Id);

            string? loginKey = null;
            string? appName = null;
            string? accountName = null;
            string? voximplantUsername = null;
            string? node = null;

            try
            {
                var videoProvider = providerRegistry.GetProvider(CommunicationCapability.VideoConference);
                if (videoProvider is VoximplantProvider voxProvider)
                {
                    var config = voxProvider.GetCurrentConfig();
                    var applicationId = voxProvider.GetProvisioningApplicationId();

                    if (config != null && applicationId.HasValue)
                    {
                        var client = voxProvider.CreateManagementClient(logger);

                        var voxUsername = !string.IsNullOrEmpty(participant.UserId)
                            ? VoximplantUserNaming.Sanitize(participant.UserId)
                            : participant.ParticipantToken;

                        var userId = await client.EnsureConferenceUserAsync(
                            applicationId.Value, voxUsername, participant.DisplayName);

                        var fullUserName = $"{voxUsername}@{config.ApplicationName}";
                        voximplantUsername = fullUserName;

                        if (userId.HasValue)
                        {
                            loginKey = await client.PrepareWebSdkLoginHashAsync(userId.Value, fullUserName);
                        }
                        appName = config.ApplicationName;
                        accountName = config.AccountName;
                        node = config.Node;

                        if (!string.IsNullOrEmpty(loginKey))
                        {
                            logger.LogInformation(
                                "Voximplant login hash generated for participant {DisplayName} in room {RoomId}",
                                participant.DisplayName, room.Id);
                        }
                        else
                        {
                            logger.LogWarning(
                                "Voximplant conference credentials unavailable for {DisplayName}: userCreated={HasUser}, hash=null",
                                participant.DisplayName, userId.HasValue);
                        }
                    }
                    else
                    {
                        logger.LogWarning(
                            "Voximplant provider not fully provisioned (config={HasConfig}, appId={AppId})",
                            config != null, applicationId);
                    }
                }
                else
                {
                    logger.LogWarning(
                        "No VideoConference-capable Voximplant provider resolved for conference join (got {ProviderType}). " +
                        "Enable the Voximplant provider and ensure the Video Conference capability is assigned to it.",
                        videoProvider?.GetType().Name ?? "none");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get Voximplant login key for participant {Token}",
                    participant.ParticipantToken);
            }

            if (string.IsNullOrEmpty(loginKey))
            {
                return new JoinResultDto
                {
                    Success = false,
                    Error = "Voximplant is not provisioned on this installation. " +
                            "Configure the Voximplant provider in Communications settings before joining a conference.",
                    DisplayName = participant.DisplayName
                };
            }

            return new JoinResultDto
            {
                Success = true,
                VoximplantLoginKey = loginKey,
                VoximplantAppName = appName,
                VoximplantAccountName = accountName,
                VoximplantUsername = voximplantUsername,
                VoximplantNode = node,
                DisplayName = participant.DisplayName
            };
        }, ct);
    }

    public async Task LeaveConferenceAsync(string participantToken, CancellationToken ct = default)
    {
        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var participant = await participantRepo.GetQueryable()
                .FirstOrDefaultAsync(p => p.ParticipantToken == participantToken, ct);

            if (participant == null) return;

            participant.IsActive = false;
            participant.LeftAt = DateTime.UtcNow;
            participantRepo.Update(participant);

            logger.LogInformation("Participant {DisplayName} left conference {RoomId}",
                participant.DisplayName, participant.ConferenceRoomId);

            // The room is intentionally NOT ended when it becomes empty — a page refresh
            // leaves and rejoins within seconds, and ending here would invalidate the link
            // ("conference has ended"). The room closes via ExpiresAt (expiry job), an
            // explicit end, or incident resolution.
        }, ct);
    }

    public async Task EndConferenceAsync(Guid roomId, CancellationToken ct = default)
    {
        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var room = await roomRepo.GetQueryable()
                .Include(r => r.Participants)
                .FirstOrDefaultAsync(r => r.Id == roomId, ct);

            if (room == null) return;

            room.Status = ConferenceRoomStatus.Ended;
            room.EndedAt = DateTime.UtcNow;
            roomRepo.Update(room);

            foreach (var participant in room.Participants.Where(p => p.IsActive))
            {
                participant.IsActive = false;
                participant.LeftAt = DateTime.UtcNow;
                participantRepo.Update(participant);
            }

            logger.LogInformation("Conference {RoomId} ended manually", roomId);
        }, ct);
    }

    public async Task<ConferenceRoom?> GetActiveRoomForIncidentAsync(Guid incidentId, CancellationToken ct = default)
    {
        return await roomRepo.GetQueryable()
            .Include(r => r.Participants)
            .FirstOrDefaultAsync(r => r.IncidentId == incidentId && r.Status == ConferenceRoomStatus.Active, ct);
    }

    public async Task<ActiveConferenceDto?> GetActiveConferenceForUserAsync(Guid incidentId, string userId, CancellationToken ct = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var room = await roomRepo.GetQueryable()
                .Include(r => r.Participants)
                .FirstOrDefaultAsync(r => r.IncidentId == incidentId && r.Status == ConferenceRoomStatus.Active, ct);

            if (room == null)
                return null;

            var userParticipant = room.Participants.FirstOrDefault(p => p.UserId == userId);

            if (userParticipant == null)
            {
                var user = await userContacts.GetContactByIdAsync(userId, ct);
                userParticipant = new ConferenceParticipant
                {
                    ConferenceRoomId = room.Id,
                    UserId = userId,
                    ParticipantToken = Guid.NewGuid().ToString("N"),
                    DisplayName = user?.DisplayName ?? userId,
                    PhoneNumber = user?.PhoneNumber
                };
                room.Participants.Add(userParticipant);
                await participantRepo.AddAsync(userParticipant, ct);
                logger.LogInformation("Dynamically added participant {UserId} to room {RoomId}", userId, room.Id);
            }

            return new ActiveConferenceDto
            {
                RoomId = room.Id,
                RoomToken = room.RoomToken,
                Status = room.Status.ToString(),
                ParticipantCount = room.Participants.Count,
                UserParticipantToken = userParticipant.ParticipantToken,
                ExpiresAt = room.ExpiresAt
            };
        }, ct);
    }

    public async Task<(IEnumerable<ConferenceRoomDto> Items, int TotalCount)> GetConferenceRoomsPagedAsync(ConferenceRoomFilter filter, CancellationToken ct = default)
    {
        var query = roomRepo.GetQueryable()
            .Include(r => r.Incident)
            .Include(r => r.Participants)
            .AsNoTracking();

        if (filter.IncidentId.HasValue)
        {
            query = query.Where(r => r.IncidentId == filter.IncidentId.Value);
        }

        if (!string.IsNullOrEmpty(filter.Status))
        {
            if (Enum.TryParse<ConferenceRoomStatus>(filter.Status, true, out var parsedStatus))
            {
                query = query.Where(r => r.Status == parsedStatus);
            }
        }

        if (filter.HasRecording.HasValue)
        {
            if (filter.HasRecording.Value)
            {
                query = query.Where(r => r.RecordingUrl != null);
            }
            else
            {
                query = query.Where(r => r.RecordingUrl == null);
            }
        }

        var total = await query.CountAsync(ct);

        var rooms = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        var dtos = rooms.Select(r => new ConferenceRoomDto
        {
            Id = r.Id,
            IncidentId = r.IncidentId,
            IncidentTitle = r.Incident?.Title ?? "Unknown Incident",
            RoomToken = r.RoomToken,
            Status = r.Status.ToString(),
            ParticipantCount = r.Participants.Count,
            CreatedAt = r.CreatedAt,
            ExpiresAt = r.ExpiresAt,
            EndedAt = r.EndedAt,
            RecordingEnabled = r.RecordingEnabled,
            RecordingUrl = r.RecordingUrl,
            VoximplantConferenceId = r.VoximplantConferenceId
        });

        return (dtos, total);
    }

    /// <summary>
    /// True when the DbUpdateException wraps a Postgres unique-violation (SQLSTATE
    /// 23505) on the given constraint/index name. Used by CreateRoomAsync to
    /// distinguish "two callers raced for the same incident" (which has a clean
    /// recovery path: return the winning room) from any other DB error (which
    /// should propagate). Defensive matching: Postgres reports the constraint
    /// name in the exception's ConstraintName property, but if the index was
    /// declared without an explicit name the implementation-detail name surfaces
    /// there — so we also match on the textual SqlState as a fallback.
    /// </summary>
    private static bool IsUniqueViolationOn(DbUpdateException ex, string indexName)
    {
        var inner = ex.InnerException;
        while (inner is not null)
        {
            if (inner is Npgsql.PostgresException pg && pg.SqlState == "23505")
            {
                return pg.ConstraintName == indexName ||
                       (pg.MessageText?.Contains(indexName, StringComparison.Ordinal) ?? false);
            }
            inner = inner.InnerException;
        }
        return false;
    }
}
