namespace LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;

public class TransientLeb2Exception : Exception
{
    public TransientLeb2Exception(string message)
        : base(message)
    {
    }

    public TransientLeb2Exception(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
