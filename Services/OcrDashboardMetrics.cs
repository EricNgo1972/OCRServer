namespace OCRServer.Services;

public sealed class OcrDashboardMetrics
{
    private long _totalRequestsSubmitted;
    private long _totalDocumentsProcessed;
    private long _totalSuccessfulRequests;
    private long _totalFailedRequests;
    private long _totalPagesRead;

    public void RecordRequestSubmitted()
        => Interlocked.Increment(ref _totalRequestsSubmitted);

    public void RecordRequestOutcome(bool isSuccess, bool hasDocument, int? pagesRead)
    {
        if (hasDocument)
            Interlocked.Increment(ref _totalDocumentsProcessed);

        if (pagesRead.HasValue && pagesRead.Value > 0)
            Interlocked.Add(ref _totalPagesRead, pagesRead.Value);

        if (isSuccess)
            Interlocked.Increment(ref _totalSuccessfulRequests);
        else
            Interlocked.Increment(ref _totalFailedRequests);
    }

    public OcrDashboardSnapshot GetSnapshot()
        => new(
            TotalRequestsSubmitted: Interlocked.Read(ref _totalRequestsSubmitted),
            TotalDocumentsProcessed: Interlocked.Read(ref _totalDocumentsProcessed),
            TotalSuccessfulRequests: Interlocked.Read(ref _totalSuccessfulRequests),
            TotalFailedRequests: Interlocked.Read(ref _totalFailedRequests),
            TotalPagesRead: Interlocked.Read(ref _totalPagesRead));
}

public sealed record OcrDashboardSnapshot(
    long TotalRequestsSubmitted,
    long TotalDocumentsProcessed,
    long TotalSuccessfulRequests,
    long TotalFailedRequests,
    long TotalPagesRead);
