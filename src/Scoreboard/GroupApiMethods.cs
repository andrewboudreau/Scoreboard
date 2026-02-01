using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Scoreboard.Models;
using Scoreboard.Services;

namespace SharedTools.Scoreboard;

public static class GroupApiMethods
{
    // POST /Scoreboard/api/groups
    public static async Task<IResult> CreateGroup(
        HttpContext httpContext,
        IGroupService groupService)
    {
        var request = await httpContext.Request.ReadFromJsonAsync<CreateGroupRequest>();
        if (request == null || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "Name is required" });
        }

        var group = await groupService.CreateGroupAsync(request.Name);
        return Results.Created($"/Scoreboard/api/groups/{group.Id}", new
        {
            groupId = group.Id,
            name = group.Name,
            adminCode = group.AdminCode
        });
    }

    // GET /Scoreboard/api/groups/join?code=XXXX
    public static async Task<IResult> JoinGroup(
        [FromQuery] string code,
        IGroupService groupService)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Results.BadRequest(new { error = "Code is required" });
        }

        // Try admin code first
        var group = await groupService.GetGroupByAdminCodeAsync(code);
        if (group != null)
        {
            var sasUrls = groupService.GenerateSasUrls(canWrite: true);
            return Results.Ok(new
            {
                groupId = group.Id,
                groupName = group.Name,
                isAdmin = true,
                sasUrls
            });
        }

        // Try member code
        var result = await groupService.GetGroupByMemberCodeAsync(code);
        if (result != null)
        {
            var (memberGroup, _) = result.Value;
            // Teammates get write access too
            var sasUrls = groupService.GenerateSasUrls(canWrite: true);
            return Results.Ok(new
            {
                groupId = memberGroup.Id,
                groupName = memberGroup.Name,
                isAdmin = false,
                sasUrls
            });
        }

        return Results.NotFound(new { error = "Invalid code" });
    }

    // POST /Scoreboard/api/groups/{id}/members?adminCode=XXXX
    public static async Task<IResult> AddMember(
        string id,
        [FromQuery] string adminCode,
        HttpContext httpContext,
        IGroupService groupService)
    {
        if (string.IsNullOrWhiteSpace(adminCode))
        {
            return Results.BadRequest(new { error = "Admin code is required" });
        }

        var group = await groupService.GetGroupByIdAsync(id);
        if (group == null)
        {
            return Results.NotFound(new { error = "Group not found" });
        }

        if (!string.Equals(group.AdminCode, adminCode, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(new { error = "Invalid admin code" }, statusCode: 403);
        }

        var request = await httpContext.Request.ReadFromJsonAsync<AddMemberRequest>();
        if (request == null || string.IsNullOrWhiteSpace(request.Label))
        {
            return Results.BadRequest(new { error = "Label is required" });
        }

        var member = await groupService.AddMemberAsync(id, request.Label);
        return Results.Created($"/Scoreboard/api/groups/{id}/members/{member.Code}", new
        {
            code = member.Code,
            label = member.Label
        });
    }

    // DELETE /Scoreboard/api/groups/{id}/members/{code}?adminCode=XXXX
    public static async Task<IResult> RevokeMember(
        string id,
        string code,
        [FromQuery] string adminCode,
        IGroupService groupService)
    {
        if (string.IsNullOrWhiteSpace(adminCode))
        {
            return Results.BadRequest(new { error = "Admin code is required" });
        }

        var group = await groupService.GetGroupByIdAsync(id);
        if (group == null)
        {
            return Results.NotFound(new { error = "Group not found" });
        }

        if (!string.Equals(group.AdminCode, adminCode, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(new { error = "Invalid admin code" }, statusCode: 403);
        }

        var revoked = await groupService.RevokeMemberAsync(id, code);
        if (!revoked)
        {
            return Results.NotFound(new { error = "Member not found" });
        }

        return Results.NoContent();
    }

    // GET /Scoreboard/api/groups/{id}/sas/refresh?code=XXXX
    public static async Task<IResult> RefreshSas(
        string id,
        [FromQuery] string code,
        IGroupService groupService)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Results.BadRequest(new { error = "Code is required" });
        }

        var group = await groupService.GetGroupByIdAsync(id);
        if (group == null)
        {
            return Results.NotFound(new { error = "Group not found" });
        }

        // Check admin code
        if (string.Equals(group.AdminCode, code, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(groupService.GenerateSasUrls(canWrite: true));
        }

        // Check member code
        var member = group.Members.FirstOrDefault(m =>
            m.Active && string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));
        if (member != null)
        {
            return Results.Ok(groupService.GenerateSasUrls(canWrite: true));
        }

        return Results.Json(new { error = "Invalid code" }, statusCode: 403);
    }
}
