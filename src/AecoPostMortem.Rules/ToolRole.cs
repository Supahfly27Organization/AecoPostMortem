namespace AecoPostMortem.Rules;

/// <summary>
/// The closed set of roles a tool can be classified into, derived purely from the argument shapes
/// its calls carried (FR-29, FR-30). There is no sixth member for "unclassifiable" — a tool that
/// matches no shape is recorded separately (<see cref="ToolRoleDerivation.Unclassified"/>) rather
/// than forced into one of these five, because assigning it a role here would be a guess.
/// </summary>
public enum ToolRole
{
    FileRead,
    Search,
    FileWrite,
    Shell,
    Spawn,
}
