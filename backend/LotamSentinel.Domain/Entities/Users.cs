namespace LotamSentinel.Domain.Entities;

/// <summary>An application login - who can access LOTAM Sentinel itself.
/// Deliberately separate from Supplier/Manufacturer (the LOTAM domain data);
/// this table has nothing to do with SEC's approved-manufacturer list.
/// Users are provisioned by an admin (via the startup bootstrap - see
/// Program.cs) rather than self-registered, matching the project's
/// enterprise-security-first direction; there is no public sign-up
/// endpoint.</summary>
public class AppUser
{
    public long Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? Mobile { get; set; }
    public string? Address { get; set; }
    public string? Department { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Admins can invite new users from the Profile page's Admin
    /// tab. The very first bootstrap user (see Program.cs) is always
    /// created as an admin; every invited user defaults to non-admin.</summary>
    public bool IsAdmin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<PasswordResetOtp> PasswordResetOtps { get; set; } = new List<PasswordResetOtp>();
    public ICollection<UserNotificationSubscription> NotificationSubscriptions { get; set; } = new List<UserNotificationSubscription>();
}

/// <summary>A one-time code emailed to the user to authorize a password
/// reset. Stored hashed with the same IPasswordHasher used for login
/// passwords, so a database read alone never exposes a usable code.
/// Single-use (Used) and short-lived (ExpiresAt) - AuthController rejects
/// it once either condition is true, regardless of whether the code text
/// matches.</summary>
public class PasswordResetOtp
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string CodeHash { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Used { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One thing a user wants emailed about when it changes - a
/// material category, a technology area, or a specific supplier.
/// TargetCode/TargetLabel are denormalized (not FK'd to MaterialCategory /
/// Technology / Supplier) so a subscription still displays sensibly even if
/// the thing it points at is later renamed, and so this table doesn't need
/// to know about three unrelated id spaces.</summary>
public class UserNotificationSubscription
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public NotificationSubscriptionType SubscriptionType { get; set; }
    public string TargetCode { get; set; } = null!;
    public string TargetLabel { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<SubscriptionRecipient> Recipients { get; set; } = new List<SubscriptionRecipient>();
}

/// <summary>An extra email address that should also be notified about this
/// one subscription (e.g. a colleague who isn't a LOTAM Sentinel user
/// themselves, or a shared department inbox) - added from the Profile
/// page's Notification Settings tab after a subscription already exists.
/// The subscription's own owner (UserNotificationSubscription.User) always
/// gets notified regardless of what's here; this table is purely additive.
/// Nothing sends to these yet - see README "Known limitations" for the
/// notification-digest background job this is the configuration surface
/// for, same as the subscriptions themselves.</summary>
public class SubscriptionRecipient
{
    public long Id { get; set; }
    public long SubscriptionId { get; set; }
    public UserNotificationSubscription Subscription { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
