namespace ERP.Application.Common;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

