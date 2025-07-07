using System.ComponentModel.DataAnnotations;

namespace LEB2SCRAPPER.Entity.ValidationAttributes;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public class RequiredHeaderAttribute : ValidationAttribute
{
    public RequiredHeaderAttribute()
    {
        ErrorMessage = "Authorization header is required";
    }

    public override bool IsValid(object? value)
    {
        if (value is string stringValue)
        {
            return !string.IsNullOrWhiteSpace(stringValue);
        }
        return false;
    }
}
