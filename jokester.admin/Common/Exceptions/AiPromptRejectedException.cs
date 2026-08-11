using jokester.admin.Application.Models.AiPromptFilter;

namespace jokester.admin.Common.Exceptions;

public sealed class AiPromptRejectedException(string fieldName, AiPromptFilterResult result)
    : AppException(ErrorCodes.AiPromptRejected, "提示词包含不允许的内容，请修改后重试")
{
    public string FieldName { get; } = fieldName;

    public AiPromptFilterResult Result { get; } = result;
}
