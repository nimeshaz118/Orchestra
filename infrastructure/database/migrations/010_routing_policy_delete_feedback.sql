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
    v_status TEXT;
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

        v_status := 'DELETED';
    ELSIF EXISTS (
        SELECT 1
        FROM ai_orchestration.routing_policy
        WHERE policy_id = p_policy_id
    ) THEN
        v_status := 'ALREADY_DELETED';
    ELSE
        v_status := 'NOT_FOUND';
    END IF;

    OPEN p_rows FOR SELECT v_status AS status;
END;
$$;

COMMIT;
