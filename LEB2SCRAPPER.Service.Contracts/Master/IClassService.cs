using LEB2SCRAPPER.Entity.Models.Class;

namespace LEB2SCRAPPER.Service.Contracts.Master;

public interface IClassService
{
    public Task<List<ClassInfo>?> GetClassesAsync(int semesterId, string token);
}
