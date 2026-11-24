using Formscope.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace Formscope.Tests;

public class StoreTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "formscope-test-" + Guid.NewGuid().ToString("N"));
    readonly AppDb db; readonly FormStore store;
    public StoreTests() { Directory.CreateDirectory(directory); db = Open(); db.Database.EnsureCreated(); db.Accounts.AddRange(new Account { Id = 1, Email = "one@example.com", PasswordHash = "test-hash" }, new Account { Id = 2, Email = "two@example.com", PasswordHash = "test-hash" }); db.SaveChanges(); store = new(db); }
    AppDb Open() => new(new DbContextOptionsBuilder<AppDb>().UseSqlite($"Data Source={Path.Combine(directory, "test.sqlite")};Default Timeout=5;Foreign Keys=True").Options);
    static SurveyInput Input(int version = 0) => new("Feedback", "A little context", [new("email", "Email", "email", true, []), new("comment", "Comments", "text", false, [])], version);
    async Task<Survey> Publish() { var form = await store.Create(1, Input()); return await store.State(1, form.Id, new("published", 1)); }
    [Fact] public async Task OtherAccountsCannotAccessForms() { var f = await store.Create(1, Input()); Assert.Equal(404, (await Assert.ThrowsAsync<DomainError>(() => store.Owned(2, f.Id))).Status); }
    [Fact] public async Task StaleDraftWritesCannotOverwrite() { var f = await store.Create(1, Input()); await store.Save(1, f.Id, Input(1) with { Title = "Saved" }); Assert.Equal(409, (await Assert.ThrowsAsync<DomainError>(() => store.Save(1, f.Id, Input(1)))).Status); Assert.Equal("Saved", (await store.Owned(1, f.Id)).Title); }
    [Fact] public async Task PublishedAndClosedQuestionsRemainFrozen() { var f = await Publish(); Assert.Equal(409, (await Assert.ThrowsAsync<DomainError>(() => store.Save(1, f.Id, Input(2)))).Status); await store.State(1, f.Id, new("closed", 2)); Assert.Equal(409, (await Assert.ThrowsAsync<DomainError>(() => store.Save(1, f.Id, Input(3)))).Status); }
    [Fact] public async Task DuplicateSubmissionIsIdempotentAndUuidCanonical() { var f = await Publish(); var id = Guid.NewGuid().ToString("D"); var body = new SubmissionInput(id, new() { ["email"] = "reader@example.com" }); var first = await store.Submit(f.Id, body); var second = await store.Submit(f.Id, body with { RequestId = id.ToUpperInvariant() }); Assert.Equal(first.Id, second.Id); Assert.Equal(1, await db.Submissions.CountAsync()); }
    [Fact] public async Task ConflictingRequestReuseRejected() { var f = await Publish(); var input = new SubmissionInput(Guid.NewGuid().ToString(), new() { ["email"] = "one@example.com" }); await store.Submit(f.Id, input); Assert.Equal(409, (await Assert.ThrowsAsync<DomainError>(() => store.Submit(f.Id, input with { Answers = new() { ["email"] = "two@example.com" } }))).Status); }
    [Fact] public async Task ClosedFormRejectsNewSubmissionsButCanReplayReceipt() { var f = await Publish(); var input = new SubmissionInput(Guid.NewGuid().ToString(), new() { ["email"] = "reader@example.com" }); var first = await store.Submit(f.Id, input); await store.State(1, f.Id, new("closed", 2)); Assert.Equal(first.Id, (await store.Submit(f.Id, input)).Id); Assert.Equal(410, (await Assert.ThrowsAsync<DomainError>(() => store.Submit(f.Id, input with { RequestId = Guid.NewGuid().ToString() }))).Status); }
    [Fact] public async Task InvalidAnswersNeverPersist() { var f = await Publish(); await Assert.ThrowsAsync<DomainError>(() => store.Submit(f.Id, new(Guid.NewGuid().ToString(), new() { ["email"] = "invalid" }))); Assert.Equal(0, await db.Submissions.CountAsync()); }
    [Fact] public async Task DraftCannotReceiveSubmissions() { var f = await store.Create(1, Input()); Assert.Equal(404, (await Assert.ThrowsAsync<DomainError>(() => store.Submit(f.Id, new(Guid.NewGuid().ToString(), new() { ["email"] = "reader@example.com" })))).Status); }
    [Fact] public async Task DatabasePersistsAcrossContextReopen() { var f = await Publish(); await using var other = Open(); var stored = await new FormStore(other).Owned(1, f.Id); Assert.Equal("published", stored.State); Assert.Equal(2, stored.Version); }
    [Fact] public async Task CompetingVersionsHaveOneWinner() { var f = await store.Create(1, Input()); async Task<int> Save(string title) { await using var context = Open(); try { return (await new FormStore(context).Save(1, f.Id, Input(1) with { Title = title })).Version; } catch (DomainError error) { return error.Status; } } var results = await Task.WhenAll(Task.Run(() => Save("First")), Task.Run(() => Save("Second"))); Assert.Equal([2, 409], results.Order().ToArray()); }
    public void Dispose() { db.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
}
