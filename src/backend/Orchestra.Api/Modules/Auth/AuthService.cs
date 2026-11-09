using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orchestra.Api.Common;
using Orchestra.Api.Data;
using Orchestra.Api.Models.Entities;

namespace Orchestra.Api.Modules.Auth;

public sealed record IssuedSession(LoginResponse Response, string RefreshToken);

public sealed class AuthService(
    OrchestraDbContext db, IPasswordHasher<AppUser> passwords, CodeService codes,
    TokenService tokens, IEmailSender emailSender, IWebHostEnvironment environment,
    IConfiguration configuration, AuthSettings settings)
{
    private const string VerifyPurpose = "EMAIL_VERIFICATION";
    private const string ResetPurpose = "PASSWORD_RESET";

    public async Task RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        ValidatePassword(request.Password);
        var domain = email[(email.LastIndexOf('@') + 1)..];
        var allowAnyDomain = environment.IsDevelopment() &&
            configuration.GetValue<bool>("DevelopmentEmail:AllowAnyDomain") &&
            !string.IsNullOrWhiteSpace(configuration["DevelopmentEmail:Recipient"]);
        if (!allowAnyDomain && !await db.ApprovedEmailDomains.AnyAsync(
                d => d.IsActive && d.DomainName.ToLower() == domain, ct))
            throw new ApiException(422, "EMAIL_DOMAIN_NOT_APPROVED", "Use an approved company email address.");
        if (request.DepartmentId == Guid.Empty || !await db.Departments.AnyAsync(
                d => d.DepartmentId == request.DepartmentId && d.Status == "ACTIVE", ct))
            throw new ApiException(422, "DEPARTMENT_NOT_AVAILABLE", "Select an active department.");
        if (await db.AppUsers.AnyAsync(u => u.Email.ToLower() == email, ct))
            throw new ApiException(409, "EMAIL_ALREADY_REGISTERED", "An account with this email already exists.");

        var now = DateTime.UtcNow;
        var user = new AppUser
        {
            UserId = Guid.NewGuid(), Email = email, RoleCode = "USER",
            DepartmentId = request.DepartmentId,
            FirstName = CleanName(request.FirstName, 100), LastName = CleanName(request.LastName, 100),
            Status = "PENDING", CreatedAt = now, UpdatedAt = now,
            SecurityStamp = Guid.NewGuid()
        };
        user.PasswordHash = passwords.HashPassword(user, request.Password);
        var plainCode = CodeService.NewCode();
        var challenge = NewChallenge(user, VerifyPurpose, plainCode);
        db.AppUsers.Add(user);
        db.VerificationCodes.Add(challenge);
        db.UserPreferences.Add(new UserPreference
        {
            PreferenceId = Guid.NewGuid(),
            UserId = user.UserId,
            PreferenceKey = "colorTheme",
            PreferenceValue = JsonSerializer.Serialize("Dark"),
            CreatedAt = now,
            UpdatedAt = now
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ApiException(409, "EMAIL_ALREADY_REGISTERED", "An account with this email already exists.");
        }
        await DeliverAsync(user.Email, VerifyPurpose, plainCode, challenge, ct);
    }

    public async Task VerifyEmailAsync(EmailCodeRequest request, CancellationToken ct)
    {
        var user = await FindByEmailAsync(request.Email, ct)
            ?? throw new ApiException(400, "INVALID_CODE", "The code is invalid or expired.");
        if (user.Status != "PENDING" || user.DeletedAt != null)
            throw new ApiException(409, "INVALID_ACCOUNT_STATE", "This account cannot be verified.");
        var challenge = await ActiveChallengeAsync(user.UserId, VerifyPurpose, ct);
        if (challenge is null || !codes.Matches(user.UserId, VerifyPurpose, request.Code, challenge.CodeHash))
        {
            await RecordBadCodeAsync(challenge, ct);
            throw new ApiException(400, "INVALID_CODE", "The code is invalid or expired.");
        }
        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var consumed = await db.VerificationCodes.Where(c => c.VerificationCodeId == challenge.VerificationCodeId &&
            c.UsedAt == null && c.InvalidatedAt == null && c.ExpiresAt > now && c.FailedAttempts < 5)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now), ct);
        if (consumed != 1)
            throw new ApiException(400, "INVALID_CODE", "The code is invalid or expired.");
        user.Status = "ACTIVE";
        user.EmailVerifiedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task ResendVerificationAsync(EmailRequest request, CancellationToken ct)
    {
        var user = await FindByEmailAsync(request.Email, ct);
        if (user is null || user.Status != "PENDING" || user.DeletedAt != null) return;
        await IssueCodeAsync(user, VerifyPurpose, ct);
    }

    public async Task<IssuedSession> LoginAsync(LoginRequest request, string? ip, string? userAgent, CancellationToken ct)
    {
        var user = await FindByEmailAsync(request.Email, ct);
        var now = DateTime.UtcNow;
        if (user is null || user.DeletedAt != null || user.LockedUntil > now ||
            user.Status != "ACTIVE" || user.EmailVerifiedAt == null)
            throw InvalidLogin();
        if (passwords.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= 5) user.LockedUntil = now.AddMinutes(15);
            await db.SaveChangesAsync(ct);
            throw InvalidLogin();
        }
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        var session = NewRefreshToken(user, Guid.NewGuid(), null, ip, userAgent);
        db.RefreshTokens.Add(session.Entity);
        await db.SaveChangesAsync(ct);
        return new IssuedSession(tokens.AccessToken(user), session.Plain);
    }

    public async Task<IssuedSession> RefreshAsync(string plain, string? ip, string? userAgent, CancellationToken ct)
    {
        var hash = TokenService.HashOpaqueToken(plain);
        var old = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new ApiException(401, "INVALID_SESSION", "The session has expired.");
        var now = DateTime.UtcNow;
        if (old.RevokedAt != null)
        {
            await db.RefreshTokens.Where(t => t.UserId == old.UserId && t.FamilyId == old.FamilyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.RevokedReason, "REUSE_DETECTED"), ct);
            throw new ApiException(401, "SESSION_REUSED", "Please sign in again.");
        }
        if (old.ExpiresAt <= now) throw new ApiException(401, "INVALID_SESSION", "The session has expired.");
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.UserId == old.UserId, ct);
        if (user is null || user.Status != "ACTIVE" || user.EmailVerifiedAt == null || user.DeletedAt != null)
            throw new ApiException(401, "INVALID_SESSION", "Please sign in again.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.RefreshTokens.Where(t => t.RefreshTokenId == old.RefreshTokenId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, "ROTATED")
                .SetProperty(t => t.LastUsedAt, now), ct);
        if (changed != 1)
        {
            await transaction.RollbackAsync(ct);
            await db.RefreshTokens.Where(t => t.UserId == old.UserId && t.FamilyId == old.FamilyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.RevokedReason, "REUSE_DETECTED"), ct);
            throw new ApiException(401, "SESSION_REUSED", "Please sign in again.");
        }
        var next = NewRefreshToken(user, old.FamilyId, old.RefreshTokenId, ip, userAgent);
        db.RefreshTokens.Add(next.Entity);
        await db.SaveChangesAsync(ct);
        await db.RefreshTokens.Where(t => t.RefreshTokenId == old.RefreshTokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReplacedByTokenId, next.Entity.RefreshTokenId), ct);
        await transaction.CommitAsync(ct);
        return new IssuedSession(tokens.AccessToken(user), next.Plain);
    }

    public Task<int> LogoutAsync(string plain, CancellationToken ct) => db.RefreshTokens
        .Where(t => t.TokenHash == TokenService.HashOpaqueToken(plain) && t.RevokedAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow)
            .SetProperty(t => t.RevokedReason, "LOGOUT"), ct);

    public async Task ForgotPasswordAsync(EmailRequest request, CancellationToken ct)
    {
        var user = await FindByEmailAsync(request.Email, ct);
        if (user is null || user.Status != "ACTIVE" || user.DeletedAt != null) return;
        try { await IssueCodeAsync(user, ResetPurpose, ct); }
        catch (ApiException ex) when (ex.Code is "CODE_RESEND_LIMIT" or "EMAIL_UNAVAILABLE")
        {
            // Recovery does not reveal whether an account exists or mail was sent.
        }
    }

    public async Task<string> VerifyResetCodeAsync(EmailCodeRequest request, CancellationToken ct)
    {
        var user = await FindByEmailAsync(request.Email, ct)
            ?? throw new ApiException(400, "INVALID_CODE", "The code is invalid or expired.");
        if (user.Status != "ACTIVE" || user.DeletedAt != null)
            throw new ApiException(400, "INVALID_CODE", "The code is invalid or expired.");
        var challenge = await ActiveChallengeAsync(user.UserId, ResetPurpose, ct);
        if (challenge is null || challenge.SecurityStampSnapshot != user.SecurityStamp ||
            !codes.Matches(user.UserId, ResetPurpose, request.Code, challenge.CodeHash))
        {
            await RecordBadCodeAsync(challenge, ct);
            throw new ApiException(400, "INVALID_CODE", "The code is invalid or expired.");
        }
        return tokens.ResetToken(user, challenge.VerificationCodeId);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        ValidatePassword(request.NewPassword);
        var parsed = tokens.ValidateResetToken(request.ResetToken)
            ?? throw new ApiException(400, "INVALID_RESET_TOKEN", "The reset token is invalid or expired.");
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.UserId == parsed.UserId, ct);
        if (user is null || user.SecurityStamp != parsed.Stamp || user.Status != "ACTIVE" || user.DeletedAt != null)
            throw new ApiException(400, "INVALID_RESET_TOKEN", "The reset token is invalid or expired.");
        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var consumed = await db.VerificationCodes.Where(c => c.VerificationCodeId == parsed.ChallengeId &&
            c.UserId == user.UserId && c.Purpose == ResetPurpose &&
            c.SecurityStampSnapshot == parsed.Stamp && c.UsedAt == null &&
            c.InvalidatedAt == null && c.ExpiresAt > now && c.FailedAttempts < 5)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now), ct);
        if (consumed != 1) throw new ApiException(400, "INVALID_RESET_TOKEN", "The reset token is invalid or expired.");
        user.PasswordHash = passwords.HashPassword(user, request.NewPassword);
        user.SecurityStamp = Guid.NewGuid();
        user.PasswordChangedAt = now;
        user.MustChangePassword = false;
        await db.SaveChangesAsync(ct);
        await db.RefreshTokens.Where(t => t.UserId == user.UserId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, "PASSWORD_RESET"), ct);
        await transaction.CommitAsync(ct);
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        ValidatePassword(newPassword);
        var user = await db.AppUsers.FirstAsync(u => u.UserId == userId, ct);
        if (passwords.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            throw new ApiException(400, "INVALID_PASSWORD", "The current password is incorrect.");
        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        user.PasswordHash = passwords.HashPassword(user, newPassword);
        user.MustChangePassword = false;
        user.PasswordChangedAt = now;
        user.SecurityStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, "PASSWORD_CHANGED"), ct);
        await transaction.CommitAsync(ct);
    }

    public Task<AppUser?> CurrentUserAsync(Guid id, CancellationToken ct) => db.AppUsers
        .AsNoTracking().FirstOrDefaultAsync(u => u.UserId == id && u.DeletedAt == null && u.Status == "ACTIVE", ct);

    public async Task<IReadOnlyList<SessionDto>> SessionsAsync(Guid userId, string? currentToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentToken))
            throw new ApiException(401, "INVALID_SESSION", "The session has expired.");

        var currentHash = TokenService.HashOpaqueToken(currentToken);
        var currentSession = await db.RefreshTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == currentHash && t.UserId == userId, ct);
        if (currentSession is null || currentSession.RevokedAt is not null ||
            currentSession.ExpiresAt <= DateTime.UtcNow)
            throw new ApiException(401, "INVALID_SESSION", "The session has expired.");

        var sessions = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt).Take(50).ToListAsync(ct);
        return sessions.Select(t => new SessionDto(t.RefreshTokenId, t.CreatedAt, t.ExpiresAt,
            t.LastUsedAt, t.UserAgent, t.RefreshTokenId == currentSession.RefreshTokenId)).ToList();
    }

    private async Task IssueCodeAsync(AppUser user, string purpose, CancellationToken ct)
    {
        var last = await db.VerificationCodes.AsNoTracking()
            .Where(c => c.UserId == user.UserId && c.Purpose == purpose)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        if (last is not null && last.CreatedAt > DateTime.UtcNow.AddSeconds(-60))
            throw new ApiException(429, "CODE_RESEND_LIMIT", "Wait a minute before requesting another code.");
        var now = DateTime.UtcNow;
        await db.VerificationCodes.Where(c => c.UserId == user.UserId && c.Purpose == purpose &&
            c.UsedAt == null && c.InvalidatedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.InvalidatedAt, now), ct);
        var plainCode = CodeService.NewCode();
        var challenge = NewChallenge(user, purpose, plainCode);
        db.VerificationCodes.Add(challenge);
        await db.SaveChangesAsync(ct);
        await DeliverAsync(user.Email, purpose, plainCode, challenge, ct);
    }

    private async Task DeliverAsync(string email, string purpose, string code, VerificationCode challenge,
        CancellationToken ct)
    {
        try
        {
            await emailSender.SendCodeAsync(email, purpose == VerifyPurpose ? "verification" : "password reset", code, ct);
            challenge.DeliveryStatus = "SENT";
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException ||
            (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            challenge.DeliveryStatus = "FAILED";
            challenge.InvalidatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            throw new ApiException(503, "EMAIL_UNAVAILABLE", "Email delivery is temporarily unavailable.");
        }
    }

    private VerificationCode NewChallenge(AppUser user, string purpose, string code) => new()
    {
        VerificationCodeId = Guid.NewGuid(), UserId = user.UserId,
        Purpose = purpose, CodeHash = codes.Hash(user.UserId, purpose, code),
        SecurityStampSnapshot = purpose == ResetPurpose ? user.SecurityStamp : null,
        DeliveryStatus = "PENDING", FailedAttempts = 0,
        CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(10)
    };

    private Task<VerificationCode?> ActiveChallengeAsync(Guid userId, string purpose, CancellationToken ct) =>
        db.VerificationCodes.AsNoTracking().Where(c => c.UserId == userId && c.Purpose == purpose &&
            c.UsedAt == null && c.InvalidatedAt == null && c.ExpiresAt > DateTime.UtcNow && c.FailedAttempts < 5)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);

    private Task<int> RecordBadCodeAsync(VerificationCode? challenge, CancellationToken ct) => challenge is null
        ? Task.FromResult(0)
        : db.VerificationCodes.Where(c => c.VerificationCodeId == challenge.VerificationCodeId &&
            c.FailedAttempts < 5 && c.UsedAt == null && c.InvalidatedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.FailedAttempts, c => c.FailedAttempts + 1), ct);

    private Task<AppUser?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var normalized = NormalizeEmail(email);
        return db.AppUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalized, ct);
    }

    private (RefreshToken Entity, string Plain) NewRefreshToken(AppUser user, Guid familyId,
        Guid? parentId, string? ip, string? userAgent)
    {
        var plain = TokenService.NewOpaqueToken();
        return (new RefreshToken
        {
            RefreshTokenId = Guid.NewGuid(), UserId = user.UserId, TokenHash = TokenService.HashOpaqueToken(plain),
            FamilyId = familyId, ParentTokenId = parentId, CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(settings.RefreshTokenDays),
            CreatedByIp = System.Net.IPAddress.TryParse(ip, out var parsedIp) ? parsedIp : null,
            UserAgent = userAgent is { Length: > 500 } ? userAgent[..500] : userAgent
        }, plain);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
    private static string CleanName(string value, int max)
    {
        var result = value.Trim();
        if (result.Length is 0 || result.Length > max)
            throw new ApiException(422, "INVALID_NAME", "Enter a valid first and last name.");
        return result;
    }
    private static void ValidatePassword(string password)
    {
        if (password.Length is < 12 or > 128)
            throw new ApiException(422, "INVALID_PASSWORD", "Password must be between 12 and 128 characters.");
    }
    private static ApiException InvalidLogin() => new(401, "INVALID_CREDENTIALS", "Invalid email or password.");
    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation };
}
