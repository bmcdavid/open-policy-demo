using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

await builder.Build().RunAsync();
