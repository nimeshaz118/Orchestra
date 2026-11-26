namespace Orchestra.Api.Modules.Maintenance;

public sealed class MaintenanceRequests
{
    private readonly HashSet<ActiveRequest> _active = [];

    // Serialize admission with the start transition so no local request can slip past its snapshot.
    internal SemaphoreSlim Admission { get; } = new(1);

    internal ActiveRequest Track(bool isAiWork)
    {
        var request = new ActiveRequest(this, isAiWork);
        _active.Add(request);
        return request;
    }

    internal ActiveRequest[] AffectedBy(string scope) =>
        _active.Where(request => !request.Token.IsCancellationRequested &&
            (request.IsAiWork || scope == MaintenanceService.AuthenticatedOperations)).ToArray();

    private async Task RemoveAsync(ActiveRequest request)
    {
        await Admission.WaitAsync();
        try
        {
            _active.Remove(request);
        }
        finally
        {
            Admission.Release();
        }
    }

    public sealed class ActiveRequest : IAsyncDisposable
    {
        private readonly MaintenanceRequests _owner;
        private readonly CancellationTokenSource _cancellation = new();
        private Task _callbacks = Task.CompletedTask;

        internal ActiveRequest(MaintenanceRequests owner, bool isAiWork)
        {
            _owner = owner;
            IsAiWork = isAiWork;
        }

        internal bool IsAiWork { get; }
        public CancellationToken Token => _cancellation.Token;
        public string? PublicMessage { get; private set; }

        internal void Cancel(string publicMessage)
        {
            PublicMessage = publicMessage;
            _callbacks = _cancellation.CancelAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _owner.RemoveAsync(this);
            try
            {
                await _callbacks;
            }
            finally
            {
                _cancellation.Dispose();
            }
        }
    }
}
