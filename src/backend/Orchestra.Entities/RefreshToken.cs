using System;
using System.Collections.Generic;
using System.Net;

namespace Orchestra.Api.Models.Entities;

public partial class RefreshToken
{
    public Guid RefreshTokenId { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = null!;

    public Guid FamilyId { get; set; }

    public Guid? ParentTokenId { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? RevokedReason { get; set; }

    public IPAddress? CreatedByIp { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public string? UserAgent { get; set; }

    public virtual ICollection<RefreshToken> InverseRefreshToken1 { get; set; } = new List<RefreshToken>();

    public virtual ICollection<RefreshToken> InverseRefreshTokenNavigation { get; set; } = new List<RefreshToken>();

    public virtual RefreshToken? RefreshToken1 { get; set; }

    public virtual RefreshToken? RefreshTokenNavigation { get; set; }

    public virtual AppUser User { get; set; } = null!;
}
