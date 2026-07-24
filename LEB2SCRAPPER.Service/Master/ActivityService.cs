using System.Runtime.ExceptionServices;
using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Entity.Models.Activity;
using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Entity.Models.Response;
using LEB2SCRAPPER.Service.Contracts.Master;
using Microsoft.Extensions.Logging;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace LEB2SCRAPPER.Service.Master;

public class ActivityService : IActivityService
{
    private const int AggregateMaxParallelism = 2;
    private readonly ILogger<ActivityService> _logger;
    private readonly IRepositoryManager _repositoryManager;

    public ActivityService(
        ICoreAdapterManager coreAdapterManager,
        ILogger<ActivityService> logger)
    {
        _repositoryManager = coreAdapterManager.RepositoryManager;
        _logger = logger;
    }

    public async Task<List<Activity>?> GetActivitiesAsync(
        int userId,
        int classId,
        string token,
        CancellationToken cancellationToken = default)
    {
        var activities = await _repositoryManager.ActivityRepository.GetActivitiesAsync(
            userId,
            classId,
            token,
            cancellationToken);

        return activities;
    }

    public async Task<List<Activity>> GetActivitiesBySemesterAsync(
        int userId,
        int semesterId,
        string token,
        CancellationToken cancellationToken = default)
    {
        var classes = await GetSemesterActivitiesAsync(
            userId,
            semesterId,
            token,
            cancellationToken);

        return classes
            .SelectMany(classResult => classResult.Activities)
            .ToList();
    }

    public async Task<SemesterSnapshotResponse> GetSemesterSnapshotAsync(
        int userId,
        int semesterId,
        string token,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var status = "failed";

        try
        {
            var classes = await GetSemesterActivitiesAsync(
                userId,
                semesterId,
                token,
                cancellationToken);
            var response = new SemesterSnapshotResponse
            {
                SemesterId = semesterId,
                Classes = classes
                    .Select(classResult => new SemesterSnapshotClass
                    {
                        Id = classResult.ClassInfo.Id,
                        Name = classResult.ClassInfo.Name,
                        Activities = classResult.Activities
                    })
                    .ToList()
            };

            status = "success";
            return response;
        }
        catch (OperationCanceledException)
        {
            status = "canceled";
            throw;
        }
        finally
        {
            _logger.LogInformation(
                "Semester snapshot finished with status {SnapshotStatus} "
                + "in {SnapshotElapsedMilliseconds} ms.",
                status,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private async Task<List<ClassActivities>> GetSemesterActivitiesAsync(
        int userId,
        int semesterId,
        string token,
        CancellationToken cancellationToken)
    {
        ValidateSemesterRequest(userId, semesterId, token);

        var discoveryStartedAt = Stopwatch.GetTimestamp();
        var discoveryStatus = "failed";
        var classCount = 0;
        List<ClassInfo> classes;

        try
        {
            var discoveredClasses = await _repositoryManager.ScrapingRepository
                .GetClassesBySemesterIdAsync(
                    semesterId,
                    token,
                    cancellationToken);
            classes = (discoveredClasses ?? new List<ClassInfo>())
                .Where(classInfo => classInfo.Id > 0)
                .GroupBy(classInfo => classInfo.Id)
                .Select(group => group.First())
                .OrderBy(classInfo => classInfo.Id)
                .ToList();
            classCount = classes.Count;
            discoveryStatus = "success";
        }
        catch (OperationCanceledException)
        {
            discoveryStatus = "canceled";
            throw;
        }
        finally
        {
            _logger.LogInformation(
                "LEB2 class discovery finished with status {ClassDiscoveryStatus} "
                + "in {ClassDiscoveryElapsedMilliseconds} ms for {ClassCount} classes.",
                discoveryStatus,
                Stopwatch.GetElapsedTime(discoveryStartedAt).TotalMilliseconds,
                classCount);
        }

        if (classes.Count == 0)
        {
            _logger.LogInformation(
                "LEB2 activity retrieval finished with status {ActivityRetrievalStatus} "
                + "in {ActivityRetrievalElapsedMilliseconds} ms for {ClassCount} classes.",
                "success",
                0D,
                0);
            return new List<ClassActivities>();
        }

        var retrievalStartedAt = Stopwatch.GetTimestamp();
        var retrievalStatus = "failed";

        try
        {
            var activitiesByClass = await RunFailFastAsync(
                classes,
                async (classInfo, requestToken) => new ClassActivities(
                    classInfo,
                    await _repositoryManager.ActivityRepository.GetActivitiesAsync(
                        userId,
                        classInfo.Id,
                        token,
                        requestToken)),
                cancellationToken);

            retrievalStatus = "success";
            return activitiesByClass.ToList();
        }
        catch (OperationCanceledException)
        {
            retrievalStatus = "canceled";
            throw;
        }
        finally
        {
            _logger.LogInformation(
                "LEB2 activity retrieval finished with status {ActivityRetrievalStatus} "
                + "in {ActivityRetrievalElapsedMilliseconds} ms for {ClassCount} classes.",
                retrievalStatus,
                Stopwatch.GetElapsedTime(retrievalStartedAt).TotalMilliseconds,
                classCount);
        }
    }

    private static void ValidateSemesterRequest(
        int userId,
        int semesterId,
        string token)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(userId),
                "User ID must be greater than zero.");
        }

        if (semesterId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(semesterId),
                "Semester ID must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token must be provided.", nameof(token));
        }
    }

    private static async Task<TResult[]> RunFailFastAsync<TInput, TResult>(
        IReadOnlyList<TInput> inputs,
        Func<TInput, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        using var linkedCancellationSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var results = new TResult[inputs.Count];
        ExceptionDispatchInfo? firstFailure = null;
        var nextIndex = -1;

        async Task RunWorkerAsync()
        {
            while (!linkedCancellationSource.IsCancellationRequested)
            {
                var index = Interlocked.Increment(ref nextIndex);

                if (index >= inputs.Count)
                {
                    return;
                }

                try
                {
                    results[index] = await operation(
                        inputs[index],
                        linkedCancellationSource.Token);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    var capturedException = ExceptionDispatchInfo.Capture(exception);

                    if (Interlocked.CompareExchange(
                            ref firstFailure,
                            capturedException,
                            null) is null)
                    {
                        linkedCancellationSource.Cancel();
                    }

                    return;
                }
            }
        }

        var workers = Enumerable
            .Range(0, Math.Min(AggregateMaxParallelism, inputs.Count))
            .Select(_ => RunWorkerAsync())
            .ToArray();

        await Task.WhenAll(workers);

        cancellationToken.ThrowIfCancellationRequested();
        firstFailure?.Throw();

        return results;
    }

    private sealed record ClassActivities(
        ClassInfo ClassInfo,
        List<Activity> Activities);
}
