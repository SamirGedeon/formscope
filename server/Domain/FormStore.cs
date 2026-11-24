using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Formscope.Domain;

public sealed class FormStore(AppDb db)
{
    public static Question[] Questions(Survey form) => JsonSerializer.Deserialize<Question[]>(form.Schema)!;
    public async Task<Survey> Owned(long uid, string id) => await db.Surveys.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.AccountId == uid) ?? throw new DomainError(404, "Form not found.");
    public async Task<Survey> Create(long uid, SurveyInput input)
    {
        var form = new Survey { AccountId = uid, Title = FormRules.Text(input.Title, 100, "Form title"), Description = FormRules.Text(input.Description, 400, "Description", false), Schema = JsonSerializer.Serialize(FormRules.Schema(input.Questions)) };
        db.Surveys.Add(form); await db.SaveChangesAsync(); return form;
    }
    public async Task<Survey> Save(long uid, string id, SurveyInput input)
    {
        var title = FormRules.Text(input.Title, 100, "Form title"); var description = FormRules.Text(input.Description, 400, "Description", false); var schema = JsonSerializer.Serialize(FormRules.Schema(input.Questions));
        await using var tx = await db.Database.BeginTransactionAsync();
        var form = await Owned(uid, id);
        if (form.Version != input.Version) throw new DomainError(409, "Form changed. Reload before saving.");
        if (form.State != "draft") throw new DomainError(409, "Published questions cannot be edited.");
        var changed = await db.Surveys.Where(x => x.Id == id && x.AccountId == uid && x.Version == input.Version && x.State == "draft").ExecuteUpdateAsync(s => s.SetProperty(x => x.Title, title).SetProperty(x => x.Description, description).SetProperty(x => x.Schema, schema).SetProperty(x => x.Version, x => x.Version + 1));
        if (changed != 1) throw new DomainError(409, "Form changed. Reload before saving.");
        var result = await Owned(uid, id); await tx.CommitAsync(); return result;
    }
    public async Task<Survey> State(long uid, string id, StateInput input)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var form = await Owned(uid, id);
        if (form.Version != input.Version) throw new DomainError(409, "Form changed. Reload before continuing.");
        FormRules.Transition(form.State, input.State, Questions(form).Length);
        var changed = await db.Surveys.Where(x => x.Id == id && x.AccountId == uid && x.Version == input.Version).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, input.State).SetProperty(x => x.Version, x => x.Version + 1));
        if (changed != 1) throw new DomainError(409, "Form changed. Reload before continuing.");
        var result = await Owned(uid, id); await tx.CommitAsync(); return result;
    }
    public async Task<Submission> Submit(string id, SubmissionInput input)
    {
        if (!Guid.TryParse(input.RequestId, out var requestGuid)) throw new DomainError(400, "Use a valid request ID.");
        var requestId = requestGuid.ToString("D");
        await using var tx = await db.Database.BeginTransactionAsync();
        var form = await db.Surveys.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new DomainError(404, "Form not found.");
        if (form.State == "draft") throw new DomainError(404, "Form not found.");
        var answers = FormRules.Answers(Questions(form), input.Answers); var fingerprint = FormRules.Fingerprint(answers);
        var existing = await db.Submissions.AsNoTracking().SingleOrDefaultAsync(x => x.SurveyId == id && x.RequestId == requestId);
        if (existing is not null)
        {
            if (existing.Fingerprint != fingerprint) throw new DomainError(409, "Request ID already used with different answers.");
            await tx.CommitAsync(); return existing;
        }
        if (form.State != "published") throw new DomainError(410, "This form is closed.");
        var submission = new Submission { SurveyId = id, RequestId = requestId, Answers = JsonSerializer.Serialize(answers), Fingerprint = fingerprint };
        db.Submissions.Add(submission); await db.SaveChangesAsync(); await tx.CommitAsync(); return submission;
    }
}
