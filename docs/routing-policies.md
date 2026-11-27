# Routing policies

The Super Admin API creates per-model routing eligibility policies:

`POST /api/v1/super-admin/routing-policies`

Super Admins can delete a policy by ID:

`DELETE /api/v1/super-admin/routing-policies/{policyId}`

Super Admins can patch selected fields on an active policy:

`PATCH /api/v1/super-admin/routing-policies/{policyId}`

Active policies can be listed for a department:

`GET /api/v1/super-admin/routing-policies/department/{departmentId}`

Deleted policies can be listed by scope or department:

- `GET /api/v1/super-admin/routing-policies/deleted/scope/{scopeType}`
- `GET /api/v1/super-admin/routing-policies/deleted/department/{departmentId}`

The operation is implemented by `RoutingPoliciesService` and, on PostgreSQL,
calls `ai_orchestration.api_create_routing_policy`. The procedure inserts the
policy and its `ROUTING_POLICY_CREATED` audit record in one database operation.
The in-memory test host uses the same service contract and writes the audit
record through the existing `AuditLog` entity.

Patch requests update only non-null fields supplied in the JSON body and return
the updated policy. Changing `scopeType` to `GLOBAL` clears `departmentId`.
Each patch writes the before and after policy values to the audit table.
An active policy is unique by model, task category, scope, and department.
Creating or patching another policy with the same definition returns
`409 ROUTING_POLICY_ALREADY_EXISTS`; soft-deleting a policy frees that
definition for a future create. Migration `009` keeps the earliest existing
active definition and archives later duplicates as inactive, audited policies.
Successful deletes return `200 OK` with a confirmation message. A policy ID
that does not exist returns `404 ROUTING_POLICY_NOT_FOUND`; retrying a delete
for an already-deleted policy returns `409 ROUTING_POLICY_ALREADY_DELETED`.
PostgreSQL locks the policy row and writes the delete audit record in the same
transaction; only a successful deletion is audited. Deleted policies remain
stored with `is_active = FALSE` and are excluded from active-policy reads.

The `.http` examples in
[`RoutingPolicies.http`](../src/backend/Orchestra.Api/Modules/RoutingPolicies/RoutingPolicies.http)
show each new request.

## Database contract

Migration `004_routing_policies.sql` adds:

- `routing_policy`: model, task category, scope, department, priority, and
  enabled state. It references the existing `ai_model`, `department`, and
  `app_user` tables.
- `model_performance_metrics`: category-specific aggregate metrics with a
  unique model/category/measurement-window key. It is intentionally not
  populated by this endpoint.
- `006_routing_policy_delete.sql` installs the atomic, audited delete procedure
  used by the PostgreSQL-backed API.
- `007_routing_policy_patch_and_deleted_reads.sql` installs the patch procedure
  and procedures for active policy reads and deleted-policy reads by scope or
  department.
- `009_routing_policy_uniqueness_and_reads.sql` prevents duplicate active
  definitions and refreshes the department-list and soft-delete procedures.
- `010_routing_policy_delete_feedback.sql` makes the soft-delete procedure
  report whether it deleted a policy, found an already-deleted policy, or found
  no policy.

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
