namespace CodeMeridian.Application.Services;

public interface IPrContextReportService
{
    Task<PrContextReport> BuildAsync(
        PrContextReportRequest request,
        CancellationToken cancellationToken = default);
}
