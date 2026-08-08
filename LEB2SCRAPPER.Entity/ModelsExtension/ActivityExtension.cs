using LEB2SCRAPPER.Entity.Models.Activity;
using System.Text.RegularExpressions;

namespace LEB2SCRAPPER.Entity.ModelsExtension;

public static class ActivityExtension
{
    public static string StatusText(this Activity activity, DateTimeOffset? utcNow = null) =>
        activity.GetStatusText(utcNow ?? DateTimeOffset.UtcNow);
    public static bool IsUnsubmitted(this Activity activity) => activity.GetIsUnsubmitted();
    public static bool ShowNotSubmittedBadge(this Activity activity) => activity.IsUnsubmitted() && !activity.IsSubmitted();
    public static bool ShowOverdueBadge(this Activity activity) => activity.DueDateExceed && !activity.IsSubmitted();
    public static bool HasDueDate(this Activity activity) => activity.DueDate.HasValue;
    public static bool IsAnnouncement(this Activity activity) => !activity.HasDueDate();
    public static bool IsSubmitted(this Activity activity) => activity.GetIsSubmitted();
    public static string CleanDescription(this Activity activity) => activity.CleanHtmlDescription();
    public static string DisplayDate(this Activity activity) => activity.GetDisplayDate();
    public static string FormattedSubmissionDate(this Activity activity) => activity.GetFormattedSubmissionDate();
    public static string DisplayType(this Activity activity)
    {
        return activity.Type switch
        {
            "QUZ" => "Quiz",
            "ASM" => "Assignment",
            _ => activity.Type
        };
    }

    private static string CleanHtmlDescription(this Activity activity)
    {
        if (string.IsNullOrEmpty(activity.Description))
            return "No description available";

        // Remove HTML tags
        var cleanText = Regex.Replace(activity.Description, "<[^>]*>", "");

        // Decode HTML entities
        cleanText = cleanText.Replace("&nbsp;", " ")
                           .Replace("&amp;", "&")
                           .Replace("&lt;", "<")
                           .Replace("&gt;", ">")
                           .Replace("&quot;", "\"")
                           .Replace("&#39;", "'");

        // Clean up whitespace
        cleanText = Regex.Replace(cleanText, @"\s+", " ").Trim();

        // Limit length for better display
        if (cleanText.Length > 200)
        {
            cleanText = cleanText.Substring(0, 200) + "...";
        }

        return string.IsNullOrEmpty(cleanText) ? "No description available" : cleanText;
    }

    private static string GetDisplayDate(this Activity activity)
    {
        if (activity.IsSubmitted())
        {
            return $"Submitted: {activity.FormattedSubmissionDate()}";
        }

        if (activity.DueDate.HasValue)
        {
            return $"Due: {activity.DueDate.Value:MMM dd, yyyy HH:mm}";
        }

        return "No due date";
    }

    private static string GetFormattedSubmissionDate(this Activity activity)
    {
        if (activity.Type == "QUZ" && activity.QuizSubmissionIsSubmitted)
        {
            // For quizzes, we might not have exact submission date
            return "Completed";
        }

        if (activity.ActivitySubmissionSubmittedAt?.Date != null)
        {
            if (DateTime.TryParse(activity.ActivitySubmissionSubmittedAt.Date, out var submissionDate))
            {
                return submissionDate.ToString("MMM dd, yyyy HH:mm");
            }
            return activity.ActivitySubmissionSubmittedAt.Date;
        }

        return "Unknown";
    }

    private static string GetStatusText(this Activity activity, DateTimeOffset utcNow)
    {
        if (activity.Type == "QUZ" && activity.QuizSubmissionIsSubmitted)
            return "Submitted";

        if (activity.ActivitySubmissionSubmittedAt != null)
            return "Submitted";

        if (activity.DueDateExceed)
            return "Overdue";

        if (activity.DueDate.HasValue && activity.DueDate.Value > utcNow.UtcDateTime)
            return "Pending";

        return "Available";
    }

    private static bool GetIsUnsubmitted(this Activity activity)
    {
        if (activity.Type == "QUZ")
            return !activity.QuizSubmissionIsSubmitted;

        if (activity.ActivitySubmissionSubmittedAt != null)
            return false;

        return activity.HasDueDate();
    }

    private static bool GetIsSubmitted(this Activity activity)
    {
        if (activity.Type == "QUZ")
            return activity.QuizSubmissionIsSubmitted;

        if (activity.ActivitySubmissionSubmittedAt != null)
            return true;

        return false;
    }
}
