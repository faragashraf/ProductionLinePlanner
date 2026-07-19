using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.DTOs;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Application.Notifications;
using ProductionLinePlanner.Domain.Authorization;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Domain.Notifications;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.Notifications;

public sealed class NotificationPolicyAdminService(
    AppDbContext dbContext,
    INotificationEventCatalog eventCatalog,
    INotificationTemplateResolver templateResolver,
    INotificationPolicyCatalogReconciler reconciler,
    IAuditEngine auditEngine) : INotificationPolicyAdminService
{
    public NotificationPolicyStudioFoundationDto GetFoundation() => new()
    {
        Events = eventCatalog.GetAll()
            .Select(definition => new NotificationPolicyCatalogItemDto
            {
                EventKey = definition.Key,
                DisplayName = definition.DisplayName,
                AllowedTokens = definition.AllowedTokens.ToArray(),
                DefaultPolicy = new NotificationPolicyDraftDto
                {
                    IsEnabled = definition.DefaultPolicy.IsEnabled,
                    Severity = definition.DefaultPolicy.Severity.ToString(),
                    SoundEnabled = definition.DefaultPolicy.Sound.Enabled,
                    ToastEnabled = definition.DefaultPolicy.Toast.Enabled,
                    InboxEnabled = definition.DefaultPolicy.Inbox.Enabled,
                    TitleTemplate = definition.DefaultPolicy.TitleTemplate,
                    MessageTemplate = definition.DefaultPolicy.MessageTemplate,
                    RecipientRules = []
                }
            })
            .ToArray(),
        Severities = Enum.GetNames<NotificationSeverity>(),
        RecipientRuleKinds = Enum.GetNames<NotificationRecipientKind>(),
        Sound = new NotificationSoundFoundationDto(),
        DeferredChannels = ["WebPush"]
    };

    public async Task<Result<IReadOnlyCollection<NotificationPolicyListItemDto>>> GetPoliciesAsync(
        CancellationToken cancellationToken = default)
    {
        var reconciliation = await reconciler.EnsureDefaultsAsync(cancellationToken);
        if (reconciliation.IsFailure)
        {
            return Result<IReadOnlyCollection<NotificationPolicyListItemDto>>.Failure(reconciliation.Error!);
        }

        var policies = await dbContext.NotificationPolicies
            .AsNoTracking()
            .ToDictionaryAsync(policy => policy.EventKey, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var items = eventCatalog.GetAll()
            .Where(definition => policies.TryGetValue(definition.Key, out _))
            .Select(definition => ToListItem(policies[definition.Key], definition))
            .ToArray();
        return Result<IReadOnlyCollection<NotificationPolicyListItemDto>>.Success(items);
    }

    public async Task<Result<NotificationPolicyDetailsDto>> GetPolicyAsync(
        string eventKey,
        CancellationToken cancellationToken = default)
    {
        var definition = eventCatalog.Find(eventKey);
        if (definition is null)
        {
            return Failure("UnknownNotificationEvent", "The notification event is not in the product catalog.");
        }

        var reconciliation = await reconciler.EnsureDefaultsAsync(cancellationToken);
        if (reconciliation.IsFailure) return Result<NotificationPolicyDetailsDto>.Failure(reconciliation.Error!);

        var policy = await dbContext.NotificationPolicies
            .AsNoTracking()
            .Include(item => item.RecipientRules)
            .SingleOrDefaultAsync(item => item.EventKey == definition.Key, cancellationToken);
        return policy is null
            ? Failure("NotificationPolicyNotFound", "The notification policy could not be loaded.")
            : Result<NotificationPolicyDetailsDto>.Success(ToDetails(policy, definition));
    }

    public async Task<Result<NotificationPolicyRecipientOptionsDto>> GetRecipientOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var users = await dbContext.AppUsers
            .AsNoTracking()
            .Where(user => user.IsActive)
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.Email)
            .Select(user => new NotificationPolicyRecipientUserOptionDto(user.Id.ToString(), user.FullName, user.Email))
            .ToArrayAsync(cancellationToken);
        var roles = await dbContext.AppRoles
            .AsNoTracking()
            .Where(role => role.IsActive)
            .OrderBy(role => role.Name)
            .Select(role => new NotificationPolicyRecipientRoleOptionDto(role.Id.ToString(), role.Name))
            .ToArrayAsync(cancellationToken);
        var permissions = PermissionCatalog.All
            .OrderBy(permission => permission.Capability, StringComparer.OrdinalIgnoreCase)
            .ThenBy(permission => permission.Name, StringComparer.OrdinalIgnoreCase)
            .Select(permission => new NotificationPolicyRecipientPermissionOptionDto(permission.Name, permission.Capability, permission.DescriptionAr))
            .ToArray();

        return Result<NotificationPolicyRecipientOptionsDto>.Success(new NotificationPolicyRecipientOptionsDto
        {
            Users = users,
            Roles = roles,
            Permissions = permissions,
            CapabilityGroups = permissions
                .Select(permission => permission.Capability)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(capability => capability, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        });
    }

    public async Task<Result<NotificationPolicyDetailsDto>> UpdatePolicyAsync(
        string eventKey,
        NotificationPolicyUpdateRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Failure("Unauthorized", "User context is required.");
        }

        var definition = eventCatalog.Find(eventKey);
        if (definition is null)
        {
            return Failure("UnknownNotificationEvent", "The notification event is not in the product catalog.");
        }

        var reconciliation = await reconciler.EnsureDefaultsAsync(cancellationToken);
        if (reconciliation.IsFailure) return Result<NotificationPolicyDetailsDto>.Failure(reconciliation.Error!);

        var policy = await dbContext.NotificationPolicies
            .Include(item => item.RecipientRules)
            .SingleOrDefaultAsync(item => item.EventKey == definition.Key, cancellationToken);
        if (policy is null)
        {
            return Failure("NotificationPolicyNotFound", "The notification policy could not be loaded.");
        }

        if (!TryDecodeRowVersion(request?.RowVersion, out var expectedRowVersion))
        {
            return Failure("ValidationError", "A valid notification policy row version is required.");
        }

        if (!expectedRowVersion.SequenceEqual(policy.RowVersion))
        {
            return Failure("ConcurrencyConflict", "This notification policy was changed by another user. Reload it before saving.");
        }

        var validation = await ValidateUpdateAsync(request, definition, cancellationToken);
        if (validation.IsFailure)
        {
            return Result<NotificationPolicyDetailsDto>.Failure(validation.Error!);
        }

        var update = validation.Value!;
        var before = new
        {
            policy.Id,
            policy.EventKey,
            policy.IsEnabled,
            policy.Severity,
            policy.IsToastEnabled,
            policy.IsInboxEnabled,
            policy.IsSoundEnabled,
            RecipientRuleCount = policy.RecipientRules.Count
        };
        try
        {
            policy.Update(
                update.IsEnabled,
                update.Severity,
                update.IsToastEnabled,
                update.IsInboxEnabled,
                update.IsSoundEnabled,
                update.SoundKey,
                update.TitleTemplateAr,
                update.MessageTemplateAr,
                actorUserId);
            dbContext.NotificationPolicyRecipientRules.RemoveRange(policy.RecipientRules);
            policy.RecipientRules.Clear();
            foreach (var rule in update.Rules)
            {
                policy.RecipientRules.Add(new NotificationPolicyRecipientRule(
                    Guid.NewGuid(),
                    policy.Id,
                    rule.RecipientKind,
                    rule.UserId,
                    rule.RoleId,
                    rule.PermissionKey,
                    rule.CapabilityKey,
                    rule.IsExcludeActor,
                    rule.SortOrder,
                    rule.IsActive));
            }

            var audit = await auditEngine.RecordAsync(
                actorUserId,
                AuditActionType.Update,
                nameof(NotificationPolicy),
                policy.Id.ToString(),
                before,
                new
                {
                    policy.Id,
                    policy.EventKey,
                    policy.IsEnabled,
                    policy.Severity,
                    policy.IsToastEnabled,
                    policy.IsInboxEnabled,
                    policy.IsSoundEnabled,
                    RecipientRuleCount = update.Rules.Count
                },
                requestMeta,
                cancellationToken);
            if (audit.IsFailure)
            {
                return Result<NotificationPolicyDetailsDto>.Failure(audit.Error!);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure("ConcurrencyConflict", "This notification policy was changed by another user. Reload it before saving.");
        }

        return Result<NotificationPolicyDetailsDto>.Success(ToDetails(policy, definition));
    }

    public async Task<Result<NotificationPolicyDetailsDto>> ReplaceRecipientRulesAsync(
        string eventKey,
        NotificationPolicyRecipientRulesReplaceRequest request,
        Guid actorUserId,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Failure("ValidationError", "Recipient rule data is required.");
        }

        var current = await GetPolicyAsync(eventKey, cancellationToken);
        if (current.IsFailure) return current;
        var policy = current.Value!;
        return await UpdatePolicyAsync(
            eventKey,
            new NotificationPolicyUpdateRequest
            {
                IsEnabled = policy.IsEnabled,
                Severity = policy.Severity,
                IsToastEnabled = policy.IsToastEnabled,
                IsInboxEnabled = policy.IsInboxEnabled,
                IsSoundEnabled = policy.IsSoundEnabled,
                SoundKey = policy.SoundKey,
                TitleTemplateAr = policy.TitleTemplateAr,
                MessageTemplateAr = policy.MessageTemplateAr,
                RowVersion = request.RowVersion,
                RecipientRules = request.RecipientRules
            },
            actorUserId,
            requestMeta,
            cancellationToken);
    }

    private async Task<Result<ValidatedUpdate>> ValidateUpdateAsync(
        NotificationPolicyUpdateRequest? request,
        NotificationEventDefinition definition,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Result<ValidatedUpdate>.Failure(new Error("ValidationError", "Notification policy data is required."));
        }

        if (!Enum.TryParse<NotificationSeverity>(request.Severity?.Trim(), ignoreCase: true, out var severity) || !Enum.IsDefined(severity))
        {
            return Result<ValidatedUpdate>.Failure(new Error("InvalidNotificationSeverity", "A supported notification severity is required."));
        }

        if (request.IsSoundEnabled && !string.IsNullOrWhiteSpace(request.SoundKey) &&
            !string.Equals(request.SoundKey.Trim(), "default", StringComparison.OrdinalIgnoreCase))
        {
            return Result<ValidatedUpdate>.Failure(new Error("InvalidSoundKey", "Only the default sound key is supported."));
        }

        var titleValidation = templateResolver.Validate(request.TitleTemplateAr, definition.AllowedTokens);
        if (titleValidation.IsFailure) return Result<ValidatedUpdate>.Failure(titleValidation.Error!);
        var messageValidation = templateResolver.Validate(request.MessageTemplateAr, definition.AllowedTokens);
        if (messageValidation.IsFailure) return Result<ValidatedUpdate>.Failure(messageValidation.Error!);
        if (request.TitleTemplateAr.Trim().Length > NotificationPolicy.MaxTitleTemplateLength ||
            request.MessageTemplateAr.Trim().Length > NotificationPolicy.MaxMessageTemplateLength)
        {
            return Result<ValidatedUpdate>.Failure(new Error("ValidationError", "Notification policy templates exceed their supported length."));
        }

        var ruleValidation = await ValidateRulesAsync(request.RecipientRules, cancellationToken);
        if (ruleValidation.IsFailure) return Result<ValidatedUpdate>.Failure(ruleValidation.Error!);

        return Result<ValidatedUpdate>.Success(new ValidatedUpdate(
            request.IsEnabled,
            severity,
            request.IsToastEnabled,
            request.IsInboxEnabled,
            request.IsSoundEnabled,
            request.IsSoundEnabled ? "default" : null,
            request.TitleTemplateAr.Trim(),
            request.MessageTemplateAr.Trim(),
            ruleValidation.Value!));
    }

    private async Task<Result<IReadOnlyCollection<ValidatedRule>>> ValidateRulesAsync(
        IReadOnlyCollection<NotificationPolicyRecipientRuleUpdateRequest>? rules,
        CancellationToken cancellationToken)
    {
        var normalized = new List<ValidatedRule>();
        foreach (var request in rules ?? [])
        {
            if (!Enum.TryParse<NotificationRecipientKind>(request.RecipientKind?.Trim(), true, out var kind) || !Enum.IsDefined(kind) || request.SortOrder < 0)
            {
                return RuleFailure("Each recipient rule needs a supported kind and non-negative sort order.");
            }

            var userId = ParseOptionalGuid(request.UserId);
            var roleId = ParseOptionalGuid(request.RoleId);
            var permissionKey = NormalizeOptional(request.PermissionKey);
            var capabilityKey = NormalizeOptional(request.CapabilityKey);
            var validShape = kind switch
            {
                NotificationRecipientKind.User => userId is not null && roleId is null && permissionKey is null && capabilityKey is null && !request.IsExcludeActor,
                NotificationRecipientKind.Role => userId is null && roleId is not null && permissionKey is null && capabilityKey is null && !request.IsExcludeActor,
                NotificationRecipientKind.Permission => userId is null && roleId is null && !string.IsNullOrWhiteSpace(permissionKey) && PermissionCatalog.IsKnown(permissionKey) && capabilityKey is null && !request.IsExcludeActor,
                NotificationRecipientKind.CapabilityGroup => userId is null && roleId is null && permissionKey is null && IsKnownCapability(capabilityKey) && !request.IsExcludeActor,
                NotificationRecipientKind.Creator => userId is null && roleId is null && permissionKey is null && capabilityKey is null && !request.IsExcludeActor,
                NotificationRecipientKind.ExcludeActor => userId is null && roleId is null && permissionKey is null && capabilityKey is null && request.IsExcludeActor,
                _ => false
            };
            if (!validShape)
            {
                return RuleFailure("Recipient rule targets do not match the selected recipient kind.");
            }

            normalized.Add(new ValidatedRule(kind, userId, roleId, permissionKey, capabilityKey, request.IsExcludeActor, request.SortOrder, request.IsActive));
        }

        if (normalized.Count > 100 || normalized.GroupBy(rule => rule.SortOrder).Any(group => group.Count() > 1))
        {
            return RuleFailure("Recipient rules must contain at most 100 entries with unique sort orders.");
        }

        var userIds = normalized.Where(rule => rule.UserId is not null).Select(rule => rule.UserId!.Value).ToArray();
        if (userIds.Length > 0)
        {
            var existing = await dbContext.AppUsers.CountAsync(user => userIds.Contains(user.Id), cancellationToken);
            if (existing != userIds.Distinct().Count()) return RuleFailure("One or more recipient users do not exist.");
        }

        var roleIds = normalized.Where(rule => rule.RoleId is not null).Select(rule => rule.RoleId!.Value).ToArray();
        if (roleIds.Length > 0)
        {
            var existing = await dbContext.AppRoles.CountAsync(role => roleIds.Contains(role.Id), cancellationToken);
            if (existing != roleIds.Distinct().Count()) return RuleFailure("One or more recipient roles do not exist.");
        }

        return Result<IReadOnlyCollection<ValidatedRule>>.Success(normalized.OrderBy(rule => rule.SortOrder).ToArray());
    }

    private static NotificationPolicyListItemDto ToListItem(NotificationPolicy policy, NotificationEventDefinition definition) => new()
    {
        EventKey = definition.Key,
        DisplayName = definition.DisplayName,
        IsEnabled = policy.IsEnabled,
        Severity = policy.Severity.ToString(),
        IsToastEnabled = policy.IsToastEnabled,
        IsInboxEnabled = policy.IsInboxEnabled,
        IsSoundEnabled = policy.IsSoundEnabled,
        UpdatedAtUtc = policy.UpdatedAtUtc.ToString("O")
    };

    private static NotificationPolicyDetailsDto ToDetails(NotificationPolicy policy, NotificationEventDefinition definition) => new()
    {
        EventKey = definition.Key,
        DisplayName = definition.DisplayName,
        AllowedTokens = definition.AllowedTokens.ToArray(),
        IsEnabled = policy.IsEnabled,
        Severity = policy.Severity.ToString(),
        IsToastEnabled = policy.IsToastEnabled,
        IsInboxEnabled = policy.IsInboxEnabled,
        IsSoundEnabled = policy.IsSoundEnabled,
        SoundKey = policy.SoundKey,
        TitleTemplateAr = policy.TitleTemplateAr,
        MessageTemplateAr = policy.MessageTemplateAr,
        RowVersion = Convert.ToBase64String(policy.RowVersion),
        RecipientRules = policy.RecipientRules
            .OrderBy(rule => rule.SortOrder)
            .Select(rule => new NotificationPolicyRecipientRuleDto
            {
                Id = rule.Id.ToString(),
                RecipientKind = rule.RecipientKind.ToString(),
                UserId = rule.UserId?.ToString(),
                RoleId = rule.RoleId?.ToString(),
                PermissionKey = rule.PermissionKey,
                CapabilityKey = rule.CapabilityKey,
                IsExcludeActor = rule.IsExcludeActor,
                SortOrder = rule.SortOrder,
                IsActive = rule.IsActive
            })
            .ToArray(),
        UpdatedAtUtc = policy.UpdatedAtUtc.ToString("O")
    };

    private static Result<NotificationPolicyDetailsDto> Failure(string code, string message) =>
        Result<NotificationPolicyDetailsDto>.Failure(new Error(code, message));

    private static Result<IReadOnlyCollection<ValidatedRule>> RuleFailure(string message) =>
        Result<IReadOnlyCollection<ValidatedRule>>.Failure(new Error("InvalidRecipientRule", message));

    private static bool TryDecodeRowVersion(string? value, out byte[] rowVersion)
    {
        rowVersion = [];
        return !string.IsNullOrWhiteSpace(value) && TryDecode(value, out rowVersion);
    }

    private static bool TryDecode(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static Guid? ParseOptionalGuid(string? value) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty ? parsed : null;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsKnownCapability(string? capability) =>
        !string.IsNullOrWhiteSpace(capability) && PermissionCatalog.ByCapability(capability).Count > 0;

    private sealed record ValidatedUpdate(
        bool IsEnabled,
        NotificationSeverity Severity,
        bool IsToastEnabled,
        bool IsInboxEnabled,
        bool IsSoundEnabled,
        string? SoundKey,
        string TitleTemplateAr,
        string MessageTemplateAr,
        IReadOnlyCollection<ValidatedRule> Rules);

    private sealed record ValidatedRule(
        NotificationRecipientKind RecipientKind,
        Guid? UserId,
        Guid? RoleId,
        string? PermissionKey,
        string? CapabilityKey,
        bool IsExcludeActor,
        int SortOrder,
        bool IsActive);
}
