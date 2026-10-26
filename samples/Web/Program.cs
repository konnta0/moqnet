using Microsoft.AspNetCore.SignalR;
using MoqNet.Samples.Web;
using MoqNet.Samples.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MoqNetOptions>(builder.Configuration.GetSection(MoqNetOptions.SectionName));
builder.Services.Configure<HubOptions>(options => options.MaximumReceiveMessageSize = 1024 * 1024);
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton<RelayHostedService>();
builder.Services.AddHostedService(static services => services.GetRequiredService<RelayHostedService>());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
