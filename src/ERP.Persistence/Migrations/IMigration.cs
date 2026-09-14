namespace ERP.Persistence.Migrations;

public interface IMigration
{
    int Version { get; }

    string Name { get; }

    IReadOnlyList<string> Statements { get; }
}

