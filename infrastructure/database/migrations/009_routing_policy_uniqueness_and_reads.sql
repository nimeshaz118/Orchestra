BEGIN;

WITH ranked_policies AS (
    SELECT
        policy_id,
        ROW_NUMBER() OVER (
            PARTITION BY
                model_id,
                task_category,
                scope_type,
                COALESCE(department_id, '00000000-0000-0000-0000-000000000000'::UUID)
            ORDER BY created_at, policy_id
        ) AS duplicate_rank
    FROM ai_orchestration.routing_policy
    WHERE is_active = TRUE
),
deactivated_duplicates AS (
    UPDATE ai_orchestration.routing_policy AS policy
    SET is_active = FALSE
    FROM ranked_policies AS ranked
    WHERE policy.policy_id = ranked.policy_id
      AND ranked.duplicate_rank > 1
    RETURNING
        policy.policy_id,
        policy.policy_name,
        policy.description,
        policy.model_id,
        policy.task_category,
        policy.scope_type,
        policy.department_id,
        policy.priority,
        policy.is_enabled
)
INSERT INTO ai_orchestration.audit_log (
    actor_role_code,
    action,
    target_type,
    target_id,
    department_id,
    old_values,
    new_values,
    outcome,
    reason,
    correlation_id
)
SELECT
    'SYSTEM',
    'ROUTING_POLICY_DELETED',
    'ROUTING_POLICY',
    policy_id,
    department_id,
    jsonb_build_object(
        'policyName', policy_name,
        'description', description,
        'modelId', model_id,
        'taskCategory', task_category,
        'scopeType', scope_type,
        'departmentId', department_id,
        'priority', priority,
        'isEnabled', is_enabled,
        'isActive', TRUE
    ),
    jsonb_build_object(
        'policyName', policy_name,
        'description', description,
        'modelId', model_id,
        'taskCategory', task_category,
        'scopeType', scope_type,
        'departmentId', department_id,
        'priority', priority,
        'isEnabled', is_enabled,
        'isActive', FALSE
    ),
    'SUCCESS',
    'Duplicate active routing policy definition archived by migration 009.',
    gen_random_uuid()
FROM deactivated_duplicates;

CREATE UNIQUE INDEX IF NOT EXISTS uq_routing_policy_active_definition
    ON ai_orchestration.routing_policy (
        model_id,
        task_category,
        scope_type,
        COALESCE(department_id, '00000000-0000-0000-0000-000000000000'::UUID)
    )
    WHERE is_active = TRUE;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_list_routing_policies_by_department(
    IN p_department_id UUID,
    IN p_rows REFCURSOR
)
LANGUAGE plpgsql
AS $$
BEGIN
    OPEN p_rows FOR
        SELECT
            policy_id,
            policy_name,
            description,
            model_id,
            task_category,
            scope_type,
            department_id,
            priority,
            is_enabled,
            is_active,
            created_by,
            created_at,
            updated_at
        FROM ai_orchestration.routing_policy
        WHERE department_id = p_department_id
          AND scope_type = 'DEPARTMENT'
          AND is_active = TRUE
        ORDER BY priority, policy_name;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_delete_routing_policy(
    IN p_policy_id UUID,
    IN p_actor_user_id UUID,
    IN p_correlation_id UUID,
    IN p_rows REFCURSOR
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_policy ai_orchestration.routing_policy%ROWTYPE;
    v_deleted BOOLEAN := FALSE;
BEGIN
    SELECT *
    INTO v_policy
    FROM ai_orchestration.routing_policy
    WHERE policy_id = p_policy_id
      AND is_active = TRUE
    FOR UPDATE;

    IF FOUND THEN
        UPDATE ai_orchestration.routing_policy
        SET is_active = FALSE
        WHERE policy_id = v_policy.policy_id;

        INSERT INTO ai_orchestration.audit_log (
            actor_user_id,
            actor_role_code,
            action,
            target_type,
            target_id,
            department_id,
            old_values,
            outcome,
            correlation_id
        )
        VALUES (
            p_actor_user_id,
            'SUPER_ADMIN',
            'ROUTING_POLICY_DELETED',
            'ROUTING_POLICY',
            v_policy.policy_id,
            v_policy.department_id,
            jsonb_build_object(
                'policyName', v_policy.policy_name,
                'taskCategory', v_policy.task_category,
                'scopeType', v_policy.scope_type,
                'modelId', v_policy.model_id,
                'priority', v_policy.priority,
                'isEnabled', v_policy.is_enabled,
                'isActive', v_policy.is_active
            ),
            'SUCCESS',
            p_correlation_id
        );

        v_deleted := TRUE;
    END IF;

    OPEN p_rows FOR
        SELECT v_deleted AS deleted;
END;
$$;

COMMIT;
