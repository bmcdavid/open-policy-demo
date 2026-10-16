package authz_test

import data.authz
import rego.v1

make_input(roles, tenant, environment, action, resource_tenant) := {
	"subject": {"id": "u1", "roles": roles, "tenant": tenant},
	"environment": environment,
	"action": action,
	"resource": {"type": "document", "tenant": resource_tenant, "id": "d1"},
}

allowed(i) if authz.decision.allow with input as i

denied(i) if not authz.decision.allow with input as i

reason_in(i, reason) if reason in authz.decision.reasons with input as i

test_reader_can_read_own_tenant_dev if allowed(make_input(["reader"], "acme", "dev", "read", "acme"))

test_reader_cannot_update if {
	i := make_input(["reader"], "acme", "dev", "update", "acme")
	denied(i)
	reason_in(i, "role does not permit action")
}

test_editor_can_update_dev if allowed(make_input(["editor"], "acme", "dev", "update", "acme"))

test_editor_cannot_delete_dev if {
	i := make_input(["editor"], "acme", "dev", "delete", "acme")
	denied(i)
	reason_in(i, "role does not permit action")
}

test_admin_can_delete_dev if allowed(make_input(["admin"], "acme", "dev", "delete", "acme"))

test_admin_cannot_delete_prod if {
	i := make_input(["admin"], "acme", "prod", "delete", "acme")
	denied(i)
	reason_in(i, "deletes in prod are restricted to platform-admin")
}

test_editor_cannot_update_prod if {
	i := make_input(["editor"], "acme", "prod", "update", "acme")
	denied(i)
	reason_in(i, "writes in prod require admin")
}

test_admin_can_update_prod if allowed(make_input(["admin"], "acme", "prod", "update", "acme"))

test_platform_admin_can_delete_prod if allowed(make_input(["platform-admin"], "platform", "prod", "delete", "acme"))

test_cross_tenant_read_denied if {
	i := make_input(["reader"], "acme", "dev", "read", "globex")
	denied(i)
	reason_in(i, "cross-tenant access denied")
}

test_platform_admin_crosses_tenants if allowed(make_input(["platform-admin"], "platform", "dev", "read", "globex"))

test_multiple_roles_union if allowed(make_input(["reader", "editor"], "acme", "dev", "update", "acme"))

test_missing_roles_denied if denied(make_input([], "acme", "dev", "read", "acme"))

test_empty_input_denied if {
	not authz.decision.allow with input as {}
	count(authz.decision.reasons) > 0 with input as {}
}

test_missing_tenant_denied if {
	i := {"subject": {"id": "u1", "roles": ["admin"]}, "environment": "dev", "action": "read", "resource": {"type": "document", "tenant": "acme"}}
	denied(i)
	reason_in(i, "cross-tenant access denied")
}

test_unknown_environment_denied if {
	i := make_input(["admin"], "acme", "production", "read", "acme")
	denied(i)
	reason_in(i, "unknown environment")
}
