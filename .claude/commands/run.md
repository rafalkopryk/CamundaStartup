---
description: Start the Aspire AppHost with configurable storage backend
allowed-tools: Bash(dotnet:*), Bash(docker:*), Bash(tail:*)
argument-hint: [postgres|sqlserver|h2|elastic]
---

# Run Camunda Aspire AppHost

Start the Aspire AppHost application with the specified storage backend.

## Supported Backends

- **postgres**: PostgreSQL secondary storage
- **sqlserver**: SQL Server secondary storage
- **h2**: H2 embedded database (lightweight, no separate container)
- **elastic**: Elasticsearch secondary storage (default)

## Instructions

1. Run the application with the storage backend specified in $ARGUMENTS (default to elastic if empty)
2. Use environment variable `Parameters__secondaryStorage` to set the backend
3. Run in background so the user can continue working
4. After starting, show the dashboard URL and list running Docker containers

## Command

```bash
Parameters__secondaryStorage=$ARGUMENTS dotnet run --project Demo/CamundaStartup.Aspire.Hosting.Camunda.AppHost
```

## Service Endpoints

- DemoApp: https://localhost:7230
- Camunda REST: http://localhost:8080
- Camunda gRPC: http://localhost:26500
- Aspire Dashboard: https://localhost:17277