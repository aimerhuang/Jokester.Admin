namespace jokester.admin.Application.Abstractions;

public interface IPromptLibrarySyncRunner
{
    Task RunAsync(PromptLibrarySyncTrigger trigger, CancellationToken cancellationToken);
}
