using Microsoft.Extensions.Hosting;
using Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Compose();

await builder.Build().RunAsync();
