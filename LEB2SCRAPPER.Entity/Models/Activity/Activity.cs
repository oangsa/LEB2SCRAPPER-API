namespace LEB2SCRAPPER.Entity.Models.Activity;

public class Activity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ClassId { get; set; }
    public int AdvStarred { get; set; }
    public string GroupType { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int PeerAssessment { get; set; }
    public int IsAllowRepeat { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string EditGroupMode { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int User { get; set; }
    public int? ActivitySubmissionId { get; set; }
    public int ClassUserId { get; set; }
    public int? ActivityGroupId { get; set; }
    public string? ActivityGroupName { get; set; }
    public ActivitySubmission? ActivitySubmissionSubmittedAt { get; set; }
    public bool DueDateExceed { get; set; }
    public bool QuizSubmissionIsSubmitted { get; set; }
    public int CountGroupMember { get; set; }
    public bool ActivitySubmissionIsLate { get; set; }
    public List<object> FileActivities { get; set; } = new();
    public List<int> Questions { get; set; } = new();
    public List<object> Submissions { get; set; } = new();
    public DateTime? LastDueDateNotificationDate { get; set; }
    public DateTime? LastStatusChangeNotificationDate { get; set; }
    public bool? PreviousSubmissionStatus { get; set; }
}
