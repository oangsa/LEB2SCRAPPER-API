using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Entity.Models.Activity;
using LEB2SCRAPPER.Infrastructure.Contracts.HttpService;
using LEB2SCRAPPER.Infrastructure.HttpService;
using LEB2SCRAPPER.Entity.Exceptions.ActivityCustomException;
using LEB2SCRAPPER.Entity.Models.Response;

namespace LEB2SCRAPPER.Repository.Master;

public class ActivityRepository : IActivityRepository
{
    private readonly IHttpService _httpService;
    private static readonly string BaseUrl = "https://app.leb2.org/api/get/assessment-activities/student";

    public ActivityRepository()
    {
        _httpService = new HttpService();
    }

    public async Task<List<Activity>> GetActivitiesAsync(int userId, int classId, string token)
    {
        if (userId <= 0 || classId <= 0 || string.IsNullOrWhiteSpace(token))
        {
            throw new ActivityCustomExceptionException("User ID, Class ID, and Token must be provided.");
        }

        var url = Url(classId.ToString(), userId.ToString());
        var headers = Getheaders(token);

        try
        {
            var response = await _httpService.GetAsync<ActivityResponse>(url, headers);

            return response.Activities ?? new List<Activity>();
        }
        catch (Exception ex)
        {
            throw new ActivityCustomExceptionException($"Failed to fetch activities for user {userId} in class {classId}: {ex.Message}");
        }
    }

    private static Dictionary<string, string> Getheaders(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("Token cannot be null or empty.", nameof(token));
        }

        return new Dictionary<string, string>
        {
            ["X-Requested-With"] = "XMLHttpRequest",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            ["Accept"] = "application/json, text/plain, */*",
            ["Accept-Language"] = "en-US,en;q=0.9",
            ["Referer"] = "https://app.leb2.org/",
            ["Cookie"] = token,
        };
    }

    private static string Url(string classId, string userId)
    {
        if (string.IsNullOrEmpty(classId) || string.IsNullOrEmpty(userId))
        {
            throw new ArgumentException("Class ID and User ID must be provided.");
        }

        var query = new List<string>
        {
            $"class_id={classId}",
            $"student_id={userId}",
            "sort[]=sequence",
            "sort[]=id",
            "select[]=activities:id,user_id,class_id,adv_starred,group_type,type,peer_assessment,is_allow_repeat,title,description,start_date,due_date,edit_group_mode,created_at",
            "select[]=user:id,firstname_en,lastname_en,firstname_th,lastname_th",
            "includes[]=user:sideload",
            "includes[]=fileactivities:ids",
            "includes[]=questions:ids",
            $"filter_groups[0][filters][0][key]=class_id",
            $"filter_groups[0][filters][0][value]={classId}"
        };

        return $"{BaseUrl}?{string.Join("&", query)}";
    }
}
