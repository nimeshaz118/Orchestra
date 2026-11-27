BEGIN;

ALTER TABLE ai_orchestration.routing_policy
ADD COLUMN IF NOT EXISTS is_active BOOLEAN;

-- Mark all existing policies as active.
UPDATE ai_orchestration.routing_policy
SET is_active = TRUE
WHERE is_active IS NULL;

-- Make new policies active by default.
ALTER TABLE ai_orchestration.routing_policy
ALTER COLUMN is_active SET DEFAULT TRUE;

-- Ensure the column cannot contain NULL.
ALTER TABLE ai_orchestration.routing_policy
ALTER COLUMN is_active SET NOT NULL;

COMMIT;




BEGIN;

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

    -- Find the active policy and lock it.
    SELECT *
    INTO v_policy
    FROM ai_orchestration.routing_policy
    WHERE policy_id = p_policy_id
      AND is_active = TRUE
    FOR UPDATE;

    IF FOUND THEN

        -- Soft delete: mark the policy as inactive.
        UPDATE ai_orchestration.routing_policy
        SET is_active = FALSE
        WHERE policy_id = v_policy.policy_id;

        -- Record the deletion in the audit log.
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

    -- Return the deletion result.
    OPEN p_rows FOR
    SELECT v_deleted AS deleted;

END;
$$;

DROP TABLE IF EXISTS ai_orchestration.model_selection_policy;

COMMIT;

BEGIN;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_list_routing_policies(
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
            created_by,
            created_at,
            updated_at
        FROM ai_orchestration.routing_policy
        WHERE is_active = TRUE
        ORDER BY priority, policy_name;

END;
$$;

COMMIT;

BEGIN;

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
            created_by,
            created_at,
            updated_at
        FROM ai_orchestration.routing_policy
        WHERE department_id = p_department_id
          AND is_active = TRUE
        ORDER BY priority, policy_name;

END;
$$;

COMMIT;
