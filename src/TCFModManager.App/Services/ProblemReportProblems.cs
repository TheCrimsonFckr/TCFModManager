using System.Globalization;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

public static class ProblemReportProblems
{
    public static string? Describe(CodePasteResult result) => result.Failure switch
    {
        null => null,

        CodePasteFailure.Network => Format(Strings.ReportProblem_UploadNetworkFormat, result.ServerMessage),

        CodePasteFailure.Rejected => string.IsNullOrWhiteSpace(result.ServerMessage)
            ? Strings.ReportProblem_UploadRejected
            : Format(Strings.ReportProblem_UploadRejectedFormat, result.ServerMessage),

        CodePasteFailure.BadResponse => Format(Strings.ReportProblem_UploadBadResponseFormat, result.ServerMessage),

        _ => Strings.ReportProblem_UploadRejected,
    };

    private static string Format(string format, string? value) =>
        string.Format(CultureInfo.CurrentCulture, format, value?.Trim());
}
