namespace RdmApi.Security;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Researcher = "Researcher";
    public const string Viewer = "Viewer";

    public static bool IsValid(string? role) =>
        role == Admin || role == Researcher || role == Viewer;
}
