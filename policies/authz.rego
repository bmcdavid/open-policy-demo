package authz

import rego.v1

known_environments := {"dev", "staging", "prod"}

roles := {r | some r in input.subject.roles}

is_platform_admin if "platform-admin" in roles

is_admin if "admin" in roles

is_admin if is_platform_admin

read_roles := {"reader", "editor", "admin", "platform-admin"}

write_roles := {"editor", "admin", "platform-admin"}

delete_roles := {"admin", "platform-admin"}

audit_roles := {"admin", "platform-admin"}

role_permits if {
	input.action in {"list", "read"}
	count(roles & read_roles) > 0
}

role_permits if {
	input.action in {"create", "update"}
	count(roles & write_roles) > 0
}

role_permits if {
	input.action == "delete"
	count(roles & delete_roles) > 0
}

role_permits if {
	input.action == "audit"
	count(roles & audit_roles) > 0
}

tenant_matches if {
	input.subject.tenant != ""
	input.subject.tenant == input.resource.tenant
}

deny contains "role does not permit action" if not role_permits

deny contains "cross-tenant access denied" if {
	not is_platform_admin
	not tenant_matches
}

deny contains "unknown environment" if not input.environment in known_environments

deny contains "writes in prod require admin" if {
	input.environment == "prod"
	input.action in {"create", "update"}
	not is_admin
}

deny contains "deletes in prod are restricted to platform-admin" if {
	input.environment == "prod"
	input.action == "delete"
	not is_platform_admin
}

default allow := false

allow if {
	role_permits
	count(deny) == 0
}

decision := {"allow": allow, "reasons": sort(deny)}
