using MoqNet.Samples.Auth;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<MoqAuthOptions>(builder.Configuration.GetSection(MoqAuthOptions.SectionName));
builder.Services.AddSingleton<MoqTicketIssuer>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "The sample identity header is development-only. Replace it with real authentication before running outside Development.");
}

app.MapGet("/", () => Results.Ok(new
{
    message = "MoqNet development ticket issuer",
    endpoint = "POST /api/moq/tickets",
    warning = "X-Sample-User is not production authentication."
}));

app.MapPost("/api/moq/tickets", (HttpRequest httpRequest, TicketRequest request, MoqTicketIssuer issuer) =>
{
    var userId = httpRequest.Headers["X-Sample-User"].ToString();
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.Unauthorized();
    }

    try
    {
        return Results.Ok(issuer.Issue(userId, request));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.Run();
