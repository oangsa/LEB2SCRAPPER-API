namespace LEB2SCRAPPER.Entity.Models.AccessKey;

public sealed record AccessKeyState(
    Guid KeyId,
    Guid? UserId,
    string? StudentId)
{
    public bool IsAssigned => UserId.HasValue;
}
