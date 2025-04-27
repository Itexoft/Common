// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Extensions;
using Itexoft.Threading;
using ExceptionHandler = System.Func<System.Exception, ulong, System.Threading.Tasks.ValueTask>;

namespace Itexoft.Core;

file static class RetryPolicyCore
{
    internal static async ValueTask Retry(
        Func<CancelToken, ValueTask> operation,
        ExceptionHandler? onError,
        ulong maxAttempts,
        CancelToken cancelToken)
    {
        for (ulong attempt = 0;; attempt++)
        {
            cancelToken.ThrowIf();

            try
            {
                await operation(cancelToken).ConfigureAwait(false);

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (maxAttempts > 0 && attempt + 1 >= maxAttempts)
                    throw;

                if (onError is not null)
                    await onError(ex, attempt).ConfigureAwait(false);
            }
        }
    }

    internal static async ValueTask<T> Retry<T>(
        Func<CancelToken, ValueTask<T>> operation,
        ExceptionHandler? onError,
        ulong maxAttempts,
        CancelToken cancelToken)
    {
        for (ulong attempt = 0;; attempt++)
        {
            cancelToken.ThrowIf();

            try
            {
                return await operation(cancelToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (maxAttempts > 0 && attempt + 1 >= maxAttempts)
                    throw;

                if (onError is not null)
                    await onError(ex, attempt).ConfigureAwait(false);
            }
        }
    }

    internal static async ValueTask Ignore(Func<CancelToken, ValueTask> operation, CancelToken cancelToken)
    {
        while (true)
        {
            cancelToken.ThrowIf();

            try
            {
                await operation(cancelToken).ConfigureAwait(false);

                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // swallow and retry
            }
        }
    }

    internal static async ValueTask<T> Ignore<T>(Func<CancelToken, ValueTask<T>> operation, CancelToken cancelToken)
    {
        while (true)
        {
            cancelToken.ThrowIf();

            try
            {
                return await operation(cancelToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // swallow and retry
            }
        }
    }
}

public readonly struct RetryPolicy
{
    private readonly ulong attempts;
    private readonly ExceptionHandler? onError;
    private readonly TimeSpan timeout;

    public RetryPolicy() : this(null, TimeSpan.Zero, 0) { }
    public RetryPolicy(ExceptionHandler? onError, TimeSpan timeout = default) : this(onError, timeout, 0) { }

    private RetryPolicy(ExceptionHandler? onError, TimeSpan timeout, ulong attempts)
    {
        this.onError = onError;
        this.timeout = timeout;
        this.attempts = attempts;
    }

    private static readonly ExceptionHandler ignoreDelegate = static (_, _) => ValueTask.CompletedTask;
    private bool IsIgnore => ReferenceEquals(this.onError, ignoreDelegate);

    public static RetryPolicy None { get; } = new();

    public static RetryPolicy Ignore { get; } = new(ignoreDelegate);

    public static RetryPolicy Limit(TimeSpan timeout) => new(static (_, _) => ValueTask.CompletedTask, timeout);

    public static RetryPolicy Limit(ulong attempts) => new(null, TimeSpan.Zero, attempts);

    public static RetryPolicy Limit(ulong attempts, TimeSpan timeout) => new(null, timeout, attempts);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RetryPolicy WithTimeout(TimeSpan timeout) => new(this.onError, timeout, this.attempts);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask Run(Func<CancelToken, ValueTask> operation, CancelToken cancelToken = default)
    {
        operation.Required();

        return this.RunCore(operation, cancelToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<T> Run<T>(Func<CancelToken, ValueTask<T>> operation, CancelToken cancelToken = default)
    {
        operation.Required();

        return this.RunCore(operation, cancelToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask Run(Func<CancelToken, Task> operation, CancelToken cancelToken = default)
    {
        operation.Required();

        return this.RunCore(ToValueTask(operation), cancelToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<T> Run<T>(Func<CancelToken, Task<T>> operation, CancelToken cancelToken = default)
    {
        operation.Required();

        return this.RunCore(ToValueTask(operation), cancelToken);
    }

    public static implicit operator RetryPolicy(ExceptionHandler? onError) => new(onError);

    private static Func<CancelToken, ValueTask> ToValueTask(Func<CancelToken, Task> operation) => cancelToken =>
    {
        cancelToken.ThrowIf();
        var task = operation(cancelToken);

        return task.IsCompletedSuccessfully ? ValueTask.CompletedTask : new(task);
    };

    private static Func<CancelToken, ValueTask<T>> ToValueTask<T>(Func<CancelToken, Task<T>> operation) =>
        cancelToken =>
        {
            cancelToken.ThrowIf();
            var task = operation(cancelToken);

            return task.IsCompletedSuccessfully ? new(task.Result) : new ValueTask<T>(task);
        };

    private ValueTask RunCore(Func<CancelToken, ValueTask> operation, CancelToken cancelToken) =>
        this.Dispatch(operation, this.PrepareToken(cancelToken));

    private ValueTask<T> RunCore<T>(Func<CancelToken, ValueTask<T>> operation, CancelToken cancelToken) =>
        this.Dispatch(operation, this.PrepareToken(cancelToken));

    private CancelToken PrepareToken(CancelToken cancelToken)
    {
        if (this.timeout.IsZero)
            return cancelToken;

        cancelToken.ThrowIf();

        return cancelToken.Branch(this.timeout);
    }

    private ValueTask Dispatch(Func<CancelToken, ValueTask> operation, CancelToken cancelToken)
    {
        if (this.IsIgnore)
            return RetryPolicyCore.Ignore(operation, cancelToken);

        if (this.onError is null && this.attempts == 0)
            return operation(cancelToken);

        return RetryPolicyCore.Retry(operation, this.onError, this.attempts, cancelToken);
    }

    private ValueTask<T> Dispatch<T>(Func<CancelToken, ValueTask<T>> operation, CancelToken cancelToken)
    {
        if (this.IsIgnore)
            return RetryPolicyCore.Ignore(operation, cancelToken);

        if (this.onError is null && this.attempts == 0)
            return operation(cancelToken);

        return RetryPolicyCore.Retry(operation, this.onError, this.attempts, cancelToken);
    }
}

public readonly struct RetryPolicy<T>
{
    public static RetryPolicy<T> None { get; } = new(RetryPolicy.None);

    public static RetryPolicy<T> Ignore { get; } = new(RetryPolicy.Ignore);

    public static RetryPolicy<T> Limit(TimeSpan timeout) => new(RetryPolicy.Limit(timeout));

    public static RetryPolicy<T> Limit(ulong attempts) => new(RetryPolicy.Limit(attempts));

    public static RetryPolicy<T> Limit(ulong attempts, TimeSpan timeout) => new(RetryPolicy.Limit(attempts, timeout));

    private readonly RetryPolicy policy;

    public RetryPolicy(ExceptionHandler? onError) : this(new RetryPolicy(onError)) { }

    private RetryPolicy(RetryPolicy policy) => this.policy = policy;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<T> Run(Func<CancelToken, ValueTask<T>> operation, CancelToken cancelToken = default) =>
        this.policy.Run(operation, cancelToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<T> Run(Func<CancelToken, Task<T>> operation, CancelToken cancelToken = default) =>
        this.policy.Run(operation, cancelToken).AsTask();

    public static implicit operator RetryPolicy<T>(ExceptionHandler? onError) => new(onError);
}
