// Cedar authorization policies mirroring authz.rego

// 1. Read actions permitted to readers, editors, admins, and platform-admins
@id("role_read")
permit (
    principal,
    action in [Action::"list", Action::"read"],
    resource
)
when {
    context.roles.containsAny(["reader", "editor", "admin", "platform-admin"])
};

// 2. Write actions permitted to editors, admins, and platform-admins
@id("role_write")
permit (
    principal,
    action in [Action::"create", Action::"update"],
    resource
)
when {
    context.roles.containsAny(["editor", "admin", "platform-admin"])
};

// 3. Delete actions permitted to admins and platform-admins
@id("role_delete")
permit (
    principal,
    action == Action::"delete",
    resource
)
when {
    context.roles.containsAny(["admin", "platform-admin"])
};

// 3b. Reading the audit trail permitted to admins and platform-admins
@id("role_audit")
permit (
    principal,
    action == Action::"audit",
    resource
)
when {
    context.roles.containsAny(["admin", "platform-admin"])
};

// 4. Cross-tenant access denied unless principal has platform-admin role
@id("cross-tenant access denied")
forbid (
    principal,
    action,
    resource
)
when {
    !(context.roles.contains("platform-admin")) &&
    (context.tenant == "" || context.tenant != context.resource_tenant)
};

// 5. Unknown environment
@id("unknown environment")
forbid (
    principal,
    action,
    resource
)
when {
    context.environment != "dev" &&
    context.environment != "staging" &&
    context.environment != "prod"
};

// 6. Prod writes require admin
@id("writes in prod require admin")
forbid (
    principal,
    action in [Action::"create", Action::"update"],
    resource
)
when {
    context.environment == "prod" &&
    !(context.roles.containsAny(["admin", "platform-admin"]))
};

// 7. Prod deletes require platform-admin
@id("deletes in prod are restricted to platform-admin")
forbid (
    principal,
    action == Action::"delete",
    resource
)
when {
    context.environment == "prod" &&
    !(context.roles.contains("platform-admin"))
};
