using Microsoft.AspNetCore.Authorization;

namespace SigpaBackend.Services;

public sealed class CoordinadorAuthorizationHandler : AuthorizationHandler<CoordinadorRequirement>
{
    private readonly UserAccessService _userAccess;

    public CoordinadorAuthorizationHandler(UserAccessService userAccess)
    {
        _userAccess = userAccess;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CoordinadorRequirement requirement)
    {
        var userId = UserAccessService.GetUserId(context.User);
        if (userId.HasValue && await _userAccess.HasRoleAsync(userId.Value, "Coordinador"))
            context.Succeed(requirement);
    }
}
