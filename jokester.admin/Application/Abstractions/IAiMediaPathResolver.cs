namespace jokester.admin.Application.Abstractions;

public interface IAiMediaPathResolver
{
    string RootPath { get; }

    string ResolveFilePath(string relativePath);
}
