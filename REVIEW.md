# Review: order cancellation

Branch `feature/order-cancellation`, compared against the feature requirements, the testing requirements and the rules in `CLAUDE.md`.

The testing requirements were not supplied with the review request, so the tests are graded against the behaviours in the feature requirements. Add rows here once they are available.

## Feature requirements

| # | Requirement | Verdict | Evidence |
|---|---|---|---|
| 1 | `POST /api/orders/{id}/cancel`, authenticated | Met | `OrdersController.cs:115-116` (the controller carries `[Authorize]`); no-token test at `OrdersControllerTests.cs:332` |
| 2 | `200 OK` with the updated order | Met | `OrdersController.cs:153` returns `Ok(ToDto(...))`; test at `OrdersControllerTests.cs:313` |
| 3 | `404` if the order is missing or belongs to another user | Met | `OrdersController.cs:127-132` filters on id and `ApplicationUserId`, and a miss gives `NotFound`; tests at `OrdersControllerTests.cs:340` and `:351` |
| 4 | `409` if the order is no longer `Pending` | Met | `OrdersController.cs:135-138`, and `:145-148` when the consumer wins mid-save; test at `OrdersControllerTests.cs:365-368` covers Confirmed, Rejected and Cancelled |
| 5 | `Cancelled` is a new final status returned by `GET /api/orders/{id}` | Met | `OrderStatus.cs:8`; the `GET` is unchanged and asserted at `OrdersControllerTests.cs:313-329` |
| 6 | Consumer does not touch stock for a cancelled order, completes the message, logs a skip | Met | `OrderProcessingService.cs:104-110` skips before any stock access and completes; log at `:107`; behaviour test at `OrdersControllerTests.cs:382`. The test checks stock and status but does not assert the log line |
| 7 | Exactly one of cancel and consumer wins; never Cancelled with stock decremented | Met (fixed in `6af663e`) | Code is in place: `AppDbContext.cs:33-35` makes `Status` a token, and `OrderProcessingService.cs:123,140,156-179` saves order and stock together and skips on a lost race. Proven on EF InMemory (`OrdersControllerTests.cs:404`, `OrderConcurrencyTests.cs:12-37`) and on real SQL Server by `scripts/e2e-cancel-race.ps1` (commit `6af663e`). That script cancels a backlog newest first while the consumer starts, and both sides win (91 Cancelled and 59 Confirmed in each of the final two runs, 84 to 107 Cancelled in earlier runs). Every order's status matched its cancel response, and stock dropped only for Confirmed orders. The consumer-side lost-race log line (`cancelled while being processed`) was seen once in an earlier run, not in the final two, and a cancel-side `409` cannot be told apart from an already-Confirmed `409` |
| 8 | Protect the order with an EF Core concurrency token | Met | `AppDbContext.cs:33-35`; snapshot updated; migration `20261027155229_OrderStatusConcurrencyToken.cs` (empty `Up` and `Down`, as expected for a token-only change) |
| 9 | Record cancellations in the orders-processed counter with outcome `cancelled` | Met | `OrderMetrics.cs:26,70-74`, called at `OrdersController.cs:151` only after a successful save; test at `OrderTelemetryTests.cs:268` |
| 10 | Cancelled orders no longer count as `Pending` | Met | The gauge counts `Status == Pending` (`OrderMetrics.cs:92`); test at `OrderTelemetryTests.cs:261,267` (1 then 0) |
| 11 | Dashboard confirmed-versus-rejected panel shows cancellations | Met (title in `5c30f85`, checked on the live stack) | `shop-overview.json:200-201` is retitled, and the query at `:245` groups by `outcome`, so a `cancelled` series appears. Checked on the live stack through the API, not by looking at a rendered panel: Grafana serves the renamed panel (`/api/dashboards/uid/shop-overview`), and the panel query run through its Prometheus proxy returns a `cancelled` series next to `confirmed` |

## Rules in `CLAUDE.md`

| # | Rule | Verdict | Evidence |
|---|---|---|---|
| 12 | Controllers, not minimal APIs | Met | `OrdersController.cs:115` |
| 13 | DTOs at the boundary, never entities | Met | `OrdersController.cs:153` returns `ToDto`, never the entity |
| 14 | API tests use NUnit and `WebApplicationFactory` | Met | `OrdersControllerTests.cs:313-404` use `CustomWebApplicationFactory` |
| 15 | `ILogger` templates, no interpolation | Met | `OrderProcessingService.cs:107,133,176`; a search for interpolated log calls found none |
| 16 | No personal data in logs or spans; low-cardinality metric tags; telemetry must not break the shop | Met | Logs carry only `OrderId` and `Status`; the only metric tag is `outcome` (`OrderMetrics.cs:72`). The metric call at `OrdersController.cs:151` is not wrapped in a try/catch, but it matches the existing `OrderPlaced()` call and counters do not throw |
| 17 | Never edit an applied migration; add a new one | Met | Only `20261027155229_OrderStatusConcurrencyToken` and the snapshot were added or changed for this feature |
| 18 | CI: no model change without a migration | Met | `dotnet ef migrations has-pending-model-changes` reported no pending changes |
| 19 | Grafana provisioning uses `$${...}` for literal templates | Met | No `${` was added in any Grafana file; the only `${` hits on the branch are GitHub Actions expressions |
| 20 | Only order tests use the emulator; everything else uses InMemory | Met | `OrderConcurrencyTests.cs` uses InMemory only; the other new tests are order or telemetry tests that already use the emulator factory |
| 21 | Docs avoid em dashes | Met (fixed in `6517074`) | The em dashes at `README.md:20-22,35` are replaced; a search of `README.md`, `CLAUDE.md`, `docs/` and `scripts/` finds none |

## Open items

- No row is partly met or missing now. Rows 7, 11 and 21 are fixed; the commits are noted in the tables.
- Row 6 is graded Met, but its test does not assert the skip log line.
- `scripts/e2e-cancel-race.ps1` is timing dependent: it warns, and does not fail, if one side never won.
- The testing requirements were never supplied, so no rows exist for them.
