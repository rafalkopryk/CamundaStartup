# ClaimsAuthorization

Configuration-driven JWT claim-to-role mapping and permission authorization for ASP.NET Core.

## Registration

```csharp
builder.Services.AddClaimsAuthorization(
    Path.Combine(builder.Environment.ContentRootPath, "Authorization", "permissions.json"),
    Path.Combine(builder.Environment.ContentRootPath, "Authorization", "roles.json"));
```

## Mapping rules

```json
{
  "roles": [
    {
      "name": "Client",
      "permissions": ["application.read"]
    },
    {
      "name": "ClientPos",
      "permissions": ["application.create"]
    }
  ],
  "mappings": [
      {
        "key": "Client",
        "role": "Client",
        "requireCif": true,
        "exclusiveGroup": "ClientType"
      },
      {
        "key": "ClientPOS",
        "role": "ClientPos",
        "clientIds": ["POS"],
        "requireCif": true,
        "exclusiveGroup": "ClientType",
        "priority": 100
      }
  ]
}
```

Conditions within a rule use AND semantics. Values in `ClientIds`, `Scopes`, and `Roles` use OR semantics. Rules are additive unless they share an `ExclusiveGroup`; within such a group, only the matching rule with the highest priority is applied.

The library reads the fixed claims `client_id`, `Cif`, `Xnuc`, `scope`/`Scope`, and `Role`/`Roles`. Scalar, repeated, and JSON-array values are supported. Roles are resolved internally without modifying `ClaimsPrincipal` or registering `IClaimsTransformation`.

## Permission authorization

Define stable permission keys in `permissions.json` and assign them to normalized roles in `roles.json`. Endpoints depend only on permissions:

```csharp
app.MapPost("/applications", CreateApplication)
    .RequirePermission("credit.application.create");
```

An authenticated principal is authorized when at least one resolved role grants the required permission. Unknown roles and permissions grant no access.

The same permission can be required from Minimal APIs, controllers, and application code:

```csharp
app.MapGet("/applications", GetApplications)
    .RequirePermission("application.read-all");

[RequirePermission("application.read-all")]
public IActionResult GetApplications() => Ok();

var result = await authorizationService.AuthorizePermissionAsync(
    User,
    "application.read-all");
```

## Optional CIF and XNUC matching

Permission authorization and identifier matching are intentionally separate. Check a permission through `IAuthorizationService`, then compare the trusted resource identifier with the token:

```csharp
var result = await authorizationService.AuthorizePermissionAsync(
    User,
    "application.read-own");

var allowed = result.Succeeded && User.HasCif(application.ClientCif);
```

The authorization-service extensions combine both checks when this pattern is used repeatedly:

```csharp
var clientResult = await authorizationService.AuthorizeCifPermissionAsync(
    User,
    "application.read-own",
    application.ClientCif);

var employerResult = await authorizationService.AuthorizeXnucPermissionAsync(
    User,
    "application.read-employer-own",
    application.EmployerXnuc);
```

Use ordinary `AuthorizePermissionAsync` for unrestricted permissions such as `application.read-all`. The library deliberately does not infer identifier requirements from permission names: the caller selects CIF, XNUC, or unrestricted authorization according to the permission's semantics. The underlying `HasCif` and `HasXnuc` helpers support scalar, repeated, and JSON-array claims.

## Choosing an authorization pattern

### Simple operation

Use one permission directly when an endpoint has no ownership or other resource-dependent rules:

```csharp
app.MapPost("/applications", CreateApplication)
    .RequirePermission("application.create");
```

The equivalent controller action is:

```csharp
[RequirePermission("application.create")]
[HttpPost("applications")]
public IActionResult CreateApplication(CreateApplicationRequest request)
{
    // Perform business validation and create the application.
    return Ok();
}
```

### Resource ownership

Load an existing resource from trusted storage before authorizing it. Never authorize ownership using only a CIF supplied in the URL or request body:

```csharp
var application = await repository.FindAsync(applicationId, cancellationToken);
if (application is null)
{
    return TypedResults.NotFound();
}

var permission = await authorizationService.AuthorizeCifPermissionAsync(
    User,
    "application.read-own",
    application.ClientCif);

var allowed = permission.Succeeded;
```

### Complex domain operations

When the same resource supports several operations, use ASP.NET Core resource-based authorization to centralize domain-specific combinations. Define operations with Microsoft's `OperationAuthorizationRequirement`:

```csharp
public static class LoanApplicationOperations
{
    public static readonly OperationAuthorizationRequirement Read =
        new() { Name = nameof(Read) };

    public static readonly OperationAuthorizationRequirement Update =
        new() { Name = nameof(Update) };
}
```

Implement an application-level resource handler. `IPermissionEvaluator` performs the same permission evaluation as the library's policy handler without recursively calling `IAuthorizationService` from another handler:

```csharp
public sealed class LoanApplicationAuthorizationHandler(
    IPermissionEvaluator permissions)
    : AuthorizationHandler<OperationAuthorizationRequirement, LoanApplication>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        LoanApplication application)
    {
        var allowed = requirement.Name switch
        {
            nameof(LoanApplicationOperations.Read) =>
                permissions.IsGranted(context.User, "application.read-all") ||
                (permissions.IsGranted(context.User, "application.read-own") &&
                 context.User.HasCif(application.ClientCif)),

            nameof(LoanApplicationOperations.Update) =>
                permissions.IsGranted(context.User, "application.update-all") ||
                (permissions.IsGranted(context.User, "application.update-own") &&
                 context.User.HasCif(application.ClientCif)),

            _ => false
        };

        if (allowed)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
```

Register the application handler and invoke it through Microsoft's `IAuthorizationService`:

```csharp
builder.Services.AddSingleton<IAuthorizationHandler,
    LoanApplicationAuthorizationHandler>();

var authorization = await authorizationService.AuthorizeAsync(
    User,
    application,
    LoanApplicationOperations.Update);

if (!authorization.Succeeded)
{
    return Forbid();
}

application.ChangeAmount(request.Amount);
await repository.SaveAsync(application, cancellationToken);
```

`ClaimsAuthorization` remains responsible only for permission evaluation and token identifier comparison. The application-specific handler decides how `all`, `own`, CIF, XNUC, and resource operations are combined. Domain logic must still validate whether the operation is legal in the resource's current state.
