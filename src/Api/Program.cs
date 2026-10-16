using Api;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);

builder.Compose();

var app = builder.Build();

await app.ConfigureRequestPipelineForApi().RunAsync();
