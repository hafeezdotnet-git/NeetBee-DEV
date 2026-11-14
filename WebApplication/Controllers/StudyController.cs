using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using NeetBee.Web.Models;
using NeetBee.Web.Services;

namespace NeetBee.Web.Controllers;

/// <summary>
/// The study pages linked from the dashboard sidebar. Each action renders a Razor view in Views/Study
/// inside the dashboard layout.
/// </summary>
[Authorize]
public sealed class StudyController(
    ILogger<StudyController> logger,
    IVoiceQuestionService voiceQuestions,
    AzureSpeechService speech) : Controller
{
    /// <summary>Cookie that remembers the rows-per-page choice (set by voice-qa.js).</summary>
    private const string VoiceQaSizeCookie = "neetbee-vqa-size";

    /// <summary>
    /// Voice Q&amp;A: questions and answers that can be read aloud, filtered by subject and year.
    /// <paramref name="all"/> shows every matching question on one page instead of paging.
    /// Without a <paramref name="page"/>, the page opens where the user left off (the next question not yet heard).
    /// </summary>
    public async Task<IActionResult> VoiceQa(int? page = null, string? subject = null, int? year = null, bool all = false,
        int? size = null, CancellationToken cancellationToken = default)
    {
        var knownSubject = voiceQuestions.Subjects.FirstOrDefault(s => string.Equals(s, subject, StringComparison.OrdinalIgnoreCase));

        // Rows per page: from the URL, else the remembered choice, else the default. Only offered sizes are accepted.
        var cookieSize = int.TryParse(Request.Cookies[VoiceQaSizeCookie], out var remembered) ? remembered : (int?)null;
        var rows = VoiceQaFilter.NormalizeSize(size ?? cookieSize);

        try
        {
            // Only known subjects and years are accepted; anything else means "all".
            var years = await voiceQuestions.GetYearsAsync(cancellationToken);
            var knownYear = year is not null && years.Contains(year.Value) ? year : null;
            var filter = new VoiceQaFilter(knownSubject, knownYear, all, rows);

            VoiceQaResume? resume = null;
            if (CurrentUserId is int userId
                && await voiceQuestions.GetProgressAsync(userId, filter, cancellationToken) is { } progress)
            {
                resume = new VoiceQaResume(progress.LastHeardNumber, progress.Total, rows, all);
            }

            var requestedPage = page ?? resume?.NextPage ?? 1;
            logger.LogInformation("GET Study/VoiceQa {Filter} page {Page} all {ShowAll} resume {Resume} requested by {UserName}",
                filter.Describe(), requestedPage, all, resume?.NextNumber, User.Identity?.Name);

            int? pageSize = all ? null : rows;
            var (questions, total) = await voiceQuestions.GetPageAsync(filter, requestedPage, pageSize, cancellationToken);

            // A page number outside the list (typed in, or after a filter change) shows the nearest page instead.
            var totalPages = all ? 1 : Math.Max(1, (int)Math.Ceiling(total / (double)rows));
            var shownPage = Math.Clamp(requestedPage, 1, totalPages);
            if (shownPage != requestedPage)
            {
                (questions, total) = await voiceQuestions.GetPageAsync(filter, shownPage, pageSize, cancellationToken);
            }

            return View(new VoiceQaPage(questions, shownPage, rows, total, filter,
                voiceQuestions.Subjects, years, AzureSpeechService.Voices, speech.IsConfigured, Resume: resume));
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Voice Q&A could not read questions from the database");
            return View(new VoiceQaPage([], 1, rows, 0, new VoiceQaFilter(knownSubject, null, all, rows),
                voiceQuestions.Subjects, [], AzureSpeechService.Voices, speech.IsConfigured,
                LoadError: "Questions could not be loaded right now. Please try again in a moment."));
        }
    }

    /// <summary>Saves the last question the user finished hearing for the given subject/year filter.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VoiceQaProgress([FromBody] VoiceQaProgressRequest request, CancellationToken cancellationToken)
    {
        if (CurrentUserId is not int userId) return Unauthorized();
        if (request.QuestionId <= 0) return BadRequest();

        var filter = await ProgressFilterAsync(request, cancellationToken);
        await voiceQuestions.SaveProgressAsync(userId, filter, request.QuestionId, cancellationToken);
        return NoContent();
    }

    /// <summary>Clears the saved progress for the given subject/year filter, so listening starts from question 1.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VoiceQaProgressReset([FromBody] VoiceQaProgressRequest request, CancellationToken cancellationToken)
    {
        if (CurrentUserId is not int userId) return Unauthorized();

        var filter = await ProgressFilterAsync(request, cancellationToken);
        await voiceQuestions.ResetProgressAsync(userId, filter, cancellationToken);
        logger.LogInformation("Voice Q&A progress reset for {Scope} by {UserName}", filter.ProgressScope, User.Identity?.Name);
        return NoContent();
    }

    /// <summary>The signed-in user's Users.Id, from the NameIdentifier claim set at sign-in.</summary>
    private int? CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>Builds the progress filter from a request, accepting only known subjects and years.</summary>
    private async Task<VoiceQaFilter> ProgressFilterAsync(VoiceQaProgressRequest request, CancellationToken cancellationToken)
    {
        var subject = voiceQuestions.Subjects.FirstOrDefault(s => string.Equals(s, request.Subject, StringComparison.OrdinalIgnoreCase));
        var years = await voiceQuestions.GetYearsAsync(cancellationToken);
        var year = request.Year is not null && years.Contains(request.Year.Value) ? request.Year : null;
        return new VoiceQaFilter(subject, year, false);
    }

    /// <summary>
    /// Returns MP3 audio for one piece of text in the chosen Azure voice.
    /// 503 means Azure is not configured, so the page uses browser speech instead.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Speak([FromBody] SpeakRequest request, CancellationToken cancellationToken)
    {
        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > AzureSpeechService.MaxTextLength
            || !AzureSpeechService.IsKnownVoice(request.Voice) || !AzureSpeechService.IsKnownRate(request.Rate))
        {
            return BadRequest();
        }

        if (!speech.IsConfigured) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var audio = await speech.SynthesizeAsync(text, request.Voice!, request.Rate, cancellationToken);
        if (audio is null) return StatusCode(StatusCodes.Status502BadGateway);

        return File(audio, "audio/mpeg");
    }

    public IActionResult Courses()
    {
        logger.LogInformation("GET Study/Courses requested by {UserName}", User.Identity?.Name);
        return View();
    }

    public IActionResult PreviousYearPapers()
    {
        logger.LogInformation("GET Study/PreviousYearPapers requested by {UserName}", User.Identity?.Name);
        return View();
    }

    public IActionResult McqPractice()
    {
        logger.LogInformation("GET Study/McqPractice requested by {UserName}", User.Identity?.Name);
        return View();
    }

    public IActionResult MockTests()
    {
        logger.LogInformation("GET Study/MockTests requested by {UserName}", User.Identity?.Name);
        return View();
    }

    public IActionResult AudioLearning()
    {
        logger.LogInformation("GET Study/AudioLearning requested by {UserName}", User.Identity?.Name);
        return View();
    }

    public IActionResult Pricing()
    {
        logger.LogInformation("GET Study/Pricing requested by {UserName}", User.Identity?.Name);
        return View();
    }

    public IActionResult Performance() => ComingSoon("Performance");

    public IActionResult SavedQuestions() => ComingSoon("Saved Questions");

    public IActionResult Achievements() => ComingSoon("Achievements");

    public IActionResult Settings() => ComingSoon("Settings");

    public IActionResult HelpSupport() => ComingSoon("Help & Support");

    /// <summary>Shows a placeholder page for a sidebar link whose content is not built yet.</summary>
    private ViewResult ComingSoon(string title)
    {
        logger.LogInformation("GET Study page '{Title}' requested by {UserName} (placeholder)", title, User.Identity?.Name);
        ViewData["Title"] = title;
        return View("ComingSoon");
    }
}
