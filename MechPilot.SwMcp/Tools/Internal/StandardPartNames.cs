using System.Text.RegularExpressions;

namespace MechPilot.SwMcp.Tools.Internal;

/// <summary>
/// Pure (SolidWorks-free) heuristic that flags a component file name as a likely
/// STANDARD / catalog part (fastener, bearing, ...) from its name alone — a
/// HINT for the resize orchestrator, which must never resize standard parts.
/// It is deliberately name-based and conservative: a true result is a strong
/// "treat as off-limits"; a false result is NOT a guarantee the part is custom.
/// When the orchestrator is unsure it should ask the user (plan-first), so this
/// only needs to catch the obvious cases. Kept SW-free so it is L1-testable.
/// </summary>
internal static class StandardPartNames
{
    // Standard-body designations (ISO 4762, GB/T 70.1, DIN912, ...): an org
    // token followed (allowing GB/T-style separators incl. 't') by a number.
    // No trailing \b — a letter→digit junction ("DIN912") is not a word boundary.
    // Requiring the digit keeps plain words ("isometric", "din_bracket") out.
    private static readonly Regex StandardOrg = new(
        @"\b(iso|gb|din|ansi|jis|asme)[\s_/.t-]*\d",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Common fastener / bearing keywords (English + Simplified Chinese).
    private static readonly Regex Keywords = new(
        @"bolt|screw|\bnut\b|washer|\bstud\b|rivet|dowel|bearing|circlip|retaining\s*ring|" +
        @"螺栓|螺钉|螺母|垫圈|垫片|轴承|卡簧|销钉",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsStandardCandidate(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }
        return StandardOrg.IsMatch(fileName) || Keywords.IsMatch(fileName);
    }
}
