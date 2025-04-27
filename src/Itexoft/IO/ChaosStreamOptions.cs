// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading;

namespace Itexoft.IO;

public sealed class ChaosStreamOptions
{
    private double disconnectProbability;
    private double dropReadProbability;
    private double dropWriteProbability;
    private double flushFailureProbability;
    private double readBitFlipProbability;
    private double readDelayProbability = 1;
    private double readFailureProbability;
    private double readTruncationProbability;
    private double stallProbability;
    private double writeBitFlipProbability;
    private double writeDelayProbability = 1;
    private double writeFailureProbability;
    private double writeTruncationProbability;

    public double ReadFailureProbability
    {
        get => this.readFailureProbability;
        set => this.readFailureProbability = ValidateProbability(value, nameof(this.ReadFailureProbability));
    }

    public double WriteFailureProbability
    {
        get => this.writeFailureProbability;
        set => this.writeFailureProbability = ValidateProbability(value, nameof(this.WriteFailureProbability));
    }

    public double FlushFailureProbability
    {
        get => this.flushFailureProbability;
        set => this.flushFailureProbability = ValidateProbability(value, nameof(this.FlushFailureProbability));
    }

    public double DropReadProbability
    {
        get => this.dropReadProbability;
        set => this.dropReadProbability = ValidateProbability(value, nameof(this.DropReadProbability));
    }

    public double DropWriteProbability
    {
        get => this.dropWriteProbability;
        set => this.dropWriteProbability = ValidateProbability(value, nameof(this.DropWriteProbability));
    }

    public double ReadTruncationProbability
    {
        get => this.readTruncationProbability;
        set => this.readTruncationProbability = ValidateProbability(value, nameof(this.ReadTruncationProbability));
    }

    public double WriteTruncationProbability
    {
        get => this.writeTruncationProbability;
        set => this.writeTruncationProbability = ValidateProbability(value, nameof(this.WriteTruncationProbability));
    }

    public double ReadBitFlipProbability
    {
        get => this.readBitFlipProbability;
        set => this.readBitFlipProbability = ValidateProbability(value, nameof(this.ReadBitFlipProbability));
    }

    public double WriteBitFlipProbability
    {
        get => this.writeBitFlipProbability;
        set => this.writeBitFlipProbability = ValidateProbability(value, nameof(this.WriteBitFlipProbability));
    }

    public double ReadDelayProbability
    {
        get => this.readDelayProbability;
        set => this.readDelayProbability = ValidateProbability(value, nameof(this.ReadDelayProbability));
    }

    public double WriteDelayProbability
    {
        get => this.writeDelayProbability;
        set => this.writeDelayProbability = ValidateProbability(value, nameof(this.WriteDelayProbability));
    }

    public TimeSpan MinReadDelay { get; set; } = TimeSpan.Zero;

    public TimeSpan MaxReadDelay { get; set; } = TimeSpan.Zero;

    public TimeSpan MinWriteDelay { get; set; } = TimeSpan.Zero;

    public TimeSpan MaxWriteDelay { get; set; } = TimeSpan.Zero;

    public int MaxReadTruncationBytes { get; set; } = 1;

    public int MaxWriteTruncationBytes { get; set; } = 1;

    public int MaxReadBitFlips { get; set; } = 1;

    public int MaxWriteBitFlips { get; set; } = 1;

    public Random? Random { get; set; }

    public Func<TimeSpan, CancelToken, ValueTask>? DelayScheduler { get; set; }

    public Func<ChaosOperationContext, bool>? ShouldApplyNoise { get; set; }

    public Func<ChaosOperationContext, bool>? ShouldApplyReadNoise { get; set; }

    public Func<ChaosOperationContext, bool>? ShouldApplyWriteNoise { get; set; }

    public Func<ChaosOperationContext, bool>? ShouldApplyFlushNoise { get; set; }

    public TimeProvider? TimeProvider { get; set; }

    public double DisconnectProbability
    {
        get => this.disconnectProbability;
        set => this.disconnectProbability = ValidateProbability(value, nameof(this.DisconnectProbability));
    }

    public Func<ChaosOperationContext, bool>? ShouldDisconnect { get; set; }

    public string? DisconnectMessage { get; set; }

    public Func<ChaosOperationContext, TimeSpan?>? StallDurationProvider { get; set; }

    public double StallProbability
    {
        get => this.stallProbability;
        set => this.stallProbability = ValidateProbability(value, nameof(this.StallProbability));
    }

    public TimeSpan MinStallDuration { get; set; } = TimeSpan.Zero;

    public TimeSpan MaxStallDuration { get; set; } = TimeSpan.Zero;

    public TimeSpan ReadPerByteDelay { get; set; } = TimeSpan.Zero;

    public TimeSpan WritePerByteDelay { get; set; } = TimeSpan.Zero;

    public ChaosStreamOptions Clone() => (ChaosStreamOptions)this.MemberwiseClone();

    internal void Validate()
    {
        if (this.MinReadDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(this.MinReadDelay), this.MinReadDelay, "Delay cannot be negative.");

        if (this.MaxReadDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(this.MaxReadDelay), this.MaxReadDelay, "Delay cannot be negative.");

        if (this.MinWriteDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(this.MinWriteDelay), this.MinWriteDelay, "Delay cannot be negative.");

        if (this.MaxWriteDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(this.MaxWriteDelay), this.MaxWriteDelay, "Delay cannot be negative.");

        if (this.MinReadDelay > this.MaxReadDelay)
            throw new ArgumentException("Minimum read delay cannot exceed maximum read delay.", nameof(this.MinReadDelay));

        if (this.MinWriteDelay > this.MaxWriteDelay)
            throw new ArgumentException("Minimum write delay cannot exceed maximum write delay.", nameof(this.MinWriteDelay));

        if (this.MaxReadTruncationBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(this.MaxReadTruncationBytes), this.MaxReadTruncationBytes, "Value must be non-negative.");

        if (this.MaxWriteTruncationBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(this.MaxWriteTruncationBytes), this.MaxWriteTruncationBytes, "Value must be non-negative.");

        if (this.MaxReadBitFlips < 0)
            throw new ArgumentOutOfRangeException(nameof(this.MaxReadBitFlips), this.MaxReadBitFlips, "Value must be non-negative.");

        if (this.MaxWriteBitFlips < 0)
            throw new ArgumentOutOfRangeException(nameof(this.MaxWriteBitFlips), this.MaxWriteBitFlips, "Value must be non-negative.");

        if (this.MinStallDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(this.MinStallDuration), this.MinStallDuration, "Duration cannot be negative.");

        if (this.MaxStallDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(this.MaxStallDuration), this.MaxStallDuration, "Duration cannot be negative.");

        if (this.MinStallDuration > this.MaxStallDuration)
            throw new ArgumentException("Minimum stall duration cannot exceed maximum stall duration.", nameof(this.MinStallDuration));

        if (this.ReadPerByteDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(this.ReadPerByteDelay), this.ReadPerByteDelay, "Delay cannot be negative.");

        if (this.WritePerByteDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(this.WritePerByteDelay), this.WritePerByteDelay, "Delay cannot be negative.");
    }

    private static double ValidateProbability(double value, string propertyName)
    {
        if (double.IsNaN(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(propertyName, value, "Probability must be in the [0, 1] range.");

        return value;
    }
}
