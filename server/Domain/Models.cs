using Microsoft.EntityFrameworkCore;
namespace Formscope.Domain;

public sealed class Account { public long Id { get; set; } public string Email { get; set; } = ""; public string PasswordHash { get; set; } = ""; }
public sealed class LoginSession { public string Id { get; set; } = ""; public long AccountId { get; set; } public long Expires { get; set; } }
public sealed class Survey { public string Id { get; set; } = Guid.NewGuid().ToString("N"); public long AccountId { get; set; } public string Title { get; set; } = ""; public string Description { get; set; } = ""; public string Schema { get; set; } = "[]"; public string State { get; set; } = "draft"; public int Version { get; set; } = 1; public long Created { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
public sealed class Submission { public long Id { get; set; } public string SurveyId { get; set; } = ""; public string RequestId { get; set; } = ""; public string Answers { get; set; } = "{}"; public string Fingerprint { get; set; } = ""; public long Created { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
public sealed record Question(string Id, string Label, string Kind, bool Required, string[] Options);
public sealed record SurveyInput(string Title, string Description, Question[] Questions, int Version);
public sealed record StateInput(string State, int Version);
public sealed record SubmissionInput(string RequestId, Dictionary<string, string> Answers);
public sealed record Credentials(string Email, string Password);
public sealed class DomainError(int status, string message) : Exception(message) { public int Status { get; } = status; }
public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<LoginSession> Sessions => Set<LoginSession>();
    public DbSet<Survey> Surveys => Set<Survey>();
    public DbSet<Submission> Submissions => Set<Submission>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>().HasIndex(x => x.Email).IsUnique();
        model.Entity<LoginSession>().HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Survey>().HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Submission>().HasOne<Survey>().WithMany().HasForeignKey(x => x.SurveyId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Submission>().HasIndex(x => new { x.SurveyId, x.RequestId }).IsUnique();
    }
}
