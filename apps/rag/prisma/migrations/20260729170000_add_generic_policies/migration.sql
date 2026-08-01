CREATE TYPE vietride_rag.policy_type AS ENUM ('FOR_OPERATOR', 'FOR_USER');

CREATE TYPE vietride_rag.policy_audit_action AS ENUM (
    'CREATE',
    'UPDATE',
    'ACTIVATE',
    'DEACTIVATE',
    'DELETE'
);

CREATE TABLE vietride_rag.policies (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NULL,
    title TEXT NOT NULL,
    description TEXT NOT NULL,
    content TEXT NOT NULL,
    policy_type vietride_rag.policy_type NOT NULL,
    category TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    active BOOLEAN NOT NULL DEFAULT TRUE,
    created_by_user_id UUID NOT NULL,
    created_by_display_name TEXT NOT NULL,
    created_by_email TEXT NOT NULL,
    row_version INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at TIMESTAMPTZ NULL,
    CONSTRAINT chk_policies_version_positive CHECK (version > 0),
    CONSTRAINT chk_policies_row_version_non_negative CHECK (row_version >= 0)
);

CREATE INDEX idx_policies_tenant_deleted_updated
    ON vietride_rag.policies (operator_id, deleted_at, updated_at DESC);
CREATE INDEX idx_policies_tenant_type_active
    ON vietride_rag.policies (operator_id, policy_type, active);

CREATE TABLE vietride_rag.policy_audit_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    policy_id UUID NOT NULL REFERENCES vietride_rag.policies (id) ON DELETE RESTRICT ON UPDATE CASCADE,
    action vietride_rag.policy_audit_action NOT NULL,
    before JSONB NULL,
    after JSONB NULL,
    actor JSONB NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_policy_audit_logs_policy_occurred
    ON vietride_rag.policy_audit_logs (policy_id, occurred_at);

CREATE TRIGGER trg_policies_updated_at
    BEFORE UPDATE ON vietride_rag.policies
    FOR EACH ROW EXECUTE FUNCTION vietride_rag.trg_set_updated_at();

CREATE OR REPLACE FUNCTION vietride_rag.reject_policy_audit_mutation()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'policy_audit_logs is immutable' USING ERRCODE = '55000';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_policy_audit_logs_immutable
    BEFORE UPDATE OR DELETE ON vietride_rag.policy_audit_logs
    FOR EACH ROW EXECUTE FUNCTION vietride_rag.reject_policy_audit_mutation();
