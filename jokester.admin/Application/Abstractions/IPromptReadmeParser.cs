using jokester.admin.Application.Models.PromptLibrary;

namespace jokester.admin.Application.Abstractions;

public interface IPromptReadmeParser
{
    PromptReadmeParseResult Parse(string markdown, PromptReadmeParseOptions options);
}
