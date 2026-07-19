# Demo application authorization

The Demo API uses JWT bearer authentication and JSON-backed, role-based permissions. Permissions are additive across all roles in the JWT. There are no deny rules, and unknown roles grant no access.

## Configuration

The app uses the standard ASP.NET Core `Bearer` authentication scheme. Configure production issuer and audience validation under `Authentication:Schemes:Bearer`. Do not keep production values or signing keys in source-controlled settings.

```json
{
  "Authentication": {
    "Schemes": {
      "Bearer": {
        "Authority": "https://identity.example.com",
        "ValidAudiences": ["camunda-startup-demo"]
      }
    }
  }
}
```

For local development in VS Code with the REST Client extension, launch VS Code with a freshly generated admin token:

```bash
DEMO_ACCESS_TOKEN=$(dotnet user-jwts create \
  --project Demo/Camunda.Startup.DemoApp/Camunda.Startup.DemoApp.csproj \
  --name DemoAdmin \
  --claim roles=weather-admin \
  --output token) code .
```

The command stores the signing key in user secrets, adds the local issuer and audiences to `appsettings.Development.json`, and exposes the token only to the launched VS Code process. `Camunda.Startup.DemoApp.http` reads it through `{{$processEnv DEMO_ACCESS_TOKEN}}` instead of storing the token in source control.

The JWT may contain one role claim, repeated role claims, or a JSON array that the JWT handler maps to individual claims:

```json
{
  "roles": ["weather-reader", "weather-requester"]
}
```

## Definitions

`permissions.json` defines stable permission keys and groups each Camunda-style action under a resource type. `roles.json` assigns those permission keys to roles. Changes are validated when the application starts and require a restart.

The seeded roles are:

| Role | Permissions |
|---|---|
| `weather-reader` | Read forecasts |
| `weather-requester` | Request forecasts |
| `weather-admin` | Read and request forecasts |

Missing or invalid JWTs receive `401 Unauthorized`. Authenticated callers without the required permission receive `403 Forbidden`.

## Token role mapping

The `mappings` array in `roles.json` resolves identity-provider claims to application roles. Keeping mappings and role permissions together makes `roles.json` the single source of application-role configuration. Each mapping is an independent rule identified by `key`. Conditions within one rule are combined with AND; values inside `clientIds`, `scopes`, and `roles` are combined with OR.

Claim names are fixed: `client_id`, `Cif`, `Xnuc`, `scope`/`Scope`, and `Role`/`Roles`. Both role claim names support scalar strings, repeated claims, and JSON arrays. Resolved roles are used internally for permission checks; the principal is not modified.

| Source | Result |
|---|---|
| `client_id=Backoffice`, scope `retail-credit:analyst`, and concrete `Xnuc` | `Analyst` |
| `client_id=MilleDesk`, `Role=Analyst`, and concrete `Xnuc` | `Analyst` |
| A concrete `Cif` exists | `Client` |
| A concrete `Cif` exists and `client_id=POS` | `ClientPos` |

`Cif`, `Xnuc`, scopes, and roles support repeated claims and JSON arrays. `*`, `null`, and empty values are not concrete identifier values. Mapping a role does not replace resource-level checks: concrete identifiers must still be checked when an endpoint accesses a particular client or employer.

Mappings are additive by default. To define a general role and a more specific replacement, put both rules in the same `ExclusiveGroup`. Only the matching rule with the highest `Priority` in that group is applied. For example, `ClientPOS` has priority `100`, so a POS token receives `ClientPos` instead of the general `Client` role. Rules outside `ClientType`, such as `AnalystBO`, remain independent and can still add their roles.

## Loan application example

The demo exposes:

- `GET /applications/{applicationId}` for reading an application;
- `PUT /applications/{applicationId}/amount` for changing its amount.

`LoanApplicationAuthorizationHandler` centralizes resource rules for the built-in `OperationAuthorizationRequirement` values `Read` and `Update`. Clients pass through `application.*-own` plus the stored CIF check; bank roles pass through `application.*-all`. Endpoints invoke the handler through the standard `IAuthorizationService.AuthorizeAsync(user, application, operation)` API.

The repository is queried before authorization, and the stored `ClientCif` is passed as the authorization resource. A CIF received from a caller must never be treated as proof of resource ownership.
