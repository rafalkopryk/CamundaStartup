---
description: Generates JobHandler classes for service tasks in BPMN files
---

# Create JobHandler Skill

Activate when user:
- Asks to create handlers for a BPMN file
- Wants to generate JobHandler classes from a workflow
- Requests scaffolding for service tasks
- Mentions "create handler" or "generate handler" with BPMN context

## Workflow

1. **Parse BPMN File**
   - Read the specified `.bpmn` file
   - Extract all `<bpmn:serviceTask>` elements
   - Find task types from `<zeebe:taskDefinition type="..." />` extensions

2. **Identify Missing Handlers**
   - Search codebase for existing `[JobWorker(Type = "...")]` attributes
   - Compare against service tasks found in BPMN
   - List service tasks that need handlers

3. **Ask User for Location**
   - Determine target project/directory for new handlers
   - Default: same directory as existing handlers or `Feature/` folder

4. **Generate JobHandler Classes**
   For each missing service task, generate:
   - Class file named `{PascalCaseName}JobHandler.cs`
   - Implements `IJobHandler` interface
   - `[JobWorker(Type = "task-type")]` attribute matching BPMN
   - Input record for process variables (if extractable from BPMN)

5. **Update Worker Registration**
   - Show user how to register new workers in `Program.cs`
   - Provide `AddWorker<T>()` code snippet

## BPMN Parsing Reference

### Service Task Structure
```xml
<bpmn:serviceTask id="Activity_DoSomething" name="Do Something">
  <bpmn:extensionElements>
    <zeebe:taskDefinition type="do-something:1" />
  </bpmn:extensionElements>
</bpmn:serviceTask>
```

### Extracting Task Type
- Look for `zeebe:taskDefinition` element inside `bpmn:extensionElements`
- The `type` attribute contains the job type (e.g., `do-something:1`)
- Naming convention: `task-name:version`

### Input/Output Mappings
```xml
<zeebe:ioMapping>
  <zeebe:input source="=variable" target="localVar" />
  <zeebe:output source="=result" target="outputVar" />
</zeebe:ioMapping>
```

## Code Generation Templates

See [TEMPLATES.md](./TEMPLATES.md) for JobHandler code templates.

## Example Output

For a service task with type `send-notification:1`:

```csharp
using Camunda.Client.Jobs;

namespace MyApp.Feature;

[JobWorker(Type = "send-notification:1")]
public class SendNotificationJobHandler : IJobHandler
{
    public async Task Handle(IJobClient client, IJob job, CancellationToken cancellationToken)
    {
        var input = job.GetVariablesAsType<SendNotificationInput>();

        // TODO: Implement notification logic

        // Job auto-completes by default
    }
}

public record SendNotificationInput(/* Add expected properties */);
```

## Registration Snippet

```csharp
var jobWorkerDefault = new JobWorkerConfiguration();
builder.Services.AddCamunda(
    options => options.Endpoint = builder.Configuration.GetConnectionString("camunda"),
    builder => builder
        .AddWorker<SendNotificationJobHandler>(jobWorkerDefault));
```

## Related Files

- `Camunda.Client/Jobs/IJobHandler.cs` - Handler interface
- `Camunda.Client/Jobs/JobWorkerAttribute.cs` - Worker attribute
- `Demo/Camunda.Startup.DemoApp/Feature/` - Example handlers
- `Demo/Camunda.Startup.DemoApp/Program.cs` - Worker registration
