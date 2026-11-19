# Routing policies

The Super Admin API creates per-model routing eligibility policies:

`POST /api/v1/super-admin/routing-policies`

The operation is implemented by `RoutingPoliciesService` and, on PostgreSQL,
calls `ai_orchestration.api_create_routing_policy`. The procedure inserts the
policy and its `ROUTING_POLICY_CREATED` audit record in one database operation.
The in-memory test host uses the same service contract and writes the audit
record through the existing `AuditLog` entity.

## Database contract

Migration `004_routing_policies.sql` adds:

- `routing_policy`: model, task category, scope, department, priority, and
  enabled state. It references the existing `ai_model`, `department`, and
  `app_user` tables.
- `model_performance_metrics`: category-specific aggregate metrics with a
  unique model/category/measurement-window key. It is intentionally not
  populated by this endpoint.

The existing `model_selection_policy` remains the scope-level ranking-weight
configuration; `routing_policy` is the per-model eligibility configuration.
Existing `ai_request` and `request_routing_attempt` records are unchanged in
this migration. They currently do not persist a task category, so adding that
execution-history field belongs to the Python/database coordination work.

## List Agent follow-up

Python should read enabled policies, apply model/provider availability,
department, privacy, capability, context, budget, credit, and model-status
constraints first, and only then rank the eligible models. Ranking remains
separate for `GENERAL_PURPOSE` and `CODING`.

The repository does not yet approve final ranking weights, normalization,
measurement windows, minimum sample sizes, or new-model treatment. Those
decisions must be agreed before implementing List Agent ranking or metrics
aggregation procedures.
