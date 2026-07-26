# Weather Forecast Load and Sizing Test

The `weather-forecast.bpmn` deployment contains one top-level process with three sequential
call activities:

1. Collect forecast
2. Validate forecast
3. Deliver forecast

Every called process executes service tasks, a Camunda-managed user task, and a
message-correlated receive task. A background service searches and completes only the
managed user tasks belonging to these three weather processes, allowing an unattended load
test while exercising Camunda user-task persistence. A release worker publishes a buffered
message before each receive task; the message is correlated by the unique `instanceId`.

## Start the environment

Docker Desktop must be running.

```bash
aspire start --isolated
```

Use the HTTPS endpoint reported for `demoapp` by Aspire. Do not assume the development port
when isolated mode is enabled.

## Smoke test

The Rider HTTP client requests in
`Demo/Camunda.Startup.DemoApp/Camunda.Startup.DemoApp.http` start one instance and save its
identifier. Run the instance-status request until it returns HTTP 200 and `completed`.

## Run 50,000 instances

```bash
dotnet run Scripts/weatherforecast-load.cs -- https://localhost:7230 50000 32 true 10
```

Arguments are:

1. Demo application base URL
2. Number of process instances (default `50000`)
3. HTTP parallelism (default `32`)
4. Wait for full end-to-end completion (default `true`)
5. Process instances started per HTTP request (default `10`, must be greater than `1`)

The command batches process starts through the job API, assigns one `loadTestId` to the run,
and checks a single aggregate status endpoint instead of polling every process instance. It
exits successfully only when every batch is accepted and, when E2E verification is enabled,
the aggregate completion count reaches the requested instance count. The completion wait has
a 30-minute timeout.

Worker and managed-user-task concurrency are configured in the demo application's
`appsettings.json`. Increase them gradually while observing Camunda backpressure, application
CPU, and SQL Server latency.

For sizing results, record at minimum total completion time, start requests per second,
Camunda CPU and memory, application CPU and memory, storage growth, job latency, and
backpressure. Run a warm-up before the measured test and repeat the test at increasing
parallelism; 50,000 total instances alone does not define the required throughput.
