using K2Agents.Api.Infrastructure;
using K2Agents.Application.Common;
using K2Agents.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace K2Agents.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = Policies.CompanyAdmin)]
public class UsersController(IUserService users) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserDto>> List([FromQuery] UserQuery query, CancellationToken ct) => users.ListAsync(query, ct);

    [HttpGet("roles")]
    public Task<IReadOnlyList<RoleDto>> Roles(CancellationToken ct) => users.RolesAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<UserDto> Get(Guid id, CancellationToken ct) => users.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var created = await users.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public Task<UserDto> Update(Guid id, UpdateUserRequest request, CancellationToken ct) => users.UpdateAsync(id, request, ct);

    [HttpPatch("{id:guid}/active")]
    public Task<UserDto> SetActive(Guid id, SetUserActiveRequest request, CancellationToken ct)
        => users.SetActiveAsync(id, request.IsActive, ct);
}
