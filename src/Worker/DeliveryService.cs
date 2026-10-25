using Core.Features.Audit;
using Core.Features.Documents;
using Core.Features.Events;
using Core.Features.Messaging;
using Microsoft.Extensions.Hosting;
using Worker.Features.Audit;
using Worker.Features.CacheInvalidation;

namespace Worker;

/// <summary>Runs the Worker's subscriptions: every event is audited, and document changes also invalidate the cache.</summary>
internal sealed class DeliveryService(EventDelivery delivery, UpsertAuditEntry upsertAudit, InvalidateDocument invalidate) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        delivery.RunAsync(
            [
                new Subscription("audit", "#", AuditHandler.Create(upsertAudit)),
                new Subscription("cache-invalidation", EventTypes.DocumentChanged, CacheInvalidationHandler.Create(invalidate)),
            ],
            stoppingToken);
}
