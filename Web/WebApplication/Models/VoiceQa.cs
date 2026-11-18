namespace NeetBee.Web.Models;

/// <summary>
/// One multiple-choice question shown on the Voice Q&amp;A page. <see cref="Id"/> is stable;
/// <see cref="Number"/> is the question's position in the current filtered list.
/// </summary>
public sealed record VoiceQuestion(
    int Id,
    int Number,
    string Subject,
    int Year,
    string? Topic,
    string Text,
    IReadOnlyList<string> Options,
    int CorrectIndex,
    string Explanation)
{
    public string CorrectLetter => ((char)('A' + CorrectIndex)).ToString();
    public string CorrectText => Options[CorrectIndex];
}

/// <summary>
/// Subject and year filters for the Voice Q&amp;A page (null means "all"), plus the rows shown per page.
/// </summary>
public sealed record VoiceQaFilter(string? Subject, int? Year, bool ShowAll, int Size = VoiceQaFilter.DefaultSize)
{
    public const int DefaultSize = 10;

    /// <summary>Rows-per-page choices offered in the pager.</summary>
    public static readonly IReadOnlyList<int> SizeOptions = [5, 10, 20, 50];

    public static int NormalizeSize(int? size) => size is int s && SizeOptions.Contains(s) ? s : DefaultSize;

    /// <summary>Route values that keep the current filters on pagination links.</summary>
    public Dictionary<string, string> RouteValues(int? page = null)
    {
        var values = new Dictionary<string, string>();
        if (Subject is not null) values["subject"] = Subject;
        if (Year is not null) values["year"] = Year.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (ShowAll) values["all"] = "true";
        if (Size != DefaultSize) values["size"] = Size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (page is not null) values["page"] = page.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return values;
    }

    /// <summary>Key under which listening progress is saved for this filter, e.g. "Biology:2016" or "all:all".</summary>
    public string ProgressScope => $"{Subject ?? "all"}:{(Year is null ? "all" : Year.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}";

    /// <summary>A short description such as "Biology · 2024" or "All subjects · All years".</summary>
    public string Describe() => $"{Subject ?? "All subjects"} · {(Year is null ? "All years" : Year.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}";
}

/// <summary>One page of Voice Q&amp;A questions plus the filter and paging numbers the view needs.</summary>
public sealed record VoiceQaPage(
    IReadOnlyList<VoiceQuestion> Questions,
    int Page,
    int PageSize,
    int TotalQuestions,
    VoiceQaFilter Filter,
    IReadOnlyList<string> Subjects,
    IReadOnlyList<int> Years,
    IReadOnlyList<SpeechVoice> Voices,
    bool AzureSpeechEnabled,
    string? LoadError = null,
    VoiceQaResume? Resume = null)
{
    public int TotalPages => Filter.ShowAll ? 1 : Math.Max(1, (int)Math.Ceiling(TotalQuestions / (double)PageSize));
    public int FirstNumber => TotalQuestions == 0 ? 0 : Filter.ShowAll ? 1 : (Page - 1) * PageSize + 1;
    public int LastNumber => Filter.ShowAll ? TotalQuestions : Math.Min(Page * PageSize, TotalQuestions);
}

/// <summary>
/// Where the user left off for the current filter. <see cref="LastHeardNumber"/> is the position (1-based) of the
/// last question heard; <see cref="NextNumber"/> is null when every question has been heard.
/// </summary>
public sealed record VoiceQaResume(int LastHeardNumber, int Total, int PageSize, bool ShowAll)
{
    public int? NextNumber => LastHeardNumber < Total ? LastHeardNumber + 1 : null;
    public int NextPage => NextNumber is null || ShowAll ? 1 : (NextNumber.Value - 1) / PageSize + 1;
}

/// <summary>Body of POST /Study/VoiceQaProgress (save) and /Study/VoiceQaProgressReset.</summary>
public sealed class VoiceQaProgressRequest
{
    public int QuestionId { get; set; }
    public string? Subject { get; set; }
    public int? Year { get; set; }
}

/// <summary>
/// A selectable voice. <see cref="AzureName"/> is the Azure AI Speech voice ID;
/// <see cref="BrowserHint"/> is matched against the browser's own voice names when Azure is not configured.
/// </summary>
public sealed record SpeechVoice(string AzureName, string DisplayName, string Gender, string BrowserHint);

/// <summary>Body of POST /Study/Speak.</summary>
public sealed class SpeakRequest
{
    public string? Text { get; set; }
    public string? Voice { get; set; }
    public double Rate { get; set; } = 1.0;
}
