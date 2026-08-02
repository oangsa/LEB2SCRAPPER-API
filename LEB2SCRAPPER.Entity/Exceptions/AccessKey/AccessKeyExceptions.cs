namespace LEB2SCRAPPER.Entity.Exceptions.AccessKey;

public sealed class AccessKeyRequiredException : Exception
{
    public AccessKeyRequiredException()
        : base("An access key is required.")
    {
    }
}

public sealed class AccessKeyInvalidException : Exception
{
    public AccessKeyInvalidException()
        : base("The access key is invalid.")
    {
    }
}

public sealed class AccessKeyNotActivatedException : Exception
{
    public AccessKeyNotActivatedException()
        : base("The access key is not activated.")
    {
    }
}

public sealed class AccessKeyAlreadyAssignedException : Exception
{
    public AccessKeyAlreadyAssignedException()
        : base("The access key is already assigned to another account.")
    {
    }
}

public sealed class AccessKeyIdentityMismatchException : Exception
{
    public AccessKeyIdentityMismatchException()
        : base("The access key cannot be used with this account.")
    {
    }
}

public sealed class AccessKeyReauthenticationRequiredException : Exception
{
    public AccessKeyReauthenticationRequiredException()
        : base("The access key requires reauthentication.")
    {
    }
}

public sealed class AccessKeyIdentityConflictException : Exception
{
    public AccessKeyIdentityConflictException()
        : base("The access key identity cannot be registered.")
    {
    }
}

public sealed class AccessKeyDatabaseException : Exception
{
    public AccessKeyDatabaseException(
        bool isTransient,
        Exception innerException)
        : base("Access-key persistence failed.", innerException)
    {
        IsTransient = isTransient;
    }

    public bool IsTransient { get; }
}

public sealed class DeviceIdRequiredException : Exception
{
    public DeviceIdRequiredException()
        : base("A device ID is required.")
    {
    }
}

public sealed class DeviceIdInvalidException : Exception
{
    public DeviceIdInvalidException()
        : base("The device ID is invalid.")
    {
    }
}

public sealed class DeviceBindingRequiredException : Exception
{
    public DeviceBindingRequiredException()
        : base("The access key is not bound to this device.")
    {
    }
}

public sealed class DeviceBindingMismatchException : Exception
{
    public DeviceBindingMismatchException()
        : base("The access key is bound to another device.")
    {
    }
}
