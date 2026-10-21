using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Domain.Entities;
using Xunit;

namespace ArvindJobHunter.Application.Tests;

public sealed class SiteGuardTests
{
    private static readonly IReadOnlyList<SiteSettings> Sites = [new("company", true, 2), new("linkedin", false, 5)];

    [Fact]
    public void EnabledSiteUnderCap_IsAllowed() => ExternalActionGuard.EnsureSiteAllowed("company", Sites, 1);

    [Fact]
    public void DisabledSite_IsRejected() =>
        Assert.Throws<ApprovalViolationException>(() => ExternalActionGuard.EnsureSiteAllowed("linkedin", Sites, 0));

    [Fact]
    public void UnknownSite_IsRejected() =>
        Assert.Throws<ApprovalViolationException>(() => ExternalActionGuard.EnsureSiteAllowed("indeed", Sites, 0));

    [Fact]
    public void CapReached_IsRejected() =>
        Assert.Throws<ApprovalViolationException>(() => ExternalActionGuard.EnsureSiteAllowed("company", Sites, 2));
}
