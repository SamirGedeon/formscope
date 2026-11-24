using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Formscope.Domain;

public static class FormRules
{
    public static string Text(string? value, int max, string name, bool required = true)
    {
        var text = value?.Trim() ?? "";
        if ((required && text.Length == 0) || text.Length > max) throw new DomainError(400, $"{name} must contain {(required ? "1" : "0")}–{max} characters.");
        return text;
    }
    public static Question[] Schema(Question[]? questions)
    {
        if (questions is null || questions.Length > 10) throw new DomainError(400, "Use at most 10 questions.");
        var ids = new HashSet<string>();
        return questions.Select(q =>
        {
            if (q is null || q.Id is null || !Regex.IsMatch(q.Id, "^[a-zA-Z0-9_-]{1,48}$") || !ids.Add(q.Id)) throw new DomainError(400, "Question IDs must be valid and unique.");
            if (q.Kind is not ("text" or "email" or "choice")) throw new DomainError(400, "Choose text, email or choice questions.");
            var options = q.Options ?? [];
            if (q.Kind == "choice")
            {
                if (options.Length is < 2 or > 8) throw new DomainError(400, "Choice questions need 2–8 options.");
                options = options.Select(x => Text(x, 60, "Option")).ToArray();
                if (options.Distinct(StringComparer.Ordinal).Count() != options.Length) throw new DomainError(400, "Options must be unique.");
            }
            else if (options.Length > 0) throw new DomainError(400, "Only choice questions accept options.");
            return q with { Label = Text(q.Label, 100, "Question label"), Options = options };
        }).ToArray();
    }
    public static Dictionary<string, string> Answers(Question[] questions, Dictionary<string, string>? answers)
    {
        if (answers is null || answers.Count > 10 || answers.Keys.Any(id => !questions.Any(q => q.Id == id))) throw new DomainError(400, "Answers contain an unknown question.");
        var normalized = new Dictionary<string, string>();
        foreach (var q in questions)
        {
            var value = Text(answers.GetValueOrDefault(q.Id), q.Kind == "email" ? 254 : 2000, q.Label, q.Required);
            if (value.Length > 0 && q.Kind == "email" && (!MailAddress.TryCreate(value, out var address) || address.Address != value || !value.Contains('@'))) throw new DomainError(400, $"Enter a valid email for {q.Label}.");
            if (value.Length > 0 && q.Kind == "choice" && !q.Options.Contains(value, StringComparer.Ordinal)) throw new DomainError(400, $"Choose a listed option for {q.Label}.");
            normalized[q.Id] = value;
        }
        return normalized;
    }
    public static string Fingerprint(Dictionary<string, string> answers) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(answers.OrderBy(x => x.Key, StringComparer.Ordinal)))));
    public static void Transition(string current, string next, int count)
    {
        if (current == "draft" && next == "published" && count > 0) return;
        if ((current == "published" && next == "closed") || (current == "closed" && next == "published")) return;
        throw new DomainError(400, current == "draft" && count == 0 ? "Add at least one question before publishing." : "This state change is not allowed.");
    }
}
