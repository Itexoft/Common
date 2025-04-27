// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.Net.Core;

internal class NetConnectionDialer : INetConnectionDialer
{
    private readonly NetConnectionOptions options;
    private readonly NetRetryBudget retryBudget;

    public NetConnectionDialer(NetConnectionOptions options)
    {
        this.options = options.Required();
        this.retryBudget = new(Math.Max(1, this.options.RetryBudgetCapacity), this.options.RetryBudgetWindow);
    }

    public NetDialTrackerCollection DialTrackers { get; } = new();

    public async ValueTask<INetConnectionHandle?> DialAsync(NetDialParameters dialParams, CancelToken cancelToken)
    {
        var endpoint = await dialParams.Required().Endpoint.ResolveAsync(cancelToken).ConfigureAwait(false);
        var changeState = dialParams.ChangeState.Required();
        var tracker = dialParams.Tracker.Required();
        var dispose = dialParams.Dispose.Required();

        try
        {
            await changeState(NetConnectionTransitionCause.DialStarted, null);

            if (!this.retryBudget.TryConsume(DateTimeOffset.UtcNow))
            {
                using (cancelToken.Bridge(out var token))
                    await Task.Delay(this.options.SoftFailureInitialBackoff, token).ConfigureAwait(false);

                return null;
            }

            var connection = await this.options.Connector.ConnectAsync(endpoint, dispose, cancelToken).ConfigureAwait(false);

            if (connection == null)
                return null;

            tracker.RecordSuccess();
            this.retryBudget.Refund(DateTimeOffset.UtcNow);
            await changeState(NetConnectionTransitionCause.DialSucceeded, null);

            return connection;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (NetRetryAfterException retryAfter)
        {
            tracker.RecordFailure(DateTimeOffset.UtcNow, retryAfter.Delay);
            await changeState(NetConnectionTransitionCause.DialFailed, retryAfter);

            using (cancelToken.Bridge(out var token))
                await Task.Delay(retryAfter.Delay, token).ConfigureAwait(false);

            return null;
        }
        catch (Exception ex)
        {
            tracker.RecordFailure(DateTimeOffset.UtcNow, this.options.EndpointBlacklistDuration);
            await changeState(NetConnectionTransitionCause.DialFailed, ex);

            return null;
        }
    }
}

public readonly struct NetDialRateMetric
{
    public string EndpointKey { get; init; }
    public int AttemptsLastMinute { get; init; }
}
