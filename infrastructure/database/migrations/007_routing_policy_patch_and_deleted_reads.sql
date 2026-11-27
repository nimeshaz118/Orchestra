BEGIN;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_patch_routing_policy(
    IN p_policy_id UUID,
    IN p_actor_user_id UUID,
    IN p_correlation_id UUID,
    IN p_policy_name VARCHAR(150),
    IN p_description TEXT,
    IN p_model_id UUID,
    IN p_task_category VARCHAR(30),
    IN p_scope_type VARCHAR(20),
    IN p_department_id UUID,
    IN p_priority INTEGER,
    IN p_is_enabled BOOLEAN,
    IN p_update_description BOOLEAN,
    IN p_update_department BOOLEAN,
    IN p_rows REFCURSOR
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_old_policy ai_orchestration.routing_policy%ROWTYPE;
    v_updated_policy ai_orchestration.routing_policy%ROWTYPE;
BEGIN
    SELECT *
    INTO v_old_policy
    FROM ai_orchestration.routing_policy
    WHERE policy_id = p_policy_id
      AND is_active = TRUE
    FOR UPDATE;

    IF FOUND THEN
        UPDATE ai_orchestration.routing_policy
        SET policy_name = COALESCE(p_policy_name, policy_name),
            description = CASE WHEN p_update_description THEN p_description ELSE description END,
            model_id = COALESCE(p_model_id, model_id),
            task_category = COALESCE(p_task_category, task_category),
            scope_type = COALESCE(p_scope_type, scope_type),
            department_id = CASE WHEN p_update_department THEN p_department_id ELSE department_id END,
            priority = COALESCE(p_priority, priority),
            is_enabled = COALESCE(p_is_enabled, is_enabled)
        WHERE policy_id = p_policy_id
        RETURNING * INTO v_updated_policy;

        INSERT INTO ai_orchestration.audit_log (
            actor_user_id,
            actor_role_code,
            action,
            target_type,
            target_id,
            department_id,
            old_values,
            new_values,
            outcome,
            correlation_id
        )
        VALUES (
            p_actor_user_id,
            'SUPER_ADMIN',
            'ROUTING_POLICY_UPDATED',
            'ROUTING_POLICY',
            v_updated_policy.policy_id,
            v_updated_policy.department_id,
            jsonb_build_object(
                'policyName', v_old_policy.policy_name,
                'description', v_old_policy.description,
                'modelId', v_old_policy.model_id,
                'taskCategory', v_old_policy.task_category,
                'scopeType', v_old_policy.scope_type,
                'departmentId', v_old_policy.department_id,
                'priority', v_old_policy.priority,
                'isEnabled', v_old_policy.is_enabled
            ),
            jsonb_build_object(
                'policyName', v_updated_policy.policy_name,
                'description', v_updated_policy.description,
                'modelId', v_updated_policy.model_id,
                'taskCategory', v_updated_policy.task_category,
                'scopeType', v_updated_policy.scope_type,
                'departmentId', v_updated_policy.department_id,
                'priority', v_updated_policy.priority,
                'isEnabled', v_updated_policy.is_enabled
            ),
            'SUCCESS',
            p_correlation_id
        );
    END IF;

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
        WHERE policy_id = p_policy_id
          AND is_active = TRUE;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_get_routing_policy(
    IN p_policy_id UUID,
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
        WHERE policy_id = p_policy_id
          AND is_active = TRUE;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_list_deleted_routing_policies_by_scope(
    IN p_scope_type VARCHAR(20),
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
        WHERE is_active = FALSE
          AND scope_type = p_scope_type
        ORDER BY priority, policy_name;
END;
$$;

CREATE OR REPLACE PROCEDURE ai_orchestration.api_list_deleted_routing_policies_by_department(
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
        WHERE is_active = FALSE
          AND department_id = p_department_id
        ORDER BY priority, policy_name;
END;
$$;

COMMIT;
