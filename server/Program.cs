using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Formscope.Domain;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var data = Path.GetFullPath(builder.Configuration["DataPath"] ?? "var");
Directory.CreateDirectory(data);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 65536);
builder.Services.AddDbContext<AppDb>(options => options.UseSqlite($"Data Source={Path.Combine(data, "formscope.sqlite")};Default Timeout=5;Foreign Keys=True"));
builder.Services.AddScoped<FormStore>();
builder.Services.AddSingleton<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(data, "keys"))).SetApplicationName("Formscope");
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddAntiforgery(options => { options.HeaderName = "X-CSRF-TOKEN"; options.Cookie.SameSite = SameSiteMode.Strict; options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; });
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "Formscope.Session"; options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(12); options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return context.Response.WriteAsJsonAsync(new { message = "Please sign in." }); };
    options.Events.OnValidatePrincipal = async context =>
    {
        var token = context.Principal?.FindFirstValue("sid"); var db = context.HttpContext.RequestServices.GetRequiredService<AppDb>();
        var hash = token is null ? "" : TokenHash(token); var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (token is null || !await db.Sessions.AnyAsync(s => s.Id == hash && s.Expires > now))
        { context.RejectPrincipal(); await context.HttpContext.SignOutAsync(); }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
    options.AddPolicy("submit", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>(); db.Database.EnsureCreated(); db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff"; context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    try { await next(context); }
    catch (DomainError error) { context.Response.StatusCode = error.Status; await context.Response.WriteAsJsonAsync(new { message = error.Message }); }
    catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { message = "Session token expired. Reload this page." }); }
});
app.UseStaticFiles(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
var api = app.MapGroup("/api").AddEndpointFilter(async (context, next) =>
{
    if (!HttpMethods.IsGet(context.HttpContext.Request.Method)) await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext);
    return await next(context);
});
api.MapGet("/health", () => new { status = "ok" });
api.MapGet("/session", (HttpContext context, IAntiforgery antiforgery) => new { csrf = antiforgery.GetAndStoreTokens(context).RequestToken, user = context.User.Identity?.IsAuthenticated == true ? new { email = context.User.FindFirstValue(ClaimTypes.Email) } : null });
api.MapPost("/auth/{action}", async (string action, Credentials input, AppDb db, IPasswordHasher<Account> hasher, HttpContext context) =>
{
    if (action is not ("signup" or "login")) throw new DomainError(404, "Endpoint not found.");
    var email = FormRules.Text(input.Email, 254, "Email").ToLowerInvariant();
    if (!System.Net.Mail.MailAddress.TryCreate(email, out var address) || address.Address != email || !email.Contains('@')) throw new DomainError(400, "Enter a valid email.");
    if (input.Password is null || input.Password.Length is < 10 or > 128) throw new DomainError(400, "Use a password of 10–128 characters.");
    var account = await db.Accounts.SingleOrDefaultAsync(x => x.Email == email);
    if (action == "signup")
    {
        if (account is not null) throw new DomainError(409, "Email already registered.");
        account = new Account { Email = email }; account.PasswordHash = hasher.HashPassword(account, input.Password); db.Accounts.Add(account);
        try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw new DomainError(409, "Email already registered."); }
    }
    else if (account is null || hasher.VerifyHashedPassword(account, account.PasswordHash, input.Password) == PasswordVerificationResult.Failed) throw new DomainError(401, "Invalid email or password.");
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); var expires = DateTimeOffset.UtcNow.AddHours(12);
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    await db.Sessions.Where(s => s.Expires <= now).ExecuteDeleteAsync();
    db.Sessions.Add(new LoginSession { Id = TokenHash(token), AccountId = account.Id, Expires = expires.ToUnixTimeSeconds() }); await db.SaveChangesAsync();
    await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()), new Claim(ClaimTypes.Email, account.Email), new Claim("sid", token)], CookieAuthenticationDefaults.AuthenticationScheme)), new AuthenticationProperties { ExpiresUtc = expires });
    return Results.Ok(new { email = account.Email });
}).RequireRateLimiting("auth");
api.MapPost("/logout", async (HttpContext context, AppDb db) => { var hash = TokenHash(context.User.FindFirstValue("sid")!); await db.Sessions.Where(s => s.Id == hash).ExecuteDeleteAsync(); await context.SignOutAsync(); return Results.Ok(new { ok = true }); }).RequireAuthorization();
api.MapGet("/forms", async (HttpContext context, AppDb db) =>
{
    var uid = UserId(context);
    var forms = await db.Surveys.AsNoTracking().Where(f => f.AccountId == uid).OrderByDescending(f => f.Created).ToListAsync();
    var ids = forms.Select(f => f.Id).ToArray();
    var counts = await db.Submissions.Where(s => ids.Contains(s.SurveyId)).GroupBy(s => s.SurveyId).Select(g => new { id = g.Key, count = g.Count() }).ToDictionaryAsync(x => x.id, x => x.count);
    return forms.Select(f => View(f, counts.GetValueOrDefault(f.Id)));
}).RequireAuthorization();
api.MapPost("/forms", async (SurveyInput input, HttpContext context, FormStore store) => Results.Created("/api/forms", View(await store.Create(UserId(context), input)))).RequireAuthorization();
api.MapGet("/forms/{id}", async (string id, HttpContext context, FormStore store, AppDb db) => View(await store.Owned(UserId(context), id), await db.Submissions.CountAsync(s => s.SurveyId == id))).RequireAuthorization();
api.MapPut("/forms/{id}", async (string id, SurveyInput input, HttpContext context, FormStore store) => View(await store.Save(UserId(context), id, input))).RequireAuthorization();
api.MapPost("/forms/{id}/state", async (string id, StateInput input, HttpContext context, FormStore store, AppDb db) => View(await store.State(UserId(context), id, input), await db.Submissions.CountAsync(s => s.SurveyId == id))).RequireAuthorization();
api.MapGet("/forms/{id}/responses", async (string id, HttpContext context, FormStore store, AppDb db) =>
{
    await store.Owned(UserId(context), id);
    return await db.Submissions.AsNoTracking().Where(s => s.SurveyId == id).OrderByDescending(s => s.Id).Take(100).Select(s => new { s.Id, s.Created, answers = JsonSerializer.Deserialize<Dictionary<string, string>>(s.Answers, (JsonSerializerOptions?)null) }).ToListAsync();
}).RequireAuthorization();
api.MapGet("/public/{id}", async (string id, AppDb db) =>
{
    var form = await db.Surveys.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id) ?? throw new DomainError(404, "Form not found.");
    if (form.State == "draft") throw new DomainError(404, "Form not found."); if (form.State == "closed") throw new DomainError(410, "This form is closed.");
    return Results.Ok(new { form.Id, form.Title, form.Description, questions = FormStore.Questions(form) });
});
api.MapPost("/public/{id}/responses", async (string id, SubmissionInput input, FormStore store) => { var result = await store.Submit(id, input); return Results.Ok(new { accepted = true, id = result.Id }); }).RequireRateLimiting("submit");
app.Map("/api/{**path}", () => Results.NotFound(new { message = "Endpoint not found." }));
app.MapFallbackToFile("index.html");
app.Run();

static long UserId(HttpContext context) => long.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
static object View(Survey form, int responses = 0) => new { form.Id, form.Title, form.Description, form.State, form.Version, form.Created, questions = FormStore.Questions(form), responses };
public partial class Program { }
