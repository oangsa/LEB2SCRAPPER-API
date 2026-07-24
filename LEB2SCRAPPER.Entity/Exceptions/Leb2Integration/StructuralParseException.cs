namespace LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;

public class StructuralParseException : Exception
{
    public StructuralParseException(string failureShape, string message)
        : base(message)
    {
        FailureShape = failureShape;
    }

    public StructuralParseException(string failureShape, string message, Exception innerException)
        : base(message, innerException)
    {
        FailureShape = failureShape;
    }

    public string FailureShape { get; }
}
