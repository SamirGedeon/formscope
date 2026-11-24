using Formscope.Domain;
using Xunit;
namespace Formscope.Tests;

public class RulesTests
{
    static Question Q(string kind = "text", bool required = true, string[]? options = null) => new("q1", "Your answer", kind, required, options ?? []);
    [Fact] public void SchemaTrimsLabelsAndOptions() { var schema = FormRules.Schema([Q("choice", options: [" One ", "Two"]) with { Label = " Name " }]); Assert.Equal("Name", schema[0].Label); Assert.Equal("One", schema[0].Options[0]); }
    [Theory][InlineData("unknown")][InlineData("")] public void UnsupportedKindsRejected(string kind) => Assert.Throws<DomainError>(() => FormRules.Schema([Q(kind)]));
    [Fact] public void DuplicateIdsRejected() => Assert.Throws<DomainError>(() => FormRules.Schema([Q(), Q()]));
    [Fact] public void DuplicateAndInsufficientOptionsRejected() { Assert.Throws<DomainError>(() => FormRules.Schema([Q("choice", options: ["One", " One "])])); Assert.Throws<DomainError>(() => FormRules.Schema([Q("choice", options: ["One"])])); }
    [Fact] public void SchemaLimitEnforced() => Assert.Throws<DomainError>(() => FormRules.Schema(Enumerable.Range(0, 11).Select(i => Q() with { Id = "q" + i }).ToArray()));
    [Fact] public void RequiredAnswersAndUnknownQuestionsRejected() { Assert.Throws<DomainError>(() => FormRules.Answers([Q()], [])); Assert.Throws<DomainError>(() => FormRules.Answers([Q()], new() { ["unknown"] = "value" })); }
    [Theory][InlineData("not-an-email")][InlineData("Person <a@example.com>")] public void EmailAnswersAreValidated(string answer) => Assert.Throws<DomainError>(() => FormRules.Answers([Q("email")], new() { ["q1"] = answer }));
    [Fact] public void ChoicesMustMatchPublishedOptions() => Assert.Throws<DomainError>(() => FormRules.Answers([Q("choice", options: ["One", "Two"])], new() { ["q1"] = "Three" }));
    [Fact] public void OptionalBlanksNormalizeToEmpty() => Assert.Equal("", FormRules.Answers([Q(required: false)], [])["q1"]);
    [Fact] public void FingerprintIgnoresDictionaryOrder() => Assert.Equal(FormRules.Fingerprint(new() { ["a"] = "One", ["b"] = "Two" }), FormRules.Fingerprint(new() { ["b"] = "Two", ["a"] = "One" }));
    [Fact] public void StateMachinePreventsEmptyPublicationAndReturnToDraft() { Assert.Throws<DomainError>(() => FormRules.Transition("draft", "published", 0)); Assert.Throws<DomainError>(() => FormRules.Transition("published", "draft", 1)); FormRules.Transition("closed", "published", 1); }
}
