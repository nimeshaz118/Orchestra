BEGIN;

CREATE TABLE ai_orchestration.routing_policy (
    policy_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    policy_name VARCHAR(150) NOT NULL,
    description TEXT NULL,
    model_id UUID NOT NULL REFERENCES ai_orchestration.ai_model(model_id),
    task_category VARCHAR(30) NOT NULL,
    scope_type VARCHAR(20) NOT NULL,
    department_id UUID NULL REFERENCES ai_orchestration.department(department_id),
    priority INTEGER NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_by UUID NOT NULL REFERENCES ai_orchestration.app_user(user_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_routing_policy_category
        CHECK (task_category IN ('GENERAL_PURPOSE', 'CODING')),
    CONSTRAINT ck_routing_policy_scope
        CHECK (
            (scope_type = 'GLOBAL' AND department_id IS NULL)
            OR (scope_type = 'DEPARTMENT' AND department_id IS NOT NULL)
        ),
    CONSTRAINT ck_routing_policy_scope_type
        CHECK (scope_type IN ('GLOBAL', 'DEPARTMENT')),
    CONSTRAINT ck_routing_policy_priority CHECK (priority > 0)
);

CREATE INDEX ix_routing_policy_category_enabled
    ON ai_orchestration.routing_policy (task_category, is_enabled);
CREATE INDEX ix_routing_policy_department_category
    ON ai_orchestration.routing_policy (department_id, task_category);
CREATE INDEX ix_routing_policy_model
    ON ai_orchestration.routing_policy (model_id);

CREATE TABLE ai_orchestration.model_performance_metrics (
    metric_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    model_id UUID NOT NULL REFERENCES ai_orchestration.ai_model(model_id),
    task_category VARCHAR(30) NOT NULL,
    total_requests BIGINT NOT NULL DEFAULT 0,
    successful_requests BIGINT NOT NULL DEFAULT 0,
    failed_requests BIGINT NOT NULL DEFAULT 0,
    success_rate NUMERIC(5,2) NOT NULL,
    average_response_time_ms NUMERIC(12,2) NOT NULL,
    total_provider_cost NUMERIC(18,8) NOT NULL DEFAULT 0,
    average_cost_per_request NUMERIC(18,8) NOT NULL DEFAULT 0,
    currency VARCHAR(10) NOT NULL,
    measurement_window_start TIMESTAMPTZ NOT NULL,
    measurement_window_end TIMESTAMPTZ NOT NULL,
    last_updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_model_metrics_category
        CHECK (task_category IN ('GENERAL_PURPOSE', 'CODING')),
    CONSTRAINT ck_model_metrics_counts
        CHECK (total_requests >= 0 AND successful_requests >= 0 AND failed_requests >= 0),
    CONSTRAINT ck_model_metrics_success_rate CHECK (success_rate BETWEEN 0 AND 100),
    CONSTRAINT ck_model_metrics_response_time CHECK (average_response_time_ms >= 0),
    CONSTRAINT ck_model_metrics_costs
        CHECK (total_provider_cost >= 0 AND average_cost_per_request >= 0),
    CONSTRAINT ck_model_metrics_window CHECK (measurement_window_end > measurement_window_start),
    CONSTRAINT uq_model_metrics_window
        UNIQUE (model_id, task_category, measurement_window_start, measurement_window_end)
);

CREATE INDEX ix_model_metrics_model_category
    ON ai_orchestration.model_performance_metrics (model_id, task_category);
CREATE INDEX ix_model_metrics_category_updated
    ON ai_orchestration.model_performance_metrics (task_category, last_updated_at);

CREATE OR REPLACE TRIGGER trg_routing_policy_updated_at
BEFORE UPDATE ON ai_orchestration.routing_policy
FOR EACH ROW EXECUTE FUNCTION ai_orchestration.set_updated_at();

CREATE OR REPLACE PROCEDURE ai_orchestration.api_create_routing_policy(
    IN p_policy_name VARCHAR(150),
    IN p_description TEXT,
    IN p_model_id UUID,
    IN p_task_category VARCHAR(30),
    IN p_scope_type VARCHAR(20),
    IN p_department_id UUID,
    IN p_priority INTEGER,
    IN p_is_enabled BOOLEAN,
    IN p_created_by UUID,
    IN p_correlation_id UUID,
    IN p_rows REFCURSOR)
LANGUAGE plpgsql AS $$
DECLARE
    v_policy_id UUID;
    v_created_at TIMESTAMPTZ;
BEGIN
    INSERT INTO ai_orchestration.routing_policy (
        policy_name, description, model_id, task_category, scope_type,
        department_id, priority, is_enabled, created_by)
    VALUES (
        p_policy_name, p_description, p_model_id, p_task_category, p_scope_type,
        p_department_id, p_priority, p_is_enabled, p_created_by)
    RETURNING policy_id, created_at INTO v_policy_id, v_created_at;

    INSERT INTO ai_orchestration.audit_log (
        actor_user_id, actor_role_code, action, target_type, target_id,
        department_id, new_values, outcome, correlation_id)
    VALUES (
        p_created_by, 'SUPER_ADMIN', 'ROUTING_POLICY_CREATED', 'ROUTING_POLICY',
        v_policy_id, p_department_id,
        jsonb_build_object(
            'policyName', p_policy_name,
            'modelId', p_model_id,
            'taskCategory', p_task_category,
            'scopeType', p_scope_type,
            'priority', p_priority,
            'isEnabled', p_is_enabled),
        'SUCCESS', p_correlation_id);

    OPEN p_rows FOR
        SELECT policy_id, policy_name, description, model_id, task_category,
               scope_type, department_id, priority, is_enabled, created_by,
               created_at, updated_at
        FROM ai_orchestration.routing_policy
        WHERE policy_id = v_policy_id;
END;
$$;


COMMIT;
