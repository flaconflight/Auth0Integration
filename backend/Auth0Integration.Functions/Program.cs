using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Auth0Integration.Functions.Configuration;
using Auth0Integration.Functions.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.Configure<Auth0Options>(
    builder.Configuration.GetSection(Auth0Options.SectionName));

builder.Services.AddSingleton<ICreditContextStore, InMemoryCreditContextStore>();
builder.Services.AddSingleton<Auth0AuthenticationService>();
builder.Services.AddSingleton<Auth0ManagementService>();

builder.Services.AddHttpClient();

builder.Build().Run();
